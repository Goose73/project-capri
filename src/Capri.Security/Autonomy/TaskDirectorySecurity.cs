using System.Security.AccessControl;
using System.Security.Principal;

namespace Capri.Security.Autonomy;

/// <summary>Creates task storage directories securely and fails closed for existing unsafe paths.</summary>
internal static class TaskDirectorySecurity
{
    public static void EnsurePrivateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User ?? throw new UnauthorizedAccessException("Current user identity unavailable.");
            var security = new DirectorySecurity();
            security.SetOwner(sid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-18"), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-544"), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));

            // Create with the intended owner and DACL; never create permissively and
            // repair security after another process could access the directory.
            try { new DirectoryInfo(path).Create(security); }
            catch (IOException) when (Directory.Exists(path)) { }
        }

        ValidateDirectory(path);
    }

    private static void ValidateDirectory(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null)
        {
            if (new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Task coordination refuses reparse paths.");
            current = Directory.GetParent(current)?.FullName;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new UnauthorizedAccessException("Current user identity unavailable.");
        var security = new DirectoryInfo(path).GetAccessControl();
        if (!sid.Equals(security.GetOwner(typeof(SecurityIdentifier))))
            throw new UnauthorizedAccessException("Task coordination directory is not owned by the current user.");
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow &&
                !sid.Equals(rule.IdentityReference) && !rule.IdentityReference.Value.Equals("S-1-5-18", StringComparison.Ordinal) &&
                !rule.IdentityReference.Value.Equals("S-1-5-32-544", StringComparison.Ordinal) &&
                (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.FullControl | FileSystemRights.Delete)) != 0)
                throw new UnauthorizedAccessException("Task coordination directory is writable by another principal.");
        }
    }
}
