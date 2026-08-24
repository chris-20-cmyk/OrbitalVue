namespace StreamVue.Player.Services;

public sealed class StreamVueSingleInstance : IDisposable
{
    private const string DefaultScope = "StreamVue.Native";
    private readonly Mutex _mutex;
    private readonly string _activationEventName;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;

    public StreamVueSingleInstance(bool waitForPreviousInstance, string? scope = null)
    {
        scope = string.IsNullOrWhiteSpace(scope) ? DefaultScope : scope.Trim();
        _activationEventName = $@"Local\{scope}.Activate";
        _mutex = new Mutex(false, $@"Local\{scope}.SingleInstance");
        try
        {
            _ownsMutex = _mutex.WaitOne(waitForPreviousInstance ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

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
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _ownsMutex = false;
        }
        _mutex.Dispose();
    }
}
