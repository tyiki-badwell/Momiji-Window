using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal interface IWindowInternal : IWindow, IDisposable
{
    User32.HWND HWND { set; get; }

    void OnMessage(IWindowManager.IMessage message);
}

internal abstract class NativeWindowBase : IWindowInternal
{
    private readonly ILoggerFactory _loggerFactory;

    internal ILogger Logger { get; }

    protected IWindowManagerInternal WindowManager { get; }

    public User32.HWND HWND { set; get; }
    public nint Handle => HWND.Handle;

    internal NativeWindowBase(
        ILoggerFactory loggerFactory,
        IWindowManagerInternal windowManager
    )
    {
        //TODO UIAutomation

        _loggerFactory = loggerFactory;
        Logger = _loggerFactory.CreateLogger<NativeWindow>();
        WindowManager = windowManager;
    }

    ~NativeWindowBase()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);

    public override string ToString()
    {
        return $"NativeWindow[HWND:{HWND}]";
    }

    public abstract void OnMessage(IWindowManager.IMessage message);

    public async ValueTask<TResult> DispatchAsync<TResult>(Func<IWindow, TResult> func)
    {
        return await WindowManager.DispatchAsync(func, this);
    }

    public nint SendMessage(
        int nMsg,
        nint wParam,
        nint lParam
    )
    {
        return WindowManager.WindowProcedure.SendMessage(HWND, (uint)nMsg, wParam, lParam);
    }

    public void PostMessage(
        int nMsg,
        nint wParam,
        nint lParam
    )
    {
        WindowManager.WindowProcedure.PostMessage(HWND, (uint)nMsg, wParam, lParam);
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
            Logger.LogWithHWndAndError(LogLevel.Trace, $"ReplyMessage {ret2}", HWND, error.ToString(), Environment.CurrentManagedThreadId);
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
    private bool _disposed;
    private readonly IWindowManager.OnMessage? _onMessage;
    private readonly IWindowManager.OnMessage? _onMessageAfter;

    private IWindowClass WindowClass { get; }

    private readonly string _windowTitle;

    internal NativeWindow(
        ILoggerFactory loggerFactory,
        WindowManager windowManager,
        IWindowManager.CreateWindowParameter parameter
    ): base(loggerFactory, windowManager)
    {
        //TODO UIAutomation
        WindowClass = WindowManager.WindowClassManager.QueryWindowClass(
            parameter.className,
            (User32.WNDCLASSEX.CS)parameter.classStyle
        );

        _windowTitle = parameter.windowTitle;

        _onMessage = parameter.onMessage;
        _onMessageAfter = parameter.onMessageAfter;

        CreateWindow(
            parameter.windowTitle,
            (uint)parameter.exStyle,
            (uint)parameter.style,
            parameter.x,
            parameter.y,
            parameter.width,
            parameter.height,
            (parameter.parent as NativeWindow)
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
        }

        if (!User32.DestroyWindow(HWND))
        {
            var error = new Win32Exception();
            Logger.LogWithHWndAndError(LogLevel.Error, "DestroyWindow failed", HWND, error.ToString(), Environment.CurrentManagedThreadId);
        }
        else
        {
            Logger.LogWithHWnd(LogLevel.Trace, $"DestroyWindow OK", HWND, Environment.CurrentManagedThreadId);
        }

        _disposed = true;
    }

    public override string ToString()
    {
        return $"NativeWindow[HWND:{HWND}][title:{_windowTitle}]";
    }

    private void CreateWindow(
        string windowTitle,
        uint exStyle,
        uint style,
        int x,
        int y,
        int width,
        int height,
        NativeWindow? parent = default
    )
    {
        var hMenu = nint.Zero;
        var parentHWnd = User32.HWND.None;

        if (parent == null)
        {
        }
        else
        {
            parentHWnd = parent.HWND;

            style |= 0x40000000U; //WS_CHILD

            hMenu = WindowManager.GenerateChildId(this); //子ウインドウ識別子
        }

        // メッセージループに移行してからCreateWindowする
        var task = DispatchAsync((window) =>
        {
            var handle = WindowManager.WindowProcedure.CreateWindow(
                exStyle,
                WindowClass.ClassName,
                windowTitle,
                style,
                x,
                y,
                width,
                height,
                parentHWnd,
                hMenu,
                WindowClass.HInstance
            );

            if (HWND.Handle == User32.HWND.None.Handle)
            {
                Logger.LogWithHWnd(LogLevel.Trace, "assign handle (after CreateWindow)", handle, Environment.CurrentManagedThreadId);
                HWND = handle;
            }

            return handle;
        });

        var handle = task.AsTask().GetAwaiter().GetResult();
        Debug.Assert(handle.Handle == HWND.Handle);

        Logger.LogWithHWnd(LogLevel.Information, "CreateWindow end", HWND, Environment.CurrentManagedThreadId);
    }

    public override void OnMessage(IWindowManager.IMessage message)
    {
        Logger.LogWithMsg(LogLevel.Trace, "OnMessage", HWND, message, Environment.CurrentManagedThreadId);

        //TODO ここで同期コンテキスト？
        //TODO エラーの伝播
        Logger.LogWithMsg(LogLevel.Trace, "onMessage", HWND, message, Environment.CurrentManagedThreadId);
        _onMessage?.Invoke(this, message);

        if (!message.Handled)
        {
            Logger.LogWithMsg(LogLevel.Trace, "no handled message", HWND, message, Environment.CurrentManagedThreadId);
            WindowClass.CallOriginalWindowProc(HWND, message);
        }

        if (HWND.Handle == User32.HWND.None.Handle)
        {
            Logger.LogWithMsg(LogLevel.Trace, "HWND was free", HWND, message, Environment.CurrentManagedThreadId);
        }
        else
        {
            Logger.LogWithMsg(LogLevel.Trace, "onMessageAfter", HWND, message, Environment.CurrentManagedThreadId);
            _onMessageAfter?.Invoke(this, message);
        }
    }
}

