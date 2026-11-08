using Capri.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Capri.Security.DependencyInjection;

/// <summary>
/// Registration helpers for Capri's security, settings, and permission services.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default paths, DPAPI credential store, JSON settings store,
    /// and Phase 1 permission policy.
    /// </summary>
    public static IServiceCollection AddCapriSecurity(this IServiceCollection services)
    {
        return AddCapriSecurity(services, paths: null);
    }

    /// <summary>
    /// Registers security services using an optional custom path provider.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="paths">Optional path provider, primarily for tests.</param>
    public static IServiceCollection AddCapriSecurity(this IServiceCollection services, ICapriPaths? paths)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (paths is null)
        {
            services.TryAddSingleton<ICapriPaths, CapriPaths>();
        }
        else
        {
            services.TryAddSingleton<ICapriPaths>(paths);
        }

        services.TryAddSingleton<ISecureCredentialStore>(serviceProvider =>
            new DpapiCredentialStore(
                serviceProvider.GetRequiredService<ICapriPaths>(),
                serviceProvider.GetService<ILogger<DpapiCredentialStore>>()));

        services.TryAddSingleton<IAppSettingsStore>(serviceProvider =>
            new JsonAppSettingsStore(
                serviceProvider.GetRequiredService<ICapriPaths>(),
                serviceProvider.GetService<ILogger<JsonAppSettingsStore>>()));

        services.TryAddSingleton<IPermissionService>(serviceProvider =>
            new DefaultPermissionService(
                serviceProvider.GetRequiredService<IAppSettingsStore>(),
                serviceProvider.GetService<ILogger<DefaultPermissionService>>()));

        return services;
    }
}
