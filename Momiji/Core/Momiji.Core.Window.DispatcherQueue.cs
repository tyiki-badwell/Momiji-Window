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

    public SafeWaitHandle SafeWaitHandle => _queueEvent.WaitHandle.SafeWaitHandle;

    internal DispatcherQueue(
        ILoggerFactory loggerFactory
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<DispatcherQueue>();
    }

    internal void Dispatch(Action item)
    {
        _queue.Enqueue(item);
        _queueEvent.Set();
    }

    internal void DispatchQueue()
    {
        _logger.LogWithLine(LogLevel.Trace, $"Queue count [{_queue.Count}]", Environment.CurrentManagedThreadId);
        _queueEvent.Reset();

        while (_queue.TryDequeue(out var result))
        {
            _logger.LogWithLine(LogLevel.Trace, "Invoke", Environment.CurrentManagedThreadId);

            //TODO ここで同期コンテキスト？
            result.Invoke();
        }
    }
}