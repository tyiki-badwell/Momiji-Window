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
    delegate bool OnUnhandledExceptionHandler(Exception exception);

    Task<IUIThread> StartAsync(
        OnUnhandledExceptionHandler? onUnhandledException = default
    );
}

public interface IUIThread : IDisposable, IAsyncDisposable
{
    ValueTask CancelAsync();
    ValueTask<TResult> DispatchAsync<TResult>(Func<IWindowManager, TResult> item);

    delegate void InactivatedEventHandler();
    event InactivatedEventHandler OnInactivated;
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

    struct CreateWindowParameter
    {
        public string windowTitle = "";
        public IWindow? parent = default;
        public string className = "";
        public int classStyle = 0;
        public int exStyle = 0;
        public int style = 0;
        public int x = unchecked((int)0x80000000U);
        public int y = unchecked((int)0x80000000U);
        public int width = unchecked((int)0x80000000U);
        public int height = unchecked((int)0x80000000U);
        public OnMessage? onMessage = default;
        public OnMessage? onMessageAfter = default;

        public CreateWindowParameter()
        {
        }
    }

    IWindow CreateWindow(
        CreateWindowParameter parameter
    );

}

public interface IWindow
{
    nint Handle { get; }
    ValueTask<T> DispatchAsync<T>(Func<IWindow, T> func);

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
