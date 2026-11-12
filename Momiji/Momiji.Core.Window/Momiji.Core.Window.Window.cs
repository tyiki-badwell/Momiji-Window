using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;
using Comctl32 = Momiji.Interop.Comctl32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal interface IWindowInternal : IWindow
{
    interface IParameter
    {
    }

    User32.HWND HWND { set; get; }

    ILogger Logger { get; }

    IWindowFunction WindowFunction { get; }

    void Initialize(IParameter parameter);

    void OnMessage(IWindowManager.IMessage message);
}

internal abstract class NativeWindowBase : IWindowInternal
{
    private readonly ILoggerFactory _loggerFactory;

    public ILogger Logger { get; }

    protected readonly IWindowMap _windowMap;
    protected readonly IWindowProcedure _windowProcedure;

    public IWindowFunction WindowFunction
    {
        get;
    }


    public User32.HWND HWND { set; get; }
    public nint Handle => HWND.Handle;

    internal NativeWindowBase(
        ILoggerFactory loggerFactory,
        IWindowMap windowMap,
        IWindowFunction windowFunction,
        IWindowProcedure windowProcedure
    )
    {
        //TODO UIAutomation

        _loggerFactory = loggerFactory;
        Logger = _loggerFactory.CreateLogger<NativeWindow>();

        _windowMap = windowMap;

        WindowFunction = windowFunction;
        _windowProcedure = windowProcedure;
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

    public abstract void Initialize(IWindowInternal.IParameter parameter);

    public override string ToString()
    {
        return $"NativeWindow[HWND:{HWND}]";
    }

    public abstract void OnMessage(IWindowManager.IMessage message);

    public async ValueTask<TResult> EnqueueAsync<TResult>(Func<IWindow, TResult> func)
    {
        return await _windowMap.EnqueueAsync(func, this);
    }

    public nint SendMessage(
        int nMsg,
        nint wParam,
        nint lParam
    )
    {
        return WindowFunction.SendMessage(HWND, (uint)nMsg, wParam, lParam);
    }

    public void PostMessage(
        int nMsg,
        nint wParam,
        nint lParam
    )
    {
        WindowFunction.PostMessage(HWND, (uint)nMsg, wParam, lParam);
    }

    public bool ReplyMessage(
        nint lResult
    )
    {
        //TODO 何でもReplyMessageしてOKというわけではないので、チェックが要る？自己責任？（例：GetWindowTextでWM_GETTEXTが来たときの戻り値が期待値と異なってしまう）
        var ret = User32.InSendMessageEx(nint.Zero);
        if (Logger.IsEnabled(LogLevel.Trace))
        {
            Logger.LogWithLine(LogLevel.Trace, $"InSendMessageEx {ret:X}", Environment.CurrentManagedThreadId);
        }
        if ((ret & (0x00000008 | 0x00000001)) == 0x00000001) //ISMEX_SEND
        {
            Logger.LogWithLine(LogLevel.Trace, "ISMEX_SEND", Environment.CurrentManagedThreadId);
            var ret2 = User32.ReplyMessage(lResult);
            var errorCode = Marshal.GetLastPInvokeError();
            if (Logger.IsEnabled(LogLevel.Trace))
            {
                Logger.LogWithHWndAndError(LogLevel.Trace, $"ReplyMessage {ret2}", HWND, errorCode, Environment.CurrentManagedThreadId);
            }
            return ret2;
        }
        return false;
    }
}

internal sealed partial class NativeWindow(
    ILoggerFactory loggerFactory,
    IWindowMap windowMap,
    IWindowFunction windowFunction,
    IWindowProcedure windowProcedure,
    IWindowClassManager windowClassManager
) : NativeWindowBase(loggerFactory, windowMap, windowFunction, windowProcedure)
{
    internal static readonly Type Type = typeof(NativeWindow);

    private bool _disposed;
    private IWindowManager.OnMessage? _onMessage;
    private IWindowManager.OnMessage? _onMessageAfter;

    private IWindowClass? WindowClass { get; set; }

    private string? _windowTitle;

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref _disposed, true, false) == true)
        {
            Logger.LogWithLine(LogLevel.Information, "already disposed", Environment.CurrentManagedThreadId);
            return;
        }

        if (disposing)
        {
            Logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
        }

        if (HWND.Handle != User32.HWND.None.Handle)
        {
            if (!User32.DestroyWindow(HWND))
            {
                var error = new Win32Exception();
                Logger.LogWithHWndAndError(LogLevel.Error, "DestroyWindow failed", HWND, error.ToString(), Environment.CurrentManagedThreadId);
            }
            else
            {
                Logger.LogWithHWnd(LogLevel.Trace, $"DestroyWindow OK", HWND, Environment.CurrentManagedThreadId);
            }
        }
    }

    public override string ToString()
    {
        return $"NativeWindow[HWND:{HWND}][title:{_windowTitle}]";
    }

    internal struct Parameter : IWindowInternal.IParameter
    {
        public IWindowManager.CreateWindowParameter parameter;
    }

    public override void Initialize(IWindowInternal.IParameter parameter)
    {
        var parameter_ = (Parameter)parameter;
        var p = parameter_.parameter;

        //TODO UIAutomation
        WindowClass = windowClassManager.QueryWindowClass(
            p.className,
            (User32.WNDCLASSEX.CS)p.classStyle
        );

        _windowTitle = p.windowTitle;

        _onMessage = p.onMessage;
        _onMessageAfter = p.onMessageAfter;

        CreateWindow(
            p.windowTitle,
            (User32.WS_EX)p.exStyle,
            (User32.WS)p.style,
            p.x,
            p.y,
            p.width,
            p.height,
            (p.parent as NativeWindow)
        );
    }

    private void CreateWindow(
        string windowTitle,
        User32.WS_EX exStyle,
        User32.WS style,
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
            //なにかすることあれば
        }
        else
        {
            parentHWnd = parent.HWND;

            if (!User32.WS.NONE.Equals(style) && User32.WS.CHILD.HasFlag(style))
            {
                //WS_CHILDがあるときは、子ウインドウ識別子をセット
                //TODO 自動でセットするのは良くないか？
                hMenu = _windowMap.GenerateChildId(this);
            }
        }

        var handle = WindowFunction.CreateWindow(
            exStyle,
            WindowClass!.ClassName,
            windowTitle,
            style,
            x,
            y,
            width,
            height,
            parentHWnd,
            hMenu,
            WindowClass!.HInstance
        );

        if (HWND.Handle == User32.HWND.None.Handle)
        {
            Logger.LogWithHWnd(LogLevel.Trace, "assign handle (after CreateWindow)", handle, Environment.CurrentManagedThreadId);
            HWND = handle;
        }

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
            WindowClass!.CallOriginalWindowProc(HWND, message);
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

