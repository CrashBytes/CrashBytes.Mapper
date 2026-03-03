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
    private readonly ConcurrentDictionary<(Type, Type), PropertyMap[]> _propertyCache = new();

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
        var maps = GetPropertyMaps(typeof(TSource), typeof(TDest));
        ApplyPropertyMaps(source, destination, maps);
        ApplyCustomResolvers(source, destination, key);
        return destination;
    }

    private object MapInternal(object source, Type sourceType, Type destType)
    {
        var key = (sourceType, destType);
        ValidateMapping(key);

        var dest = Activator.CreateInstance(destType)
            ?? throw new InvalidOperationException($"Cannot create instance of {destType.Name}. Ensure it has a parameterless constructor.");

        var maps = GetPropertyMaps(sourceType, destType);
        ApplyPropertyMaps(source, dest, maps);
        ApplyCustomResolvers(source, dest, key);
        return dest;
    }

    private void ValidateMapping((Type Source, Type Dest) key)
    {
        if (!_config.Mappings.ContainsKey(key))
            throw new InvalidOperationException($"No mapping configured from {key.Source.Name} to {key.Dest.Name}. Call CreateMap<{key.Source.Name}, {key.Dest.Name}>() first.");
    }

    private PropertyMap[] GetPropertyMaps(Type sourceType, Type destType)
    {
        return _propertyCache.GetOrAdd((sourceType, destType), _ =>
        {
            var sourceProps = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite);

            var maps = new List<PropertyMap>();
            foreach (var destProp in destProps)
            {
                if (sourceProps.TryGetValue(destProp.Name, out var sourceProp) &&
                    destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
                {
                    maps.Add(new PropertyMap(sourceProp, destProp));
                }
            }

            return maps.ToArray();
        });
    }

    private static void ApplyPropertyMaps(object source, object dest, PropertyMap[] maps)
    {
        foreach (var map in maps)
        {
            var value = map.Source.GetValue(source);
            map.Dest.SetValue(dest, value);
        }
    }

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

    private record struct PropertyMap(PropertyInfo Source, PropertyInfo Dest);
}
