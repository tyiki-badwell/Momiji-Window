using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal class NativeWindow : IWindow
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly WindowManager _windowManager;

    private readonly IWindowManager.OnMessage? _onMessage;

    internal User32.HWND _hWindow;
    public nint Handle => _hWindow.Handle;

    internal NativeWindow(
        ILoggerFactory loggerFactory,
        WindowManager windowManager,
        IWindowManager.OnMessage? onMessage = default
    )
    {
        //TODO UIAutomation

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<NativeWindow>();
        _windowManager = windowManager;

        _onMessage = onMessage;
    }

    public override string ToString()
    {
        return $"NativeWindow[HWND:{_hWindow}]";
    }

    public ValueTask<T> DispatchAsync<T>(Func<IWindow, T> item)
    {
        return _windowManager.DispatchAsync(item, this);
    }

    internal class MixedThreadDpiHostingBehaviorSetter : IDisposable
    {
        private readonly ILogger _logger;

        private bool _disposed;
        private readonly User32.DPI_HOSTING_BEHAVIOR _oldBehavior;

        public MixedThreadDpiHostingBehaviorSetter(
            ILoggerFactory loggerFactory
        )
        {
            _logger = loggerFactory.CreateLogger<MixedThreadDpiHostingBehaviorSetter>();
            _oldBehavior = User32.SetThreadDpiHostingBehavior(User32.DPI_HOSTING_BEHAVIOR.MIXED);
            _logger.LogWithLine(LogLevel.Trace, $"ON SetThreadDpiHostingBehavior [{_oldBehavior} -> MIXED]", Environment.CurrentManagedThreadId);
        }

        ~MixedThreadDpiHostingBehaviorSetter()
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

            if (_oldBehavior != User32.DPI_HOSTING_BEHAVIOR.INVALID)
            {
                var oldBehavior = User32.SetThreadDpiHostingBehavior(_oldBehavior);
                _logger.LogWithLine(LogLevel.Trace, $"OFF SetThreadDpiHostingBehavior [{oldBehavior} -> {_oldBehavior}]", Environment.CurrentManagedThreadId);
            }

            _disposed = true;
        }
    }

    internal class StringToHGlobalUni(
        string text
    ) : IDisposable
    {
        public readonly nint Handle = Marshal.StringToHGlobalUni(text);

        private bool _disposed;

        ~StringToHGlobalUni()
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

            if (Handle != nint.Zero)
            {
                Marshal.FreeHGlobal(Handle);
            }

            _disposed = true;
        }
    }

    internal void CreateWindow(
        string windowTitle,
        NativeWindow? parent = default
    )
    {
        // メッセージループに移行してからCreateWindowする
        var task = DispatchAsync((window) =>
        {
            //TODO パラメーターにする
            var exStyle = 0U;
            var style = 0U;

            var parentHWnd = User32.HWND.None;
            if (parent == null)
            {
                style = 0x10000000U; //WS_VISIBLE
                //style |= 0x80000000U; //WS_POPUP
                style |= 0x00C00000U; //WS_CAPTION
                style |= 0x00080000U; //WS_SYSMENU
                style |= 0x00040000U; //WS_THICKFRAME
                style |= 0x00020000U; //WS_MINIMIZEBOX
                style |= 0x00010000U; //WS_MAXIMIZEBOX
            }
            else
            {
                parentHWnd = parent._hWindow;

                style = 0x10000000U; //WS_VISIBLE
                style |= 0x40000000U; //WS_CHILD
            }

            var CW_USEDEFAULT = unchecked((int)0x80000000);

            //TODO DPI awareness 
            using var behaviorSetter = new MixedThreadDpiHostingBehaviorSetter(_loggerFactory);

            //ウインドウタイトル
            using var lpszWindowName = new StringToHGlobalUni(windowTitle);

            //TODO WindowClassの取り出し方変える
            var windowClass = _windowManager.WindowClassManager.WindowClass;

            return CreateWindowImpl(
                exStyle,
                windowClass.ClassName,
                lpszWindowName.Handle, 
                style,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                parentHWnd,
                nint.Zero, //TODO メニューハンドル
                windowClass.HInstance
            );
        });

        var handle = task.AsTask().Result;

        _logger.LogWithHWnd(LogLevel.Information, $"CreateWindow end [{handle}]", _hWindow, Environment.CurrentManagedThreadId);
    }

    internal void CreateWindow(
        NativeWindow parent,
        string className,
        string windowTitle
    )
    {
        var parentHWnd = parent._hWindow;

        // メッセージループに移行してからCreateWindowする
        var task = DispatchAsync((window) =>
        {
            //TODO パラメーターにする
            var exStyle = 0U;
            var style = 0U;

            style = 0x10000000U; //WS_VISIBLE
            style |= 0x40000000U; //WS_CHILD

            var CW_USEDEFAULT = unchecked((int)0x80000000);

            using var lpszClassName = new StringToHGlobalUni(className);
            using var lpszWindowName = new StringToHGlobalUni(windowTitle);

            return CreateWindowImpl(
                exStyle,
                lpszClassName.Handle,
                lpszWindowName.Handle,
                style,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                parentHWnd,
                nint.Zero, //TODO 子ウインドウ識別子
                nint.Zero
            );
        });

        var handle = task.AsTask().Result;

        _logger.LogWithHWnd(LogLevel.Information, $"CreateWindow end [{handle}]", _hWindow, Environment.CurrentManagedThreadId);
    }

    private User32.HWND CreateWindowImpl(
        uint dwExStyle,
        nint lpszClassName,
        nint lpszWindowName,
        uint style,
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
            _windowManager.WindowClassManager.ThrowIfOccurredInWndProc();
            throw error;
        }

        var behavior = User32.GetWindowDpiHostingBehavior(hWindow);
        _logger.LogWithLine(LogLevel.Trace, $"GetWindowDpiHostingBehavior {behavior}", Environment.CurrentManagedThreadId);

        return hWindow;
    }

    public bool Close()
    {
        _logger.LogWithHWnd(LogLevel.Information, "Close", _hWindow, Environment.CurrentManagedThreadId);
        SendMessage(
            0x0010, //WM_CLOSE
            nint.Zero,
            nint.Zero
        );

        return true;
    }

    public nint SendMessage(
        int nMsg,
        nint wParam,
        nint lParam
    )
    {
        //TODO SendMessage/SendNotifyMessage/SendMessageCallback/SendMessageTimeout の使い分け？
        _logger.LogWithMsg(LogLevel.Trace, "SendMessageW", _hWindow, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
        var result =
            User32.SendMessageW(
                _hWindow,
                (uint)nMsg,
                wParam,
                lParam
            );
        var error = new Win32Exception();
        _logger.LogWithHWndAndError(LogLevel.Trace, $"SendMessageW result:[{result}]", _hWindow, error.ToString(), Environment.CurrentManagedThreadId);

        if (error.NativeErrorCode != 0)
        {
            //UIPIに引っかかると5が返ってくる
            throw error;
        }
        return result;
    }

    public void PostMessage(
        int nMsg,
        nint wParam,
        nint lParam
    )
    {
        _logger.LogWithMsg(LogLevel.Trace, "PostMessageW", _hWindow, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
        var result =
            User32.PostMessageW(
                _hWindow,
                (uint)nMsg,
                wParam,
                lParam
            );
        var error = new Win32Exception();
        _logger.LogWithHWndAndError(LogLevel.Trace, $"PostMessageW result:[{result}]", _hWindow, error.ToString(), Environment.CurrentManagedThreadId);

        if (!result)
        {
            if (error.NativeErrorCode != 0)
            {
                throw error;
            }
        }
    }

    public bool ReplyMessage(
        nint lResult
    )
    {
        //TODO 何でもReplyMessageしてOKというわけではないので、チェックが要る？自己責任？（例：GetWindowTextでWM_GETTEXTが来たときの戻り値が期待値と異なってしまう）
        var ret = User32.InSendMessageEx(nint.Zero);
        _logger.LogWithLine(LogLevel.Trace, $"InSendMessageEx {ret:X}", Environment.CurrentManagedThreadId);
        if ((ret & (0x00000008 | 0x00000001)) == 0x00000001) //ISMEX_SEND
        {
            _logger.LogWithLine(LogLevel.Trace, "ISMEX_SEND", Environment.CurrentManagedThreadId);
            var ret2 = User32.ReplyMessage(lResult);
            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Trace, $"ReplyMessage {ret2}", _hWindow, error.ToString(), Environment.CurrentManagedThreadId);
            return ret2;
        }
        return false;
    }

    public ValueTask<bool> MoveAsync(
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        return DispatchAsync((window) =>
        {
            return ((NativeWindow)window).MoveImpl(
                x,
                y,
                width,
                height,
                repaint
            );
        });
    }

    private bool MoveImpl(
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        _logger.LogWithHWnd(LogLevel.Information, $"MoveWindow x:[{x}] y:[{y}] width:[{width}] height:[{height}] repaint:[{repaint}]", _hWindow, Environment.CurrentManagedThreadId);
        var result =
            User32.MoveWindow(
                _hWindow,
                x,
                y,
                width,
                height,
                repaint
        );

        if (!result)
        {
            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Error, "MoveWindow", _hWindow, error.ToString(), Environment.CurrentManagedThreadId);
        }
        return result;
    }

    public ValueTask<bool> ShowAsync(
        int cmdShow
    )
    {
        return DispatchAsync((window) =>
        {
            return ((NativeWindow)window).ShowImpl(cmdShow);
        });
    }

    private bool ShowImpl(
        int cmdShow
    )
    {
        _logger.LogWithHWnd(LogLevel.Information, $"ShowWindow cmdShow:[{cmdShow}]", _hWindow, Environment.CurrentManagedThreadId);
        var result =
            User32.ShowWindow(
                _hWindow,
                cmdShow
            );

        var error = new Win32Exception();

        //result=0: 実行前は非表示だった/ <>0:実行前から表示されていた
        _logger.LogWithHWndAndError(LogLevel.Information, $"ShowWindow result:[{result}]", _hWindow, error.ToString(), Environment.CurrentManagedThreadId);

        if (error.NativeErrorCode == 1400) // ERROR_INVALID_WINDOW_HANDLE
        {
            throw error;
        }

        {
            var wndpl = new User32.WINDOWPLACEMENT()
            {
                length = Marshal.SizeOf<User32.WINDOWPLACEMENT>()
            };
            User32.GetWindowPlacement(_hWindow, ref wndpl);

            _logger.LogWithHWnd(LogLevel.Information, $"GetWindowPlacement result cmdShow:[{cmdShow}] -> wndpl:[{wndpl}]", _hWindow, Environment.CurrentManagedThreadId);
        }

        return result;
    }

    public ValueTask<bool> SetWindowStyleAsync(
        int style
    )
    {
        return DispatchAsync((window) =>
        {
            return ((NativeWindow)window).SetWindowStyleImpl(style);
        });
    }

    private bool SetWindowStyleImpl(
        int style
    )
    {
        //TODO DPI対応
        var clientRect = new User32.RECT();

        {
            var result = User32.GetClientRect(_hWindow, ref clientRect);
            if (!result)
            {
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Error, "GetClientRect failed", _hWindow, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var result = User32.AdjustWindowRectExForDpi(ref clientRect, style, false, 0, 96);
            if (!result)
            {
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Error, "AdjustWindowRectExForDpi failed", _hWindow, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var (result, error) = SetWindowLong(-16, new nint(style)); //GWL_STYLE
            if (result == nint.Zero && error.NativeErrorCode != 0)
            {
                _logger.LogWithHWndAndError(LogLevel.Error, "SetWindowLong failed", _hWindow, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var width = clientRect.right - clientRect.left;
            var height = clientRect.bottom - clientRect.top;

            _logger.LogWithHWnd(LogLevel.Information, $"SetWindowPos", _hWindow, Environment.CurrentManagedThreadId);
            var result =
                User32.SetWindowPos(
                        _hWindow,
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
                _logger.LogWithHWndAndError(LogLevel.Error, "SetWindowPos failed", _hWindow, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        return true;
    }

    private (nint, Win32Exception) SetWindowLong(
        int nIndex,
        nint dwNewLong
    )
    {
        Marshal.SetLastPInvokeError(0);
        _logger.LogWithHWnd(LogLevel.Information, $"SetWindowLong nIndex:[{nIndex:X}] dwNewLong:[{dwNewLong:X}]", _hWindow, Environment.CurrentManagedThreadId);
        var result =
            Environment.Is64BitProcess
                ? User32.SetWindowLongPtrW(_hWindow, nIndex, dwNewLong)
                : User32.SetWindowLongW(_hWindow, nIndex, dwNewLong);
        var error = new Win32Exception();

        return (result, error);
    }

    internal void WndProc(IWindowManager.Message message)
    {
        _logger.LogWithMsg(LogLevel.Trace, "WndProc", _hWindow, message.Msg, message.WParam, message.LParam, Environment.CurrentManagedThreadId);

        switch (message.Msg)
        {
            case 0x0082://WM_NCDESTROY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCDESTROY", _hWindow, Environment.CurrentManagedThreadId);
                _hWindow = default;
                break;
        }

        //TODO ここで同期コンテキスト？
        _onMessage?.Invoke(this, message);
    }
}
        /*
        //表示していないとwinrt::hresult_invalid_argumentになる
        var item = GraphicsCaptureItemInterop.CreateForWindow(hWindow);
        item.Closed += (item, obj) => {
            Logger.Log(LogLevel.Information, "[window] GraphicsCaptureItem closed");
        };

        unsafe
        {

            Windows.Win32.Graphics.Direct3D11.ID3D11Device* d;

            Windows.Win32.PInvoke.D3D11CreateDevice(
                null,
                Windows.Win32.Graphics.Direct3D.D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                null,
                Windows.Win32.Graphics.Direct3D11.D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                null,
                11,
                &d,
                null,
                null
                );
            Windows.Win32.PInvoke.CreateDirect3D11DeviceFromDXGIDevice(null, a.ObjRef);
        }

        IInspectable a;

        IDirect3DDevice canvas;

        using var pool =
            Direct3D11CaptureFramePool.Create(
                canvas,
                Windows.Graphics.DirectX.DirectXPixelFormat.R8G8B8A8UIntNormalized,
                2,
                item.Size
            );

        pool.FrameArrived += (pool, obj) => {
            using var frame = pool.TryGetNextFrame();
            //frame.Surface;
            Logger.Log(LogLevel.Information, "[window] FrameArrived");

        };

        using var session = pool.CreateCaptureSession(item);
        session.StartCapture();
        Logger.Log(LogLevel.Information, "[window] StartCapture");
        */
