using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal interface IWindowClassManager : IDisposable
{
    IWindowClass QueryWindowClass(
        string className,
        User32.WNDCLASSEX.CS classStyle
    );
}

internal sealed partial class WindowClassManager : IWindowClassManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WindowClassManager> _logger;
    private readonly IWindowProcedure _windowProcedure;
    private bool _disposed;

    private sealed record WindowClassMapKey(
        string ClassName,
        User32.WNDCLASSEX.CS ClassStyle
    );

    private readonly ConcurrentDictionary<WindowClassMapKey, Lazy<IWindowClass>> _windowClassMap = [];

    public WindowClassManager(
        ILoggerFactory loggerFactory,
        IWindowProcedure windowProcedure
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(windowProcedure);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowClassManager>();
        _windowProcedure = windowProcedure;
    }

    ~WindowClassManager()
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
        if (Interlocked.CompareExchange(ref _disposed, true, false) == true)
        {
            _logger.LogWithLine(LogLevel.Information, "already disposed", Environment.CurrentManagedThreadId);
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);

            foreach (var windowClass in _windowClassMap.Values)
            {
                //クローズしていないウインドウが残っていると失敗する
                windowClass.Value.Dispose();
            }
            _windowClassMap.Clear();
        }
    }

    public IWindowClass QueryWindowClass(
        string className, 
        User32.WNDCLASSEX.CS classStyle
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var queryClassName = className.Trim().ToUpper();
        var queryClassStyle = classStyle;

        var windowClass = _windowClassMap.GetOrAdd(new(queryClassName, queryClassStyle), (key) => {
            var windowClass = new Lazy<IWindowClass>(() => new WindowClass(_loggerFactory, _windowProcedure), LazyThreadSafetyMode.PublicationOnly);

            try
            {
                windowClass.Value.Initialize(key.ClassStyle, key.ClassName);
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "initialize error", Environment.CurrentManagedThreadId);
                throw;
            }

            return windowClass;
        });

        return windowClass.Value;
    }
}
