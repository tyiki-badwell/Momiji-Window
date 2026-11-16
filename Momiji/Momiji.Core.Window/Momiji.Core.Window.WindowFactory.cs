using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

internal interface IWindowFactory
{
    IWindowInternal CreateNativeWindow();
    IWindowInternal CreateNativeSubclassWindow();
}

internal sealed partial class WindowFactory : IWindowFactory
{
    private readonly ILoggerFactory _loggerFactory;

    private readonly ILogger<WindowFactory> _logger;

    private readonly IWindowMap _windowMap;
    private readonly IWindowFunction _windowFunction;
    private readonly IWindowProcedure _windowProcedure;
    private readonly IWindowClassManager _windowClassManager;

    public WindowFactory(
        ILoggerFactory loggerFactory,
        IWindowMap windowMap,
        IWindowFunction windowFunction,
        IWindowProcedure windowProcedure,
        IWindowClassManager windowClassManager
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(windowMap);
        ArgumentNullException.ThrowIfNull(windowFunction);
        ArgumentNullException.ThrowIfNull(windowProcedure);
        ArgumentNullException.ThrowIfNull(windowClassManager);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowFactory>();
        _windowMap = windowMap;
        _windowFunction = windowFunction;
        _windowProcedure = windowProcedure;
        _windowClassManager = windowClassManager;
    }

    public IWindowInternal CreateNativeWindow()
    {
        _logger.LogWithLine(LogLevel.Trace, "CreateNativeWindow", Environment.CurrentManagedThreadId);
        return new NativeWindow(
            _loggerFactory,
            _windowMap,
            _windowFunction,
            _windowProcedure,
            _windowClassManager
        );
    }
    public IWindowInternal CreateNativeSubclassWindow()
    {
        _logger.LogWithLine(LogLevel.Trace, "CreateNativeSubclassWindow", Environment.CurrentManagedThreadId);
        return new SubClassNativeWindow2(
            _loggerFactory,
            _windowMap,
            _windowFunction,
            _windowProcedure
        );
    }

}
