using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Capri.Automation.Media;

/// <summary>
/// Sends media keys through the Win32 <c>SendInput</c> API.
/// </summary>
public sealed class WindowsMediaKeySender : IMediaKeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Input injection failures must be surfaced as an automation result.")]
    public bool TrySend(int virtualKey, out string? failureReason)
    {
        failureReason = null;

        if (virtualKey is < 0 or > 0xFF)
        {
            failureReason = "The virtual-key code is out of range.";
            return false;
        }

        try
        {
            var inputs = new[]
            {
                CreateKeyboardInput((ushort)virtualKey, KeyEventExtendedKey),
                CreateKeyboardInput((ushort)virtualKey, KeyEventExtendedKey | KeyEventKeyUp),
            };

            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent == inputs.Length)
            {
                return true;
            }

            var win32Error = Marshal.GetLastWin32Error();
            failureReason = win32Error != 0
                ? new Win32Exception(win32Error).Message
                : "SendInput did not accept the media key.";
            return false;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or PlatformNotSupportedException
                or DllNotFoundException
                or EntryPointNotFoundException)
        {
            failureReason = exception.Message;
            return false;
        }
    }

    private static INPUT CreateKeyboardInput(ushort virtualKey, uint flags)
    {
        return new INPUT
        {
            type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero,
                },
            },
        };
    }

    // DllImport is used deliberately: the array of explicit-layout structs is
    // simpler to review as hand-written marshalling than a generated stub.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;

        [FieldOffset(0)]
        public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}
