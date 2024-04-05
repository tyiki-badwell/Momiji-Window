namespace Momiji.Core.Window;

public class WindowException : Exception
{
    public WindowException(string message) : base(message)
    {
    }

    public WindowException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}

public interface IUIThreadFactory : IDisposable, IAsyncDisposable
{
    public record class Param
    {
        public int CS
        {
            get; init;
        }
    }

    Task<IUIThread> StartAsync(
        IUIThread.OnStop? onStop = default,
        IUIThread.OnUnhandledException? onUnhandledException = default
    );
}

public interface IUIThread : IDisposable, IAsyncDisposable
{
    public interface IMessage
    {
        int Msg { get; }
        nint WParam { get; }
        nint LParam { get; }
        int OwnerThreadId { get; }
        nint Result { get; set; }
        bool Handled { get; set; }
    }

    delegate void OnStop(Exception? exception);
    delegate void OnMessage(IWindow sender, IMessage message);
    delegate bool OnUnhandledException(Exception exception);

    ValueTask<TResult> DispatchAsync<TResult>(Func<TResult> func);

    IWindow CreateWindow(
        string windowTitle,
        IWindow? parent = default,
        string className = "",
        OnMessage? onMessage = default,
        OnMessage? onMessageAfter = default
    );
}

public interface IWindow: IDisposable
{
    nint Handle
    {
        get;
    }
    ValueTask<T> DispatchAsync<T>(Func<IWindow, T> item);

    bool Close();

    nint SendMessage(
        int nMsg,
        nint wParam,
        nint lParam
    );

    void PostMessage(
        int nMsg,
        nint wParam,
        nint lParam
    );

    bool ReplyMessage(
        nint lResult
    );
}
