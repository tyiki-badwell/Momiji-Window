using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

internal interface IDispatcherQueue : IDisposable
{
    void Enqueue(SendOrPostCallback callback, object? param);
    void Enqueue(Action action);
    delegate void EnqueueHandler();
    event EnqueueHandler OnEnqueue;

    bool TryDequeue([MaybeNullWhen(false)] out object item);

    ValueTask<TResult> EnqueueAsync<TResult>(Func<TResult> func);
    int Count { get; }
}

internal sealed partial class DispatcherQueue : IDispatcherQueue
{
    private bool _disposed;
    private readonly ILogger<DispatcherQueue> _logger;
    public event IDispatcherQueue.EnqueueHandler? OnEnqueue;
    public int Count => _queue.Count;

    //TODO RTWQにする
    private readonly ConcurrentQueue<object> _queue = new();

    private readonly IUIThreadActivator _uiThreadActivator;

    public DispatcherQueue(
        ILogger<DispatcherQueue> logger,
        IUIThreadActivator uiThreadActivator
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiThreadActivator);

        _logger = logger;

        _uiThreadActivator = uiThreadActivator;
        _uiThreadActivator.OnInactivated += OnInactivated;
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
        if (Interlocked.CompareExchange(ref _disposed, true, false) == true)
        {
            _logger.LogWithLine(LogLevel.Information, "already disposed", Environment.CurrentManagedThreadId);
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
            _uiThreadActivator.OnInactivated -= OnInactivated;

            //TODO 終了する前にQueueにタスクがある場合の掃除
            if (!_queue.IsEmpty)
            {
                _logger.LogWithLine(LogLevel.Warning, $"queue left count {_queue.Count}", Environment.CurrentManagedThreadId);
            }
        }
    }

    private void OnInactivated()
    {
        _logger.LogWithLine(LogLevel.Trace, "OnInactivated", Environment.CurrentManagedThreadId);
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithLine(LogLevel.Trace, $"left [{Count}]", Environment.CurrentManagedThreadId);
        }
    }

    public async ValueTask<TResult> EnqueueAsync<TResult>(Func<TResult> func)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //TODO 拡張メソッドでよさそう

        _logger.LogWithLine(LogLevel.Trace, "EnqueueAsync", Environment.CurrentManagedThreadId);
        if (_uiThreadActivator.IsActivatedThread)
        {
            _logger.LogWithLine(LogLevel.Trace, "EnqueueAsync called from same thread id then immidiate mode", Environment.CurrentManagedThreadId);
            try
            {
                return func();
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "EnqueueAsync error", Environment.CurrentManagedThreadId);
                throw;
            }
        }

        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        Enqueue(() =>
        {
            _logger.LogWithLine(LogLevel.Trace, "EnqueueAsync called from other thread id then async mode", Environment.CurrentManagedThreadId);
            try
            {
                //TODO キャンセルできるようにする？
                tcs.SetResult(func());
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "EnqueueAsync error", Environment.CurrentManagedThreadId);
                tcs.SetException(e);
            }
        });

        return await tcs.Task;
    }

    public void Enqueue(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _uiThreadActivator.ThrowIfNoActive();

        if (_uiThreadActivator.IsActivatedThread)
        {
            //TODO 同一スレッドからならQueueに入れないようにする？
            _logger.LogWithLine(LogLevel.Trace, "★EnqueueAsync called from same thread id.", Environment.CurrentManagedThreadId);
        }

        _logger.LogWithLine(LogLevel.Trace, "Enqueue Action", Environment.CurrentManagedThreadId);
        _queue.Enqueue(action);
        OnEnqueue?.Invoke();
    }

    public void Enqueue(SendOrPostCallback callback, object? param)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _uiThreadActivator.ThrowIfNoActive();

        if (_uiThreadActivator.IsActivatedThread)
        {
            //TODO 同一スレッドからならQueueに入れないようにする？
            _logger.LogWithLine(LogLevel.Trace, "★EnqueueAsync called from same thread id.", Environment.CurrentManagedThreadId);
        }

        _logger.LogWithLine(LogLevel.Trace, "Enqueue SendOrPostCallback", Environment.CurrentManagedThreadId);
        _queue.Enqueue((callback, param));
        OnEnqueue?.Invoke();
    }

    public bool TryDequeue([MaybeNullWhen(false)] out object item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _queue.TryDequeue(out item);
    }
}
