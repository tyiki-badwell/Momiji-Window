using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal class WindowClassManager : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly PinnedDelegate<User32.WNDPROC> _wndProc;
    private readonly WindowClass _windowClass;

    internal WindowClass WindowClass => _windowClass;

    private readonly ConcurrentDictionary<User32.HWND, NativeWindow> _windowMap = new();
    private readonly Stack<NativeWindow> _windowStack = new();

    private readonly Stack<Exception> _wndProcExceptionStack = new();
    private readonly ConcurrentDictionary<User32.HWND, nint> _oldWndProcMap = new();

    internal WindowClassManager(
        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        //TODO windowとthreadが1:1のモード
        var param = new IWindowManager.Param();
        configuration.GetSection($"{typeof(WindowManager).FullName}").Bind(param);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowClassManager>();

        _wndProc = new PinnedDelegate<User32.WNDPROC>(new(WndProc));
        _windowClass =
            new WindowClass(
                _loggerFactory,
                _wndProc,
                param.CS
            );
    }
    ~WindowClassManager()
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

            //クローズしていないウインドウが残っていると失敗する
            _windowClass.Dispose();
            _wndProc.Dispose();
        }

        _disposed = true;
    }

    internal bool IsEmpty => _windowMap.IsEmpty;
 
    private bool Remove(User32.HWND hwnd)
    {
        return _windowMap.TryRemove(hwnd, out var _);
    }

    internal void Push(NativeWindow window)
    {
        _windowStack.Push(window);
    }

    internal NativeWindow Pop()
    {
        return _windowStack.Pop();
    }

    internal void CloseAll()
    {
        foreach (var item in _windowMap)
        {
            if (!_windowMap.ContainsKey(item.Key))
            {
                //ループ中に削除されていた場合はスキップ
                continue;
            }

            try
            {
                //TODO 親ウインドウにだけ実行する(順序に依っては子ウインドウのcloseで1400になる)
                item.Value.Close();
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "close failed.", Environment.CurrentManagedThreadId);
            }
        }
    }

    internal void ForceClose()
    {
        _logger.LogWithLine(LogLevel.Warning, $"window left {_windowMap.Count}", Environment.CurrentManagedThreadId);
        foreach (var item in _windowMap)
        {
            //TODO closeした通知を流す必要あり
            var hwnd = item.Key;
            _logger.LogWithHWnd(LogLevel.Warning, $"DestroyWindow", hwnd, Environment.CurrentManagedThreadId);
            if (!User32.DestroyWindow(hwnd))
            {
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Error, "DestroyWindow failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
            }
        }

        _windowMap.Clear();
    }

    internal User32.HWND CreateWindow(
        int dwExStyle,
        nint lpszClassName,
        nint lpszWindowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        User32.HWND hwndParent,
        nint hMenu,
        nint hInst
    )
    {
        _logger.LogWithLine(LogLevel.Trace, "CreateWindowEx", Environment.CurrentManagedThreadId);
        var hWindow =
            User32.CreateWindowExW(
                dwExStyle,
                lpszClassName,
                lpszWindowName,
                style,
                x,
                y,
                width,
                height,
                hwndParent,
                hMenu,
                hInst,
                nint.Zero
            );
        var error = new Win32Exception();
        _logger.LogWithHWndAndError(LogLevel.Information, "CreateWindowEx result", hWindow, error.ToString(), Environment.CurrentManagedThreadId);
        if (hWindow.Handle == User32.HWND.None.Handle)
        {
            ThrowIfOccurredInWndProc();
            throw new WindowException("CreateWindowEx failed", error);
        }

        var behavior = User32.GetWindowDpiHostingBehavior(hWindow);
        _logger.LogWithLine(LogLevel.Trace, $"GetWindowDpiHostingBehavior {behavior}", Environment.CurrentManagedThreadId);

        return hWindow;
    }

    internal void DispatchMessage()
    {
        var msg = new User32.MSG();

        while (true)
        {
            _logger.LogWithLine(LogLevel.Trace, "PeekMessage", Environment.CurrentManagedThreadId);
            if (!User32.PeekMessageW(
                    ref msg,
                    User32.HWND.None,
                    0,
                    0,
                    0x0001 // PM_REMOVE
            ))
            {
                _logger.LogWithLine(LogLevel.Trace, "PeekMessage NONE", Environment.CurrentManagedThreadId);
                return;
            }
            _logger.LogWithHWnd(LogLevel.Trace, $"MSG {msg}", msg.hwnd, Environment.CurrentManagedThreadId);

            if (msg.hwnd.Handle == User32.HWND.None.Handle)
            {
                _logger.LogWithHWnd(LogLevel.Trace, "hwnd is none", msg.hwnd, Environment.CurrentManagedThreadId);
            }

            //TODO: msg.hwnd がnullのとき(= thread宛メッセージ)は、↓以降はバイパスしてthread宛メッセージを処理する

            {
                _logger.LogWithHWnd(LogLevel.Trace, "TranslateMessage", msg.hwnd, Environment.CurrentManagedThreadId);
                var result = User32.TranslateMessage(ref msg);
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"TranslateMessage [{result:X}]", msg.hwnd, error.ToString(), Environment.CurrentManagedThreadId);
            }

            {
                var isWindowUnicode = (msg.hwnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(msg.hwnd);
                _logger.LogWithHWnd(LogLevel.Trace, $"IsWindowUnicode [{isWindowUnicode}]", msg.hwnd, Environment.CurrentManagedThreadId);

                _logger.LogWithHWnd(LogLevel.Trace, "DispatchMessage", msg.hwnd, Environment.CurrentManagedThreadId);
                var result = isWindowUnicode
                            ? User32.DispatchMessageW(ref msg)
                            : User32.DispatchMessageA(ref msg)
                            ;
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"DispatchMessage [{result:X}]", msg.hwnd, error.ToString(), Environment.CurrentManagedThreadId);
            }
        }
    }

    private nint WndProc(User32.HWND hwnd, uint msg, nint wParam, nint lParam)
    {
        var message = new IWindowManager.Message()
        {
            Msg = (int)msg,
            WParam = wParam,
            LParam = lParam
        };

        try
        {
            _logger.LogWithMsg(LogLevel.Trace, "WndProc", hwnd, message.Msg, message.WParam, message.LParam, Environment.CurrentManagedThreadId);

            WindowDebug.CheckDpiAwarenessContext(_loggerFactory, hwnd);

            {
                WndProcBefore(hwnd, message);
                if (message.Handled)
                {
                    return message.Result;
                }
            }

            if (TryGetWindow(hwnd, out var window))
            {
                //ウインドウに流す
                window.WndProc(message);
                if (message.Handled)
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"handled msg:[{msg:X}]", hwnd, Environment.CurrentManagedThreadId);
                    return message.Result;
                }
                else
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"no handled msg:[{msg:X}]", hwnd, Environment.CurrentManagedThreadId);
                }
            }
            else
            {
                _logger.LogWithHWnd(LogLevel.Trace, "unkown window handle", hwnd, Environment.CurrentManagedThreadId);
            }

            var isWindowUnicode = (hwnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(hwnd);

            //TODO 子ウインドウでないときはこの処理が無駄
            if (_oldWndProcMap.TryGetValue(hwnd, out var oldWndProc))
            {
                _logger.LogWithMsg(LogLevel.Trace, "CallWindowProc", hwnd, message.Msg, message.WParam, message.LParam, Environment.CurrentManagedThreadId);
                var result = isWindowUnicode
                            ? User32.CallWindowProcW(oldWndProc, hwnd, msg, wParam, lParam)
                            : User32.CallWindowProcA(oldWndProc, hwnd, msg, wParam, lParam)
                            ;
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"CallWindowProc [{result:X}]", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
                return result;
            }
            else
            {
                var result = isWindowUnicode
                    ? User32.DefWindowProcW(hwnd, msg, wParam, lParam)
                    : User32.DefWindowProcA(hwnd, msg, wParam, lParam)
                    ;
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"DefWindowProc [{result:X}]", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
                return result;
            }
        }
        catch (Exception e)
        {
            //msgによってreturnを分ける
            return WndProcError(hwnd, msg, wParam, lParam, e);
        }
    }

    private bool TryGetWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out NativeWindow window)
    {
        if (_windowMap.TryGetValue(hwnd, out window))
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"window map found. hash:[{window.GetHashCode():X}]", hwnd, Environment.CurrentManagedThreadId);
            return true;
        }

        if (_windowStack.TryPeek(out var windowFromStack))
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"window stack hash:{windowFromStack.GetHashCode():X}", hwnd, Environment.CurrentManagedThreadId);

            if (windowFromStack._hWindow.Handle != User32.HWND.None.Handle)
            {
                _logger.LogWithHWnd(LogLevel.Trace, "no managed", hwnd, Environment.CurrentManagedThreadId);
                return false;
            }
        }
        else
        {
            _logger.LogWithHWnd(LogLevel.Trace, "stack none", hwnd, Environment.CurrentManagedThreadId);
            return false;
        }

        //最速でHWNDを受け取る
        windowFromStack._hWindow = hwnd;
        if (_windowMap.TryAdd(hwnd, windowFromStack))
        {
            _logger.LogWithHWnd(LogLevel.Information, "add window map", hwnd, Environment.CurrentManagedThreadId);
            window = windowFromStack;
            return true;
        }
        else
        {
            throw new WindowException($"failed add window map hwnd:[{hwnd}]");
        }
    }

    private void WndProcBefore(User32.HWND hwnd, IWindowManager.Message message)
    {
        switch (message.Msg)
        {
            case 0x0018://WM_SHOWWINDOW
                _logger.LogWithHWnd(LogLevel.Trace, $"WM_SHOWWINDOW {message.WParam:X} {message.LParam:X}", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x001C://WM_ACTIVATEAPP
                _logger.LogWithHWnd(LogLevel.Trace, $"WM_ACTIVATEAPP {message.WParam:X} {message.LParam:X}", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x0024://WM_GETMINMAXINFO
                _logger.LogWithHWnd(LogLevel.Trace, $"WM_GETMINMAXINFO {message.WParam:X} {message.LParam:X}", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x0046://WM_WINDOWPOSCHANGING
                _logger.LogWithHWnd(LogLevel.Trace, "WM_WINDOWPOSCHANGING", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x0081://WM_NCCREATE
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCCREATE", hwnd, Environment.CurrentManagedThreadId);
                OnWM_NCCREATE(hwnd);
                break;

            case 0x0082://WM_NCDESTROY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCDESTROY", hwnd, Environment.CurrentManagedThreadId);
                OnWM_NCDESTROY(hwnd);
                message.Handled = true;
                break;

            case 0x0083://WM_NCCALCSIZE
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCCALCSIZE", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x0210://WM_PARENTNOTIFY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_PARENTNOTIFY", hwnd, Environment.CurrentManagedThreadId);
                OnWM_PARENTNOTIFY(hwnd, message.WParam, message.LParam);
                break;

            case 0x02E0://WM_DPICHANGED
                _logger.LogWithHWnd(LogLevel.Trace, "WM_DPICHANGED", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x02E2://WM_DPICHANGED_BEFOREPARENT
                _logger.LogWithHWnd(LogLevel.Trace, "WM_DPICHANGED_BEFOREPARENT", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x02E3://WM_DPICHANGED_AFTERPARENT
                _logger.LogWithHWnd(LogLevel.Trace, "WM_DPICHANGED_AFTERPARENT", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x02E4://WM_GETDPISCALEDSIZE
                _logger.LogWithHWnd(LogLevel.Trace, "WM_GETDPISCALEDSIZE", hwnd, Environment.CurrentManagedThreadId);
                break;
        }
    }

    private void OnWM_PARENTNOTIFY(User32.HWND hwnd, nint wParam, nint lParam)
    {
        var GWLP_WNDPROC = -4;

        switch (wParam & 0xFFFF)
        {
            case 0x0001: //WM_CREATE
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY WM_CREATE {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
                    var childHWnd = (User32.HWND)lParam;

                    //TODO 異なるスレッドで作成したwindowのwndprocは差し替えても流れていかない

                    if (_oldWndProcMap.TryAdd(childHWnd, nint.Zero))
                    {
                        var oldWndProc = ChildWindowSetWindowLong(childHWnd, GWLP_WNDPROC, _wndProc.FunctionPointer);

                        if (oldWndProc == _wndProc.FunctionPointer)
                        {
                            //変更前・変更後のWndProcが同じだった＝WindowManager経由で作ったWindow　→　ここで管理する必要なし
                            //TODO SetWindowLongする前にバイパスした方がよい
                            _logger.LogWithHWnd(LogLevel.Information, $"IGNORE hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                            _oldWndProcMap.TryRemove(childHWnd, out var _);
                        }
                        else
                        {
                            _logger.LogWithHWnd(LogLevel.Information, $"add to old wndproc map hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                            if (!_oldWndProcMap.TryUpdate(childHWnd, oldWndProc, nint.Zero))
                            {
                                //更新できなかった
                                _logger.LogWithHWnd(LogLevel.Error, $"failed add to old wndproc map hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                            }
                        }
                    }
                    else
                    {
                        //すでに登録されているのは異常事態
                        _logger.LogWithHWnd(LogLevel.Warning, $"found in old wndproc map hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                    }

                    break;
                }
            case 0x0002: //WM_DESTROY
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY WM_DESTROY {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
                    var childHWnd = (User32.HWND)lParam;

                    if (_oldWndProcMap.TryRemove(childHWnd, out var oldWndProc))
                    {
                        _logger.LogWithHWnd(LogLevel.Information, $"remove old wndproc map hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                        var _ = ChildWindowSetWindowLong(childHWnd, GWLP_WNDPROC, oldWndProc);

                        //WM_NCDESTROYが発生する前にWndProcを元に戻したので、ここで呼び出しする
                        //TODO 親ウインドウのWM_PARENTNOTIFYが発生するより先に子ウインドウのWM_NCDESTROYが発生した場合は、ココを通らない
                        OnWM_NCDESTROY(childHWnd);
                    }
                    else
                    {
                        _logger.LogWithHWnd(LogLevel.Warning, $"not found in old wndproc map hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                    }
                    break;
                }
        }
    }

    private nint ChildWindowSetWindowLong(
        User32.HWND childHWnd,
        int nIndex,
        nint dwNewLong
    )
    {
        Marshal.SetLastPInvokeError(0);
        _logger.LogWithHWnd(LogLevel.Information, $"child SetWindowLong nIndex:[{nIndex:X}] dwNewLong:[{dwNewLong:X}]", childHWnd, Environment.CurrentManagedThreadId);

        var isChildWindowUnicode = (childHWnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(childHWnd);

        //SetWindowLong～のエラー判定のために、エラーコードをリセットする
        Marshal.SetLastPInvokeError(0);
        var result = isChildWindowUnicode
                        ? Environment.Is64BitProcess
                            ? User32.SetWindowLongPtrW(childHWnd, nIndex, dwNewLong)
                            : User32.SetWindowLongW(childHWnd, nIndex, dwNewLong)
                        : Environment.Is64BitProcess
                            ? User32.SetWindowLongPtrA(childHWnd, nIndex, dwNewLong)
                            : User32.SetWindowLongA(childHWnd, nIndex, dwNewLong)
                        ;
        var error = new Win32Exception();
        _logger.LogWithHWndAndError(LogLevel.Information, $"SetWindowLong result:[{result:X}]", childHWnd, error.ToString(), Environment.CurrentManagedThreadId);
        if (result == 0 && error.NativeErrorCode != 0)
        {
            throw new WindowException("SetWindowLong failed", error);
        }

        return result;
    }

    private void OnWM_NCCREATE(User32.HWND hwnd)
    {
        //TODO トップレベルウインドウだったときのみ呼び出す
        if (!User32.EnableNonClientDpiScaling(hwnd))
        {
            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Error, "EnableNonClientDpiScaling failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
        }
    }

    private void OnWM_NCDESTROY(User32.HWND hwnd)
    {
        if (Remove(hwnd))
        {
            _logger.LogWithHWnd(LogLevel.Information, "remove window map", hwnd, Environment.CurrentManagedThreadId);

            //TODO メインウインドウなら、自身を終わらせる動作を入れるか？
        }
        else
        {
            _logger.LogWithHWnd(LogLevel.Warning, "failed. remove window map", hwnd, Environment.CurrentManagedThreadId);
        }
    }

    internal class WndProcException(
        string message,
        User32.HWND hwnd,
        uint msg,
        nint wParam,
        nint lParam,
        Exception innerException
    ) : Exception(message, innerException)
    {
        public readonly User32.HWND Hwnd = hwnd;
        public readonly uint Msg = msg;
        public readonly nint WParam = wParam;
        public readonly nint LParam = lParam;
    }

    private nint WndProcError(User32.HWND hwnd, uint msg, nint wParam, nint lParam, Exception exception)
    {
        var result = nint.Zero;

        _wndProcExceptionStack.Push(new WndProcException("error occurred in WndProc", hwnd, msg, wParam, lParam, exception));

        switch (msg)
        {
            case 0x0001://WM_CREATE
                _logger.LogWithHWnd(LogLevel.Error, "WM_CREATE error", hwnd, Environment.CurrentManagedThreadId);
                result = -1;
                break;

            case 0x0081://WM_NCCREATE
                _logger.LogWithHWnd(LogLevel.Error, "WM_NCCREATE error", hwnd, Environment.CurrentManagedThreadId);
                result = -1;
                break;
        }

        return result;
    }

    internal void ThrowIfOccurredInWndProc()
    {
        lock (_wndProcExceptionStack)
        {
            if (_wndProcExceptionStack.Count > 0)
            {
                var aggregateException = new AggregateException(_wndProcExceptionStack.AsEnumerable());
                _wndProcExceptionStack.Clear();
                throw aggregateException;
            }
        }
    }
}
