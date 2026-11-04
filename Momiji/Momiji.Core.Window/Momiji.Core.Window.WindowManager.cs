using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal interface IWindowManagerInternal : IWindowManager
{
    ValueTask<TResult> EnqueueAsync<TResult>(Func<IWindowInternal, TResult> item, IWindowInternal window);
    int GenerateChildId(IWindowInternal window);
    nuint GenerateSubclassId();
}

internal sealed partial class WindowManager : IWindowManagerInternal, IDisposable
{
    private bool _disposed;

    private readonly ILogger<WindowManager> _logger;
    private readonly IDispatcherQueue _dispatcherQueue;
    public IWindowProcedure WindowProcedure { get; }
    private readonly IDispatcherQueueSynchronizationContext _dispatcherQueueSynchronizationContext;
    private readonly IServiceProvider _serviceProvider; //TODO ActivatorUtilities.CreateInstance のほうがよいか？
    private readonly IWindowMap _windowMap;


    private readonly ConcurrentDictionary<int, WeakReference<IWindowInternal>> _childIdMap = [];
    private int childId;
    private uint subclassId;
    public WindowManager(
        ILogger<WindowManager> logger,
        IServiceProvider serviceProvider,
        IDispatcherQueue dispatcherQueue,
        IWindowMap windowMap,
        IWindowProcedure windowProcedure,
        IDispatcherQueueSynchronizationContext dispatcherQueueSynchronizationContext
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(windowMap);
        ArgumentNullException.ThrowIfNull(windowProcedure);
        ArgumentNullException.ThrowIfNull(dispatcherQueueSynchronizationContext);

        _logger = logger;

        _serviceProvider = serviceProvider;

        _dispatcherQueue = dispatcherQueue;
        _dispatcherQueueSynchronizationContext = dispatcherQueueSynchronizationContext;

        _windowMap = windowMap;

        WindowProcedure = windowProcedure;
        WindowProcedure.OnWindowMessage += OnWindowMessage;
        WindowProcedure.OnThreadMessage += OnThreadMessage;
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

            WindowProcedure.OnWindowMessage -= OnWindowMessage;
            WindowProcedure.OnThreadMessage -= OnThreadMessage;
        }
    }


    public int GenerateChildId(IWindowInternal window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = Interlocked.Increment(ref childId);
        _childIdMap.TryAdd(id, new(window));
        return id;
    }

    public nuint GenerateSubclassId()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = Interlocked.Increment(ref subclassId);
        return id;
    }

    public async ValueTask<IWindow> CreateWindowAsync(
        IWindowManager.CreateWindowParameter parameter
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var window = _serviceProvider.GetRequiredKeyedService<IWindowInternal>(NativeWindow.Type);
        var parameter_ = new NativeWindow.Parameter()
        {
            parameter = parameter
        };
        try
        {
            return await EnqueueAsync((window) =>
            {
                window.Initialize(parameter_);
                return window;
            }, window);
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "Initialize failed.", Environment.CurrentManagedThreadId);
            throw;
        }
    }

    public async ValueTask<TResult> EnqueueAsync<TResult>(Func<IWindowInternal, TResult> item, IWindowInternal window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogWithLine(LogLevel.Trace, "EnqueueAsync", Environment.CurrentManagedThreadId);

        TResult func()
        {
            return _windowMap.InvokeWithContext(item, window);
        }

        return await _dispatcherQueue.EnqueueAsync(func);
    }

    private void OnThreadMessage(IWindowManager.IMessage message)
    {
        _logger.LogWithMsg(LogLevel.Trace, "OnThreadMessage", User32.HWND.None, message, Environment.CurrentManagedThreadId);

        using var switchContext = new DispatcherQueueSynchronizationContextScope(_dispatcherQueueSynchronizationContext, _logger);

        //TODO スレッドメッセージ用の処理

    }

    private void OnWindowMessage(User32.HWND hwnd, IWindowManager.IMessage message)
    {
        _logger.LogWithMsg(LogLevel.Trace, "OnWindowMessage", hwnd, message, Environment.CurrentManagedThreadId);

        using var switchContext = new DispatcherQueueSynchronizationContextScope(_dispatcherQueueSynchronizationContext, _logger);

        try
        {
            WindowDebug.CheckDpiAwarenessContext(_logger, hwnd);

            WndProcBefore(hwnd, message);
            if (message.Handled)
            {
                return;
            }

            if (_windowMap.TryGetWindow(hwnd, out var window))
            {
                //ウインドウに流す
                window.OnMessage(message);

                WndProcAfter(window, message);
            }
            else
            {
                _logger.LogWithMsg(LogLevel.Trace, "unkown window handle", hwnd, message, Environment.CurrentManagedThreadId);
                CallOriginalWindowProc(hwnd, message);
            }
        }
        catch (Exception)
        {
            //msgによってreturnを分ける
            WndProcError(hwnd, message);
            throw;
        }
    }

    private void WndProcBefore(User32.HWND hwnd, IWindowManager.IMessage message)
    {
        switch (message.Msg)
        {
            case 0x0081://WM_NCCREATE
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCCREATE Before", hwnd, Environment.CurrentManagedThreadId);
                OnWM_NCCREATE_Before(hwnd);
                break;

            case 0x0210://WM_PARENTNOTIFY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_PARENTNOTIFY Before", hwnd, Environment.CurrentManagedThreadId);
                OnWM_PARENTNOTIFY_Before(hwnd, message.WParam, message.LParam);
                break;
        }
    }

    /*
    private void OnWM_COMMAND(User32.HWND hwnd, nint wParam, nint lParam)
    {
        //WParam 上位ワード　0：メニュー／1：アクセラレータ／その他：ボタン識別子　下位ワード　識別子
        //LParam ウインドウハンドル
        var wParamHi = (wParam & 0xFFFF0000) >> 16;
        var childId = (int)(wParam & 0xFFFF);
        var childHWnd = (User32.HWND)lParam;
        _logger.LogWithHWnd(LogLevel.Trace, $"WM_COMMAND [wParamHi:{wParamHi:X}][childId:{childId}] [childHWnd:{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);

        if (_childIdMap.TryGetValue(childId, out var window))
        {
            //管理下にあるコントロール

            if (_windowMap.TryGetValue(childHWnd, out var window2))
            {
                //既に管理対象のwindowだった
                _logger.LogWithHWnd(LogLevel.Trace, $"is managed window childHWnd:[{childHWnd}][{window2.Handle:X}]", hwnd, Environment.CurrentManagedThreadId);
            }
            else
            {
                //TODO windowに、HWNDのアサインとWndProcの差し替えを設ける

                //最速でHWNDを受け取る
                window._hWindow = childHWnd;
                if (_windowMap.TryAdd(childHWnd, window))
                {
                    _logger.LogWithHWnd(LogLevel.Information, "add window map", childHWnd, Environment.CurrentManagedThreadId);
                }
                else
                {
                    throw new WindowException($"failed add window map hwnd:[{childHWnd}]");
                }

                BindWndProc(hwnd, childHWnd);
            }
        }

    }
    */

    private void OnWM_PARENTNOTIFY_Before(User32.HWND hwnd, nint wParam, nint lParam)
    {
        //WParam 子のウインドウメッセージ
        //LParam 下位ワード　X座標／上位ワード　Y座標

        var msg = wParam & 0xFFFF;
        var childId = (wParam & 0xFFFF0000) >> 16;
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY [msg:{msg:X}][childId:{childId}] {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
        }

        switch (msg)
        {
            case 0x0001: //WM_CREATE
                {
                    if (_logger.IsEnabled(LogLevel.Trace))
                    {
                        _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY WM_CREATE {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
                    }
                    var childHWnd = (User32.HWND)lParam;

                    {
                        var currentThreadId = Kernel32.GetCurrentThreadId();
                        var childWindowThreadId = User32.GetWindowThreadProcessId(childHWnd, out var processId);
                        if (_logger.IsEnabled(LogLevel.Trace))
                        {
                            _logger.LogWithHWnd(LogLevel.Trace, $"GetWindowThreadProcessId [childHWnd:{childHWnd}] [childWindowThreadId:{childWindowThreadId:X}] [processId:{processId:X}] / current thread id:[{currentThreadId:X}]", hwnd, Environment.CurrentManagedThreadId);
                        }
                        if (currentThreadId != childWindowThreadId)
                        {
                            _logger.LogWithHWnd(LogLevel.Trace, "made by other thread", hwnd, Environment.CurrentManagedThreadId);

                            //TODO 異なるスレッドで作成したウインドウが子ウインドウになった場合の制御
                            // UIThread経由で作ったWindow
                            // 　→ wndprocを差し替えても、windowを積んだstackは元のスレッドにあるので括り付けられない
                            // 　→ 元のスレッドのwndprocが動くわけでもないので、括り付けられないのは変わらない
                            // _windowMapに入れることにするか？

                        }
                    }

                    if (_windowMap.FindWindow(childHWnd, out var childWindow))
                    {
                        //既に管理対象のwindowだった
                        if (_logger.IsEnabled(LogLevel.Trace))
                        {
                            _logger.LogWithHWnd(LogLevel.Trace, $"is managed window childHWnd:[{childHWnd}][{childWindow.Handle:X}]", hwnd, Environment.CurrentManagedThreadId);
                        }
                    }
                    else
                    {
                        var window = _serviceProvider.GetRequiredKeyedService<IWindowInternal>(SubClassNativeWindow2.Type);
                        var parameter_ = new SubClassNativeWindow2.Parameter()
                        {
                            childHWnd = childHWnd
                        };
                        try
                        {
                            window.Initialize(parameter_);
                        }
                        catch (Exception e)
                        {
                            _logger.LogWithLine(LogLevel.Error, e, "Initialize failed.", Environment.CurrentManagedThreadId);
                            throw;
                        }

                        _windowMap.Add(window);
                    }

                    break;
                }
            case 0x0002: //WM_DESTROY
                {
                    if (_logger.IsEnabled(LogLevel.Trace))
                    {
                        _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY WM_DESTROY {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
                    }
                    var childHWnd = (User32.HWND)lParam;

                    if (_windowMap.FindWindow(childHWnd, out var childWindow))
                    {
                        //既に管理対象のwindowだった
                        if (_logger.IsEnabled(LogLevel.Trace))
                        {
                            _logger.LogWithHWnd(LogLevel.Trace, $"is managed window childHWnd:[{childHWnd}][{childWindow.Handle:X}]", hwnd, Environment.CurrentManagedThreadId);
                        }
                        
                        if (childWindow is SubClassNativeWindow subclass)
                        {
                            _windowMap.Remove(subclass);
                        }
                        if (childWindow is SubClassNativeWindow2 subclass2)
                        {
                            _windowMap.Remove(subclass2);
                        }
                    }
                    else
                    {
                        //UnbindWndProc(hwnd, childHWnd);
                    }
                    break;
                }
        }
    }

    private void OnWM_NCCREATE_Before(User32.HWND hwnd)
    {
        //TODO トップレベルウインドウだったときのみ呼び出す
        if (!User32.EnableNonClientDpiScaling(hwnd))
        {
            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Error, "EnableNonClientDpiScaling failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
        }
    }

    private void WndProcError(User32.HWND hwnd, IWindowManager.IMessage message)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"WndProcError [{SynchronizationContext.Current}]", hwnd, Environment.CurrentManagedThreadId);
        }

        switch (message.Msg)
        {
            case 0x0001://WM_CREATE
                _logger.LogWithHWnd(LogLevel.Error, "WM_CREATE error", hwnd, Environment.CurrentManagedThreadId);
                message.Result = -1;
                message.Handled = true;
                break;

            case 0x0081://WM_NCCREATE
                _logger.LogWithHWnd(LogLevel.Error, "WM_NCCREATE error", hwnd, Environment.CurrentManagedThreadId);
                message.Result = -1;
                message.Handled = true;
                break;
        }
    }

    private void WndProcAfter(IWindowInternal window, IWindowManager.IMessage message)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"WndProcAfter [{SynchronizationContext.Current}]", window.HWND, Environment.CurrentManagedThreadId);
        }

        switch (message.Msg)
        {
            case 0x0081://WM_NCCREATE
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCCREATE After", window.HWND, Environment.CurrentManagedThreadId);
                OnWM_NCCREATE_After(window);
                break;

            case 0x0082://WM_NCDESTROY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCDESTROY After", window.HWND, Environment.CurrentManagedThreadId);
                OnWM_NCDESTROY_After(window);
                break;
        }
    }

    private void OnWM_NCCREATE_After(IWindowInternal window)
    {
        //作成に成功してからWindowMapに入れる
        _windowMap.Add(window);
    }

    private void OnWM_NCDESTROY_After(IWindowInternal window)
    {
        _windowMap.Remove(window);
    }

    private void CallOriginalWindowProc(User32.HWND hwnd, IWindowManager.IMessage message)
    {
        var isWindowUnicode = User32.IsWindowUnicode(hwnd);

        _logger.LogWithMsg(LogLevel.Trace, "DefWindowProc", hwnd, message, Environment.CurrentManagedThreadId);
        message.Result = isWindowUnicode
            ? User32.DefWindowProcW(hwnd, (uint)message.Msg, message.WParam, message.LParam)
            : User32.DefWindowProcA(hwnd, (uint)message.Msg, message.WParam, message.LParam)
            ;
        message.Handled = true;
        var error = Marshal.GetLastPInvokeError();
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithMsgAndError(LogLevel.Trace, "DefWindowProc result", hwnd, message, error.ToString(), Environment.CurrentManagedThreadId);
        }
    }

}
