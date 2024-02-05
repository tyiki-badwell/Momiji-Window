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

public interface IWindowManager : IDisposable, IAsyncDisposable
{
    public record class Param
    {
        public int CS
        {
            get; init;
        }
    }

    Task StartAsync(CancellationToken stoppingToken);
    Task CancelAsync();

    public record class Message
    {
        public int Msg
        {
            get; init;
        }
        public nint WParam
        {
            get; init;
        }
        public nint LParam
        {
            get; init;
        }
        public nint Result
        {
            get; set;
        }

        public bool Handled
        {
            get; set;
        }
        public override string ToString()
        {
            return $"[Msg:{Msg:X}][WParam:{WParam:X}][LParam:{LParam:X}][Result:{Result:X}][Handled:{Handled}]";
        }
    }

    public delegate void OnMessage(IWindow sender, Message message);

    public IWindow CreateWindow(
        string windowTitle,
        OnMessage? onMessage = default
    );

    public IWindow CreateWindow(
        IWindow parent,
        string windowTitle,
        OnMessage? onMessage = default
    );

    public IWindow CreateChildWindow(
        IWindow parent,
        string className,
        string windowTitle,
        OnMessage? onMessage = default
    );

    void CloseAll();
}

public interface IWindow
{
    nint Handle
    {
        get;
    }
    ValueTask<T> DispatchAsync<T>(Func<IWindow, T> item);
    bool Close();
    ValueTask<bool> MoveAsync(
        int x,
        int y,
        int width,
        int height,
        bool repaint
    );

    ValueTask<bool> ShowAsync(
        int cmdShow
    );

    ValueTask<bool> SetWindowStyleAsync(
        int style
    );

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
