using Capri.Automation;
using Capri.Automation.Apps;
using Capri.Automation.DependencyInjection;
using Capri.Automation.Media;
using Capri.Automation.Processes;
using Capri.Automation.Web;
using Capri.Core.Abstractions;
using Capri.Core.Abstractions.Automation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Capri.Tests.Automation;

public sealed class AutomationServiceRegistrationTests
{
    [Fact]
    public void AutomationServicesResolve()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IPermissionService>());
        services.AddCapriAutomation();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ShellProcessStarter>(provider.GetRequiredService<IProcessStarter>());
        Assert.IsType<AppAliasResolver>(provider.GetRequiredService<IAppAliasResolver>());
        Assert.IsType<AppPathValidator>(provider.GetRequiredService<IAppPathValidator>());
        Assert.IsType<WebLauncher>(provider.GetRequiredService<IWebLauncher>());
        Assert.IsType<AppLauncher>(provider.GetRequiredService<IAppLauncher>());
        Assert.IsType<WindowsMediaKeySender>(provider.GetRequiredService<IMediaKeySender>());
        Assert.IsType<MediaControl>(provider.GetRequiredService<IMediaControl>());
        Assert.IsType<AutomationService>(provider.GetRequiredService<IAutomationService>());
    }

    [Fact]
    public void ExistingRegistrationsAreNotOverwritten()
    {
        var services = new ServiceCollection();
        var permissionService = Substitute.For<IPermissionService>();
        var automationService = Substitute.For<IAutomationService>();
        services.AddSingleton(permissionService);
        services.AddSingleton(automationService);
        services.AddCapriAutomation();

        using var provider = services.BuildServiceProvider();

        Assert.Same(automationService, provider.GetRequiredService<IAutomationService>());
    }
}
