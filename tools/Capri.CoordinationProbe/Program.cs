using System.Diagnostics;
using Capri.Core.Autonomy;
using Capri.Security;
using Capri.Security.Autonomy;

if (args.Length != 2) return 2;
var control = new FileTaskControl(new CapriPaths(args[1]));
try
{
    switch (args[0])
    {
        case "claim":
            var claim = new TaskOwnership(Guid.NewGuid(), Environment.ProcessId,
                Process.GetCurrentProcess().StartTime.ToUniversalTime(), Environment.MachineName);
            if (!control.TryAcquire(claim)) return 3;
            Console.WriteLine(claim.TaskId);
            Console.Out.Flush();
            Console.ReadLine(); // parent decides release vs process kill
            control.Release(claim.TaskId);
            return 0;
        case "stop":
            Console.WriteLine(control.RequestStop().Generation);
            return 0;
        case "reset":
            Console.WriteLine(control.ResetStop().Generation);
            return 0;
        default: return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.GetType().Name);
    return 4;
}
