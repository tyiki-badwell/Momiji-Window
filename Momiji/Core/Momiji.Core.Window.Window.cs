using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Log;
using static System.Runtime.InteropServices.JavaScript.JSType;
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

    internal void CreateWindow(
        WindowClass windowClass
    )
    {
        // メッセージループに移行してからCreateWindowする
        var task = DispatchAsync(() =>
        {
            //TODO パラメーターにする
            var style = unchecked((int)0x10000000); //WS_VISIBLE
            //style |= unchecked((int)0x80000000); //WS_POPUP
            style |= unchecked((int)0x00C00000); //WS_CAPTION
            style |= unchecked((int)0x00080000); //WS_SYSMENU
            style |= unchecked((int)0x00040000); //WS_THICKFRAME
            style |= unchecked((int)0x00020000); //WS_MINIMIZEBOX
            style |= unchecked((int)0x00010000); //WS_MAXIMIZEBOX

            var CW_USEDEFAULT = unchecked((int)0x80000000);

            //TODO DPI awareness 

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
                    User32.HWND.None,
                    nint.Zero, //TODO メニューハンドル
                    windowClass.HInstance,
                    nint.Zero
                );
            var error = Marshal.GetLastPInvokeError();
            _logger.LogWithHWndAndErrorId(LogLevel.Information, "CreateWindowEx result", hWindow, error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);
            if (hWindow.Handle == User32.HWND.None.Handle)
            {
                hWindow = default;
                _windowManager.ThrowIfOccurredInWndProc();
                throw new WindowException($"CreateWindowEx failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }

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
            var style = unchecked((int)0x10000000); //WS_VISIBLE
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
                var error = Marshal.GetLastPInvokeError();
                _logger.LogWithHWndAndErrorId(LogLevel.Information, "CreateWindowEx result", hWindow, error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);
                if (hWindow.Handle == User32.HWND.None.Handle)
                {
                    hWindow = default;
                    _windowManager.ThrowIfOccurredInWndProc();
                    throw new WindowException($"CreateWindowEx failed [{error} {Marshal.GetPInvokeErrorMessage(error)}][{className}]");
                }
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
        var error = Marshal.GetLastPInvokeError();
        _logger.LogWithHWndAndErrorId(LogLevel.Trace, $"SendMessageW result:[{result}]", _hWindow, error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);

        if (error != 0)
        {
            //UIPIに引っかかると5が返ってくる
            throw new WindowException($"SendMessageW failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
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
        var error = Marshal.GetLastPInvokeError();
        _logger.LogWithHWndAndErrorId(LogLevel.Trace, $"PostMessageW result:[{result}]", _hWindow, error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);

        if (!result)
        {
            if (error != 0)
            {
                throw new WindowException($"PostMessageW failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
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
                var error = Marshal.GetLastPInvokeError();
                _logger.LogWithHWndAndErrorId(LogLevel.Error, "MoveWindow", _hWindow, error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);
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

            var error = Marshal.GetLastPInvokeError();

            //result=0: 実行前は非表示だった/ <>0:実行前から表示されていた
            _logger.LogWithHWndAndErrorId(LogLevel.Information, $"ShowWindow result:[{result}]", _hWindow, error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);

            if (error == 1400) // ERROR_INVALID_WINDOW_HANDLE
            {
                throw new WindowException($"ShowWindow failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
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
                    var error = Marshal.GetLastPInvokeError();
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "GetClientRect failed", _hWindow, error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);
                    return false;
                }
            }

            {
                var result = User32.AdjustWindowRect(ref clientRect, style, false);
                if (!result)
                {
                    var error = Marshal.GetLastPInvokeError();
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "AdjustWindowRect failed", _hWindow, error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);
                    return false;
                }
            }

            {
                var (result, error) = SetWindowLong(-16, new nint(style)); //GWL_STYLE
                if (result == nint.Zero && error != 0)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "SetWindowLong failed", _hWindow, error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);
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
                    var error = Marshal.GetLastPInvokeError();
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "SetWindowPos failed", _hWindow, error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);
                    return false;
                }
            }

            return true;
        }).Result;
    }

    private (nint, int) SetWindowLong(
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
        var error = Marshal.GetLastPInvokeError();

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
