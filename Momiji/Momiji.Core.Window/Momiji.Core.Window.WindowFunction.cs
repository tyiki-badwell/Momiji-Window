using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal interface IWindowFunction
{
    void PostMessage(
        User32.HWND hwnd,
        uint nMsg,
        nint wParam,
        nint lParam
    );

    nint SendMessage(
        User32.HWND hwnd,
        uint nMsg,
        nint wParam,
        nint lParam
    );

    User32.HWND CreateWindow(
        User32.WS_EX dwExStyle,
        nint lpszClassName,
        string windowTitle,
        User32.WS style,
        int x,
        int y,
        int width,
        int height,
        User32.HWND hwndParent,
        nint hMenu,
        nint hInst
    );
    bool MoveWindow(
        User32.HWND hwnd,
        int x,
        int y,
        int width,
        int height,
        bool repaint
    );

    bool ShowWindow(
        User32.HWND hwnd,
        int cmdShow
    );

    bool SetWindowStyle(
        User32.HWND hwnd,
        int style
    );

    (nint, Win32Exception) SetWindowLong(
        User32.HWND hwnd,
        int nIndex,
        nint dwNewLong
    );
}

internal sealed partial class WindowFunction : IWindowFunction
{
    private readonly ILogger<WindowFunction> _logger;
    private readonly IUIThreadActivator _uiThreadActivator;

    private readonly IWindowProcedure _windowProcedure;
    public WindowFunction(
        ILogger<WindowFunction> logger,
        IUIThreadActivator uiThreadActivator,
        IWindowProcedure windowProcedure
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiThreadActivator);
        ArgumentNullException.ThrowIfNull(windowProcedure);

