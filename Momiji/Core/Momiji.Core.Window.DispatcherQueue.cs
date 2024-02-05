using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Momiji.Internal.Log;

namespace Momiji.Core.Window;

internal class DispatcherQueue
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _queueEvent = new();

    private readonly DispatcherQueueSynchronizationContext _dispatcherQueueSynchronizationContext;

    public SafeWaitHandle SafeWaitHandle => _queueEvent.WaitHandle.SafeWaitHandle;

    internal DispatcherQueue(
        ILoggerFactory loggerFactory
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<DispatcherQueue>();
        _dispatcherQueueSynchronizationContext = new(_loggerFactory, this);
    }

    internal void Dispatch(Action item)
    {
        _queue.Enqueue(item);
        _queueEvent.Set();
    }

    internal void DispatchQueue()
    {
        //TODO UIスレッドで呼び出す必要アリ
        _logger.LogWithLine(LogLevel.Trace, $"Queue count [{_queue.Count}]", Environment.CurrentManagedThreadId);
        _queueEvent.Reset();

        while (_queue.TryDequeue(out var result))
        {
            _logger.LogWithLine(LogLevel.Trace, "Invoke start", Environment.CurrentManagedThreadId);

            //TODO ここで実行コンテキスト？

            var oldContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(_dispatcherQueueSynchronizationContext);
                result.Invoke();
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

            _dispatcherQueue.Dispatch(() => { d(state); });
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            _logger.LogWithLine(LogLevel.Trace, "Post", Environment.CurrentManagedThreadId);

            _dispatcherQueue.Dispatch(() => { d(state); });
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