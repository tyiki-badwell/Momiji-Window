﻿using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;
using Momiji.Internal.Util;

namespace Momiji.Core.Window;

internal interface IDispatcherQueue : IDisposable
{
    void Dispatch(SendOrPostCallback callback, object? param);
    ValueTask<TResult> DispatchAsync<TResult>(Func<TResult> func);
    WaitHandle WaitHandle { get; }
    void DispatchQueue();

    event IUIThreadFactory.OnUnhandledExceptionHandler OnUnhandledException;
}

internal sealed partial class DispatcherQueue : IDispatcherQueue
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    //TODO RTWQにする
    private readonly ConcurrentQueue<object> _queue = new();
    private readonly ManualResetEventSlim _queueEvent = new();

    private DispatcherQueueSynchronizationContext DispatcherQueueSynchronizationContext { get; }
    private IUIThreadChecker UIThreadChecker { get; }

    public WaitHandle WaitHandle => _queueEvent.WaitHandle;

    public event IUIThreadFactory.OnUnhandledExceptionHandler? OnUnhandledException;

    internal DispatcherQueue(
        ILoggerFactory loggerFactory,
        IUIThreadChecker uiThreadChecker
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<DispatcherQueue>();
        UIThreadChecker = uiThreadChecker;
        UIThreadChecker.OnInactivated += OnInactivated;

        DispatcherQueueSynchronizationContext = new(_loggerFactory, this);
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

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
            UIThreadChecker.OnInactivated -= OnInactivated;

            //TODO 終了する前にQueueにタスクがある場合の掃除
            if (!_queue.IsEmpty)
            {
                _logger.LogWithLine(LogLevel.Warning, $"queue left count {_queue.Count}", Environment.CurrentManagedThreadId);
            }

            _queueEvent.Dispose();
        }

        _disposed = true;
    }

    private void OnInactivated()
    {
        _logger.LogWithLine(LogLevel.Trace, "OnInactivated", Environment.CurrentManagedThreadId);
        DispatchQueue();
    }

    public async ValueTask<TResult> DispatchAsync<TResult>(Func<TResult> func)
    {
        _logger.LogWithLine(LogLevel.Trace, "DispatchAsync", Environment.CurrentManagedThreadId);

        if (UIThreadChecker.IsActivatedThread)
        {
            _logger.LogWithLine(LogLevel.Trace, "Dispatch called from same thread id then immidiate mode", Environment.CurrentManagedThreadId);
            return func();
        }

        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatch(() =>
        {
            _logger.LogWithLine(LogLevel.Trace, "Dispatch called from other thread id then async mode", Environment.CurrentManagedThreadId);
            try
            {
                //TODO キャンセルできるようにする？
                tcs.SetResult(func());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        return await tcs.Task;
    }

    internal void Dispatch(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UIThreadChecker.ThrowIfNoActive();

        _logger.LogWithLine(LogLevel.Trace, "Dispatch Action", Environment.CurrentManagedThreadId);
        _queue.Enqueue(action);
        _queueEvent.Set();
    }

    public void Dispatch(SendOrPostCallback callback, object? param)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UIThreadChecker.ThrowIfNoActive();

        _logger.LogWithLine(LogLevel.Trace, "Dispatch SendOrPostCallback", Environment.CurrentManagedThreadId);
        _queue.Enqueue((callback, param));
        _queueEvent.Set();
    }

    public void DispatchQueue()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //UIスレッドで呼び出す必要アリ
        UIThreadChecker.ThrowIfCalledFromOtherThread();

        _logger.LogWithLine(LogLevel.Trace, $"Queue count [{_queue.Count}]", Environment.CurrentManagedThreadId);
        _queueEvent.Reset();

        while (_queue.TryDequeue(out var item))
        {
            _logger.LogWithLine(LogLevel.Trace, "Invoke start", Environment.CurrentManagedThreadId);

            //TODO ここで実行コンテキスト？

            using var switchContext = new SwitchSynchronizationContextRAII(DispatcherQueueSynchronizationContext, _logger);
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

                var handled = OnUnhandledException?.Invoke(e);

                if (!(handled.HasValue && handled.Value))
                {
                    //ループを終了させる
                    //TODO 全部やりきらせないとダメかも？
                    _logger.LogWithLine(LogLevel.Trace, "loop end", Environment.CurrentManagedThreadId);
                    throw;
                }
            }
            _logger.LogWithLine(LogLevel.Trace, "Invoke end", Environment.CurrentManagedThreadId);
        }
    }
}

//TODO 実験中
internal sealed class DispatcherQueueSynchronizationContext : SynchronizationContext
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
