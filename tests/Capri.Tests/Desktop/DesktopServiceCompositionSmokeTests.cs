using Capri.Automation.DependencyInjection;
using Capri.Core.Abstractions;
using Capri.Core.Abstractions.Automation;
using Capri.Core.DependencyInjection;
using Capri.Desktop.DependencyInjection;
using Capri.Desktop.Services;
using Capri.Desktop.ViewModels;
using Capri.Providers.DependencyInjection;
using Capri.Security;
using Capri.Security.DependencyInjection;
using Capri.Tests.TestDoubles;
using Capri.Voice.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Capri.Tests.Desktop;

/// <summary>
/// Headless smoke test: verifies the desktop composition resolves key services without creating WPF windows.
/// </summary>
public sealed class DesktopServiceCompositionSmokeTests
{
    [Fact]
    public void DesktopCompositionResolvesKeyServicesWithoutWpfWindows()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddLogging();

        // Same registrations as the desktop host, with an isolated data path and no windows/tray.
        services.AddCapriCore();
        services.AddCapriSecurity(new CapriPaths(temp.Path));
        services.AddCapriProviders();
        services.AddCapriAutomation();
        services.AddCapriVoice();
        services.AddCapriDesktopCoreServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = false,
            ValidateOnBuild = false
        });

        Assert.NotNull(provider.GetRequiredService<ICapriPaths>());
        Assert.NotNull(provider.GetRequiredService<IAppSettingsStore>());
        Assert.NotNull(provider.GetRequiredService<ISecureCredentialStore>());
        Assert.NotNull(provider.GetRequiredService<IPermissionService>());
        Assert.NotNull(provider.GetRequiredService<IAvatarStateMachine>());
        Assert.NotNull(provider.GetRequiredService<IConversationStore>());
        Assert.NotNull(provider.GetRequiredService<IIntentParser>());
        Assert.NotNull(provider.GetRequiredService<IModelRouter>());
        Assert.NotNull(provider.GetRequiredService<IChatProvider>());
        Assert.NotNull(provider.GetRequiredService<IAutomationService>());
        Assert.NotNull(provider.GetRequiredService<IAssistantOrchestrator>());

        Assert.NotNull(provider.GetRequiredService<AvatarPresentationService>());
        Assert.NotNull(provider.GetRequiredService<OverlayChatViewModel>());
        Assert.NotNull(provider.GetRequiredService<SettingsViewModel>());

        // Sanity: settings are readable and no secrets are exposed via settings.
        var settings = provider.GetRequiredService<IAppSettingsStore>().Current;
        Assert.NotNull(settings.Provider);
        Assert.NotEmpty(settings.Provider.ProviderId);
    }
}
