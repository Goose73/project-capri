using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Capri.Tests")]

namespace Capri.Automation.Autonomy;

internal static class WindowObservation
{
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, [Out] char[] text, int maximum);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, [Out] char[] imageName, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(IntPtr process, ref uint count, [Out] char[]? familyName);

    internal static string PackageFamilyNameFromBuffer(char[] family, uint familyLength)
    {
        var length = (int)Math.Min(familyLength, (uint)family.Length);
        var terminator = Array.IndexOf(family, '\0', 0, length);
        return new string(family, 0, terminator >= 0 ? terminator : length);
    }

    internal static HashSet<IntPtr> SnapshotWindows(string noteName)
    {
        var matching = new HashSet<IntPtr>();
        var expectedTitle = noteName + " - Notepad";
        EnumWindows((window, _) =>
        {
            var title = new char[512];
            var titleLength = GetWindowText(window, title, title.Length);
            if (titleLength > 0 && string.Equals(new string(title, 0, titleLength), expectedTitle, StringComparison.OrdinalIgnoreCase)) matching.Add(window);
            return true;
        }, IntPtr.Zero);
        return matching;
    }

    internal static bool HasNewNotepadWindow(string executablePath, string noteName, HashSet<IntPtr> preDispatchWindows)
    {
        var expectedTitle = noteName + " - Notepad";
        var found = false;
        EnumWindows((window, _) =>
        {
            if (preDispatchWindows.Contains(window)) return true;
            var title = new char[512];
            var titleLength = GetWindowText(window, title, title.Length);
            if (titleLength == 0 || !string.Equals(new string(title, 0, titleLength), expectedTitle, StringComparison.OrdinalIgnoreCase)) return true;
            if (GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0) return true;
            try
            {
                using var owner = Process.GetProcessById((int)processId);
                var image = new char[1024];
                uint imageLength = (uint)image.Length;
                var processHandle = owner.SafeHandle.DangerousGetHandle();
                if (!QueryFullProcessImageName(processHandle, 0, image, ref imageLength)) return true;
                var ownerPath = Path.GetFullPath(new string(image, 0, (int)imageLength));
                var expectedPath = Path.GetFullPath(executablePath);
                var trustedImage = string.Equals(ownerPath, expectedPath, StringComparison.OrdinalIgnoreCase);
                var family = new char[256];
                uint familyLength = (uint)family.Length;
                var packageResult = GetPackageFamilyName(processHandle, ref familyLength, family);
                var trustedPackage = packageResult == 0 && string.Equals(PackageFamilyNameFromBuffer(family, familyLength), "Microsoft.WindowsNotepad_8wekyb3d8bbwe", StringComparison.Ordinal);
                if (trustedImage || trustedPackage)
                {
                    found = true;
                    return false;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Window/process may disappear while enumerating; it is not evidence.
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
