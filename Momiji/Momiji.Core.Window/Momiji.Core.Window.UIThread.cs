using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

internal interface IUIThreadInternal : IUIThread, IDisposable, IAsyncDisposable
{
    void RunMessageLoop(
        TaskCompletionSource<IUIThread> startTcs,
        CancellationToken ct = default
    );
}

internal sealed partial class UIThread : IUIThreadInternal
{
    private readonly ILogger<UIThread> _logger;
    private bool _disposed;

    public event IUIThread.InactivatedEventHandler? OnInactivated;

    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();


    private bool _run;

    private readonly IUIThreadActivator _uiThreadActivator;
    private readonly IDispatcherQueue _dispatcherQueue;
    private readonly IDispatcherQueueTaskExecutor _dispatcherQueueTaskExecutor;
    private readonly IDispatcherQueueSynchronizationContext _dispatcherQueueSynchronizationContext;
    private readonly IWindowManager _windowManager;
    public IWindowManager WindowManager => _windowManager;

    private readonly IWindowProcedure _windowProcedure;
    private readonly IWindowFunction _windowFunction;
    private readonly IWindowMap _windowMap;
    private readonly IOnUnhandledExceptionHandler _onUnhandledExceptionHandler;

    public UIThread(
        ILogger<UIThread> logger,
        IUIThreadActivator uiThreadActivator,
        IDispatcherQueue dispatcherQueue,
        IDispatcherQueueTaskExecutor dispatcherQueueTaskExecutor,
        IDispatcherQueueSynchronizationContext dispatcherQueueSynchronizationContext,
        IWindowManager windowManager,
        IWindowProcedure windowProcedure,
        IWindowFunction windowFunction,
        IWindowMap windowMap,
        IOnUnhandledExceptionHandler onUnhandledExceptionHandler
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiThreadActivator);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(dispatcherQueueTaskExecutor);
        ArgumentNullException.ThrowIfNull(dispatcherQueueSynchronizationContext);
        ArgumentNullException.ThrowIfNull(windowManager);
        ArgumentNullException.ThrowIfNull(windowProcedure);
        ArgumentNullException.ThrowIfNull(windowFunction);
        ArgumentNullException.ThrowIfNull(windowMap);
        ArgumentNullException.ThrowIfNull(onUnhandledExceptionHandler);

        _logger = logger;

        _uiThreadActivator = uiThreadActivator;
        _uiThreadActivator.OnInactivated += OnInactivatedBridge;

        _dispatcherQueue = dispatcherQueue;
        _dispatcherQueueSynchronizationContext = dispatcherQueueSynchronizationContext;

        _dispatcherQueueTaskExecutor = dispatcherQueueTaskExecutor;
        //RTWorkQueueManager = new(configuration, _loggerFactory);
        _windowManager = windowManager;
        _windowProcedure = windowProcedure;
        _windowFunction = windowFunction;
        _windowMap = windowMap;

