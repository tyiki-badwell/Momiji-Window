using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal abstract class NativeWindowBase : IWindow
{
    private readonly ILoggerFactory _loggerFactory;

    internal ILogger Logger { get; }

    protected WindowContext WindowContext { get; }

    internal User32.HWND HWindow { set; get; }
    public nint Handle => HWindow.Handle;

    internal NativeWindowBase(
        ILoggerFactory loggerFactory,
        WindowContext windowContext
    )
    {
        //TODO UIAutomation

        _loggerFactory = loggerFactory;
        Logger = _loggerFactory.CreateLogger<NativeWindow>();
        WindowContext = windowContext;
    }

    public override string ToString()
    {
        return $"NativeWindow[HWND:{HWindow}]";
    }

    internal abstract void WndProc(IUIThread.IMessage message);

    public async ValueTask<TResult> DispatchAsync<TResult>(Func<IWindow, TResult> item)
    {
        return await WindowContext.DispatchAsync(item, this);
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
        User32.HWND targetHWnd,
        int nIndex,
        nint dwNewLong
    )
    {
        var isWindowUnicode = (targetHWnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(targetHWnd);

        //SetWindowLong～のエラー判定のために、エラーコードをリセットする
        Marshal.SetLastPInvokeError(0);
        Logger.LogWithHWnd(LogLevel.Information, $"SetWindowLong nIndex:[{nIndex:X}] dwNewLong:[{dwNewLong:X}]", targetHWnd, Environment.CurrentManagedThreadId);
        var result = isWindowUnicode
                        ? Environment.Is64BitProcess
                            ? User32.SetWindowLongPtrW(targetHWnd, nIndex, dwNewLong)
                            : User32.SetWindowLongW(targetHWnd, nIndex, dwNewLong)
                        : Environment.Is64BitProcess
                            ? User32.SetWindowLongPtrA(targetHWnd, nIndex, dwNewLong)
                            : User32.SetWindowLongA(targetHWnd, nIndex, dwNewLong)
                        ;
        var error = new Win32Exception();
        Logger.LogWithHWndAndError(LogLevel.Information, $"SetWindowLong result:[{result:X}]", targetHWnd, error.ToString(), Environment.CurrentManagedThreadId);

        return (result, error);
    }
}

internal sealed class NativeWindow : NativeWindowBase
{
    private readonly IUIThread.OnMessage? _onMessage;
    private readonly IUIThread.OnMessage? _onMessageAfter;

    private WindowClass WindowClass { get; }

    internal NativeWindow(
        ILoggerFactory loggerFactory,
        WindowContext windowContext,
        string className,
        User32.WNDCLASSEX.CS classStyle,
        string windowTitle,
        NativeWindow? parent = default,
        IUIThread.OnMessage? onMessage = default,
        IUIThread.OnMessage? onMessageAfter = default
    ): base(loggerFactory, windowContext)
    {
        //TODO UIAutomation
        WindowClass = WindowContext.WindowClassManager.QueryWindowClass(className, classStyle);

        _onMessage = onMessage;
        _onMessageAfter = onMessageAfter;

        CreateWindow(windowTitle, parent);
    }

    private void CreateWindow(
        string windowTitle,
        NativeWindow? parent = default
    )
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

            hMenu = WindowContext.WindowManager.GenerateChildId(this); //子ウインドウ識別子
        }

        var CW_USEDEFAULT = unchecked((int)0x80000000);

        // メッセージループに移行してからCreateWindowする
        var task = DispatchAsync((window) =>
        {
            return WindowContext.WindowProcedure.CreateWindow(
                exStyle,
                WindowClass.ClassName,
                windowTitle,
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

    internal override void WndProc(IUIThread.IMessage message)
    {
        Logger.LogWithMsg(LogLevel.Trace, "WndProc", HWindow, message, Environment.CurrentManagedThreadId);

        //TODO ここで同期コンテキスト？
        //TODO エラーの伝播
        Logger.LogWithMsg(LogLevel.Trace, "onMessage", HWindow, message, Environment.CurrentManagedThreadId);
        _onMessage?.Invoke(this, message);

        if (!message.Handled)
        {
            Logger.LogWithMsg(LogLevel.Trace, "no handled message", HWindow, message, Environment.CurrentManagedThreadId);
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
}

internal sealed class SubClassNativeWindow : NativeWindowBase, IDisposable
{
    private bool _disposed;
    private readonly nint _oldWinProc = default;

    internal SubClassNativeWindow(
        ILoggerFactory loggerFactory,
        WindowContext windowContext,
        User32.HWND childHWnd
    ) : base(loggerFactory, windowContext)
    {
        //TODO UIAutomation

        var (result, error) = SetWindowLong(childHWnd, -4, WindowContext.WindowProcedure.FunctionPointer); //GWLP_WNDPROC
        if (result == nint.Zero && error.NativeErrorCode != 0)
        {
            Logger.LogWithHWndAndError(LogLevel.Error, "SetWindowLong failed", childHWnd, error.ToString(), Environment.CurrentManagedThreadId);
            ExceptionDispatchInfo.Throw(error);
        }

        if (result == WindowContext.WindowProcedure.FunctionPointer)
        {
            //変更前・変更後のWndProcが同じだった＝UIThread経由で作ったWindow　→　先にバイパスしたのにここに来たら異常事態
            throw new InvalidOperationException("already managed");
        }

        HWindow = childHWnd;
        _oldWinProc = result;
    }

    ~SubClassNativeWindow()
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
            Logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
            var (result, error) = SetWindowLong(HWindow, -4, _oldWinProc);
            if (result == nint.Zero && error.NativeErrorCode != 0)
            {
                Logger.LogWithHWnd(LogLevel.Error, error, "SetWindowLong failed", HWindow, Environment.CurrentManagedThreadId);
            }
        }

        _disposed = true;
    }

    internal override void WndProc(IUIThread.IMessage message)
    {
        Logger.LogWithMsg(LogLevel.Trace, "WndProc", HWindow, message, Environment.CurrentManagedThreadId);

        //TODO ここで同期コンテキスト？
        //TODO エラーの伝播
        Logger.LogWithMsg(LogLevel.Trace, "onMessage", HWindow, message, Environment.CurrentManagedThreadId);

        if (!message.Handled)
        {
            Logger.LogWithMsg(LogLevel.Trace, "no handled message", HWindow, message, Environment.CurrentManagedThreadId);
            //サブクラス化している場合は、オリジナルのプロシージャを実行する
            var isWindowUnicode = User32.IsWindowUnicode(HWindow);
            message.Result = isWindowUnicode
                ? User32.CallWindowProcW(_oldWinProc, HWindow, (uint)message.Msg, message.WParam, message.LParam)
                : User32.CallWindowProcA(_oldWinProc, HWindow, (uint)message.Msg, message.WParam, message.LParam)
                ;
            var error = new Win32Exception();
            Logger.LogWithMsgAndError(LogLevel.Trace, "CallWindowProc result", HWindow, message, error.ToString(), Environment.CurrentManagedThreadId);

            message.Handled = true;
        }

        if (HWindow.Handle == User32.HWND.None.Handle)
        {
            Logger.LogWithMsg(LogLevel.Trace, "HWND was free", HWindow, message, Environment.CurrentManagedThreadId);
        }
        else
        {
            Logger.LogWithMsg(LogLevel.Trace, "onMessageAfter", HWindow, message, Environment.CurrentManagedThreadId);
        }
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
