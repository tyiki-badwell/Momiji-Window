using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;
using Momiji.Internal.Util;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal sealed class NativeWindow : IWindow
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    private readonly IUIThread.OnMessage? _onMessage;
    private readonly IUIThread.OnMessage? _onMessageAfter;

    private readonly WindowContext _windowContext;
    private readonly WindowClass _windowClass;

    internal User32.HWND _hWindow;
    public nint Handle => _hWindow.Handle;

    internal NativeWindow(
        ILoggerFactory loggerFactory,
        WindowContext windowContext,
        string className,
        User32.WNDCLASSEX.CS classStyle,
        IUIThread.OnMessage? onMessage,
        IUIThread.OnMessage? onMessageAfter
    )
    {
        //TODO UIAutomation

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<NativeWindow>();
        _windowContext = windowContext;
        _windowClass = _windowContext.WindowClassManager.QueryWindowClass(className, classStyle);

        _onMessage = onMessage;
        _onMessageAfter = onMessageAfter;
    }

    public override string ToString()
    {
        return $"NativeWindow[HWND:{_hWindow}]";
    }

    public async ValueTask<TResult> DispatchAsync<TResult>(Func<IWindow, TResult> item)
    {
        _logger.LogWithLine(LogLevel.Trace, "DispatchAsync", Environment.CurrentManagedThreadId);

        TResult func()
        {
            return _windowContext.WindowManager.InvokeWithContext(item, this);
        }

        return await _windowContext.DispatchAsync(func);
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
            _logger.LogWithLine(LogLevel.Trace, $"ON SetThreadDpiHostingBehavior [{_oldBehavior} -> MIXED]", Environment.CurrentManagedThreadId);
        }

        public void Dispose()
        {
            if (_oldBehavior != User32.DPI_HOSTING_BEHAVIOR.INVALID)
            {
                var oldBehavior = User32.SetThreadDpiHostingBehavior(_oldBehavior);
                _logger.LogWithLine(LogLevel.Trace, $"OFF SetThreadDpiHostingBehavior [{oldBehavior} -> {_oldBehavior}]", Environment.CurrentManagedThreadId);
            }
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

            var hMenu = nint.Zero;
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

                hMenu = _windowContext.WindowManager.GenerateChildId((NativeWindow)window); //子ウインドウ識別子
            }

            var CW_USEDEFAULT = unchecked((int)0x80000000);

            //DPI awareness 
            using var switchBehavior = new SwitchThreadDpiHostingBehaviorMixedRAII(_logger);

            //ウインドウタイトル
            using var lpszWindowName = new StringToHGlobalUniRAII(windowTitle, _logger);

            return CreateWindowImpl(
                exStyle,
                _windowClass.ClassName,
                lpszWindowName.Handle, 
                style,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                parentHWnd,
                hMenu,
                _windowClass.HInstance
            );
        });

        var handle = task.AsTask().GetAwaiter().GetResult();

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
            _windowContext.WindowProcedure.ThrowIfOccurredInWndProc();
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
        return _windowContext.WindowProcedure.SendMessage(_hWindow, (uint)nMsg, wParam, lParam);
    }

    public void PostMessage(
        int nMsg,
        nint wParam,
        nint lParam
    )
    {
        _windowContext.WindowProcedure.PostMessage(_hWindow, (uint)nMsg, wParam, lParam);
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

    internal void WndProc(IUIThread.IMessage message)
    {
        _logger.LogWithMsg(LogLevel.Trace, "WndProc", _hWindow, message, Environment.CurrentManagedThreadId);

        switch (message.Msg)
        {
            case 0x0082://WM_NCDESTROY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCDESTROY", _hWindow, Environment.CurrentManagedThreadId);
                _hWindow = default;
                break;
        }

        //TODO ここで同期コンテキスト？
        //TODO エラーの伝播
        _logger.LogWithMsg(LogLevel.Trace, "onMessage", _hWindow, message, Environment.CurrentManagedThreadId);
        _onMessage?.Invoke(this, message);

        if (!message.Handled)
        {
            _logger.LogWithMsg(LogLevel.Trace, "no handled message", _hWindow, message, Environment.CurrentManagedThreadId);

            //スーパークラス化している場合は、オリジナルのプロシージャを実行する
            _windowClass.CallOriginalWindowProc(_hWindow, message);
        }

        _logger.LogWithMsg(LogLevel.Trace, "onMessageAfter", _hWindow, message, Environment.CurrentManagedThreadId);
        _onMessageAfter?.Invoke(this, message);
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