internal sealed class SubClassNativeWindow : NativeWindowBase
{
    private bool _disposed;
    private readonly nint _oldWinProc = default;

    internal SubClassNativeWindow(
        ILoggerFactory loggerFactory,
        WindowManager windowManager,
        User32.HWND childHWnd
    ) : base(loggerFactory, windowManager)
    {
        //TODO UIAutomation

        var (result, error) = SetWindowLong(childHWnd, -4, WindowManager.WindowProcedure.FunctionPointer); //GWLP_WNDPROC
        if (result == nint.Zero && error.NativeErrorCode != 0)
        {
            Logger.LogWithHWndAndError(LogLevel.Error, "SetWindowLong failed", childHWnd, error.ToString(), Environment.CurrentManagedThreadId);
            ExceptionDispatchInfo.Throw(error);
        }

        if (result == WindowManager.WindowProcedure.FunctionPointer)
        {
            //変更前・変更後のWndProcが同じだった＝UIThread経由で作ったWindow　→　先にバイパスしたのにここに来たら異常事態
            throw new InvalidOperationException("already managed");
        }

        HWND = childHWnd;
        _oldWinProc = result;
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
        }

        var (result, error) = SetWindowLong(HWND, -4, _oldWinProc);
        if (result == nint.Zero && error.NativeErrorCode != 0)
        {
            Logger.LogWithHWnd(LogLevel.Error, error, "SetWindowLong failed", HWND, Environment.CurrentManagedThreadId);
        }

        _disposed = true;
    }

    public override string ToString()
    {
        return $"SubClassNativeWindow[HWND:{HWND}]";
    }

    public override void OnMessage(IWindowManager.IMessage message)
    {
        Logger.LogWithMsg(LogLevel.Trace, "OnMessage", HWND, message, Environment.CurrentManagedThreadId);

        //TODO ここで同期コンテキスト？
        //TODO エラーの伝播

        if (!message.Handled)
        {
            Logger.LogWithMsg(LogLevel.Trace, "no handled message", HWND, message, Environment.CurrentManagedThreadId);
            //サブクラス化している場合は、オリジナルのプロシージャを実行する
            var isWindowUnicode = User32.IsWindowUnicode(HWND);
            message.Result = isWindowUnicode
                ? User32.CallWindowProcW(_oldWinProc, HWND, (uint)message.Msg, message.WParam, message.LParam)
                : User32.CallWindowProcA(_oldWinProc, HWND, (uint)message.Msg, message.WParam, message.LParam)
                ;
            var error = new Win32Exception();
            Logger.LogWithMsgAndError(LogLevel.Trace, "CallWindowProc result", HWND, message, error.ToString(), Environment.CurrentManagedThreadId);

            message.Handled = true;
        }

        if (HWND.Handle == User32.HWND.None.Handle)
        {
            Logger.LogWithMsg(LogLevel.Trace, "HWND was free", HWND, message, Environment.CurrentManagedThreadId);
        }
        else
        {
            Logger.LogWithMsg(LogLevel.Trace, "onMessageAfter", HWND, message, Environment.CurrentManagedThreadId);
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
