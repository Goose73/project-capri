using Capri.Automation.Apps;
using Capri.Automation.Media;
using Capri.Automation.Processes;
using Capri.Automation.Web;
using Capri.Core.Abstractions.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Capri.Automation.DependencyInjection;

/// <summary>
/// Registration helpers for automation services.
/// </summary>
public static class AutomationServiceCollectionExtensions
{
    /// <summary>
    /// Registers URL validation, web and application launching, media keys, and
    /// the coordinating automation service. Add <c>AddCapriSecurity</c> first so
    /// <see cref="Capri.Core.Abstractions.IPermissionService"/> is available.
    /// </summary>
    public static IServiceCollection AddCapriAutomation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IProcessStarter, ShellProcessStarter>();
        services.TryAddSingleton<IAppAliasResolver, AppAliasResolver>();
        services.TryAddSingleton<IAppPathValidator, AppPathValidator>();
        services.TryAddSingleton<IWebLauncher, WebLauncher>();
        services.TryAddSingleton<IAppLauncher, AppLauncher>();
        services.TryAddSingleton<IMediaKeySender, WindowsMediaKeySender>();
        services.TryAddSingleton<IMediaControl, MediaControl>();
        services.TryAddSingleton<IAutomationService, AutomationService>();

        return services;
    }
}
