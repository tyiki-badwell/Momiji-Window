using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;

namespace Momiji.Core.Window;

public class DispatcherQueue : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly int _uiThreadId;

    //TODO RTWQにする
    private readonly ConcurrentQueue<object> _queue = new();
    private readonly ManualResetEventSlim _queueEvent = new();

    private readonly DispatcherQueueSynchronizationContext _dispatcherQueueSynchronizationContext;

    public WaitHandle WaitHandle => _queueEvent.WaitHandle;

    private readonly IWindowManager.OnUnhandledException? _onUnhandledException;

    public DispatcherQueue(
        ILoggerFactory loggerFactory,
        IWindowManager.OnUnhandledException? onUnhandledException = default
    )
    {
        _uiThreadId = Environment.CurrentManagedThreadId;
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<DispatcherQueue>();
        _dispatcherQueueSynchronizationContext = new(_loggerFactory, this);
        _onUnhandledException = onUnhandledException;

        _logger.LogWithLine(LogLevel.Trace, $"DispatcherQueue [uiThreadId:{_uiThreadId}]", Environment.CurrentManagedThreadId);
    }

    ~DispatcherQueue()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
            _queueEvent.Dispose();
        }

        _disposed = true;
    }

    public void Dispatch(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogWithLine(LogLevel.Trace, "Dispatch Action", Environment.CurrentManagedThreadId);
        _queue.Enqueue(action);
        _queueEvent.Set();
    }

    public void Dispatch(SendOrPostCallback callback, object? param)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogWithLine(LogLevel.Trace, "Dispatch SendOrPostCallback", Environment.CurrentManagedThreadId);
        _queue.Enqueue((callback, param));
        _queueEvent.Set();
    }

    private void ThrowIfCalledFromOtherThread()
    {
        if (_uiThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException($"called from invalid thread id [construct:{_uiThreadId:X}] [current:{Environment.CurrentManagedThreadId:X}]");
        }
    }

    public void DispatchQueue()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //UIスレッドで呼び出す必要アリ
        ThrowIfCalledFromOtherThread();

        _logger.LogWithLine(LogLevel.Trace, $"Queue count [{_queue.Count}]", Environment.CurrentManagedThreadId);
        _queueEvent.Reset();

        while (_queue.TryDequeue(out var item))
        {
            _logger.LogWithLine(LogLevel.Trace, "Invoke start", Environment.CurrentManagedThreadId);

            //TODO ここで実行コンテキスト？

            var oldContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(_dispatcherQueueSynchronizationContext);

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

                var handled = _onUnhandledException?.Invoke(e);

                if (!(handled.HasValue && handled.Value))
                {
                    //ループを終了させる
                    _logger.LogWithLine(LogLevel.Trace, "loop end", Environment.CurrentManagedThreadId);
                    throw;
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }
            _logger.LogWithLine(LogLevel.Trace, "Invoke end", Environment.CurrentManagedThreadId);
        }
    }

    //TODO 実験中
    private class DispatcherQueueSynchronizationContext : SynchronizationContext
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;

        private readonly DispatcherQueue _dispatcherQueue;

        internal DispatcherQueueSynchronizationContext(
            ILoggerFactory loggerFactory,
            DispatcherQueue dispatcherQueue
        )
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<DispatcherQueueSynchronizationContext>();
            _dispatcherQueue = dispatcherQueue;
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            _logger.LogWithLine(LogLevel.Trace, "Send", Environment.CurrentManagedThreadId);
            throw new NotSupportedException("Send");
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            _logger.LogWithLine(LogLevel.Trace, "Post", Environment.CurrentManagedThreadId);
            _dispatcherQueue.Dispatch(d, state);
        }

        public override SynchronizationContext CreateCopy()
        {
            _logger.LogWithLine(LogLevel.Trace, "CreateCopy", Environment.CurrentManagedThreadId);
            return this;
        }

        public override void OperationStarted()
        {
            _logger.LogWithLine(LogLevel.Trace, "OperationStarted", Environment.CurrentManagedThreadId);
        }

        public override void OperationCompleted()
        {
            _logger.LogWithLine(LogLevel.Trace, "OperationCompleted", Environment.CurrentManagedThreadId);
        }
    }
}