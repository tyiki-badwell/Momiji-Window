using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using Momiji.Internal.Window;

internal partial class OnUnhandledExceptionHandler : IOnUnhandledExceptionHandler, IDisposable
{
    private readonly ILogger<OnUnhandledExceptionHandler> _logger;
    private bool _disposed;

    public OnUnhandledExceptionHandler(
        ILogger<OnUnhandledExceptionHandler> logger
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    ~OnUnhandledExceptionHandler()
    {
        Dispose(false);
    }

    public event IOnUnhandledExceptionHandler.OnUnhandledExceptionHandler? OnUnhandledException;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public bool DoUnhandledException(Exception exception)
    {
        if (OnUnhandledException == null)
        {
            return false;
        }

        var handled = false;
        var list = OnUnhandledException.GetInvocationList().ToList();
        foreach (var item in list)
        {
            if (item is IOnUnhandledExceptionHandler.OnUnhandledExceptionHandler handler)
            {
                try
                {
                    handled = handler(this, exception);
                }
                catch (Exception e)
                {
                    _logger.LogWithLine(LogLevel.Information, e, "Error occurred while handling unhandled exception", Environment.CurrentManagedThreadId);
                }
            }
        }

        return handled;
    }

    private void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref _disposed, true, false) == true)
        {
            _logger.LogWithLine(LogLevel.Information, "already disposed", Environment.CurrentManagedThreadId);
            return;
        }

        if (disposing)
        {
            OnUnhandledException = null;

            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);

        }
    }

}
