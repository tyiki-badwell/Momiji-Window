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

    public Task<T> DispatchAsync<T>(Func<T> item)
    {
        return _windowManager.DispatchAsync(this, item);
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

    internal void CreateWindow(
        WindowClass windowClass,
        NativeWindow? parent = default
    )
    {
        // メッセージループに移行してからCreateWindowする
        var task = DispatchAsync(() =>
        {
            //TODO パラメーターにする
            var style = 0;
            var parentHWnd = User32.HWND.None;

            if (parent == null)
            {
                style = unchecked((int)0x10000000); //WS_VISIBLE
                                                    //style |= unchecked((int)0x80000000); //WS_POPUP
                style |= unchecked((int)0x00C00000); //WS_CAPTION
                style |= unchecked((int)0x00080000); //WS_SYSMENU
                style |= unchecked((int)0x00040000); //WS_THICKFRAME
                style |= unchecked((int)0x00020000); //WS_MINIMIZEBOX
                style |= unchecked((int)0x00010000); //WS_MAXIMIZEBOX
            }
            else
            {
                parentHWnd = parent._hWindow;

                style = unchecked((int)0x10000000); //WS_VISIBLE
                style |= unchecked((int)0x40000000); //WS_CHILD
            }

            var CW_USEDEFAULT = unchecked((int)0x80000000);

            //TODO DPI awareness 
            using var behaviorSetter = new MixedThreadDpiHostingBehaviorSetter(_loggerFactory);

            _logger.LogWithLine(LogLevel.Trace, "CreateWindowEx", Environment.CurrentManagedThreadId);
            var hWindow =
                User32.CreateWindowExW(
                    0,
                    windowClass.ClassName,
                    nint.Zero, //TODO ウインドウタイトル
                    style,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    parentHWnd,
                    nint.Zero, //TODO メニューハンドル
                    windowClass.HInstance,
                    nint.Zero
                );
            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Information, "CreateWindowEx result", hWindow, error.ToString(), Environment.CurrentManagedThreadId);
            if (hWindow.Handle == User32.HWND.None.Handle)
            {
                hWindow = default;
                _windowManager.ThrowIfOccurredInWndProc();
                throw new WindowException("CreateWindowEx failed", error);
            }

            var behavior = User32.GetWindowDpiHostingBehavior(hWindow);
            _logger.LogWithLine(LogLevel.Trace, $"GetWindowDpiHostingBehavior {behavior}", Environment.CurrentManagedThreadId);

            return hWindow;
        });
        var handle = task.Result;
        _logger.LogWithHWnd(LogLevel.Information, "CreateWindow end", _hWindow, Environment.CurrentManagedThreadId);
    }

    internal void CreateWindow(
        NativeWindow parent,
        string className
    )
    {
        var parentHWnd = parent._hWindow;

        // メッセージループに移行してからCreateWindowする
        var task = DispatchAsync(() =>
        {
            //TODO パラメーターにする
            var style = 0;
            style = unchecked((int)0x10000000); //WS_VISIBLE
            style |= unchecked((int)0x40000000); //WS_CHILD

            var CW_USEDEFAULT = unchecked((int)0x80000000);

            var lpszClassName = Marshal.StringToHGlobalUni(className);

            _logger.LogWithLine(LogLevel.Trace, $"CreateWindowEx {className}", Environment.CurrentManagedThreadId);

            try
            {
                var hWindow =
                    User32.CreateWindowExW(
                        0,
                        lpszClassName,
                        nint.Zero, //TODO ウインドウタイトル
                        style,
                        CW_USEDEFAULT,
                        CW_USEDEFAULT,
                        CW_USEDEFAULT,
                        CW_USEDEFAULT,
                        parentHWnd,
                        nint.Zero, //TODO 子ウインドウ識別子
                        nint.Zero,
                        nint.Zero
                    );
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Information, "CreateWindowEx result", hWindow, error.ToString(), Environment.CurrentManagedThreadId);
                if (hWindow.Handle == User32.HWND.None.Handle)
                {
                    hWindow = default;
                    _windowManager.ThrowIfOccurredInWndProc();
                    throw new WindowException($"CreateWindowEx failed [{className}]", error);
                }

                var behavior = User32.GetWindowDpiHostingBehavior(hWindow);
                _logger.LogWithLine(LogLevel.Trace, $"GetWindowDpiHostingBehavior {behavior}", Environment.CurrentManagedThreadId);

                return hWindow;
            }
            finally
            {
                Marshal.FreeHGlobal(lpszClassName);
            }
        });
        var handle = task.Result;
        _logger.LogWithHWnd(LogLevel.Information, "CreateWindow end", _hWindow, Environment.CurrentManagedThreadId);
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
        _logger.LogWithMsg(LogLevel.Trace, "SendMessageW", _hWindow, (uint)nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
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
            throw new WindowException("SendMessageW failed", error);
        }
        return result;
    }

    public void PostMessage(
        int nMsg,
        nint wParam,
        nint lParam
    )
    {
        _logger.LogWithMsg(LogLevel.Trace, "PostMessageW", _hWindow, (uint)nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
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
                throw new WindowException("PostMessageW failed", error);
            }
        }
    }

    public bool Move(
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        return DispatchAsync(() =>
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
        }).Result;
    }

    public bool Show(
        int cmdShow
    )
    {
        return DispatchAsync(() =>
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
                throw new WindowException("ShowWindow failed", error);
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
        }).Result;
    }

    public bool SetWindowStyle(
        int style
    )
    {
        return DispatchAsync(() =>
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
        }).Result;
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

    internal nint WndProc(uint msg, nint wParam, nint lParam, out bool handled)
    {
        handled = false;
        _logger.LogWithMsg(LogLevel.Trace, "WndProc", _hWindow, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        switch (msg)
        {
            case 0x0082://WM_NCDESTROY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCDESTROY", _hWindow, Environment.CurrentManagedThreadId);
                _hWindow = default;
                break;
        }

        if (_onMessage != null)
        {
            var result = _onMessage.Invoke(this, (int)msg, wParam, lParam, out handled);
            if (handled)
            {
                return result;
            }
        }

        return nint.Zero;
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