        _onUnhandledExceptionHandler = onUnhandledExceptionHandler;
    }

    ~UIThread()
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

            //TODO 仮処理
            _onUnhandledExceptionHandler.DoDisposeAsyncCoreSyncWait(DisposeAsyncCore(), _logger);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _onUnhandledExceptionHandler.DoDisposeAsyncCore(DisposeAsyncCore(), _logger);

        Dispose(false);

        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsyncCore()
    {
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync start", Environment.CurrentManagedThreadId);

        try
        {
            await CancelAsync();
        }
        finally
        {
            _uiThreadActivator.OnInactivated -= OnInactivatedBridge;
            _cts.Dispose();

            _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
        }

    }

    public async ValueTask CancelAsync()
    {
        if (!_cts.IsCancellationRequested)
        {
            _logger.LogWithLine(LogLevel.Trace, "cancel", Environment.CurrentManagedThreadId);
            _cts.Cancel();
        }
        else
        {
            _logger.LogWithLine(LogLevel.Trace, "already cancelled", Environment.CurrentManagedThreadId);
        }

        if (_run)
        {
            //ループの終了を待つ
            _logger.LogWithLine(LogLevel.Trace, "await task start", Environment.CurrentManagedThreadId);
            await _tcs.Task;
            _logger.LogWithLine(LogLevel.Trace, "await task end", Environment.CurrentManagedThreadId);
        }
        else
        {
            _logger.LogWithLine(LogLevel.Trace, "none task", Environment.CurrentManagedThreadId);
        }
    }

    private void OnInactivatedBridge()
    {
        _logger.LogWithLine(LogLevel.Trace, "OnInactivated", Environment.CurrentManagedThreadId);
        OnInactivated?.Invoke();
    }
    public async ValueTask<TResult> EnqueueAsync<TResult>(Func<TResult> func)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _dispatcherQueue.EnqueueAsync(func);
    }

    public void RunMessageLoop(
        TaskCompletionSource<IUIThread> startTcs,
        CancellationToken ct = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _windowFunction.Setup();

        _logger.LogWithLine(LogLevel.Information, "start message loop", Environment.CurrentManagedThreadId);

        try
        {
            using var active = _uiThreadActivator.Activate();
            _run = true;

            startTcs.SetResult(this);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
            var linkedCt = linkedCts.Token;

            RunMessageLoopMain(linkedCt);

            //TODO ここを抜けてactiveを破棄した後でSendOrPostCallbackが呼ばれる場合がある. 待つ方法？

        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "message loop error", Environment.CurrentManagedThreadId);
            _tcs.SetException(e);
        }
        finally
        {
            _logger.LogWithLine(LogLevel.Information, "end message loop.", Environment.CurrentManagedThreadId);
            _tcs.TrySetResult();
            _run = false;
        }
    }

    private void RunMessageLoopMain(
        CancellationToken ct
    )
    {
        using var waitHandlesPin = new PinnedBuffer<nint[]>([
            _dispatcherQueue.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
            ct.WaitHandle.SafeWaitHandle.DangerousGetHandle()
        ]);

        using var switchContext = new DispatcherQueueSynchronizationContextScope(_dispatcherQueueSynchronizationContext, _logger);

        var cancelled = false;
        var forceCancelled = false;
        var handleCount = (uint)waitHandlesPin.Target.Length;

        var closeTimeoutCounter = new Stopwatch();

        while (true)
        {
            if (cancelled)
            {
                if (_windowMap.IsEmpty)
                {
                    _logger.LogWithLine(LogLevel.Information, "all closed.", Environment.CurrentManagedThreadId);
                    break;
                }

                var elapsed = closeTimeoutCounter.Elapsed.TotalSeconds;
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogWithLine(LogLevel.Information, $"wait close. elapsed: {elapsed} sec", Environment.CurrentManagedThreadId);
                }

                // 10秒以内にクローズされなければ、ループを終わらせる
                //TODO 設定にする
                if(elapsed > 10)
                {
                    _logger.LogWithLine(LogLevel.Warning, "wait close timeout.", Environment.CurrentManagedThreadId);
                    break;
                }
                else if (!forceCancelled && elapsed > 5)
                {
                    _logger.LogWithLine(LogLevel.Warning, "forceCancel", Environment.CurrentManagedThreadId);
                    forceCancelled = true;
                    _windowMap.ForceClose();
                }
            }
            else if (ct.IsCancellationRequested)
            {
                cancelled = true;

                _logger.LogWithLine(LogLevel.Information, "canceled.", Environment.CurrentManagedThreadId);
                _windowMap.CloseAll();

                closeTimeoutCounter.Start();
            }

            {
                var res =
                    _windowFunction.MsgWaitForMultipleObjectsEx(
                        handleCount,
                        waitHandlesPin
                    );
                if (res == 258) // WAIT_TIMEOUT
                {
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx timeout.", Environment.CurrentManagedThreadId);
                    continue;
                }
                else if (res == handleCount) // WAIT_OBJECT_0+2
                {
                    //ウインドウメッセージキューのディスパッチ
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx comes window message.", Environment.CurrentManagedThreadId);
                    _windowProcedure.DispatchMessage();
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    //タスクキューのディスパッチ
                    //TODO RTWQなどに置き換えできる？
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx comes queue event.", Environment.CurrentManagedThreadId);
                    _dispatcherQueue.Reset();
                    _dispatcherQueueTaskExecutor.DispatchQueue();
                    continue;
                }
                else if (res == 1) // WAIT_OBJECT_0+1
                {
                    //キャンセル通知
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx comes cancel event.", Environment.CurrentManagedThreadId);
                    //ctがシグナル状態になりっぱなしになるので、リストから外す
                    handleCount--;
                    continue;
                }
                else
                {
                    //エラー
                    throw new Win32Exception($"MsgWaitForMultipleObjectsEx [result:{res}]");
                }
            }
        }
    }

}