        _logger = logger;
        _uiThreadActivator = uiThreadActivator;
        _windowProcedure = windowProcedure;
    }

    public nint SendMessage(
        User32.HWND hwnd,
        uint nMsg,
        nint wParam,
        nint lParam
    )
    {
        _uiThreadActivator.ThrowIfNoActive();

        //TODO SendMessage/SendNotifyMessage/SendMessageCallback/SendMessageTimeout の使い分け？
        _logger.LogWithWndProcParam(LogLevel.Trace, "SendMessageW", hwnd, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
        var result =
            User32.SendMessageW(
                hwnd,
                nMsg,
                wParam,
                lParam
            );

        var lastError = Marshal.GetLastPInvokeError();

        if (lastError != 0)
        {
            //UIPIに引っかかると5が返ってくる

            if (nMsg == 0x0010 && lastError == 1400)
            {
                //直接呼出しで、WM_CLOSEで1400が返ってくるのはOKにする
                //TODO 直接呼出ししたことの判定方法　CLOSEのキャンセルもある　そもそもココで判定したくない
            }
            else
            {
                var error = new Win32Exception(lastError);
                _logger.LogWithHWndAndError(LogLevel.Error, $"SendMessageW result:[{result}]", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
                throw error;
            }
        }
        return result;
    }

    public void PostMessage(
        User32.HWND hwnd,
        uint nMsg,
        nint wParam,
        nint lParam
    )
    {
        _uiThreadActivator.ThrowIfNoActive();

        bool result;

        if ( 
                (hwnd.Handle == User32.HWND.None.Handle)
           &&   (!_uiThreadActivator.IsActivatedThread)
        )
        {
            _logger.LogWithWndProcParam(LogLevel.Trace, "PostThreadMessageW", hwnd, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
            result =
                User32.PostThreadMessageW(
                    _uiThreadActivator.NativeThreadId,
                    nMsg,
                    wParam,
                    lParam
                );
        }
        else
        {
            _logger.LogWithWndProcParam(LogLevel.Trace, "PostMessageW", hwnd, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
            result =
                User32.PostMessageW(
                    hwnd,
                    nMsg,
                    wParam,
                    lParam
                );
        }

        if (result)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogWithHWnd(LogLevel.Trace, $"PostMessageW result:[{result}]", hwnd, Environment.CurrentManagedThreadId);
            }
        }
        else
        {
            var lastError = Marshal.GetLastPInvokeError();
            var error = new Win32Exception(lastError);
            _logger.LogWithHWndAndError(LogLevel.Error, $"PostMessageW result:[{result}]", hwnd, error.ToString(), Environment.CurrentManagedThreadId);

            throw error;
        }
    }

    internal readonly ref struct SwitchThreadDpiHostingBehaviorMixedRAII
    {
        private readonly ILogger _logger;
        private readonly User32.DPI_HOSTING_BEHAVIOR _oldBehavior;

        public SwitchThreadDpiHostingBehaviorMixedRAII(
            ILogger logger
        )
        {
            _logger = logger;
            _oldBehavior = User32.SetThreadDpiHostingBehavior(User32.DPI_HOSTING_BEHAVIOR.MIXED);
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogWithLine(LogLevel.Trace, $"ON SetThreadDpiHostingBehavior [{_oldBehavior} -> MIXED]", Environment.CurrentManagedThreadId);
            }
        }

        public void Dispose()
        {
            if (_oldBehavior != User32.DPI_HOSTING_BEHAVIOR.INVALID)
            {
                var oldBehavior = User32.SetThreadDpiHostingBehavior(_oldBehavior);
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogWithLine(LogLevel.Trace, $"OFF SetThreadDpiHostingBehavior [{oldBehavior} -> {_oldBehavior}]", Environment.CurrentManagedThreadId);
                }
            }
        }
    }

    public User32.HWND CreateWindow(
        User32.WS_EX dwExStyle,
        nint lpszClassName,
        string windowTitle,
        User32.WS style,
        int x,
        int y,
        int width,
        int height,
        User32.HWND hwndParent,
        nint hMenu,
        nint hInst
    )
    {
        //UIスレッドで呼び出す必要アリ
        _uiThreadActivator.ThrowIfCalledFromOtherThread();

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithLine(LogLevel.Trace, $"CreateWindowEx {dwExStyle} {style}", Environment.CurrentManagedThreadId);
        }

        //DPI awareness 
        using var switchBehavior = new SwitchThreadDpiHostingBehaviorMixedRAII(_logger);

        //ウインドウタイトル
        using var lpszWindowName = new HGlobalUnicodeStringScope(windowTitle, _logger);

        var hWindow =
            User32.CreateWindowExW(
                dwExStyle,
                lpszClassName,
                lpszWindowName.Handle,
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

        if (hWindow.Handle == User32.HWND.None.Handle)
        {
            var lastError = Marshal.GetLastPInvokeError();
            var error = new Win32Exception(lastError);
            _logger.LogWithHWndAndError(LogLevel.Error, "CreateWindowEx result", hWindow, error.ToString(), Environment.CurrentManagedThreadId);

            _windowProcedure.ThrowIfOccurredInWndProc();
            throw error;
        }
        else
        {
            _logger.LogWithHWnd(LogLevel.Trace, "CreateWindowEx ok", hWindow, Environment.CurrentManagedThreadId);
        }

        var behavior = User32.GetWindowDpiHostingBehavior(hWindow);
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithLine(LogLevel.Trace, $"GetWindowDpiHostingBehavior {behavior}", Environment.CurrentManagedThreadId);
        }

        return hWindow;
    }

    public bool MoveWindow(
        User32.HWND hwnd,
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogWithHWnd(LogLevel.Information, $"MoveWindow x:[{x}] y:[{y}] width:[{width}] height:[{height}] repaint:[{repaint}]", hwnd, Environment.CurrentManagedThreadId);
        }
        var result =
            User32.MoveWindow(
                hwnd,
                x,
                y,
                width,
                height,
                repaint
        );

        if (!result)
        {
            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Error, "MoveWindow", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
        }
        return result;
    }
    public bool ShowWindow(
        User32.HWND hwnd,
        int cmdShow
    )
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogWithHWnd(LogLevel.Information, $"ShowWindow cmdShow:[{cmdShow}]", hwnd, Environment.CurrentManagedThreadId);
        }
        var result =
            User32.ShowWindow(
                hwnd,
                cmdShow
            );

        var errorCode = Marshal.GetLastPInvokeError();

        //result=0: 実行前は非表示だった/ <>0:実行前から表示されていた
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogWithHWndAndError(LogLevel.Information, $"ShowWindow result:[{result}]", hwnd, errorCode, Environment.CurrentManagedThreadId);
        }

        if (errorCode == 1400) // ERROR_INVALID_WINDOW_HANDLE
        {
            var error = new Win32Exception(errorCode);
            throw error;
        }

        {
            var wndpl = new User32.WINDOWPLACEMENT()
            {
                length = Marshal.SizeOf<User32.WINDOWPLACEMENT>()
            };
            User32.GetWindowPlacement(hwnd, ref wndpl);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogWithHWnd(LogLevel.Information, $"GetWindowPlacement result cmdShow:[{cmdShow}] -> wndpl:[{wndpl}]", hwnd, Environment.CurrentManagedThreadId);
            }
        }

        return result;
    }

    public bool SetWindowStyle(
        User32.HWND hwnd,
        int style
    )
    {
        //TODO DPI対応
        var clientRect = new User32.RECT();

        {
            var result = User32.GetClientRect(hwnd, ref clientRect);
            if (!result)
            {
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Error, "GetClientRect failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var result = User32.AdjustWindowRectExForDpi(ref clientRect, style, false, 0, 96);
            if (!result)
            {
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Error, "AdjustWindowRectExForDpi failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var (result, error) = SetWindowLong(hwnd, -16, new nint(style)); //GWL_STYLE
            if (result == nint.Zero && error.NativeErrorCode != 0)
            {
                _logger.LogWithHWndAndError(LogLevel.Error, "SetWindowLong failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var width = clientRect.right - clientRect.left;
            var height = clientRect.bottom - clientRect.top;

            _logger.LogWithHWnd(LogLevel.Information, "SetWindowPos", hwnd, Environment.CurrentManagedThreadId);
            var result =
                User32.SetWindowPos(
                        hwnd,
                        User32.HWND.None,
                        0,
                        0,
                        width,
                        height,
                        0x0002 //SWP_NOMOVE
                               //| 0x0001 //SWP_NOSIZE
                        | 0x0004 //SWP_NOZORDER
                        | 0x0020 //SWP_FRAMECHANGED
                    );

            if (!result)
            {
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Error, "SetWindowPos failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        return true;
    }

    public (nint, Win32Exception) SetWindowLong(
        User32.HWND hwnd,
        int nIndex,
        nint dwNewLong
    )
    {
        var isWindowUnicode = (hwnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(hwnd);

        //SetWindowLong～のエラー判定のために、エラーコードをリセットする
        Marshal.SetLastPInvokeError(0);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogWithHWnd(LogLevel.Information, $"SetWindowLong nIndex:[{nIndex:X}] dwNewLong:[{dwNewLong:X}]", hwnd, Environment.CurrentManagedThreadId);
        }

        var result = isWindowUnicode
                        ? Environment.Is64BitProcess
                            ? User32.SetWindowLongPtrW(hwnd, nIndex, dwNewLong)
                            : User32.SetWindowLongW(hwnd, nIndex, dwNewLong)
                        : Environment.Is64BitProcess
                            ? User32.SetWindowLongPtrA(hwnd, nIndex, dwNewLong)
                            : User32.SetWindowLongA(hwnd, nIndex, dwNewLong)
                        ;
        var errorCode = Marshal.GetLastPInvokeError();
        var error = new Win32Exception(errorCode);
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogWithHWndAndError(LogLevel.Information, $"SetWindowLong result:[{result:X}]", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
        }
        return (result, error);
    }
}
