using Capri.Automation;
using Capri.Core.Abstractions;
using Capri.Core.Abstractions.Automation;
using Capri.Core.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Capri.Tests.Automation;

public sealed class AutomationServiceTests
{
    [Fact]
    public async Task PermissionDenialBlocksWebLaunch()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PermissionDecision(false, false, "Web access is blocked."));
        var web = Substitute.For<IWebLauncher>();
        var service = CreateService(permissions: permissions, web: web);

        var result = await service.ExecuteAsync(
            new Intent(IntentKind.OpenWeb, "https://example.com"));

        Assert.False(result.Succeeded);
        Assert.Equal("Web access is blocked.", result.Message);
        await web.DidNotReceive().OpenAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermissionDenialBlocksApplicationLaunch()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PermissionDecision(false, false, "App launch is blocked."));
        var aliases = new StubAppAliasResolver();
        var target = new AppTarget("Notepad", @"C:\Windows\System32\notepad.exe", "notepad");
        aliases.Add("notepad", target);
        var apps = Substitute.For<IAppLauncher>();
        var service = CreateService(permissions: permissions, aliases: aliases, apps: apps);

        var result = await service.ExecuteAsync(new Intent(IntentKind.LaunchApp, "notepad"));

        Assert.False(result.Succeeded);
        Assert.Equal("App launch is blocked.", result.Message);
        await apps.DidNotReceive().LaunchAsync(Arg.Any<AppTarget>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermissionDenialBlocksMediaControl()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PermissionDecision(false, false, "Media control is blocked."));
        var media = Substitute.For<IMediaControl>();
        var service = CreateService(permissions: permissions, media: media);

        var result = await service.ExecuteAsync(
            new Intent(IntentKind.MediaControl, Media: MediaCommand.PlayPause));

        Assert.False(result.Succeeded);
        Assert.Equal("Media control is blocked.", result.Message);
        await media.DidNotReceive().ExecuteAsync(Arg.Any<MediaCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidUrlIsRejectedBeforePermissionIsRequested()
    {
        var permissions = Substitute.For<IPermissionService>();
        var web = Substitute.For<IWebLauncher>();
        var service = CreateService(permissions: permissions, web: web);

        var result = await service.ExecuteAsync(
            new Intent(IntentKind.OpenWeb, "javascript:alert(1)"));

        Assert.False(result.Succeeded);
        Assert.Contains("javascript", result.Message, StringComparison.OrdinalIgnoreCase);
        await permissions.DidNotReceive().RequestAsync(
            Arg.Any<PermissionRequest>(),
            Arg.Any<CancellationToken>());
        await web.DidNotReceive().OpenAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllowedWebRequestDelegatesToLauncher()
    {
        var permissions = AllowAll();
        var web = Substitute.For<IWebLauncher>();
        web.OpenAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(new AutomationResult(true, "opened"));
        var service = CreateService(permissions: permissions, web: web);

        var result = await service.ExecuteAsync(
            new Intent(IntentKind.OpenWeb, "https://example.com/a"));

        Assert.True(result.Succeeded);
        await permissions.Received(1).RequestAsync(
            Arg.Is<PermissionRequest>(request => request.Kind == PermissionKind.OpenWeb),
            Arg.Any<CancellationToken>());
        await web.Received(1).OpenAsync(
            Arg.Is<Uri>(uri => uri.AbsoluteUri == "https://example.com/a"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllowedAppLaunchResolvesAliasAndDelegates()
    {
        var permissions = AllowAll();
        var target = new AppTarget("Notepad", @"C:\Windows\System32\notepad.exe", "notepad");
        var aliases = new StubAppAliasResolver();
        aliases.Add("notepad", target);
        var apps = Substitute.For<IAppLauncher>();
        apps.LaunchAsync(Arg.Any<AppTarget>(), Arg.Any<CancellationToken>())
            .Returns(new AutomationResult(true, "launched"));
        var service = CreateService(
            permissions: permissions,
            aliases: aliases,
            apps: apps);

        var result = await service.ExecuteAsync(new Intent(IntentKind.LaunchApp, "notepad"));

        Assert.True(result.Succeeded);
        await permissions.Received(1).RequestAsync(
            Arg.Is<PermissionRequest>(request => request.Kind == PermissionKind.LaunchApplication),
            Arg.Any<CancellationToken>());
        await apps.Received(1).LaunchAsync(
            Arg.Is<AppTarget>(app => app == target),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownAliasFailsWithoutRequestingPermission()
    {
        var permissions = Substitute.For<IPermissionService>();
        var aliases = new StubAppAliasResolver();
        var service = CreateService(permissions: permissions, aliases: aliases);

        var result = await service.ExecuteAsync(new Intent(IntentKind.LaunchApp, "unknown"));

        Assert.False(result.Succeeded);
        await permissions.DidNotReceive().RequestAsync(
            Arg.Any<PermissionRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllowedMediaRequestDelegatesToMediaControl()
    {
        var permissions = AllowAll();
        var media = Substitute.For<IMediaControl>();
        media.ExecuteAsync(Arg.Any<MediaCommand>(), Arg.Any<CancellationToken>())
            .Returns(new AutomationResult(true, "sent"));
        var service = CreateService(permissions: permissions, media: media);

        var result = await service.ExecuteAsync(
            new Intent(IntentKind.MediaControl, Media: MediaCommand.Next));

        Assert.True(result.Succeeded);
        await permissions.Received(1).RequestAsync(
            Arg.Is<PermissionRequest>(request => request.Kind == PermissionKind.MediaControl),
            Arg.Any<CancellationToken>());
        await media.Received(1).ExecuteAsync(
            MediaCommand.Next,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingMediaCommandFailsWithoutPermissionRequest()
    {
        var permissions = Substitute.For<IPermissionService>();
        var service = CreateService(permissions: permissions);

        var result = await service.ExecuteAsync(new Intent(IntentKind.MediaControl));

        Assert.False(result.Succeeded);
        await permissions.DidNotReceive().RequestAsync(
            Arg.Any<PermissionRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonAutomationIntentFails()
    {
        var service = CreateService();

        var result = await service.ExecuteAsync(new Intent(IntentKind.Chat, "hello"));

        Assert.False(result.Succeeded);
        Assert.Contains("not an automation action", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NullIntentReturnsFailureInsteadOfThrowing()
    {
        var service = CreateService();

        var result = await service.ExecuteAsync(null!);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task LauncherExceptionIsConvertedToFailure()
    {
        var permissions = AllowAll();
        var web = Substitute.For<IWebLauncher>();
        web.OpenAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        var service = CreateService(permissions: permissions, web: web);

        var result = await service.ExecuteAsync(
            new Intent(IntentKind.OpenWeb, "https://example.com"));

        Assert.False(result.Succeeded);
        Assert.Contains("unexpectedly", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationIsConvertedToFailure()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var service = CreateService(permissions: permissions);

        var result = await service.ExecuteAsync(
            new Intent(IntentKind.OpenWeb, "https://example.com"));

        Assert.False(result.Succeeded);
        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IPermissionService AllowAll()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PermissionDecision(true));
        return permissions;
    }

    private static AutomationService CreateService(
        IPermissionService? permissions = null,
        IWebLauncher? web = null,
        IAppAliasResolver? aliases = null,
        IAppLauncher? apps = null,
        IMediaControl? media = null)
    {
        return new AutomationService(
            permissions ?? Substitute.For<IPermissionService>(),
            web ?? Substitute.For<IWebLauncher>(),
            aliases ?? new StubAppAliasResolver(),
            apps ?? Substitute.For<IAppLauncher>(),
            media ?? Substitute.For<IMediaControl>());
    }
}
