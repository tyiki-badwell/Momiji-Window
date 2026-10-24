using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

internal interface IDispatcherQueueTaskExecutor : IDisposable
{
    void DispatchQueue();
}

internal sealed partial class DispatcherQueueTaskExecutor : IDispatcherQueueTaskExecutor
{
    private readonly ILogger<DispatcherQueueTaskExecutor> _logger;
    private bool _disposed;

    private readonly IUIThreadActivator _uiThreadActivator;
    private readonly IDispatcherQueue _dispatcherQueue;

    private readonly IDispatcherQueueSynchronizationContext _dispatcherQueueSynchronizationContext;
    private readonly IOnUnhandledExceptionHandler _onUnhandledExceptionHandler;

    public DispatcherQueueTaskExecutor(
        ILogger<DispatcherQueueTaskExecutor> logger,
        IUIThreadActivator uiThreadActivator,
        IOnUnhandledExceptionHandler onUnhandledExceptionHandler,
        IDispatcherQueue dispatcherQueue,
        IDispatcherQueueSynchronizationContext dispatcherQueueSynchronizationContext
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiThreadActivator);
        ArgumentNullException.ThrowIfNull(onUnhandledExceptionHandler);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(dispatcherQueueSynchronizationContext);

        _logger = logger;

        _uiThreadActivator = uiThreadActivator;
        _uiThreadActivator.OnInactivated += OnInactivated;

        _onUnhandledExceptionHandler = onUnhandledExceptionHandler;

        _dispatcherQueue = dispatcherQueue;
        _dispatcherQueueSynchronizationContext = dispatcherQueueSynchronizationContext;
    }

    ~DispatcherQueueTaskExecutor()
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
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
            _uiThreadActivator.OnInactivated -= OnInactivated;
        }
    }

    public void DispatchQueue()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //UIスレッドで呼び出す必要アリ
        _uiThreadActivator.ThrowIfCalledFromOtherThread();

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithLine(LogLevel.Trace, $"Queue count [{_dispatcherQueue.Count}]", Environment.CurrentManagedThreadId);
        }

        while (_dispatcherQueue.TryDequeue(out var item))
        {
            _logger.LogWithLine(LogLevel.Trace, "Invoke start", Environment.CurrentManagedThreadId);

            //TODO ここで実行コンテキスト？

            using var switchContext = new DispatcherQueueSynchronizationContextScope(_dispatcherQueueSynchronizationContext, _logger);
            try
            {
                if (item is Action action)
                {
                    action();
                }
                else if (item is (SendOrPostCallback d, object state))
                {
                    d(state);
                }
                else
                {
                    throw new ArgumentException($"unknown queue item type [{item.GetType()}]");
                }
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "Invoke error", Environment.CurrentManagedThreadId);

                var handled = _onUnhandledExceptionHandler.DoUnhandledException(e);

                if (handled)
                {
                    _logger.LogWithLine(LogLevel.Trace, "handled", Environment.CurrentManagedThreadId);
                    continue;
                }

                //ループを終了させる
                //TODO 全部やりきらせないとダメかも？
                _logger.LogWithLine(LogLevel.Trace, "loop end", Environment.CurrentManagedThreadId);
                throw;
            }
            _logger.LogWithLine(LogLevel.Trace, "Invoke end", Environment.CurrentManagedThreadId);
        }
    }
    private void OnInactivated()
    {
        _logger.LogWithLine(LogLevel.Trace, "OnInactivated", Environment.CurrentManagedThreadId);
        DispatchQueue();
    }

}
