using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

public class WindowManager : IWindowManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private bool _disposed;

    private readonly CancellationTokenSource _processCancel;
    private readonly Task _processTask;
    private int _uiThreadId;

    private DispatcherQueue? _dispatcherQueue;
    private WindowClassManager? _windowClassManager;

    public WindowManager(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        TaskCompletionSource startTcs,
        IWindowManager.OnStop? onStop = default,
        IWindowManager.OnUnhandledException? onUnhandledException = default
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowManager>();

        _configuration = configuration;

        _processCancel = new CancellationTokenSource();

        _processTask = Start(
            startTcs, 
            onStop, 
            onUnhandledException
        );
    }

    ~WindowManager()
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

    protected async virtual ValueTask DisposeAsyncCore()
    {
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync start", Environment.CurrentManagedThreadId);
        try
        {
            await CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            //_desktop?.Close();
            //_windowStation?.Close();

            _processCancel.Dispose();
        }
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    private async Task CancelAsync()
    {
        if (_processCancel.IsCancellationRequested)
        {
            _logger.LogWithLine(LogLevel.Information, "already cancelled.", Environment.CurrentManagedThreadId);
        }
        else
        {
            _logger.LogWithLine(LogLevel.Information, "cancel.", Environment.CurrentManagedThreadId);
            _processCancel.Cancel();
        }

        try
        {
            await _processTask;
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "failed.", Environment.CurrentManagedThreadId);
        }
    }

    public async ValueTask<TResult> DispatchAsync<TResult>(Func<TResult> func)
    {
        _logger.LogWithLine(LogLevel.Trace, "DispatchAsync", Environment.CurrentManagedThreadId);

        //TODO 即時実行か遅延実行かの判断はDispatcherQueueに移した方がよいか？
        if (_uiThreadId == Environment.CurrentManagedThreadId)
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

    private void Dispatch(Action action)
    {
        CheckRunning();
        _dispatcherQueue!.Dispatch(action);
    }

    private void CheckRunning()
    {
        _logger.LogWithLine(LogLevel.Information, $"CheckRunning {_processTask.Status}", Environment.CurrentManagedThreadId);

        if (!(_processTask.Status == TaskStatus.WaitingForActivation) || (_processTask.Status == TaskStatus.Running))
        {
            throw new InvalidOperationException("message loop is not exists.");
        }
    }

    public IWindow CreateWindow(
        string windowTitle,
        IWindow? parent,
        string className,
        IWindowManager.OnMessage? onMessage,
        IWindowManager.OnMessage? onMessageAfter
    )
    {
        CheckRunning();

        var windowClassManager = _windowClassManager!;
        var windowClass = windowClassManager.QueryWindowClass(className, 0);

        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                windowClassManager,
                windowClass,
                onMessage,
                onMessageAfter
            );

        window.CreateWindow(
            windowTitle, 
            (parent as NativeWindow)
        );

        return window;
    }

    private Task<Task> Start(
        TaskCompletionSource startTcs,
        IWindowManager.OnStop? onStop = default,
        IWindowManager.OnUnhandledException? onUnhandledException = default
    )
    {
        return Run(
            startTcs,
            onUnhandledException
        ).ContinueWith(async (task) => {
            _logger.LogWithLine(LogLevel.Information, $"process task end {task.Status} {_processTask?.Status}", Environment.CurrentManagedThreadId);

            //TODO エラー時のみキャンセルすればよいハズ？
            await CancelAsync().ConfigureAwait(false);
            _logger.LogWithLine(LogLevel.Trace, "cancel end", Environment.CurrentManagedThreadId);

            if (!_processCancel.IsCancellationRequested)
            {
                _logger.LogWithLine(LogLevel.Warning, "キャンセル済になってない", Environment.CurrentManagedThreadId);
            }

            try
            {
                await task;
                onStop?.Invoke(default);
            }
            catch (Exception e)
            {
                //_processTaskのエラー状態を伝播する
                onStop?.Invoke(e);
            }
            finally
            {
                _logger.LogWithLine(LogLevel.Information, "stopped.", Environment.CurrentManagedThreadId);
            }
        });
    }

    private Task Run(
        TaskCompletionSource startTcs,
        IWindowManager.OnUnhandledException? onUnhandledException = default
    )
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            _logger.LogWithLine(LogLevel.Information, "*** thread start ***", Environment.CurrentManagedThreadId);

            try
            {
                using var dispatcherQueue = new DispatcherQueue(_loggerFactory, onUnhandledException);
                _dispatcherQueue = dispatcherQueue;

                using var windowClassManager = new WindowClassManager(_configuration, _loggerFactory, _dispatcherQueue);
                _windowClassManager = windowClassManager;

                SetupMessageLoopThread();

                startTcs.SetResult();

                RunMessageLoop(dispatcherQueue, windowClassManager);
                _logger.LogWithLine(LogLevel.Information, "message loop normal end.", Environment.CurrentManagedThreadId);
                tcs.SetResult();

                //TODO DispatcherQueueをdisposeした後でSendOrPostCallbackが呼ばれる場合がある. 待つ方法？
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "message loop abnormal end.", Environment.CurrentManagedThreadId);

                startTcs.TrySetException(e);
                tcs.SetException(e);
            }
            finally
            {
                _dispatcherQueue = default;
                _windowClassManager = default;
            }

            _logger.LogWithLine(LogLevel.Information, "*** thread end ***", Environment.CurrentManagedThreadId);
            _uiThreadId = default;
        })
        {
            IsBackground = false,
            Name = "Momiji UI Thread"
        };

        //TODO MTAも指定可能にしてみる？
        thread.SetApartmentState(ApartmentState.STA);
        _logger.LogWithLine(LogLevel.Information, $"GetApartmentState {thread.GetApartmentState()}", Environment.CurrentManagedThreadId);
        _uiThreadId = thread.ManagedThreadId;
        thread.Start();

        //このタスクでcontinue withすると、UIスレッドでQueue登録してスレッド終了し、QueueのCOMアクセスが失敗する
        return tcs.Task;
    }

    private void SetupMessageLoopThread()
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
            _logger.LogWithError(LogLevel.Information, $"GetStartupInfoW [{si.dwFlags}][{si.wShowWindow}]", error.ToString(), Environment.CurrentManagedThreadId);
        }
    }

    private void RunMessageLoop(
        DispatcherQueue dispatcherQueue,
        WindowClassManager windowClassManager
    )
    {
        var forceCancel = false;
        var cancelled = false;

        var ct = _processCancel.Token;

        using var waitHandlesPin = new PinnedBuffer<nint[]>([
            dispatcherQueue.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
            ct.WaitHandle.SafeWaitHandle.DangerousGetHandle()
        ]);
        var handleCount = waitHandlesPin.Target.Length;

        _logger.LogWithLine(LogLevel.Information, "start message loop", Environment.CurrentManagedThreadId);
        while (true)
        {
            if (forceCancel)
            {
                _logger.LogWithLine(LogLevel.Information, "force canceled.", Environment.CurrentManagedThreadId);
                break;
            }

            if (cancelled)
            {
                if (windowClassManager.IsEmpty)
                {
                    _logger.LogWithLine(LogLevel.Information, "all closed.", Environment.CurrentManagedThreadId);
                    break;
                }
            }
            else if (ct.IsCancellationRequested)
            {
                cancelled = true;

                _logger.LogWithLine(LogLevel.Information, "canceled.", Environment.CurrentManagedThreadId);
                windowClassManager.CloseAll();

                // 10秒以内にクローズされなければ、ループを終わらせる
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
                        (uint)handleCount,
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
                    windowClassManager.DispatchMessage();
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    //タスクキューのディスパッチ
                    //TODO RTWQなどに置き換えできる？
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx comes queue event.", Environment.CurrentManagedThreadId);
                    dispatcherQueue.DispatchQueue();
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
                    var error = new Win32Exception();
                    throw error;
                }
            }
        }
        _logger.LogWithLine(LogLevel.Information, "end message loop.", Environment.CurrentManagedThreadId);
    }

}