using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal sealed class UIThread : IUIThread
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private bool _run;

    private UIThreadActivator UIThreadActivator { get; }
    private DispatcherQueue DispatcherQueue { get; }
    private WindowManager WindowManager { get; }

    internal UIThread(
        ILoggerFactory loggerFactory,
        IUIThreadFactory.OnUnhandledException? onUnhandledException = default
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<UIThread>();

        UIThreadActivator = new(_loggerFactory);
        DispatcherQueue = new(_loggerFactory, UIThreadActivator, onUnhandledException);
        WindowManager = new(_loggerFactory, UIThreadActivator, DispatcherQueue);
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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
            DisposeAsyncCore().AsTask().GetAwaiter().GetResult();
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);

        Dispose(false);

        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsyncCore()
    {
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync start", Environment.CurrentManagedThreadId);

        await CancelAsync();

        WindowManager.Dispose();
        DispatcherQueue.Dispose();

        _cts.Dispose();

        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
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
            try
            {
                //ループの終了を待つ
                _logger.LogWithLine(LogLevel.Trace, "await task start", Environment.CurrentManagedThreadId);
                await _tcs.Task;
                _logger.LogWithLine(LogLevel.Trace, "await task end", Environment.CurrentManagedThreadId);
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "message loop error", Environment.CurrentManagedThreadId);
            }
        }
        else
        {
            _logger.LogWithLine(LogLevel.Trace, "none task", Environment.CurrentManagedThreadId);
        }
    }

    public async ValueTask<TResult> DispatchAsync<TResult>(Func<IWindowManager, TResult> func)
    {
        return await DispatcherQueue.DispatchAsync(()=> func(WindowManager));
    }

    internal void RunMessageLoop(
        TaskCompletionSource<IUIThread> startTcs,
        CancellationToken ct = default
    )
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
        var linkedCt = linkedCts.Token;

        using var waitHandlesPin = new PinnedBuffer<nint[]>([
            DispatcherQueue.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
            linkedCt.WaitHandle.SafeWaitHandle.DangerousGetHandle()
        ]);
        var handleCount = (uint)waitHandlesPin.Target.Length;

        Setup();

        _logger.LogWithLine(LogLevel.Information, "start message loop", Environment.CurrentManagedThreadId);

        try
        {
            using var active = UIThreadActivator.Activate();
            _run = true;

            startTcs.SetResult(new UIThreadOperator(_loggerFactory, this));

            RunMessageLoopMain(
                waitHandlesPin,
                linkedCt
            );
        }
        catch (Exception e)
        {
            _tcs.SetException(e);
        }
        finally
        {
            _tcs.TrySetResult();
        }

        _logger.LogWithLine(LogLevel.Information, "end message loop.", Environment.CurrentManagedThreadId);
    }

    private void RunMessageLoopMain(
        PinnedBuffer<nint[]> waitHandlesPin,
        CancellationToken ct    
    )
    {
        var forceCancel = false;
        var cancelled = false;
        var handleCount = (uint)waitHandlesPin.Target.Length;

        while (true)
        {
            if (forceCancel)
            {
                _logger.LogWithLine(LogLevel.Information, "force canceled.", Environment.CurrentManagedThreadId);
                break;
            }

            if (cancelled)
            {
                if (WindowManager.IsEmpty)
                {
                    _logger.LogWithLine(LogLevel.Information, "all closed.", Environment.CurrentManagedThreadId);
                    break;
                }
            }
            else if (ct.IsCancellationRequested)
            {
                cancelled = true;

                _logger.LogWithLine(LogLevel.Information, "canceled.", Environment.CurrentManagedThreadId);
                WindowManager.CloseAll();

                // 10秒以内にクローズされなければ、ループを終わらせる
                //TODO 設定にする
                var _ =
                    Task.Delay(10000, CancellationToken.None)
                    .ContinueWith(
                        (task) => { forceCancel = true; },
                        TaskScheduler.Default
                    );
            }

            {
                _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx", Environment.CurrentManagedThreadId);
                var res =
                    User32.MsgWaitForMultipleObjectsEx(
                        handleCount,
                        waitHandlesPin.AddrOfPinnedObject,
                        1000,
                        0x04FF, //QS_ALLINPUT
                        0x0004 //MWMO_INPUTAVAILABLE
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
                    WindowManager.WindowProcedure.DispatchMessage();
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    //タスクキューのディスパッチ
                    //TODO RTWQなどに置き換えできる？
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx comes queue event.", Environment.CurrentManagedThreadId);
                    DispatcherQueue.DispatchQueue();
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

    private void Setup()
    {
        WindowDebug.CheckIntegrityLevel(_loggerFactory);
        WindowDebug.CheckDesktop(_loggerFactory);
        WindowDebug.CheckGetProcessInformation(_loggerFactory);

        {
            var result = User32.IsGUIThread(true);
            if (!result)
            {
                var error = new Win32Exception();
                throw error;
            }
        }

        { //メッセージキューが無ければ作られるハズ
            var result =
                User32.GetQueueStatus(
                    0x04FF //QS_ALLINPUT
                );
            var error = new Win32Exception();
            _logger.LogWithError(LogLevel.Information, $"GetQueueStatus {result}", error.ToString(), Environment.CurrentManagedThreadId);
        }

        {
            var si = new Kernel32.STARTUPINFOW()
            {
                cb = Marshal.SizeOf<Kernel32.STARTUPINFOW>()
            };
            Kernel32.GetStartupInfoW(ref si);
            var error = new Win32Exception();
            _logger.LogWithError(LogLevel.Information, $"GetStartupInfoW [dwFlags:{si.dwFlags}][wShowWindow:{si.wShowWindow}]", error.ToString(), Environment.CurrentManagedThreadId);
        }
    }

}
