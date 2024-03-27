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

    internal ILogger Logger { get; }

    private readonly IUIThread.OnMessage? _onMessage;
    private readonly IUIThread.OnMessage? _onMessageAfter;

    private WindowContext WindowContext { get; }
    private WindowClass WindowClass { get; }

    internal User32.HWND HWindow { set; get; }
    public nint Handle => HWindow.Handle;

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
        Logger = _loggerFactory.CreateLogger<NativeWindow>();
        WindowContext = windowContext;
        WindowClass = WindowContext.WindowClassManager.QueryWindowClass(className, classStyle);

        _onMessage = onMessage;
        _onMessageAfter = onMessageAfter;
    }

    public override string ToString()
    {
        return $"NativeWindow[HWND:{HWindow}]";
    }

    internal void WndProc(IUIThread.IMessage message)
    {
        Logger.LogWithMsg(LogLevel.Trace, "WndProc", HWindow, message, Environment.CurrentManagedThreadId);

        //TODO ここで同期コンテキスト？
        //TODO エラーの伝播
        Logger.LogWithMsg(LogLevel.Trace, "onMessage", HWindow, message, Environment.CurrentManagedThreadId);
        _onMessage?.Invoke(this, message);

        if (!message.Handled)
        {
            Logger.LogWithMsg(LogLevel.Trace, "no handled message", HWindow, message, Environment.CurrentManagedThreadId);

            //スーパークラス化している場合は、オリジナルのプロシージャを実行する
            WindowClass.CallOriginalWindowProc(HWindow, message);
        }

        if (HWindow.Handle == User32.HWND.None.Handle)
        {
            Logger.LogWithMsg(LogLevel.Trace, "HWND was free", HWindow, message, Environment.CurrentManagedThreadId);
        }
        else
        {
            Logger.LogWithMsg(LogLevel.Trace, "onMessageAfter", HWindow, message, Environment.CurrentManagedThreadId);
            _onMessageAfter?.Invoke(this, message);
        }
    }

    public async ValueTask<TResult> DispatchAsync<TResult>(Func<IWindow, TResult> item)
    {
        return await WindowContext.DispatchAsync(item, this);
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
                parentHWnd = parent.HWindow;

                style = 0x10000000U; //WS_VISIBLE
                style |= 0x40000000U; //WS_CHILD

                hMenu = WindowContext.WindowManager.GenerateChildId((NativeWindow)window); //子ウインドウ識別子
            }

            var CW_USEDEFAULT = unchecked((int)0x80000000);

            //DPI awareness 
            using var switchBehavior = new SwitchThreadDpiHostingBehaviorMixedRAII(Logger);

            //ウインドウタイトル
            using var lpszWindowName = new StringToHGlobalUniRAII(windowTitle, Logger);

            return CreateWindowImpl(
                exStyle,
                WindowClass.ClassName,
                lpszWindowName.Handle, 
                style,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                parentHWnd,
                hMenu,
                WindowClass.HInstance
            );
        });

        var handle = task.AsTask().GetAwaiter().GetResult();

        if (HWindow.Handle == User32.HWND.None.Handle)
        {
            Logger.LogWithHWnd(LogLevel.Trace, "assign handle (after CreateWindow)", handle, Environment.CurrentManagedThreadId);
            HWindow = handle;
        }

        Logger.LogWithHWnd(LogLevel.Information, "CreateWindow end", HWindow, Environment.CurrentManagedThreadId);
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
        Logger.LogWithLine(LogLevel.Trace, "CreateWindowEx", Environment.CurrentManagedThreadId);
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
        Logger.LogWithHWndAndError(LogLevel.Information, "CreateWindowEx result", hWindow, error.ToString(), Environment.CurrentManagedThreadId);
        if (hWindow.Handle == User32.HWND.None.Handle)
        {
            WindowContext.WindowProcedure.ThrowIfOccurredInWndProc();
            throw error;
        }

        var behavior = User32.GetWindowDpiHostingBehavior(hWindow);
        Logger.LogWithLine(LogLevel.Trace, $"GetWindowDpiHostingBehavior {behavior}", Environment.CurrentManagedThreadId);

        return hWindow;
    }

    public bool Close()
    {
        Logger.LogWithHWnd(LogLevel.Information, "Close", HWindow, Environment.CurrentManagedThreadId);
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
        return WindowContext.WindowProcedure.SendMessage(HWindow, (uint)nMsg, wParam, lParam);
    }

    public void PostMessage(
        int nMsg,
        nint wParam,
        nint lParam
    )
    {
        WindowContext.WindowProcedure.PostMessage(HWindow, (uint)nMsg, wParam, lParam);
    }

    public bool ReplyMessage(
        nint lResult
    )
    {
        //TODO 何でもReplyMessageしてOKというわけではないので、チェックが要る？自己責任？（例：GetWindowTextでWM_GETTEXTが来たときの戻り値が期待値と異なってしまう）
        var ret = User32.InSendMessageEx(nint.Zero);
        Logger.LogWithLine(LogLevel.Trace, $"InSendMessageEx {ret:X}", Environment.CurrentManagedThreadId);
        if ((ret & (0x00000008 | 0x00000001)) == 0x00000001) //ISMEX_SEND
        {
            Logger.LogWithLine(LogLevel.Trace, "ISMEX_SEND", Environment.CurrentManagedThreadId);
            var ret2 = User32.ReplyMessage(lResult);
            var error = new Win32Exception();
            Logger.LogWithHWndAndError(LogLevel.Trace, $"ReplyMessage {ret2}", HWindow, error.ToString(), Environment.CurrentManagedThreadId);
            return ret2;
        }
        return false;
    }

    internal (nint, Win32Exception) SetWindowLong(
        int nIndex,
        nint dwNewLong
    )
    {
        Marshal.SetLastPInvokeError(0);
        Logger.LogWithHWnd(LogLevel.Information, $"SetWindowLong nIndex:[{nIndex:X}] dwNewLong:[{dwNewLong:X}]", HWindow, Environment.CurrentManagedThreadId);
        var result =
            Environment.Is64BitProcess
                ? User32.SetWindowLongPtrW(HWindow, nIndex, dwNewLong)
                : User32.SetWindowLongW(HWindow, nIndex, dwNewLong);
        var error = new Win32Exception();

        return (result, error);
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
