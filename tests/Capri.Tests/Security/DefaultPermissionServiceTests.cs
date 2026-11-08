using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Security;

public sealed class DefaultPermissionServiceTests
{
    [Theory]
    [InlineData(PermissionKind.LaunchApplication)]
    [InlineData(PermissionKind.OpenWeb)]
    [InlineData(PermissionKind.MediaControl)]
    public async Task EnabledCategoriesAreAllowedWithoutConfirmation(PermissionKind kind)
    {
        var settings = new FakeAppSettingsStore();
        var service = new DefaultPermissionService(settings);

        var decision = await service.RequestAsync(new PermissionRequest(kind, "test"));

        Assert.True(decision.Allowed);
        Assert.False(decision.Remember);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public async Task DisabledAutomationDeniesAllCategories()
    {
        var settings = Settings(automation => automation with { Enabled = false });
        var service = new DefaultPermissionService(settings);

        var decision = await service.RequestAsync(
            new PermissionRequest(PermissionKind.LaunchApplication, "test"));

        Assert.False(decision.Allowed);
        Assert.Contains("Automation is disabled", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledAppLaunchIsDenied()
    {
        var settings = Settings(automation => automation with { AllowAppLaunch = false });
        var service = new DefaultPermissionService(settings);

        var decision = await service.RequestAsync(
            new PermissionRequest(PermissionKind.LaunchApplication, "test"));

        Assert.False(decision.Allowed);
        Assert.Contains("Application launching is disabled", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledWebLaunchIsDenied()
    {
        var settings = Settings(automation => automation with { AllowWebLaunch = false });
        var service = new DefaultPermissionService(settings);

        var decision = await service.RequestAsync(
            new PermissionRequest(PermissionKind.OpenWeb, "test"));

        Assert.False(decision.Allowed);
        Assert.Contains("Opening web pages is disabled", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledMediaControlIsDenied()
    {
        var settings = Settings(automation => automation with { AllowMediaControl = false });
        var service = new DefaultPermissionService(settings);

        var decision = await service.RequestAsync(
            new PermissionRequest(PermissionKind.MediaControl, "test"));

        Assert.False(decision.Allowed);
        Assert.Contains("Media control is disabled", decision.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PermissionKind.FileAccess)]
    [InlineData(PermissionKind.RunCommand)]
    [InlineData(PermissionKind.Unknown)]
    public async Task UnsupportedCategoriesAreDenied(PermissionKind kind)
    {
        var service = new DefaultPermissionService(new FakeAppSettingsStore());

        var decision = await service.RequestAsync(new PermissionRequest(kind, "test"));

        Assert.False(decision.Allowed);
        Assert.False(decision.Remember);
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
    }

    [Fact]
    public async Task CancelledRequestThrows()
    {
        var service = new DefaultPermissionService(new FakeAppSettingsStore());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RequestAsync(new PermissionRequest(PermissionKind.OpenWeb, "test"), cts.Token));
    }

    private static FakeAppSettingsStore Settings(
        Func<AppSettings.AutomationSettings, AppSettings.AutomationSettings> configure)
    {
        var defaults = CapriDefaultSettings.Create();
        return new FakeAppSettingsStore(
            defaults with { Automation = configure(defaults.Automation) });
    }
}
