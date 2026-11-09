namespace Capri.Hub;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var lifetime = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            lifetime.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            return await HubCommands.RunAsync(
                args, Environment.GetEnvironmentVariable,
                new PingPcProbe(), new UdpWakeSender(),
                Console.Out, Console.Error, lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Capri Hub cancelled.");
            return 130;
        }
        catch (System.Net.Sockets.SocketException)
        {
            Console.Error.WriteLine("Capri Hub could not send the packet or reach the network.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }
}
