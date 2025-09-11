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
        return OnUnhandledException?.Invoke(this, exception) ?? false;
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            OnUnhandledException = null;

            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);

        }

        _disposed = true;
    }

}
