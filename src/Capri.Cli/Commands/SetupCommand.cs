using Capri.Cli.Services;

namespace Capri.Cli.Commands;

/// <summary>
/// Guided first-run setup wizard entry point. Supports repair/diagnostics mode.
/// </summary>
public sealed class SetupCommand
{
    private readonly ICliConsole _console;
    private readonly ICliServices _services;
    private readonly IEndpointProbe _endpointProbe;

    public SetupCommand(ICliConsole console, ICliServices services, IEndpointProbe endpointProbe)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _endpointProbe = endpointProbe ?? throw new ArgumentNullException(nameof(endpointProbe));
    }

    /// <summary>Runs the setup wizard.</summary>
    public Task<int> ExecuteAsync(bool repair, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var wizard = new SetupWizard(_console, _services, _endpointProbe, repair);
        return wizard.RunAsync(cancellationToken);
    }
}
