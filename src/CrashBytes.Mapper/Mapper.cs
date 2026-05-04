using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace CrashBytes.Mapper;

/// <summary>
/// Fluent configuration builder for the object mapper.
/// </summary>
public class MapperConfiguration
{
    internal readonly Dictionary<(Type Source, Type Dest), Delegate?> Mappings = new();
    internal readonly Dictionary<(Type Source, Type Dest), List<(string DestProp, Delegate Resolver)>> CustomResolvers = new();

    /// <summary>
    /// Registers a mapping from <typeparamref name="TSource"/> to <typeparamref name="TDest"/>
    /// using convention-based property matching.
    /// </summary>
    public MappingExpression<TSource, TDest> CreateMap<TSource, TDest>()
    {
        var key = (typeof(TSource), typeof(TDest));
        Mappings[key] = null; // null = use convention-based mapping
        return new MappingExpression<TSource, TDest>(this);
    }

    /// <summary>
    /// Builds an <see cref="IMapper"/> from this configuration.
    /// </summary>
    public IMapper BuildMapper() => new ObjectMapper(this);
}

/// <summary>
/// Fluent API for customizing individual property mappings.
/// </summary>
public class MappingExpression<TSource, TDest>
{
    private readonly MapperConfiguration _config;

    internal MappingExpression(MapperConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Specifies a custom resolver for a destination property.
    /// </summary>
    public MappingExpression<TSource, TDest> ForMember<TMember>(
        Expression<Func<TDest, TMember>> destMember,
        Func<TSource, TMember> resolver)
    {
        if (destMember is null) throw new ArgumentNullException(nameof(destMember));
        if (resolver is null) throw new ArgumentNullException(nameof(resolver));

        var memberExpr = destMember.Body as MemberExpression
            ?? throw new ArgumentException("Expression must be a member access.", nameof(destMember));

        var key = (typeof(TSource), typeof(TDest));
        if (!_config.CustomResolvers.ContainsKey(key))
            _config.CustomResolvers[key] = new List<(string, Delegate)>();

        _config.CustomResolvers[key].Add((memberExpr.Member.Name, resolver));
        return this;
    }
}

/// <summary>
/// Interface for performing object-to-object mapping.
/// </summary>
public interface IMapper
{
    /// <summary>Maps <paramref name="source"/> to a new instance of <typeparamref name="TDest"/>.</summary>
    TDest Map<TDest>(object source);

    /// <summary>Maps <paramref name="source"/> to a new instance of <typeparamref name="TDest"/>.</summary>
    TDest Map<TSource, TDest>(TSource source);

    /// <summary>Maps <paramref name="source"/> onto an existing <paramref name="destination"/> instance.</summary>
    TDest Map<TSource, TDest>(TSource source, TDest destination);
}

internal class ObjectMapper : IMapper
{
    private readonly MapperConfiguration _config;

    // Plan that describes how to map a (TSource, TDest) pair.
    // The plan compiles two delegates the first time it's needed:
    //   - CreateAndCopy: produces a new TDest from the source (used by the create overloads)
    //   - CopyOnto:      copies convention-matching properties onto an existing TDest
    // Both delegates use compiled System.Linq.Expressions so each subsequent call is
    // effectively native field/property access — no per-call reflection.
    private readonly ConcurrentDictionary<(Type Source, Type Dest), MappingPlan> _planCache = new();

    internal ObjectMapper(MapperConfiguration config)
    {
        _config = config;
    }

    public TDest Map<TDest>(object source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return (TDest)MapInternal(source, source.GetType(), typeof(TDest))!;
    }

    public TDest Map<TSource, TDest>(TSource source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return (TDest)MapInternal(source, typeof(TSource), typeof(TDest))!;
    }

    public TDest Map<TSource, TDest>(TSource source, TDest destination)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (destination is null) throw new ArgumentNullException(nameof(destination));

        var key = (typeof(TSource), typeof(TDest));
        ValidateMapping(key);
        var plan = GetPlan(typeof(TSource), typeof(TDest));
        plan.CopyOnto(source!, destination);
        ApplyCustomResolvers(source!, destination, key);
        return destination;
    }

    private object MapInternal(object source, Type sourceType, Type destType)
    {
        var key = (sourceType, destType);
        ValidateMapping(key);

        var plan = GetPlan(sourceType, destType);
        var dest = plan.CreateAndCopy(source);
        ApplyCustomResolvers(source, dest, key);
        return dest;
    }

    private void ValidateMapping((Type Source, Type Dest) key)
    {
        if (!_config.Mappings.ContainsKey(key))
            throw new InvalidOperationException($"No mapping configured from {key.Source.Name} to {key.Dest.Name}. Call CreateMap<{key.Source.Name}, {key.Dest.Name}>() first.");
    }

    private MappingPlan GetPlan(Type sourceType, Type destType)
        => _planCache.GetOrAdd((sourceType, destType), static k => MappingPlan.Build(k.Source, k.Dest));

