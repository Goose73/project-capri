using Capri.Cli;
using Capri.Cli.Services;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task HelpReturnsSuccess()
    {
        using var fixture = new ApplicationFixture();

        var exitCode = await fixture.Application.RunAsync(CliParser.Parse(["help"]));

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Usage:", fixture.Console.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void AssemblyNameIsCapri()
    {
        Assert.Equal("capri", typeof(Capri.Cli.AssemblyMarker).Assembly.GetName().Name);
    }

    [Fact]
    public async Task UnknownCommandReturnsUsageError()
    {
        using var fixture = new ApplicationFixture();

        var exitCode = await fixture.Application.RunAsync(CliParser.Parse(["not-a-command"]));

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("Unknown command", fixture.Console.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonInteractiveRepairReturnsUsageInsteadOfPretendingSuccess()
    {
        using var fixture = new ApplicationFixture();

        var exitCode = await fixture.Application.RunAsync(CliParser.Parse(["setup", "--repair"]));

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--repair", fixture.Console.Error, StringComparison.Ordinal);
        Assert.Contains("interactive", fixture.Console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoArgumentsInNonInteractiveTerminalShowsHelp()
    {
        using var fixture = new ApplicationFixture();

        var exitCode = await fixture.Application.RunAsync(CliParser.Parse(Array.Empty<string>()));

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("not interactive", fixture.Console.Out, StringComparison.Ordinal);
        Assert.Contains("Usage:", fixture.Console.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GuiAliasLaunchesDesktop()
    {
        using var fixture = new ApplicationFixture();

        var exitCode = await fixture.Application.RunAsync(CliParser.Parse(["gui"]));

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(1, fixture.Launcher.LaunchCount);
    }

    [Fact]
    public async Task FailedLaunchReturnsError()
    {
        using var fixture = new ApplicationFixture(
            launcher: new StubDesktopLauncher(new DesktopLaunchResult(false, "desktop not found")));

        var exitCode = await fixture.Application.RunAsync(CliParser.Parse(["start"]));

        Assert.Equal(CliExitCodes.Error, exitCode);
        Assert.Contains("desktop not found", fixture.Console.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VersionReturnsSuccess()
    {
        using var fixture = new ApplicationFixture();

        var exitCode = await fixture.Application.RunAsync(CliParser.Parse(["--version"]));

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("0.1.0", fixture.Console.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var fixture = new ApplicationFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Application.RunAsync(CliParser.Parse(["status"]), cancellation.Token));
    }

    [Fact]
    public async Task ConfigGetRoutesThroughApplication()
    {
        using var fixture = new ApplicationFixture();

        var exitCode = await fixture.Application.RunAsync(
            CliParser.Parse(["config", "get", "provider.model"]));

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal($"gpt-4o-mini{Environment.NewLine}", fixture.Console.Out);
    }

    private sealed class ApplicationFixture : IDisposable
    {
        private readonly TempDirectory _temp;
        private readonly StubCliServices _services;

        public ApplicationFixture(
            StubDesktopLauncher? launcher = null,
            bool probeReachable = true,
            bool dpapiAvailable = true)
        {
            _temp = new TempDirectory();
            _services = StubCliServices.CreateInTemp(_temp.Path, CapriDefaultSettings.Create());
            Console = new FakeCliConsole(interactive: false);
            Launcher = launcher ?? new StubDesktopLauncher();
            Application = new CliApplication(
                Console,
                _services,
                new StubEndpointProbe(probeReachable),
                Launcher,
                new StubDpapiProbe(dpapiAvailable));
        }

        public FakeCliConsole Console { get; }

        public StubDesktopLauncher Launcher { get; }

        public CliApplication Application { get; }

        public void Dispose()
        {
            _services.Dispose();
            _temp.Dispose();
        }
    }
}
