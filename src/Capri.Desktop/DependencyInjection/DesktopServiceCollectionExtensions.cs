using Capri.Core.Abstractions;
using Capri.Core.Autonomy;
using Capri.Automation.Autonomy;
using Capri.Security;
using Capri.Desktop.Services;
using Capri.Desktop.ViewModels;
using Capri.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Capri.Desktop.DependencyInjection;

/// <summary>
/// Registration helpers for the WPF desktop composition root.
/// </summary>
public static class DesktopServiceCollectionExtensions
{
    /// <summary>
    /// Registers headless/testable desktop services (view models and presentation helpers)
    /// without WPF windows or tray infrastructure. Does not register Core/Security/Providers/
    /// Automation/Voice; callers compose those explicitly.
    /// </summary>
    public static IServiceCollection AddCapriDesktopCoreServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IUiDispatcher, WpfUiDispatcher>();
        services.TryAddSingleton<AvatarPresentationService>();
        services.TryAddSingleton<OverlayChatViewModel>();
        services.TryAddTransient<SettingsViewModel>();

        return services;
    }

    /// <summary>
    /// Registers WPF windows, view models, and desktop services for the running application.
    /// </summary>
    public static IServiceCollection AddCapriDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCapriDesktopCoreServices();

        // Companion UX: transparent character window + compact chat panel (toggled by clicking Capri).
        services.TryAddSingleton<CharacterWindow>();
        services.TryAddSingleton<ChatPanelWindow>();
        services.TryAddTransient<SettingsWindow>();
        services.TryAddSingleton<IOverlayController>(sp => sp.GetRequiredService<CharacterWindow>());
        services.TryAddSingleton<TrayIconService>();
        services.TryAddSingleton<ITaskApproval, WpfTaskApproval>();
        services.TryAddSingleton<ITaskExecutor, SafeTaskExecutor>();
        services.TryAddSingleton<ITaskJournal, TaskMetadataJournal>();
        services.TryAddSingleton<TaskCoordinator>();

        return services;
    }
}
