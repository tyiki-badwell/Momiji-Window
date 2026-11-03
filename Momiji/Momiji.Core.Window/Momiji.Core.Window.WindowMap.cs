using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal interface IWindowMap
{
    void CloseAll();
    void ForceClose();
    bool IsEmpty { get; }
    void Add(IWindowInternal window);
    void Remove(IWindowInternal window);
    bool FindWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out IWindowInternal window);

    TResult InvokeWithContext<TResult>(Func<IWindowInternal, TResult> func, IWindowInternal window);
    bool TryGetWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out IWindowInternal window);
}

internal sealed partial class WindowMap : IWindowMap, IDisposable
{
    private bool _disposed;

    private readonly ILogger<WindowMap> _logger;

    private readonly IUIThreadActivator _uiThreadActivator;

    public bool IsEmpty => _windowMap.IsEmpty;

    private readonly ConcurrentDictionary<User32.HWND, IWindowInternal> _windowMap = [];
    private readonly ConcurrentStack<IWindowInternal> _windowStack = [];

    public WindowMap(
        ILogger<WindowMap> logger,
        IUIThreadActivator uiThreadActivator
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        _uiThreadActivator = uiThreadActivator;
        _uiThreadActivator.OnInactivated += OnInactivated;
    }

    ~WindowMap()
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

