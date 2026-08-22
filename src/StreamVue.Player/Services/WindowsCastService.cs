using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StreamVue.Player.Services;

public sealed class WindowsCastService
{
    public const string NearbyDisplayShortcut = "Windows + K";
    public const string DisplaySettingsUri = "ms-settings:display";

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VirtualKeyLeftWindows = 0x5B;
    private const ushort VirtualKeyK = 0x4B;

    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10);

    public void OpenNearbyDisplayPicker()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Wireless display casting requires Windows 10 or Windows 11.");

        var inputs = new[]
        {
            CreateKeyboardInput(VirtualKeyLeftWindows, keyUp: false),
            CreateKeyboardInput(VirtualKeyK, keyUp: false),
            CreateKeyboardInput(VirtualKeyK, keyUp: true),
            CreateKeyboardInput(VirtualKeyLeftWindows, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not open the nearby display picker.");
    }

    public void OpenDisplaySettings()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Display settings are unavailable on this version of Windows.");

        Process.Start(new ProcessStartInfo(DisplaySettingsUri) { UseShellExecute = true });
    }

    private static Input CreateKeyboardInput(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = keyUp ? KeyEventKeyUp : 0
            }
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}
