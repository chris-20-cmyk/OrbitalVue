using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace StreamVue.Player.Services;

public sealed class WindowsWakeTimer : IDisposable
{
    private const uint TimerAllAccess = 0x001F0003;
    private readonly EventWaitHandle? _waitHandle;
    private RegisteredWaitHandle? _registeredWait;
    private bool _disposed;

    public WindowsWakeTimer()
    {
        if (!OperatingSystem.IsWindows()) return;
        var nativeHandle = CreateWaitableTimerEx(IntPtr.Zero, null, 0, TimerAllAccess);
        if (nativeHandle == IntPtr.Zero || nativeHandle == new IntPtr(-1)) return;
        _waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset)
        {
            SafeWaitHandle = new SafeWaitHandle(nativeHandle, ownsHandle: true)
        };
    }

    public DateTimeOffset? NextWakeUtc { get; private set; }
    public bool IsAvailable => _waitHandle is not null;
    public event EventHandler? Triggered;

    public bool Schedule(DateTimeOffset? wakeUtc, bool resumeSystem)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Cancel();
        if (wakeUtc is null || _waitHandle is null) return false;

        var due = wakeUtc.Value.ToUniversalTime();
        if (due <= DateTimeOffset.UtcNow.AddSeconds(1)) due = DateTimeOffset.UtcNow.AddSeconds(1);
        var fileTime = due.UtcDateTime.ToFileTimeUtc();
        if (!SetWaitableTimer(_waitHandle.SafeWaitHandle, ref fileTime, 0, IntPtr.Zero, IntPtr.Zero, resumeSystem))
            return false;

        NextWakeUtc = due;
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _waitHandle,
            (_, _) => Triggered?.Invoke(this, EventArgs.Empty),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);
        return true;
    }

    public void Cancel()
    {
        _registeredWait?.Unregister(null);
        _registeredWait = null;
        if (_waitHandle is not null) CancelWaitableTimer(_waitHandle.SafeWaitHandle);
        NextWakeUtc = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
        _waitHandle?.Dispose();
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWaitableTimerEx(
        IntPtr timerAttributes,
        string? timerName,
        uint flags,
        uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(
        SafeWaitHandle timer,
        ref long dueTime,
        int periodMilliseconds,
        IntPtr completionRoutine,
        IntPtr completionArgument,
        [MarshalAs(UnmanagedType.Bool)] bool resumeSystem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelWaitableTimer(SafeWaitHandle timer);
}
