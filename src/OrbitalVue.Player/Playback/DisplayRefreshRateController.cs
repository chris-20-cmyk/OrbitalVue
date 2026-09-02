using System.Runtime.InteropServices;

namespace OrbitalVue.Player.Playback;

public sealed class DisplayRefreshRateController : IDisposable
{
    private const int EnumCurrentSettings = -1;
    private const uint MonitorDefaultToNearest = 2;
    private const uint DmDisplayFrequency = 0x00400000;
    private const uint CdsFullscreen = 0x00000004;
    private const int DispChangeSuccessful = 0;

    private readonly nint _windowHandle;
    private string? _deviceName;
    private DevMode? _originalMode;
    private double _lastRequestedFps;
    private bool _changed;

    public DisplayRefreshRateController(nint windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public string Status { get; private set; } = "Display default";

    public bool TryMatch(double framesPerSecond)
    {
        if (framesPerSecond is < 20 or > 120) return false;
        if (Math.Abs(_lastRequestedFps - framesPerSecond) < 0.01) return _changed;
        _lastRequestedFps = framesPerSecond;

        if (!TryGetDeviceName(out var deviceName))
        {
            Status = "Display unavailable";
            return false;
        }

        var current = CreateMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref current))
        {
            Status = "Display unavailable";
            return false;
        }

        _deviceName ??= deviceName;
        _originalMode ??= current;

        var candidates = new List<DevMode>();
        for (var index = 0; ; index++)
        {
            var candidate = CreateMode();
            if (!EnumDisplaySettings(deviceName, index, ref candidate)) break;
            if (candidate.DmPelsWidth != current.DmPelsWidth ||
                candidate.DmPelsHeight != current.DmPelsHeight ||
                candidate.DmBitsPerPel != current.DmBitsPerPel ||
                candidate.DmDisplayFrequency is < 23 or > 240) continue;
            candidates.Add(candidate);
        }

        var preferredRate = SelectBestRefreshRate(
            framesPerSecond,
            (int)current.DmDisplayFrequency,
            candidates.Select(candidate => (int)candidate.DmDisplayFrequency));
        var bestMode = candidates.FirstOrDefault(candidate => candidate.DmDisplayFrequency == preferredRate);

        if (preferredRate == 0)
        {
            Status = $"No match for {framesPerSecond:0.##} fps";
            return false;
        }

        var best = bestMode;
        if (best.DmDisplayFrequency == current.DmDisplayFrequency)
        {
            Status = $"{current.DmDisplayFrequency} Hz matched";
            return true;
        }

        best.DmFields = DmDisplayFrequency;
        if (ChangeDisplaySettingsEx(deviceName, ref best, nint.Zero, CdsFullscreen, nint.Zero) != DispChangeSuccessful)
        {
            Status = "Refresh match rejected";
            return false;
        }

        _changed = true;
        Status = $"{best.DmDisplayFrequency} Hz matched";
        return true;
    }

    public static int SelectBestRefreshRate(double framesPerSecond, int currentRate, IEnumerable<int> availableRates)
    {
        if (framesPerSecond is < 20 or > 120) return 0;
        var bestRate = 0;
        var bestScore = double.MaxValue;

        foreach (var rate in availableRates.Distinct())
        {
            if (rate is < 23 or > 240) continue;
            var multiple = rate / framesPerSecond;
            var nearestMultiple = Math.Clamp(Math.Round(multiple), 1, 5);
            var cadenceError = Math.Abs(multiple - nearestMultiple);
            if (cadenceError > 0.04) continue;

            var score = cadenceError * 100d + Math.Abs(rate - currentRate) * 0.002d;
            if (score >= bestScore) continue;
            bestScore = score;
            bestRate = rate;
        }

        return bestRate;
    }

    public void Restore()
    {
        if (!_changed || _originalMode is null || string.IsNullOrWhiteSpace(_deviceName))
        {
            Status = "Display default";
            _lastRequestedFps = 0;
            return;
        }

        var mode = _originalMode.Value;
        mode.DmFields = DmDisplayFrequency;
        ChangeDisplaySettingsEx(_deviceName, ref mode, nint.Zero, CdsFullscreen, nint.Zero);
        _changed = false;
        _lastRequestedFps = 0;
        Status = "Display restored";
    }

    public void Dispose()
    {
        Restore();
        GC.SuppressFinalize(this);
    }

    private bool TryGetDeviceName(out string deviceName)
    {
        var monitor = MonitorFromWindow(_windowHandle, MonitorDefaultToNearest);
        var info = new MonitorInfoEx { CbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (monitor != nint.Zero && GetMonitorInfo(monitor, ref info))
        {
            deviceName = info.DeviceName;
            return !string.IsNullOrWhiteSpace(deviceName);
        }

        deviceName = string.Empty;
        return false;
    }

    private static DevMode CreateMode() => new() { DmSize = (short)Marshal.SizeOf<DevMode>() };

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref DevMode mode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string deviceName, ref DevMode mode, nint window, uint flags, nint parameter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int CbSize;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)] public string DmDeviceName;
        public short DmSpecVersion;
        public short DmDriverVersion;
        public short DmSize;
        public short DmDriverExtra;
        public uint DmFields;
        public int DmPositionX;
        public int DmPositionY;
        public uint DmDisplayOrientation;
        public uint DmDisplayFixedOutput;
        public short DmColor;
        public short DmDuplex;
        public short DmYResolution;
        public short DmTtOption;
        public short DmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)] public string DmFormName;
        public short DmLogPixels;
        public uint DmBitsPerPel;
        public uint DmPelsWidth;
        public uint DmPelsHeight;
        public uint DmDisplayFlags;
        public uint DmDisplayFrequency;
        public uint DmIcmMethod;
        public uint DmIcmIntent;
        public uint DmMediaType;
        public uint DmDitherType;
        public uint DmReserved1;
        public uint DmReserved2;
        public uint DmPanningWidth;
        public uint DmPanningHeight;
    }
}
