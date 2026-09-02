using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OrbitalVue.Player.Playback;

public sealed class FullscreenWindowController
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static readonly nint HwndTopmost = new(-1);
    private static readonly nint HwndNotTopmost = new(-2);

    private WindowSnapshot? _snapshot;

    public bool IsFullscreen => _snapshot is not null;

    public FullscreenDisplayBounds? ActiveDisplay { get; private set; }

    public void Enter(Window window)
    {
        if (_snapshot is not null) return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
            throw new InvalidOperationException("The OrbitalVue window is not ready for fullscreen yet.");

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitorInfo = MonitorInfo.Create();
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not identify the active display.");

        var restoreBounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        _snapshot = new WindowSnapshot(window.WindowState, restoreBounds, window.ResizeMode, window.Topmost);
        ActiveDisplay = FullscreenDisplayBounds.FromMonitorRectangle(
            monitorInfo.Monitor.Left,
            monitorInfo.Monitor.Top,
            monitorInfo.Monitor.Right,
            monitorInfo.Monitor.Bottom);

        window.WindowState = WindowState.Normal;
        window.ResizeMode = ResizeMode.NoResize;
        window.Topmost = true;

        var bounds = ActiveDisplay.Value;
        if (!SetWindowPos(
                handle,
                HwndTopmost,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                SwpFrameChanged | SwpShowWindow | SwpNoOwnerZOrder))
        {
            var error = Marshal.GetLastWin32Error();
            var snapshot = _snapshot;
            _snapshot = null;
            ActiveDisplay = null;
            RestoreWindow(window, snapshot);
            throw new Win32Exception(error, "Windows could not enter fullscreen mode.");
        }
    }

    public void SetActive(Window window, bool active)
    {
        if (_snapshot is null) return;
        window.Topmost = active;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return;
        SetWindowPos(
            handle,
            active ? HwndTopmost : HwndNotTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    public void Exit(Window window)
    {
        if (_snapshot is not { } snapshot) return;
        _snapshot = null;
        ActiveDisplay = null;

        RestoreWindow(window, snapshot);
    }

    public static FullscreenDisplayBounds GetWindowBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero || !GetWindowRect(handle, out var rectangle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read the OrbitalVue window bounds.");
        return FullscreenDisplayBounds.FromMonitorRectangle(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
    }

    private static void RestoreWindow(Window window, WindowSnapshot snapshot)
    {
        window.Topmost = false;
        window.WindowState = WindowState.Normal;
        window.ResizeMode = snapshot.ResizeMode;

        if (!snapshot.RestoreBounds.IsEmpty)
        {
            window.Left = snapshot.RestoreBounds.Left;
            window.Top = snapshot.RestoreBounds.Top;
            window.Width = snapshot.RestoreBounds.Width;
            window.Height = snapshot.RestoreBounds.Height;
        }

        window.Topmost = snapshot.Topmost;
        window.WindowState = snapshot.WindowState == WindowState.Minimized
            ? WindowState.Normal
            : snapshot.WindowState;
    }

    private sealed record WindowSnapshot(
        WindowState WindowState,
        Rect RestoreBounds,
        ResizeMode ResizeMode,
        bool Topmost);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;

        public static MonitorInfo Create() => new() { Size = Marshal.SizeOf<MonitorInfo>() };
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRectangle rectangle);
}

public readonly record struct FullscreenDisplayBounds(int Left, int Top, int Width, int Height)
{
    public static FullscreenDisplayBounds FromMonitorRectangle(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
}