    private void ApplyCustomResolvers(object source, object dest, (Type, Type) key)
    {
        if (!_config.CustomResolvers.TryGetValue(key, out var resolvers))
            return;

        var destProps = dest.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name);

        foreach (var (destPropName, resolver) in resolvers)
        {
            if (destProps.TryGetValue(destPropName, out var destProp))
            {
                var value = resolver.DynamicInvoke(source);
                destProp.SetValue(dest, value);
            }
        }
    }
}

/// <summary>
/// A compiled mapping plan for a particular (source, destination) type pair.
/// The expression trees are built once per pair and cached; subsequent calls
/// invoke a JIT-compiled delegate with no reflection overhead.
/// </summary>
internal sealed class MappingPlan
{
    public Func<object, object> CreateAndCopy { get; }
    public Action<object, object> CopyOnto { get; }

    private MappingPlan(Func<object, object> createAndCopy, Action<object, object> copyOnto)
    {
        CreateAndCopy = createAndCopy;
        CopyOnto = copyOnto;
    }

    public static MappingPlan Build(Type sourceType, Type destType)
    {
        var pairs = MatchProperties(sourceType, destType);
        return new MappingPlan(
            BuildCreateAndCopy(sourceType, destType, pairs),
            BuildCopyOnto(sourceType, destType, pairs));
    }

    private static List<(PropertyInfo Source, PropertyInfo Dest)> MatchProperties(Type sourceType, Type destType)
    {
        var sourceProps = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var pairs = new List<(PropertyInfo, PropertyInfo)>();
        foreach (var destProp in destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                          .Where(p => p.CanWrite))
        {
            if (sourceProps.TryGetValue(destProp.Name, out var sourceProp) &&
                destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
            {
                pairs.Add((sourceProp, destProp));
            }
        }

        return pairs;
    }

    private static Func<object, object> BuildCreateAndCopy(
        Type sourceType,
        Type destType,
        List<(PropertyInfo Source, PropertyInfo Dest)> pairs)
    {
        // (object src) => {
        //     var typedSrc = (TSource)src;
        //     var dest = new TDest();
        //     dest.P1 = typedSrc.P1;
        //     dest.P2 = typedSrc.P2;
        //     ...
        //     return (object)dest;
        // }
        var srcParam = Expression.Parameter(typeof(object), "src");
        var typedSrc = Expression.Variable(sourceType, "typedSrc");
        var dest = Expression.Variable(destType, "dest");

        var ctor = destType.GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                $"Cannot create instance of {destType.Name}. Ensure it has a parameterless constructor.");

        var body = new List<Expression>
        {
            Expression.Assign(typedSrc, Expression.Convert(srcParam, sourceType)),
            Expression.Assign(dest, Expression.New(ctor)),
        };

        foreach (var (sp, dp) in pairs)
        {
            body.Add(Expression.Assign(
                Expression.Property(dest, dp),
                MakeAssignableValue(Expression.Property(typedSrc, sp), dp.PropertyType)));
        }

        body.Add(Expression.Convert(dest, typeof(object)));

        var block = Expression.Block(new[] { typedSrc, dest }, body);
        return Expression.Lambda<Func<object, object>>(block, srcParam).Compile();
    }

    private static Action<object, object> BuildCopyOnto(
        Type sourceType,
        Type destType,
        List<(PropertyInfo Source, PropertyInfo Dest)> pairs)
    {
        // (object src, object dst) => {
        //     var typedSrc = (TSource)src;
        //     var typedDst = (TDest)dst;
        //     typedDst.P1 = typedSrc.P1;
        //     ...
        // }
        var srcParam = Expression.Parameter(typeof(object), "src");
        var dstParam = Expression.Parameter(typeof(object), "dst");
        var typedSrc = Expression.Variable(sourceType, "typedSrc");
        var typedDst = Expression.Variable(destType, "typedDst");

        var body = new List<Expression>
        {
            Expression.Assign(typedSrc, Expression.Convert(srcParam, sourceType)),
            Expression.Assign(typedDst, Expression.Convert(dstParam, destType)),
        };

        foreach (var (sp, dp) in pairs)
        {
            body.Add(Expression.Assign(
                Expression.Property(typedDst, dp),
                MakeAssignableValue(Expression.Property(typedSrc, sp), dp.PropertyType)));
        }

        // Empty body still needs at least one expression for the block.
        if (body.Count == 2)
        {
            body.Add(Expression.Empty());
        }

        var block = Expression.Block(new[] { typedSrc, typedDst }, body);
        return Expression.Lambda<Action<object, object>>(block, srcParam, dstParam).Compile();
    }

    private static Expression MakeAssignableValue(Expression value, Type destPropertyType)
    {
        // The IsAssignableFrom check during MatchProperties guarantees compatibility,
        // but a Convert may still be required when the source property is a more
        // derived reference type or a value type identical to the destination type.
        return value.Type == destPropertyType
            ? value
            : Expression.Convert(value, destPropertyType);
    }
}
