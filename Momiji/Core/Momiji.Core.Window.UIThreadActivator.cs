using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;

internal interface IUIThreadChecker
{
    bool IsActivatedThread
    {
        get;
    }
    void ThrowIfCalledFromOtherThread();
    void ThrowIfNoActive();
    uint NativeThreadId { get; }

    delegate void InactivatedEventHandler();

    event InactivatedEventHandler OnInactivated;
}

internal class UIThreadActivator : IUIThreadChecker
{
    private readonly ILoggerFactory _loggerFactory;
    internal int _uiThreadId;

    public event IUIThreadChecker.InactivatedEventHandler? OnInactivated;

    public uint NativeThreadId
    {
        get; private set;
    }

    internal UIThreadActivator(
        ILoggerFactory loggerFactory
    )
    {
        _loggerFactory = loggerFactory;
    }

    internal IDisposable Activate()
    {
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

    internal class Token : IDisposable
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

            _logger.LogWithLine(LogLevel.Trace, $"DispatcherQueue Activate [uiThreadId:{_activator._uiThreadId}]", Environment.CurrentManagedThreadId);
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
                _logger.LogWithLine(LogLevel.Trace, $"DispatcherQueue Disactivate [uiThreadId:{_activator._uiThreadId}]", Environment.CurrentManagedThreadId);
                _activator.OnInactivated?.Invoke();

                _activator._uiThreadId = 0;
                _activator.NativeThreadId = 0;
            }

            _disposed = true;
        }
    }
}
