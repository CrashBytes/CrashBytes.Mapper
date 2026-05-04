using System.Collections.Concurrent;

namespace CrashBytes.Mapper;

/// <summary>
/// Static convenience entry point for ad-hoc mapping without explicit configuration.
/// Each (TSource, TDest) pair is auto-registered on first use using convention-based
/// property matching, then reuses the compiled-expression cache for subsequent calls.
/// </summary>
/// <remarks>
/// For applications that need custom property resolvers, ignores, or DI lifetime
/// management, prefer <see cref="MapperConfiguration"/> directly or register an
/// <see cref="IMapper"/> via the <c>AddCrashBytesMapper</c> extension.
/// </remarks>
public static class Mapper
{
    private static readonly ConcurrentDictionary<(Type Source, Type Dest), IMapper> _ambientMappers = new();

    /// <summary>
    /// Maps <paramref name="source"/> to a new instance of <typeparamref name="TDest"/>
    /// using convention-based property matching (case-insensitive name match plus
    /// assignment compatibility on property type).
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type. Must have a public parameterless constructor.</typeparam>
    /// <param name="source">The instance to map from.</param>
    /// <returns>A new <typeparamref name="TDest"/> with matching properties copied from <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static TDest Map<TSource, TDest>(TSource source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return GetAmbientMapper<TSource, TDest>().Map<TSource, TDest>(source);
    }

    /// <summary>
    /// Maps <paramref name="source"/> onto an existing <paramref name="destination"/> instance,
    /// overwriting matching properties. Properties on <paramref name="destination"/> that have
    /// no convention match on the source are left unchanged.
    /// </summary>
    public static TDest Map<TSource, TDest>(TSource source, TDest destination)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        return GetAmbientMapper<TSource, TDest>().Map(source, destination);
    }

    /// <summary>
    /// Registers a custom mapping for the given (TSource, TDest) pair, replacing any
    /// previously cached ambient mapping. Use this to declare custom property resolvers
    /// for the static entry points.
    /// </summary>
    /// <param name="configure">
    /// Invoked with a fresh <see cref="MappingExpression{TSource, TDest}"/> so callers can
    /// register <c>ForMember</c> resolvers without constructing a <see cref="MapperConfiguration"/>.
    /// </param>
    public static void ConfigureMap<TSource, TDest>(Action<MappingExpression<TSource, TDest>> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var config = new MapperConfiguration();
        var expression = config.CreateMap<TSource, TDest>();
        configure(expression);

        var mapper = config.BuildMapper();
        _ambientMappers[(typeof(TSource), typeof(TDest))] = mapper;
    }

    /// <summary>
    /// Clears all ambient mappings registered via <see cref="ConfigureMap"/> or
    /// auto-created via <see cref="Map{TSource, TDest}(TSource)"/>. Primarily useful
    /// in tests; production code should generally not call this.
    /// </summary>
    public static void Reset() => _ambientMappers.Clear();

    private static IMapper GetAmbientMapper<TSource, TDest>()
    {
        return _ambientMappers.GetOrAdd((typeof(TSource), typeof(TDest)), static _ =>
        {
            var config = new MapperConfiguration();
            config.CreateMap<TSource, TDest>();
            return config.BuildMapper();
        });
    }
}
