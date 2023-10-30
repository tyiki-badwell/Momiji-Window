using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
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

    private readonly ConcurrentDictionary<User32.HWND, (nint, PinnedDelegate<User32.WNDPROC>)> _oldWndProcMap = new();
    internal NativeWindow(
        ILoggerFactory loggerFactory,
        WindowManager windowManager,
        IWindowManager.OnMessage? onMessage = default
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<NativeWindow>();
        _windowManager = windowManager;

        _onMessage = onMessage;

        _logger.LogInformation("Create end");
    }

    public T Dispatch<T>(Func<T> item)
    {
        return _windowManager.DispatchAsync(item).Result;
    }

    internal void CreateWindow(
        WindowClass windowClass,
        NativeWindow? parent = default
    )
    {
        var thisHashCode = GetHashCode();
        var parentHWnd = (parent == null) ? User32.HWND.None : parent._hWindow;

        // メッセージループに移行してからCreateWindowする
        Dispatch(() => {
            //TODO パラメーターにする
            var style = unchecked((int)0x10000000); //WS_VISIBLE
            if (parentHWnd.Handle == User32.HWND.None.Handle)
            {
                //style |= unchecked((int)0x80000000); //WS_POPUP
                style |= unchecked((int)0x00C00000); //WS_CAPTION
                style |= unchecked((int)0x00080000); //WS_SYSMENU
                style |= unchecked((int)0x00040000); //WS_THICKFRAME
                style |= unchecked((int)0x00020000); //WS_MINIMIZEBOX
                style |= unchecked((int)0x00010000); //WS_MAXIMIZEBOX
            }
            else
            {
                style |= unchecked((int)0x40000000); //WS_CHILD
            }

            var CW_USEDEFAULT = unchecked((int)0x80000000);

            //TODO DPI awareness 
            
            _logger.LogWithThreadId(LogLevel.Trace, "CreateWindowEx", Environment.CurrentManagedThreadId);
            var hWindow =
                User32.CreateWindowExW(
                    0,
                    windowClass.ClassName,
                    nint.Zero,
                    style,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    parentHWnd,
                    nint.Zero,
                    windowClass.HInstance,
                    new nint(thisHashCode)
                );
            var error = Marshal.GetLastPInvokeError();
            _logger.LogWithHWndAndErrorId(LogLevel.Information, "CreateWindowEx result", hWindow, error);
            if (hWindow.Handle == User32.HWND.None.Handle)
            {
                hWindow = default;
                throw new WindowException($"CreateWindowEx failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }

            return hWindow;
        });
        _logger.LogWithHWndAndThreadId(LogLevel.Information, "CreateWindow end", _hWindow, Environment.CurrentManagedThreadId);
    }

    public bool Close()
    {
        _logger.LogInformation($"Close {_hWindow}");
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
        //違うスレッドから実行しても、PeekMessageの中でWndProcが直接呼ばれるようで、InSendMessageExの判定に移らない様子
        _logger.LogMsgWithThreadId(LogLevel.Trace, "SendMessageW", _hWindow, (uint)nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
        var result =
            User32.SendMessageW(
                _hWindow,
                (uint)nMsg,
                wParam,
                lParam
            );
        var error = Marshal.GetLastPInvokeError();
        _logger.LogTrace($"SendMessageW hwnd:[{_hWindow}] result:[{result}] error:[{error}]");

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
        _logger.LogMsgWithThreadId(LogLevel.Trace, "PostMessageW", _hWindow, (uint)nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
        var result =
            User32.PostMessageW(
                _hWindow,
                (uint)nMsg,
                wParam,
                lParam
            );
        var error = Marshal.GetLastPInvokeError();
        _logger.LogTrace($"PostMessageW hwnd:[{_hWindow}] result:[{result}] error:[{error}]");

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
        return Dispatch(() =>
        {
            _logger.LogInformation($"MoveWindow hwnd:[{_hWindow}] x:[{x}] y:[{y}] width:[{width}] height:[{height}] repaint:[{repaint}] / current {Environment.CurrentManagedThreadId:X}");
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
                _logger.LogWithHWndAndErrorId(LogLevel.Error, "MoveWindow", _hWindow, Marshal.GetLastPInvokeError());
            }
            return result;
        });
    }

    public bool Show(
        int cmdShow
    )
    {
        return Dispatch(() =>
        {
            _logger.LogInformation($"ShowWindow hwnd:[{_hWindow}] cmdShow:[{cmdShow}] / current {Environment.CurrentManagedThreadId:X}");
            var result =
                User32.ShowWindow(
                    _hWindow,
                    cmdShow
                );

            var error = Marshal.GetLastPInvokeError();

            //result=0: 実行前は非表示だった/ <>0:実行前から表示されていた
            _logger.LogInformation($"ShowWindow hwnd:[{_hWindow}] result:[{result}] error:[{error}]");

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

                _logger.LogInformation($"GetWindowPlacement result cmdShow:[{cmdShow}] -> wndpl:[{wndpl}]");
            }

            return result;
        });
    }

    public bool SetWindowStyle(
        int style
    )
    {
        return Dispatch(() =>
        {
            //TODO DPI対応
            var clientRect = new User32.RECT();

            {
                var result = User32.GetClientRect(_hWindow, ref clientRect);
                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "GetClientRect failed", _hWindow, Marshal.GetLastPInvokeError());
                    return false;
                }
            }

            {
                var result = User32.AdjustWindowRect(ref clientRect, style, false);
                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "AdjustWindowRect failed", _hWindow, Marshal.GetLastPInvokeError());
                    return false;
                }
            }

            {
                var (result, error) = SetWindowLong(-16, new nint(style)); //GWL_STYLE
                if (result == nint.Zero && error != 0)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "SetWindowLong failed", _hWindow, error);
                    return false;
                }
            }

            {
                var width = clientRect.right - clientRect.left;
                var height = clientRect.bottom - clientRect.top;

                _logger.LogInformation($"SetWindowPos hwnd:[{_hWindow}] / current {Environment.CurrentManagedThreadId:X}");
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
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "SetWindowPos failed", _hWindow, Marshal.GetLastPInvokeError());
                    return false;
                }
            }

            return true;
        });
    }

    private (nint, int) SetWindowLong(
        int nIndex,
        nint dwNewLong
    )
    {
        Marshal.SetLastPInvokeError(0);
        _logger.LogInformation($"SetWindowLong hwnd:[{_hWindow}] nIndex:[{nIndex:X}] dwNewLong:[{dwNewLong:X}] / current {Environment.CurrentManagedThreadId:X}");
        var result =
            Environment.Is64BitProcess
                ? User32.SetWindowLongPtrW(_hWindow, nIndex, dwNewLong)
                : User32.SetWindowLongW(_hWindow, nIndex, dwNewLong);
        var error = Marshal.GetLastPInvokeError();

        return (result, error);
    }

    private (nint, int) ChildWindowSetWindowLong(
        User32.HWND childHWnd,
        int nIndex,
        nint dwNewLong
    )
    {
        Marshal.SetLastPInvokeError(0);
        _logger.LogInformation($"child SetWindowLong hwnd:[{childHWnd}] nIndex:[{nIndex:X}] dwNewLong:[{dwNewLong:X}] / current {Environment.CurrentManagedThreadId:X}");

        var isChildeWindowUnicode = (childHWnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(childHWnd);
        var result = isChildeWindowUnicode
                        ? Environment.Is64BitProcess
                            ? User32.SetWindowLongPtrW(childHWnd, nIndex, dwNewLong)
                            : User32.SetWindowLongW(childHWnd, nIndex, dwNewLong)
                        : Environment.Is64BitProcess
                            ? User32.SetWindowLongPtrA(childHWnd, nIndex, dwNewLong)
                            : User32.SetWindowLongA(childHWnd, nIndex, dwNewLong)
                        ;
        var error = Marshal.GetLastPInvokeError();

        return (result, error);
    }

    internal nint WndProc(uint msg, nint wParam, nint lParam, out bool handled)
    {
        handled = false;
        _logger.LogMsgWithThreadId(LogLevel.Trace, "WndProc", _hWindow, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        switch (msg)
        {
            case 0x0082://WM_NCDESTROY
                _logger.LogTrace("WM_NCDESTROY");
                _hWindow = default;
                break;
        }

        if (_onMessage != null)
        {
            var result = _onMessage.Invoke((int)msg, wParam, lParam, out handled);
            if (handled)
            {
                return result;
            }
        }

        switch (msg)
        {
            case 0x0010://WM_CLOSE

                /*
                 * TODO _onMessageで実装する
                _logger.LogTrace("WM_CLOSE");
                try
                {
                    _onPreCloseWindow?.Invoke();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "onPreCloseWindow error");
                }
                */
                /*
                _logger.LogTrace($"DestroyWindow {_hWindow:X} current {Environment.CurrentManagedThreadId:X}");
                var result = User32.DestroyWindow(_hWindow);
                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "DestroyWindow", _hWindow, Marshal.GetLastPInvokeError());
                }
                handled = true;
                */
                break;

            case 0x0210://WM_PARENTNOTIFY

                //TODO _onMessageで実装する
                _logger.LogTrace("WM_PARENTNOTIFY");

                var GWLP_WNDPROC = -4;

                switch (wParam & 0xFFFF)
                {
                    case 0x0001: //WM_CREATE
                        {
                            _logger.LogTrace($"WM_PARENTNOTIFY WM_CREATE {wParam:X}");
                            var childHWnd = (User32.HWND)lParam;
                            var subWndProc = new PinnedDelegate<User32.WNDPROC>(new(SubWndProc));

                            //TODO クラスにする
                            var (oldWndProc, result) = ChildWindowSetWindowLong(childHWnd, GWLP_WNDPROC, subWndProc.FunctionPointer);
                            _oldWndProcMap.TryAdd(childHWnd, (oldWndProc, subWndProc));
                            break;
                        }
                    case 0x0002: //WM_DESTROY
                        {
                            _logger.LogTrace($"WM_PARENTNOTIFY WM_DESTROY {wParam:X}");
                            var childHWnd = (User32.HWND)lParam;
                            if (_oldWndProcMap.TryRemove(childHWnd, out var pair))
                            {
                                var (_, result) = ChildWindowSetWindowLong(childHWnd, GWLP_WNDPROC, pair.Item1);
                                pair.Item2.Dispose();
                            }
                            break;
                        }
                }

                break;
        }
        return nint.Zero;
    }

    private nint SubWndProc(User32.HWND hwnd, uint msg, nint wParam, nint lParam)
    {
        _logger.LogMsgWithThreadId(LogLevel.Trace, "SubWndProc", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        var isWindowUnicode = (hwnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(hwnd);
        nint result;

        if (_oldWndProcMap.TryGetValue(hwnd, out var pair))
        {
            _logger.LogMsgWithThreadId(LogLevel.Trace, "CallWindowProc", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);
            result = isWindowUnicode
                        ? User32.CallWindowProcW(pair.Item1, hwnd, msg, wParam, lParam)
                        : User32.CallWindowProcA(pair.Item1, hwnd, msg, wParam, lParam)
                        ;
        }
        else
        {
            _logger.LogWarning("unkown hwnd -> DefWindowProc");
            result = isWindowUnicode
                        ? User32.DefWindowProcW(hwnd, msg, wParam, lParam)
                        : User32.DefWindowProcA(hwnd, msg, wParam, lParam)
                        ;
        }

        switch (msg)
        {
            case 0x000F://WM_PAINT
                //TODO _onMessageで実装する
                /*
                _logger.LogTrace("SubWndProc WM_PAINT");
                try
                {
                    _onPostPaint?.Invoke(hwnd.Handle);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "onPostPaint error");
                }
                */
                break;

            default:
                break;
        }

        return result;
    }

}
        /*
        //表示していないとwinrt::hresult_invalid_argumentになる
        var item = GraphicsCaptureItemInterop.CreateForWindow(hWindow);
        item.Closed += (item, obj) => {
            Logger.LogInformation("[window] GraphicsCaptureItem closed");
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
            Logger.LogInformation("[window] FrameArrived");

        };

        using var session = pool.CreateCaptureSession(item);
        session.StartCapture();
        Logger.LogInformation("[window] StartCapture");
        */
