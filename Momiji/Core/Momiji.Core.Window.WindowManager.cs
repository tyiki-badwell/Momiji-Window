using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal sealed class WindowManager : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;
    private WindowProcedure WindowProcedure { get; }

    internal bool IsEmpty => _windowMap.IsEmpty;

    private readonly ConcurrentDictionary<User32.HWND, NativeWindow> _windowMap = [];
    private readonly Stack<NativeWindow> _windowStack = [];

    private readonly ConcurrentDictionary<int, WeakReference<NativeWindow>> _childIdMap = [];
    private int childId = 0;

    //TODO Windowに移動する
    private readonly ConcurrentDictionary<User32.HWND, nint> _oldWndProcMap = [];

    internal WindowManager(
        ILoggerFactory loggerFactory,
        WindowProcedure windowProcedure
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowContext>();
        WindowProcedure = windowProcedure;
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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);

            ForceClose();
        }

        _disposed = true;
    }

    internal readonly ref struct SwitchThreadDpiAwarenessContextPerMonitorAwareV2RAII
    {
        private readonly ILogger _logger;
        private readonly NativeWindow _window;

        private readonly User32.DPI_AWARENESS_CONTEXT _oldContext;

        public SwitchThreadDpiAwarenessContextPerMonitorAwareV2RAII(
            NativeWindow window,
            ILogger logger
        )
        {
            _window = window;
            _logger = logger;

            _oldContext = User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT.PER_MONITOR_AWARE_V2);

            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Trace, $"ON SetThreadDpiAwarenessContext [{_oldContext:X} -> PER_MONITOR_AWARE_V2]", _window.HWindow, error.ToString(), Environment.CurrentManagedThreadId);
        }

        public void Dispose()
        {
            if (_oldContext.Handle != 0)
            {
                var oldContext = User32.SetThreadDpiAwarenessContext(_oldContext);

                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"OFF SetThreadDpiAwarenessContext [{oldContext} -> {_oldContext}]", _window.HWindow, error.ToString(), Environment.CurrentManagedThreadId);
            }
        }
    }

    internal TResult InvokeWithContext<TResult>(Func<IWindow, TResult> item, NativeWindow window)
    {
        //TODO スレッドセーフになっているか要確認(再入しても問題ないなら気にしない)
        //TODO 何かのコンテキストに移す
        _logger.LogWithLine(LogLevel.Trace, $"Push [handle:{window.Handle:X}] [stack:{_windowStack.Count}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);
        _windowStack.Push(window);

        try
        {
            //TODO CreateWindowだけ囲えば良さそうだが、一旦全部囲ってしまう
            using var switchContext = new SwitchThreadDpiAwarenessContextPerMonitorAwareV2RAII(window, _logger);

            _logger.LogWithLine(LogLevel.Trace, "Invoke start", Environment.CurrentManagedThreadId);
            var result = item(window);
            _logger.LogWithLine(LogLevel.Trace, "Invoke end", Environment.CurrentManagedThreadId);
            return result;
        }
        finally
        {
            var result = _windowStack.Pop();
            _logger.LogWithLine(LogLevel.Trace, $"Pop [handle:{window.Handle:X}] [stack:{_windowStack.Count}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);
            Debug.Assert(result == window);
        }
    }

    internal int GenerateChildId(NativeWindow window)
    {
        var id = Interlocked.Increment(ref childId);
        _childIdMap.TryAdd(id, new(window));
        return id;
    }

    internal void CloseAll()
    {
        //TODO SendMessageに行くのでUIスレッドで呼び出す必要は無いが、統一すべき？
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

    private void ForceClose()
    {
        if (_windowMap.IsEmpty)
        {
            return;
        }

        _logger.LogWithLine(LogLevel.Warning, $"window left {_windowMap.Count}", Environment.CurrentManagedThreadId);
        foreach (var item in _windowMap)
        {
            var hwnd = item.Key;
            _logger.LogWithHWnd(LogLevel.Warning, $"DestroyWindow", hwnd, Environment.CurrentManagedThreadId);

            //TODO WM_NCDESTROYが発生してMapから削除されるので、foreachじゃダメかも？
            if (!User32.DestroyWindow(hwnd))
            {
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Error, "DestroyWindow failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
            }
            else
            {
                _logger.LogWithHWnd(LogLevel.Trace, $"DestroyWindow OK", hwnd, Environment.CurrentManagedThreadId);
            }
        }

        _windowMap.Clear();
    }

    internal void OnMessage(User32.HWND hwnd, IUIThread.IMessage message)
    {
        _logger.LogWithMsg(LogLevel.Trace, "OnMessage", hwnd, message, Environment.CurrentManagedThreadId);

        try
        {
            WindowDebug.CheckDpiAwarenessContext(_logger, hwnd);

            WndProcBefore(hwnd, message);
            if (message.Handled)
            {
                return;
            }

            if (TryGetWindow(hwnd, out var window))
            {
                //ウインドウに流す
                window.WndProc(message);

                WndProcAfter(message, window);
            }
            else
            {
                _logger.LogWithMsg(LogLevel.Trace, "unkown window handle", hwnd, message, Environment.CurrentManagedThreadId);
                message.Result = CallOriginalWindowProc(hwnd, message);
                message.Handled = true;
            }

            if (message.Handled)
            {
                _logger.LogWithMsg(LogLevel.Trace, "handled msg", hwnd, message, Environment.CurrentManagedThreadId);
            }
            else
            {
                _logger.LogWithMsg(LogLevel.Trace, "no handled msg", hwnd, message, Environment.CurrentManagedThreadId);
            }

        }
        catch (Exception)
        {
            //msgによってreturnを分ける
            WndProcError(hwnd, message);
            throw;
        }
    }

    private void WndProcBefore(User32.HWND hwnd, IUIThread.IMessage message)
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
        _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY [msg:{msg:X}][childId:{childId}] {wParam:X}", hwnd, Environment.CurrentManagedThreadId);

        switch (msg)
        {
            case 0x0001: //WM_CREATE
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY WM_CREATE {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
                    var childHWnd = (User32.HWND)lParam;

                    {
                        var currentThreadId = Kernel32.GetCurrentThreadId();
                        var childWindowThreadId = User32.GetWindowThreadProcessId(childHWnd, out var processId);
                        _logger.LogWithHWnd(LogLevel.Trace, $"GetWindowThreadProcessId [childHWnd:{childHWnd}] [childWindowThreadId:{childWindowThreadId:X}] [processId:{processId:X}] / current thread id:[{currentThreadId:X}]", hwnd, Environment.CurrentManagedThreadId);

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

                    if (FindWindow(childHWnd, out var childWindow))
                    {
                        //既に管理対象のwindowだった
                        _logger.LogWithHWnd(LogLevel.Trace, $"is managed window childHWnd:[{childHWnd}][{childWindow.Handle:X}]", hwnd, Environment.CurrentManagedThreadId);
                    }
                    else
                    {
                        //TODO windowに、HWNDのアサインとWndProcの差し替えを設ける
                        BindWndProc(hwnd, childHWnd);
                    }

                    break;
                }
            case 0x0002: //WM_DESTROY
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY WM_DESTROY {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
                    var childHWnd = (User32.HWND)lParam;

                    if (FindWindow(childHWnd, out var childWindow))
                    {
                        //既に管理対象のwindowだった
                        _logger.LogWithHWnd(LogLevel.Trace, $"is managed window childHWnd:[{childHWnd}][{childWindow.Handle:X}]", hwnd, Environment.CurrentManagedThreadId);
                    }
                    else
                    {
                        UnbindWndProc(hwnd, childHWnd);
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

    private void WndProcError(User32.HWND hwnd, IUIThread.IMessage message)
    {
        _logger.LogWithLine(LogLevel.Trace, $"WndProcError [handle:{hwnd}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);

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

    private void WndProcAfter(IUIThread.IMessage message, NativeWindow window)
    {
        switch (message.Msg)
        {
            case 0x0081://WM_NCCREATE
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCCREATE After", window.HWindow, Environment.CurrentManagedThreadId);
                OnWM_NCCREATE_After(window);
                break;

            case 0x0082://WM_NCDESTROY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCDESTROY After", window.HWindow, Environment.CurrentManagedThreadId);
                OnWM_NCDESTROY_After(window);
                break;
        }
    }

    private void OnWM_NCCREATE_After(NativeWindow window)
    {
        //作成に成功してからWindowMapに入れる
        Add(window);
    }

    private void OnWM_NCDESTROY_After(NativeWindow window)
    {
        Remove(window);
    }

    private nint CallOriginalWindowProc(User32.HWND hwnd, IUIThread.IMessage message)
    {
        var isWindowUnicode = (hwnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(hwnd);

        //TODO 子ウインドウでないときはこの処理が無駄
        if (_oldWndProcMap.TryGetValue(hwnd, out var oldWndProc))
        {
            _logger.LogWithMsg(LogLevel.Trace, "CallWindowProc", hwnd, message, Environment.CurrentManagedThreadId);
            var result = isWindowUnicode
                ? User32.CallWindowProcW(oldWndProc, hwnd, (uint)message.Msg, message.WParam, message.LParam)
                : User32.CallWindowProcA(oldWndProc, hwnd, (uint)message.Msg, message.WParam, message.LParam)
                ;
            var error = new Win32Exception();
            _logger.LogWithMsgAndError(LogLevel.Trace, "CallWindowProc result", hwnd, message, error.ToString(), Environment.CurrentManagedThreadId);
            return result;
        }
        else
        {
            _logger.LogWithMsg(LogLevel.Trace, "DefWindowProc", hwnd, message, Environment.CurrentManagedThreadId);
            var result = isWindowUnicode
                ? User32.DefWindowProcW(hwnd, (uint)message.Msg, message.WParam, message.LParam)
                : User32.DefWindowProcA(hwnd, (uint)message.Msg, message.WParam, message.LParam)
                ;
            var error = new Win32Exception();
            _logger.LogWithMsgAndError(LogLevel.Trace, "DefWindowProc result", hwnd, message, error.ToString(), Environment.CurrentManagedThreadId);
            return result;
        }
    }

    [Conditional("DEBUG")]
    private void PrintWindowStack()
    {
        _logger.LogTrace("================================= PrintWindowStack start");
        _logger.LogWithLine(LogLevel.Trace, $"window stack {string.Join("<-", _windowStack.Select((window, idx) => $"[hash:{window.GetHashCode():X}][handle:{window.HWindow:X}]"))}", Environment.CurrentManagedThreadId);
        _logger.LogTrace("================================= PrintWindowStack end");
    }

    [Conditional("DEBUG")]
    private void PrintWindowMap()
    {
        _logger.LogTrace("================================= PrintWindowMap start");

        foreach (var item in _windowMap)
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"window map [hash:{item.Value.GetHashCode():X}]", item.Key, Environment.CurrentManagedThreadId);
        }

        _logger.LogTrace("================================= PrintWindowMap end");
    }

    private void Add(NativeWindow window)
    {
        if (_windowMap.TryAdd(window.HWindow, window))
        {
            _logger.LogWithHWnd(LogLevel.Information, "add window map", window.HWindow, Environment.CurrentManagedThreadId);
        }
        else
        {
            //TODO handleが再利用されている？
            throw new WindowException($"failed add window map hwnd:[{window.HWindow}]");
        }
    }

    private void Remove(NativeWindow window)
    {
        if (_windowMap.TryRemove(window.HWindow, out var window_))
        {
            _logger.LogWithHWnd(LogLevel.Information, "remove window map", window_.HWindow, Environment.CurrentManagedThreadId);
            window_.HWindow = User32.HWND.None;
        }
        else
        {
            throw new WindowException($"failed remove window map hwnd:[{window.HWindow}]");
        }
    }

    private bool FindWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out NativeWindow window)
    {
        if (_windowMap.TryGetValue(hwnd, out window))
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"in window map [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
            return true;
        }
        return false;
    }

    private bool TryGetWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out NativeWindow window)
    {
        PrintWindowStack();
        PrintWindowMap();

        if (FindWindow(hwnd, out window))
        {
            return true;
        }

        if (_windowStack.TryPeek(out window))
        {
            if (window.HWindow.Handle == hwnd.Handle)
            {
                _logger.LogWithHWnd(LogLevel.Trace, $"in window stack [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
                return true;
            }

            if (window.HWindow.Handle == User32.HWND.None.Handle)
            {
                _logger.LogWithHWnd(LogLevel.Trace, $"in window stack (until create process) [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
                //最速でHWNDを受け取る
                window.HWindow = hwnd;
                return true;
            }

            _logger.LogWithHWnd(LogLevel.Trace, $"in window stack, but other window [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
            return false;
        }
        else
        {
            _logger.LogWithHWnd(LogLevel.Trace, "stack is empty", hwnd, Environment.CurrentManagedThreadId);
            return false;
        }
    }

    //TODO Windowに移動する

    private void BindWndProc(
        User32.HWND hwnd,
        User32.HWND childHWnd
    )
    {
        const int GWLP_WNDPROC = -4;

        if (_oldWndProcMap.TryAdd(childHWnd, nint.Zero))
        {
            var oldWndProc = ChildWindowSetWindowLong(childHWnd, GWLP_WNDPROC, WindowProcedure.FunctionPointer);

            if (oldWndProc == WindowProcedure.FunctionPointer)
            {
                //変更前・変更後のWndProcが同じだった＝UIThread経由で作ったWindow　→　先にバイパスしたのにここに来たら異常事態
                _logger.LogWithHWnd(LogLevel.Information, $"IGNORE childHWnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                _oldWndProcMap.TryRemove(childHWnd, out var _);
            }
            else
            {
                _logger.LogWithHWnd(LogLevel.Information, $"add to old wndproc map childHWnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                if (!_oldWndProcMap.TryUpdate(childHWnd, oldWndProc, nint.Zero))
                {
                    //更新できなかった
                    _logger.LogWithHWnd(LogLevel.Error, $"failed add to old wndproc map childHWnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                }
            }
        }
        else
        {
            //すでに登録されているのは異常事態
            _logger.LogWithHWnd(LogLevel.Warning, $"found in old wndproc map childHWnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
        }
    }

    private void UnbindWndProc(
        User32.HWND hwnd,
        User32.HWND childHWnd
    )
    {
        const int GWLP_WNDPROC = -4;

        if (_oldWndProcMap.TryRemove(childHWnd, out var oldWndProc))
        {
            _logger.LogWithHWnd(LogLevel.Information, $"remove old wndproc map childHWnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
            var _ = ChildWindowSetWindowLong(childHWnd, GWLP_WNDPROC, oldWndProc);

            //WM_NCDESTROYが発生する前にWndProcを元に戻したので、ここで呼び出しする
            //TODO 親ウインドウのWM_PARENTNOTIFYが発生するより先に子ウインドウのWM_NCDESTROYが発生した場合は、ココを通らない
            UnbindWindow(childHWnd);
        }
        else
        {
            _logger.LogWithHWnd(LogLevel.Warning, $"not found in old wndproc map childHWnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
        }
    }

    private bool UnbindWindow(User32.HWND hwnd)
    {
        if (_windowMap.TryRemove(hwnd, out var window))
        {
            _logger.LogWithHWnd(LogLevel.Information, "remove window map", hwnd, Environment.CurrentManagedThreadId);
            window.HWindow = User32.HWND.None;
            return true;
        }
        else
        {
            _logger.LogWithHWnd(LogLevel.Warning, "failed. remove window map", hwnd, Environment.CurrentManagedThreadId);
            return false;
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
            throw error;
        }

        return result;
    }
}