internal sealed partial class SubClassNativeWindow : NativeWindowBase
{
    internal static readonly Type Type = typeof(SubClassNativeWindow);

    private bool _disposed;
    private nint _oldWinProc;

    internal SubClassNativeWindow(
        ILoggerFactory loggerFactory,
        IWindowMap windowMap,
        IWindowFunction windowFunction,
        IWindowProcedure windowProcedure
    ) : base(loggerFactory, windowMap, windowFunction, windowProcedure)
    {
        //TODO UIAutomation
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref _disposed, true, false) == true)
        {
            Logger.LogWithLine(LogLevel.Information, "already disposed", Environment.CurrentManagedThreadId);
            return;
        }

        if (disposing)
        {
            Logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
        }

        var (result, error) = WindowFunction.SetWindowLong(HWND, -4, _oldWinProc);
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

    internal struct Parameter : IWindowInternal.IParameter
    {
        public User32.HWND childHWnd;
    }

    public override void Initialize(IWindowInternal.IParameter parameter)
    {
        var parameter_ = (Parameter)parameter;

        HWND = parameter_.childHWnd;

        var (result, error) = WindowFunction.SetWindowLong(HWND, -4, _windowProcedure.FunctionPointer); //GWLP_WNDPROC
        if (result == nint.Zero && error.NativeErrorCode != 0)
        {
            Logger.LogWithHWndAndError(LogLevel.Error, "SetWindowLong failed", HWND, error.ToString(), Environment.CurrentManagedThreadId);
            ExceptionDispatchInfo.Throw(error);
        }

        if (result == _windowProcedure.FunctionPointer)
        {
            //変更前・変更後のWndProcが同じだった＝UIThread経由で作ったWindow　→　先にバイパスしたのにここに来たら異常事態
            throw new InvalidOperationException("already managed");
        }

        _oldWinProc = result;
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
            var errorCode = Marshal.GetLastPInvokeError();
            Logger.LogWithMsgAndError(LogLevel.Trace, "CallWindowProc result", HWND, message, errorCode, Environment.CurrentManagedThreadId);

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

internal sealed partial class SubClassNativeWindow2 : NativeWindowBase
{
    internal static readonly Type Type = typeof(SubClassNativeWindow2);

    private bool _disposed;
    private nuint _uidSubclass;

    internal SubClassNativeWindow2(
        ILoggerFactory loggerFactory,
        IWindowMap windowMap,
        IWindowFunction windowFunction,
        IWindowProcedure windowProcedure
    ) : base(loggerFactory, windowMap, windowFunction, windowProcedure)
    {
        //TODO UIAutomation

    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref _disposed, true, false) == true)
        {
            Logger.LogWithLine(LogLevel.Information, "already disposed", Environment.CurrentManagedThreadId);
            return;
        }

        if (disposing)
        {
            Logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
        }

        var result = Comctl32.RemoveWindowSubclass(HWND, _windowProcedure.SubFunctionPointer, _uidSubclass);
        if (!result)
        {
            var error = new Win32Exception();
            Logger.LogWithHWndAndError(LogLevel.Error, "RemoveWindowSubclass failed", HWND, error.ToString(), Environment.CurrentManagedThreadId);
        }

        _disposed = true;
    }

    public override string ToString()
    {
        return $"SubClassNativeWindow[HWND:{HWND}]";
    }

    internal struct Parameter : IWindowInternal.IParameter
    {
        public User32.HWND childHWnd;
    }

    public override void Initialize(IWindowInternal.IParameter parameter)
    {
        var parameter_ = (Parameter)parameter;
        var h = parameter_.childHWnd;

        _uidSubclass = _windowMap.GenerateSubclassId();

        var result =
            Comctl32.SetWindowSubclass(
                h,
                _windowProcedure.SubFunctionPointer,
                _uidSubclass,
                0
            );
        if (!result)
        {
            var error = new Win32Exception();
            Logger.LogWithHWndAndError(LogLevel.Error, "SetWindowSubclass failed", h, error.ToString(), Environment.CurrentManagedThreadId);
            ExceptionDispatchInfo.Throw(error);
        }

        HWND = h;
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

            message.Result = Comctl32.DefSubclassProc(HWND, (uint)message.Msg, message.WParam, message.LParam);

            var errorCode = Marshal.GetLastPInvokeError();
            Logger.LogWithMsgAndError(LogLevel.Trace, "CallWindowProc result", HWND, message, errorCode, Environment.CurrentManagedThreadId);

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
