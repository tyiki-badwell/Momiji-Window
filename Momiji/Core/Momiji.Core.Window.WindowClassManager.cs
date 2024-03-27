﻿using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal sealed class WindowClassManager : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;
    private WindowProcedure WindowProcedure { get; }

    private sealed record WindowClassMapKey(
        string ClassName,
        User32.WNDCLASSEX.CS ClassStyle
    );

    private readonly ConcurrentDictionary<WindowClassMapKey, WindowClass> _windowClassMap = [];

    public WindowClassManager(
        ILoggerFactory loggerFactory,
        WindowProcedure windowProcedure
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowClassManager>();
        WindowProcedure = windowProcedure;
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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);

            foreach (var windowClass in _windowClassMap.Values)
            {
                //クローズしていないウインドウが残っていると失敗する
                windowClass.Dispose();
            }
            _windowClassMap.Clear();
        }

        _disposed = true;
    }

    internal WindowClass QueryWindowClass(
        string className, 
        User32.WNDCLASSEX.CS classStyle
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var queryClassName = className.Trim().ToUpper();
        var queryClassStyle = classStyle;

        return _windowClassMap.GetOrAdd(new(queryClassName, queryClassStyle), (key) => {
            return new WindowClass(_loggerFactory, WindowProcedure, key.ClassStyle, key.ClassName);
        });
    }
}
