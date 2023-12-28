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

    public delegate nint OnMessage(IWindow sender, int msg, nint wParam, nint lParam, out bool handled);

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
    Task<T> DispatchAsync<T>(Func<T> item);
    bool Close();
    bool Move(
        int x,
        int y,
        int width,
        int height,
        bool repaint
    );

    bool Show(
        int cmdShow
    );

    bool SetWindowStyle(
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
