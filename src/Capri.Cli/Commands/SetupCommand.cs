namespace Capri.Cli.Commands;

/// <summary>
/// Honest guided-setup placeholder. The wizard is a Milestone 2 deliverable;
/// this command must not pretend otherwise.
/// </summary>
public static class SetupCommand
{
    /// <summary>Prints the current setup guidance and returns success.</summary>
    public static int Execute(bool repair, ICliConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        console.WriteLine("Guided setup wizard: Milestone 2 (not yet complete).");
        console.WriteLine("Use `capri config` or desktop Settings.");
        console.WriteLine(string.Empty);

        if (repair)
        {
            console.WriteLine("Repair mode is also planned for Milestone 2 and is not implemented.");
            console.WriteLine("Run `capri doctor` to diagnose the current installation.");
        }
        else
        {
            console.WriteLine("For now:");
            console.WriteLine("  - `capri status` shows the active provider, model, and key presence.");
            console.WriteLine("  - `capri doctor` checks settings, local endpoints, and DPAPI.");
            console.WriteLine("  - `capri config list` shows safe settings; `capri config set` changes them.");
            console.WriteLine("  - Enter API keys only in the desktop Settings window.");
        }

        console.WriteLine(string.Empty);
        console.WriteLine("No changes were made.");
        return CliExitCodes.Success;
    }
}
