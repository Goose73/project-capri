using Capri.Cli;
using Capri.Cli.Services;

using var cancellation = new CancellationTokenSource();

void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
{
    if (cancellation.IsCancellationRequested)
    {
        // A second Ctrl+C lets the process terminate immediately.
        eventArgs.Cancel = false;
        return;
    }

    eventArgs.Cancel = true;
    cancellation.Cancel();
}

Console.CancelKeyPress += OnCancelKeyPress;

try
{
    var invocation = CliParser.Parse(args);
    using var services = CliServices.CreateDefault();
    var application = new CliApplication(
        new SystemCliConsole(),
        services,
        new HttpEndpointProbe(),
        DesktopLauncher.CreateDefault(),
        new DpapiAvailabilityProbe());

    return await application
        .RunAsync(invocation, cancellation.Token)
        .ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("capri: cancelled.");
    return CliExitCodes.Cancelled;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"capri: {CliErrors.Describe(exception)}");
    return CliExitCodes.Error;
}
finally
{
    Console.CancelKeyPress -= OnCancelKeyPress;
}
