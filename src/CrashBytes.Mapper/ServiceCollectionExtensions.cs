using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrashBytes.Mapper;

/// <summary>
/// DI extensions for registering <see cref="IMapper"/> in a
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IMapper"/> built from the supplied configuration callback.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional callback to declare <c>CreateMap</c> / <c>ForMember</c> on the
    /// <see cref="MapperConfiguration"/> before it is built. May be <see langword="null"/>
    /// to register an empty configuration that callers can extend through the static
    /// <see cref="Mapper"/> facade.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddCrashBytesMapper(
        this IServiceCollection services,
        Action<MapperConfiguration>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        var config = new MapperConfiguration();
        configure?.Invoke(config);

        services.TryAddSingleton<IMapper>(_ => config.BuildMapper());
        services.TryAddSingleton(config);
        return services;
    }
}
