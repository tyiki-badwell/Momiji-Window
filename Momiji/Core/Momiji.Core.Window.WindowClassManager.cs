using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

public class WindowClassManager : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly int _uiThreadId;

    private readonly PinnedDelegate<User32.WNDPROC> _wndProc;
    private readonly ConcurrentDictionary<(string, uint), WindowClass> _windowClassMap = [];

    //TODO ↓機能過多
    public bool IsEmpty => _windowMap.IsEmpty;

    private readonly WindowClassManagerSynchronizationContext _windowClassManagerSynchronizationContext;

    private readonly ConcurrentDictionary<User32.HWND, NativeWindow> _windowMap = [];
    private readonly Stack<NativeWindow> _windowStack = [];

    private readonly ConcurrentDictionary<int, NativeWindow> _childIdMap = [];
    private int childId = 0;

    private readonly Stack<Exception> _wndProcExceptionStack = [];
    private readonly ConcurrentDictionary<User32.HWND, nint> _oldWndProcMap = [];
    //TODO ↑機能過多

    public WindowClassManager(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        DispatcherQueue dispatcherQueue
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var param = new IWindowManagerFactory.Param();
        configuration.GetSection($"{typeof(WindowManager).FullName}").Bind(param);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowClassManager>();
        _uiThreadId = Environment.CurrentManagedThreadId;

        _wndProc = new(new(WndProc));

        //top level window
        QueryWindowClass(string.Empty, (uint)param.CS);

        _windowClassManagerSynchronizationContext = new(_loggerFactory, dispatcherQueue);
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

            ForceClose();

            foreach (var windowClass in _windowClassMap.Values)
            {
                //クローズしていないウインドウが残っていると失敗する
                windowClass.Dispose();
            }
            _windowClassMap.Clear();

            _wndProc.Dispose();
        }

        _disposed = true;
    }

    internal WindowClass QueryWindowClass(string className, uint cs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var queryClassName = className.Trim().ToUpper();
        var queryClassStyle = cs;

        return _windowClassMap.GetOrAdd((queryClassName, queryClassStyle), (query) => {
            return new WindowClass(_loggerFactory, _wndProc, query.Item2, query.Item1);
        });
    }

    private bool Remove(User32.HWND hwnd)
    {
        return _windowMap.TryRemove(hwnd, out var _);
    }

    internal class PerMonitorAwareV2ThreadDpiAwarenessContextSetter : IDisposable
    {
        private readonly ILogger _logger;
        private readonly NativeWindow _window;

        private bool _disposed;
        private readonly User32.DPI_AWARENESS_CONTEXT _oldContext;

        public PerMonitorAwareV2ThreadDpiAwarenessContextSetter(
            ILoggerFactory loggerFactory,
            NativeWindow window
        )
        {
            _logger = loggerFactory.CreateLogger<PerMonitorAwareV2ThreadDpiAwarenessContextSetter>();
            _window = window;

            _oldContext = User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT.PER_MONITOR_AWARE_V2);

            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Trace, $"ON SetThreadDpiAwarenessContext [{_oldContext:X} -> PER_MONITOR_AWARE_V2]", _window._hWindow, error.ToString(), Environment.CurrentManagedThreadId);
        }

        ~PerMonitorAwareV2ThreadDpiAwarenessContextSetter()
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
            }

            if (_oldContext.Handle != 0)
            {
                var oldContext = User32.SetThreadDpiAwarenessContext(_oldContext);

                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"OFF SetThreadDpiAwarenessContext [{oldContext} -> {_oldContext}]", _window._hWindow, error.ToString(), Environment.CurrentManagedThreadId);
            }

            _disposed = true;
        }
    }

    internal TResult InvokeWithContext<TResult>(Func<IWindow, TResult> item, NativeWindow window)
    {
        //TODO スレッドセーフになっているか要確認(再入しても問題ないなら気にしない)
        Push(window);

        try
        {
            //TODO CreateWindowだけ囲えば良さそうだが、一旦全部囲ってしまう
            using var context = new PerMonitorAwareV2ThreadDpiAwarenessContextSetter(_loggerFactory, window);

            _logger.LogWithLine(LogLevel.Trace, "Invoke start", Environment.CurrentManagedThreadId);
            var result = item(window);
            _logger.LogWithLine(LogLevel.Trace, "Invoke end", Environment.CurrentManagedThreadId);
            return result;
        }
        finally
        {
            var result = Pop();
            Debug.Assert(result == window);
        }
    }

    private void Push(NativeWindow window)
    {
        //TODO 何かのコンテキストに移す
        _logger.LogWithLine(LogLevel.Trace, $"Push [handle:{window.Handle:X}] [stack:{_windowStack.Count}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);
        _windowStack.Push(window);
    }

    private NativeWindow Pop()
    {
        var window = _windowStack.Pop();
        //TODO 何かのコンテキストに移す
        _logger.LogWithLine(LogLevel.Trace, $"Pop [handle:{window.Handle:X}] [stack:{_windowStack.Count}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);
        return window;
    }

    internal int GenerateChildId(NativeWindow window)
    {
        var id = Interlocked.Increment(ref childId);
        _childIdMap.TryAdd(id, window);
        return id;
    }

    public void CloseAll()
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

    private void ThrowIfCalledFromOtherThread()
    {
        if (_uiThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException($"called from invalid thread id [construct:{_uiThreadId:X}] [current:{Environment.CurrentManagedThreadId:X}]");
        }
    }

    public void DispatchMessage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //UIスレッドで呼び出す必要アリ
        ThrowIfCalledFromOtherThread();

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

    private record class Message : IWindowManager.IMessage
    {
        public required int Msg
        {
            get; init;
        }
        public required nint WParam
        {
            get; init;
        }
        public required nint LParam
        {
            get; init;
        }

        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

        public int OwnerThreadId => _ownerThreadId;

        private nint _result;

        public nint Result
        {
            get => _result;
            set
            {
                if (OwnerThreadId != Environment.CurrentManagedThreadId)
                {
                    throw new InvalidOperationException("called from other thread");
                }

                _result = value;
            }
        }

        public bool Handled
        {
            get; set;
        }
        public override string ToString()
        {
            return $"[Msg:{Msg:X}][WParam:{WParam:X}][LParam:{LParam:X}][OwnerThreadId:{OwnerThreadId:X}][Result:{Result:X}][Handled:{Handled}]";
        }
    }

    private nint WndProc(User32.HWND hwnd, uint msg, nint wParam, nint lParam)
    {
        var message = new Message()
        {
            Msg = (int)msg,
            WParam = wParam,
            LParam = lParam
        };

        var oldContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(_windowClassManagerSynchronizationContext);

            _logger.LogWithMsg(LogLevel.Trace, "WndProc", hwnd, message, Environment.CurrentManagedThreadId);

            WindowDebug.CheckDpiAwarenessContext(_logger, hwnd);

            {
                WndProcBefore(hwnd, message);
                if (message.Handled)
                {
                    return message.Result;
                }
            }

            {
                if (TryGetWindow(hwnd, out var window))
                {
                    //ウインドウに流す
                    window.WndProc(message);
                    if (message.Handled)
                    {
                        _logger.LogWithMsg(LogLevel.Trace, "handled msg", hwnd, message, Environment.CurrentManagedThreadId);
                        return message.Result;
                    }
                    else
                    {
                        _logger.LogWithMsg(LogLevel.Trace, "no handled msg", hwnd, message, Environment.CurrentManagedThreadId);
                    }
                }
                else
                {
                    _logger.LogWithMsg(LogLevel.Trace, "unkown window handle", hwnd, message, Environment.CurrentManagedThreadId);
                    message.Result = CallOriginalWindowProc(hwnd, message);
                }
            }

            return message.Result;

        }
        catch (Exception e)
        {
            //msgによってreturnを分ける
            return WndProcError(hwnd, msg, wParam, lParam, e);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }

    private nint CallOriginalWindowProc(User32.HWND hwnd, IWindowManager.IMessage message)
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

        foreach (var item in _windowStack)
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"window stack [hash:{item.GetHashCode():X}]", item._hWindow, Environment.CurrentManagedThreadId);
        }

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

    private bool TryGetWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out NativeWindow window)
    {
        PrintWindowStack();
        PrintWindowMap();

        if (_windowMap.TryGetValue(hwnd, out window))
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"window map found. [hash:{window.GetHashCode():X}]", hwnd, Environment.CurrentManagedThreadId);
            return true;
        }

        return BindToWindowInStack(hwnd, out window);
    }

    private bool BindToWindowInStack(User32.HWND hwnd, [MaybeNullWhen(false)] out NativeWindow window)
    {
        if (_windowStack.TryPeek(out window))
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"window stack hash:{window.GetHashCode():X}", hwnd, Environment.CurrentManagedThreadId);

            if (window._hWindow.Handle != User32.HWND.None.Handle)
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
        window._hWindow = hwnd;
        if (_windowMap.TryAdd(hwnd, window))
        {
            _logger.LogWithHWnd(LogLevel.Information, "add window map", hwnd, Environment.CurrentManagedThreadId);
            return true;
        }
        else
        {
            throw new WindowException($"failed add window map hwnd:[{hwnd}]");
        }
    }

    private void WndProcBefore(User32.HWND hwnd, IWindowManager.IMessage message)
    {
        switch (message.Msg)
        {
            case 0x0001://WM_CREATE
                _logger.LogWithHWnd(LogLevel.Trace, $"WM_CREATE {message.WParam:X} {message.LParam:X}", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x0002://WM_DESTROY
                _logger.LogWithHWnd(LogLevel.Trace, $"WM_DESTROY {message.WParam:X} {message.LParam:X}", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x0006://WM_ACTIVATE
                _logger.LogWithHWnd(LogLevel.Trace, $"WM_ACTIVATE {message.WParam:X} {message.LParam:X}", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x0008://WM_KILLFOCUS
                _logger.LogWithHWnd(LogLevel.Trace, $"WM_KILLFOCUS {message.WParam:X} {message.LParam:X}", hwnd, Environment.CurrentManagedThreadId);
                break;

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

            case 0x0047://WM_WINDOWPOSCHANGED
                _logger.LogWithHWnd(LogLevel.Trace, "WM_WINDOWPOSCHANGED", hwnd, Environment.CurrentManagedThreadId);
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

            case 0x0086://WM_NCACTIVATE
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCACTIVATE", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x0111://WM_COMMAND
                _logger.LogWithHWnd(LogLevel.Trace, "WM_COMMAND", hwnd, Environment.CurrentManagedThreadId);
                OnWM_COMMAND(hwnd, message.WParam, message.LParam);
                break;

            case 0x0210://WM_PARENTNOTIFY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_PARENTNOTIFY", hwnd, Environment.CurrentManagedThreadId);
                OnWM_PARENTNOTIFY(hwnd, message.WParam, message.LParam);
                break;

            case 0x0281://WM_IME_SETCONTEXT
                _logger.LogWithHWnd(LogLevel.Trace, "WM_IME_SETCONTEXT", hwnd, Environment.CurrentManagedThreadId);
                break;

            case 0x0282://WM_IME_NOTIFY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_IME_NOTIFY", hwnd, Environment.CurrentManagedThreadId);
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

    private void OnWM_PARENTNOTIFY(User32.HWND hwnd, nint wParam, nint lParam)
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
                            // WindowManager経由で作ったWindow
                            // 　→ wndprocを差し替えても、windowを積んだstackは元のスレッドにあるので括り付けられない
                            // 　→ 元のスレッドのwndprocが動くわけでもないので、括り付けられないのは変わらない
                            // _windowMapに入れることにするか？

                        }
                    }

                    if (_windowMap.TryGetValue(childHWnd, out var childWindow))
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

                    if (_windowMap.TryGetValue(childHWnd, out var childWindow))
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


    private void BindWndProc(
        User32.HWND hwnd,
        User32.HWND childHWnd
    )
    {
        const int GWLP_WNDPROC = -4;

        if (_oldWndProcMap.TryAdd(childHWnd, nint.Zero))
        {
            var oldWndProc = ChildWindowSetWindowLong(childHWnd, GWLP_WNDPROC, _wndProc.FunctionPointer);

            if (oldWndProc == _wndProc.FunctionPointer)
            {
                //変更前・変更後のWndProcが同じだった＝WindowManager経由で作ったWindow　→　先にバイパスしたのにここに来たら異常事態
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
            OnWM_NCDESTROY(childHWnd);
        }
        else
        {
            _logger.LogWithHWnd(LogLevel.Warning, $"not found in old wndproc map childHWnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
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

    private class WndProcException(
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

        //TODO 何かのコンテキストに移す _windowClassManagerSynchronizationContextがよい？
        _logger.LogWithLine(LogLevel.Trace, $"WndProcError [handle:{hwnd}] [stack:{_windowStack.Count}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);
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
        //TODO 何かのコンテキストに移す
        _logger.LogWithLine(LogLevel.Trace, $"ThrowIfOccurredInWndProc [stack:{_windowStack.Count}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);
        lock (_wndProcExceptionStack)
        {
            if (_wndProcExceptionStack.Count == 1)
            {
                ExceptionDispatchInfo.Throw(_wndProcExceptionStack.Pop());
            }
            else if (_wndProcExceptionStack.Count > 1)
            {
                var aggregateException = new AggregateException(_wndProcExceptionStack.AsEnumerable());
                _wndProcExceptionStack.Clear();
                throw aggregateException;
            }
        }
    }

    //TODO 実験中
    private class WindowClassManagerSynchronizationContext : SynchronizationContext
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;

        private readonly DispatcherQueue _dispatcherQueue;

        internal WindowClassManagerSynchronizationContext(
            ILoggerFactory loggerFactory,
            DispatcherQueue dispatcherQueue
        )
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<WindowClassManagerSynchronizationContext>();
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
}
