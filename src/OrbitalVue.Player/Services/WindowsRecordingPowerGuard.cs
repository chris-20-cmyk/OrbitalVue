using System.Runtime.InteropServices;

namespace OrbitalVue.Player.Services;

public sealed class WindowsRecordingPowerGuard : IDisposable
{
    private bool _active;

    public void SetActive(bool active)
    {
        if (_active == active || !OperatingSystem.IsWindows()) return;
        _active = active;
        SetThreadExecutionState(active
            ? ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.AwayModeRequired
            : ExecutionState.Continuous);
    }

    public void Dispose() => SetActive(false);

    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        AwayModeRequired = 0x00000040,
        Continuous = 0x80000000
    }

    [DllImport("kernel32.dll")]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState executionState);
}
