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
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private bool _disposed;

    internal int _uiThreadId;

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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            OnInactivated = null;
        }

        _disposed = true;
    }

    public IDisposable Activate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new Token(this);
    }

    public bool IsActivatedThread => (_uiThreadId == Environment.CurrentManagedThreadId);

    public void ThrowIfCalledFromOtherThread()
    {
        if (!IsActivatedThread)
        {
            throw new InvalidOperationException($"called from invalid thread id [activate:{_uiThreadId:X}] [current:{Environment.CurrentManagedThreadId:X}]");
        }
    }

    public void ThrowIfNoActive()
    {
        if (_uiThreadId == 0)
        {
            throw new InvalidOperationException($"no active");
        }
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
                _activator.OnInactivated?.Invoke();

                _activator._uiThreadId = 0;
                _activator.NativeThreadId = 0;
            }

            _disposed = true;
        }
    }
}
