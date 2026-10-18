using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;

internal interface IUIThreadActivator : IDisposable
{
    bool IsActivatedThread { get; }
    void ThrowIfCalledFromOtherThread();
    void ThrowIfNoActive();
    uint NativeThreadId { get; }
    IDisposable Activate();

    delegate void InactivatedEventHandler();
    event InactivatedEventHandler OnInactivated;
}

internal partial class UIThreadActivator(
    ILoggerFactory loggerFactory
    ) : IUIThreadActivator
{
    internal enum LifecycleState
    {
        Created,
        Active,
        Stopping,
        Stopped
    }

    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    private readonly ILogger<UIThreadActivator> _logger = loggerFactory.CreateLogger<UIThreadActivator>();

    private bool _disposed;

    private int _lifecycleState = (int)LifecycleState.Created;

    public LifecycleState LifecycleStateVolatile => (LifecycleState)Volatile.Read(ref _lifecycleState);

    internal int _uiThreadId;

    public bool IsActivatedThread => (_uiThreadId == Environment.CurrentManagedThreadId);

    public event IUIThreadActivator.InactivatedEventHandler? OnInactivated;

    public uint NativeThreadId
    {
        get; private set;
    }

    ~UIThreadActivator()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref _disposed, true, false) == true)
        {
            _logger.LogWithLine(LogLevel.Information, "already disposed", Environment.CurrentManagedThreadId);
            return;
        }

        if (disposing)
        {
            OnInactivated = null;
        }
    }

    private bool LifecycleStateCompareExchange(LifecycleState value, LifecycleState comparand)
    {
        return Interlocked.CompareExchange(ref _lifecycleState, (int)value, (int)comparand) != (int)comparand;
    }

    public IDisposable Activate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (LifecycleStateCompareExchange(LifecycleState.Active, LifecycleState.Created))
        {
            throw new InvalidOperationException("already activated.");
        }

        return new Token(this);
    }

    public void ThrowIfCalledFromOtherThread()
    {
        if (!IsActivatedThread)
        {
            throw new InvalidOperationException($"called from invalid thread id [activate:{_uiThreadId:X}] [current:{Environment.CurrentManagedThreadId:X}]");
        }
    }

    public void ThrowIfNoActive()
    {
        if (LifecycleStateVolatile != LifecycleState.Active)
        {
            throw new InvalidOperationException($"no active [{LifecycleStateVolatile}]");
        }
    }

    private void Stopping()
    {
        Interlocked.Exchange(ref _lifecycleState, (int)LifecycleState.Stopping);
    }

    private void Stopped()
    {
        Interlocked.Exchange(ref _lifecycleState, (int)LifecycleState.Stopped);
    }


    internal partial class Token : IDisposable
    {
        private readonly ILogger _logger;
        private readonly UIThreadActivator _activator;
        private bool _disposed;

        internal Token(
            UIThreadActivator activator
        )
        {
            _activator = activator;
            _logger = _activator._loggerFactory.CreateLogger<Token>();

            var threadId = Environment.CurrentManagedThreadId;

            if (0 != Interlocked.CompareExchange(ref _activator._uiThreadId, threadId, 0))
            {
                throw new InvalidOperationException($"already activated [uiThreadId:{_activator._uiThreadId}][now:{Environment.CurrentManagedThreadId}]");
            }
            _activator.NativeThreadId = Kernel32.GetCurrentThreadId();

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogWithLine(LogLevel.Trace, $"Activate [uiThreadId:{_activator._uiThreadId}]", Environment.CurrentManagedThreadId);
            }
        }

        ~Token()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogWithLine(LogLevel.Trace, $"Inactivate [uiThreadId:{_activator._uiThreadId}]", Environment.CurrentManagedThreadId);
                }
                _activator.Stopping();
                _activator.OnInactivated?.Invoke();

                _activator._uiThreadId = 0;
                _activator.NativeThreadId = 0;
                _activator.Stopped();
            }

            _disposed = true;
        }
    }
}