            _uiThreadActivator.OnInactivated -= OnInactivated;
        }
    }

    public void CloseAll()
    {
        //TODO SendMessageに行くのでUIスレッドで呼び出す必要は無いが、統一すべき？
        foreach (var item in _windowMap)
        {
            if (!_windowMap.ContainsKey(item.Key))
            {
                //ループ中に削除されていた場合はスキップ
                continue;
            }

            try
            {
                //TODO 親ウインドウにだけ実行する(順序に依っては子ウインドウのcloseで1400になる)
                item.Value.Close();
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "close failed.", Environment.CurrentManagedThreadId);
            }
        }
    }

    private void OnInactivated()
    {
        _logger.LogWithLine(LogLevel.Trace, "OnInactivated", Environment.CurrentManagedThreadId);
        ForceClose();
    }

    public void ForceClose()
    {
        if (_windowMap.IsEmpty)
        {
            return;
        }

        _logger.LogWithLine(LogLevel.Warning, $"window left {_windowMap.Count}", Environment.CurrentManagedThreadId);
        PrintWindowMap();

        foreach (var item in _windowMap)
        {
            //NOTE UIThreadで実行しないとエラーになる
            item.Value.Dispose();
        }

        //TODO mapが空になるのを待つ？
        _logger.LogWithLine(LogLevel.Warning, $"window left {_windowMap.Count}", Environment.CurrentManagedThreadId);

        _windowMap.Clear();
    }

    [Conditional("DEBUG")]
    private void PrintWindowMap()
    {
        _logger.LogWithLine(LogLevel.Trace, "================================= PrintWindowMap start", Environment.CurrentManagedThreadId);

        if (_windowMap.IsEmpty)
        {
            _logger.LogWithLine(LogLevel.Trace, "EMPTY", Environment.CurrentManagedThreadId);
        }
        else
        {
            foreach (var item in _windowMap)
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"window map [window:{item.Value}]", item.Key, Environment.CurrentManagedThreadId);
                }
            }
        }
        _logger.LogWithLine(LogLevel.Trace, "================================= PrintWindowMap end", Environment.CurrentManagedThreadId);
    }

    public void Add(IWindowInternal window)
    {
        if (_windowMap.TryAdd(window.HWND, window))
        {
            _logger.LogWithHWnd(LogLevel.Information, "add window map", window.HWND, Environment.CurrentManagedThreadId);
        }
        else
        {
            //TODO handleが再利用されている？
            throw new WindowException($"failed add window map hwnd:[{window.HWND}]");
        }
    }

    public void Remove(IWindowInternal window)
    {
        if (_windowMap.TryRemove(window.HWND, out var window_))
        {
            _logger.LogWithHWnd(LogLevel.Information, "remove window map", window_.HWND, Environment.CurrentManagedThreadId);
            window_.HWND = User32.HWND.None;
        }
        else
        {
            throw new WindowException($"failed remove window map hwnd:[{window.HWND}]");
        }
    }

    public bool FindWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out IWindowInternal window)
    {
        if (_windowMap.TryGetValue(hwnd, out window))
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogWithHWnd(LogLevel.Trace, $"in window map [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
            }
            return true;
        }
        return false;
    }

    internal readonly ref struct SwitchThreadDpiAwarenessContextPerMonitorAwareV2RAII
    {
        private readonly ILogger _logger;
        private readonly IWindowInternal _window;

        private readonly User32.DPI_AWARENESS_CONTEXT _oldContext;

        public SwitchThreadDpiAwarenessContextPerMonitorAwareV2RAII(
            IWindowInternal window,
            ILogger logger
        )
        {
            _window = window;
            _logger = logger;

            _oldContext = User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT.PER_MONITOR_AWARE_V2);

            var errorCode = Marshal.GetLastPInvokeError();
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogWithHWndAndError(LogLevel.Trace, $"ON SetThreadDpiAwarenessContext [{_oldContext:X} -> PER_MONITOR_AWARE_V2]", _window.HWND, errorCode, Environment.CurrentManagedThreadId);
            }
        }

        public void Dispose()
        {
            if (_oldContext.Handle != 0)
            {
                var oldContext = User32.SetThreadDpiAwarenessContext(_oldContext);

                var errorCode = Marshal.GetLastPInvokeError();
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogWithHWndAndError(LogLevel.Trace, $"OFF SetThreadDpiAwarenessContext [{oldContext} -> {_oldContext}]", _window.HWND, errorCode, Environment.CurrentManagedThreadId);
                }
            }
        }
    }

    public TResult InvokeWithContext<TResult>(Func<IWindowInternal, TResult> func, IWindowInternal window)
    {
        //TODO スレッドセーフになっているか要確認(再入しても問題ないなら気にしない)
        //TODO 何かのコンテキストに移す
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithLine(LogLevel.Trace, $"Push [handle:{window.Handle:X}] [stack:{_windowStack.Count}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);
        }
        _windowStack.Push(window);

        try
        {
            //TODO CreateWindowだけ囲えば良さそうだが、一旦全部囲ってしまう
            using var switchContext = new SwitchThreadDpiAwarenessContextPerMonitorAwareV2RAII(window, _logger);

            _logger.LogWithLine(LogLevel.Trace, "Invoke start", Environment.CurrentManagedThreadId);
            var result = func(window);
            _logger.LogWithLine(LogLevel.Trace, "Invoke end", Environment.CurrentManagedThreadId);
            return result;
        }
        finally
        {
            if (!_windowStack.TryPop(out var window_))
            {
                Debug.Fail("pop failed.");
            }
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogWithLine(LogLevel.Trace, $"Pop [handle:{window.Handle:X}] [stack:{_windowStack.Count}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);
            }
            Debug.Assert(window_ == window);
        }
    }

    [Conditional("DEBUG")]
    private void PrintWindowStack()
    {
        _logger.LogWithLine(LogLevel.Trace, "================================= PrintWindowStack start", Environment.CurrentManagedThreadId);
        if (_windowStack.IsEmpty)
        {
            _logger.LogWithLine(LogLevel.Trace, "EMPTY", Environment.CurrentManagedThreadId);
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogWithLine(LogLevel.Trace, $"window stack {string.Join("<-", _windowStack.Select((window, idx) => $"[hash:{window.GetHashCode():X}][window:{window}]"))}", Environment.CurrentManagedThreadId);
            }
        }
        _logger.LogWithLine(LogLevel.Trace, "================================= PrintWindowStack end", Environment.CurrentManagedThreadId);
    }

    public bool TryGetWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out IWindowInternal window)
    {
        PrintWindowStack();
        PrintWindowMap();

        if (FindWindow(hwnd, out window))
        {
            return true;
        }

        if (_windowStack.TryPeek(out window))
        {
            if (window.HWND.Handle == hwnd.Handle)
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"in window stack [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
                }
                return true;
            }

            if (window.HWND.Handle == User32.HWND.None.Handle)
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"in window stack (until create process) [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
                }
                //最速でHWNDを受け取る
                window.HWND = hwnd;
                return true;
            }

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogWithHWnd(LogLevel.Trace, $"in window stack, but other window [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
            }
            return false;
        }
        else
        {
            _logger.LogWithHWnd(LogLevel.Trace, "stack is empty", hwnd, Environment.CurrentManagedThreadId);
            return false;
        }
    }
}
