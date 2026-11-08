using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Capri.Tests.TestDoubles;

/// <summary>
/// Creates and removes an isolated temporary directory for a test.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Capri.Tests",
            Guid.NewGuid().ToString("N"));

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new UnauthorizedAccessException("Current user identity unavailable.");
        var security = new DirectorySecurity();
        security.SetOwner(sid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-18"), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-544"), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));

        // The root fixture itself is protected and current-user-owned even when the
        // test process is elevated; descendants inherit the intended access rules.
        new DirectoryInfo(Path).Create(security);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; the OS will eventually remove temp data.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
