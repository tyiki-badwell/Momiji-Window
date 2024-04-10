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
    delegate void OnStop(Exception? exception);
    delegate bool OnUnhandledException(Exception exception);

    ValueTask<TResult> DispatchAsync<TResult>(Func<IWindowManager, TResult> func);

}

public interface IWindowManager
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

    delegate void OnMessage(IWindow sender, IMessage message);

    IWindow CreateWindow(
        string windowTitle,
        IWindow? parent = default,
        string className = "",
        OnMessage? onMessage = default,
        OnMessage? onMessageAfter = default
    );

}

public interface IWindow
{
    nint Handle { get; }
    ValueTask<T> DispatchAsync<T>(Func<IWindow, T> func);

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
