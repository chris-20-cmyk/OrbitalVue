namespace OrbitalVue.Player.Services;

public sealed class OrbitalVueSingleInstance : IDisposable
{
    private const string DefaultScope = "OrbitalVue.Native";
    private readonly Semaphore _instanceGate;
    private readonly string _activationEventName;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;

    public OrbitalVueSingleInstance(bool waitForPreviousInstance, string? scope = null)
    {
        scope = string.IsNullOrWhiteSpace(scope) ? DefaultScope : scope.Trim();
        _activationEventName = $@"Local\{scope}.Activate";
        _instanceGate = new Semaphore(1, 1, $@"Local\{scope}.SingleInstance");
        _ownsMutex = _instanceGate.WaitOne(waitForPreviousInstance ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);

        if (!_ownsMutex) return;
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _activationEventName);
        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => ActivationRequested?.Invoke(this, EventArgs.Empty),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public bool IsPrimary => _ownsMutex;
    public event EventHandler? ActivationRequested;

    public void SignalPrimary()
    {
        if (_ownsMutex) return;
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(_activationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public void Dispose()
    {
        _activationWait?.Unregister(null);
        _activationWait = null;
        _activationEvent?.Dispose();
        _activationEvent = null;
        if (_ownsMutex)
        {
            try { _instanceGate.Release(); }
            catch (SemaphoreFullException) { }
            _ownsMutex = false;
        }
        _instanceGate.Dispose();
    }
}
