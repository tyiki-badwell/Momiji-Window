using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

public static class IUIThreadExtensions
{
    public static IWindow CreateWindow(
        this IUIThread uiThread,
        IWindowManager.CreateWindowParameter parameter
    )
    {
        return uiThread.CreateWindowAsync(parameter).AsTask().GetAwaiter().GetResult();
    }

    public static async ValueTask<IWindow> CreateWindowAsync(
        this IUIThread uiThread,
        IWindowManager.CreateWindowParameter parameter
    )
    {
        return await uiThread.EnqueueAsync(
            (manager) => 
            manager.CreateWindow(
                parameter
            )
        );
    }
}

public static class IWindowExtensions
{
    public static nint Close(
        this IWindow window
    )
    {
        return window.SendMessage(
            0x0010, //WM_CLOSE
            nint.Zero,
            nint.Zero
        );
    }

    public static bool Move(
        this IWindow window,
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        return window.MoveAsync(
            x,
            y,
            width,
            height,
            repaint
        ).AsTask().GetAwaiter().GetResult();
    }

    public static async ValueTask<bool> MoveAsync(
        this IWindow window,
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        return await window.EnqueueAsync(
            (window) =>
            ((NativeWindow)window).MoveImpl(
                x,
                y,
                width,
                height,
                repaint
            )
        );
    }

    private static bool MoveImpl(
        this NativeWindow window,
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        if (window.Logger.IsEnabled(LogLevel.Information))
        {
            window.Logger.LogWithHWnd(LogLevel.Information, $"MoveWindow x:[{x}] y:[{y}] width:[{width}] height:[{height}] repaint:[{repaint}]", window.HWND, Environment.CurrentManagedThreadId);
        }
        var result =
            User32.MoveWindow(
                window.HWND,
                x,
                y,
                width,
                height,
                repaint
        );

        if (!result)
        {
            var error = new Win32Exception();
            window.Logger.LogWithHWndAndError(LogLevel.Error, "MoveWindow", window.HWND, error.ToString(), Environment.CurrentManagedThreadId);
        }
        return result;
    }

    public static bool Show(
        this IWindow window,
        int cmdShow
    )
    {
        return window.ShowAsync(
            cmdShow
        ).AsTask().GetAwaiter().GetResult();
    }

    public static async ValueTask<bool> ShowAsync(
        this IWindow window,
        int cmdShow
    )
    {
        return await window.EnqueueAsync(
            (window) =>
            ((NativeWindow)window).ShowImpl(cmdShow)
        );
    }

    private static bool ShowImpl(
        this NativeWindow window,
        int cmdShow
    )
    {
        if (window.Logger.IsEnabled(LogLevel.Information))
        {
            window.Logger.LogWithHWnd(LogLevel.Information, $"ShowWindow cmdShow:[{cmdShow}]", window.HWND, Environment.CurrentManagedThreadId);
        }
        var result =
            User32.ShowWindow(
                window.HWND,
                cmdShow
            );

        var errorCode = Marshal.GetLastPInvokeError();

        //result=0: 実行前は非表示だった/ <>0:実行前から表示されていた
        if (window.Logger.IsEnabled(LogLevel.Information))
        {
            window.Logger.LogWithHWndAndError(LogLevel.Information, $"ShowWindow result:[{result}]", window.HWND, errorCode, Environment.CurrentManagedThreadId);
        }

        if (errorCode == 1400) // ERROR_INVALID_WINDOW_HANDLE
        {
            var error = new Win32Exception(errorCode);
            throw error;
        }

        {
            var wndpl = new User32.WINDOWPLACEMENT()
            {
                length = Marshal.SizeOf<User32.WINDOWPLACEMENT>()
            };
            User32.GetWindowPlacement(window.HWND, ref wndpl);

            if (window.Logger.IsEnabled(LogLevel.Information))
            {
                window.Logger.LogWithHWnd(LogLevel.Information, $"GetWindowPlacement result cmdShow:[{cmdShow}] -> wndpl:[{wndpl}]", window.HWND, Environment.CurrentManagedThreadId);
            }
        }

        return result;
    }

    public static bool SetWindowStyle(
        this IWindow window,
        int style
    )
    {
        return window.SetWindowStyleAsync(
            style
        ).AsTask().GetAwaiter().GetResult();
    }

    public static async ValueTask<bool> SetWindowStyleAsync(
        this IWindow window,
        int style
    )
    {
        return await window.EnqueueAsync(
            (window) =>
            ((NativeWindow)window).SetWindowStyleImpl(style)
        );
    }

    private static bool SetWindowStyleImpl(
        this NativeWindow window,
        int style
    )
    {
        //TODO DPI対応
        var clientRect = new User32.RECT();

        {
            var result = User32.GetClientRect(window.HWND, ref clientRect);
            if (!result)
            {
                var error = new Win32Exception();
                window.Logger.LogWithHWndAndError(LogLevel.Error, "GetClientRect failed", window.HWND, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var result = User32.AdjustWindowRectExForDpi(ref clientRect, style, false, 0, 96);
            if (!result)
            {
                var error = new Win32Exception();
                window.Logger.LogWithHWndAndError(LogLevel.Error, "AdjustWindowRectExForDpi failed", window.HWND, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var (result, error) = window.SetWindowLong(window.HWND, -16, new nint(style)); //GWL_STYLE
            if (result == nint.Zero && error.NativeErrorCode != 0)
            {
                window.Logger.LogWithHWndAndError(LogLevel.Error, "SetWindowLong failed", window.HWND, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var width = clientRect.right - clientRect.left;
            var height = clientRect.bottom - clientRect.top;

            window.Logger.LogWithHWnd(LogLevel.Information, "SetWindowPos", window.HWND, Environment.CurrentManagedThreadId);
            var result =
                User32.SetWindowPos(
                        window.HWND,
                        User32.HWND.None,
                        0,
                        0,
                        width,
                        height,
                        0x0002 //SWP_NOMOVE
                               //| 0x0001 //SWP_NOSIZE
                        | 0x0004 //SWP_NOZORDER
                        | 0x0020 //SWP_FRAMECHANGED
                    );

            if (!result)
            {
                var error = new Win32Exception();
                window.Logger.LogWithHWndAndError(LogLevel.Error, "SetWindowPos failed", window.HWND, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        return true;
    }

}

public static class IHostBuilderExtensions
{
    public static IHostBuilder UseMomijiWindow(this IHostBuilder hostBuilder)
    {
        return hostBuilder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IUIThreadFactory, UIThreadFactory>();
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();

            services.AddTransient<IUIThreadRunner, UIThreadRunner>();

            services.AddScoped<IUIThreadInternal, UIThread>();
            services.AddScoped<IUIThreadActivator, UIThreadActivator>();
            services.AddScoped<IDispatcherQueue, DispatcherQueue>();
            services.AddScoped<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
            services.AddScoped<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
            services.AddScoped<IWindowManagerInternal, WindowManager>();
            services.AddScoped<IWindowProcedure, WindowProcedure>();
            services.AddScoped<IWindowClassManager, WindowClassManager>();

            services.AddTransient<IWindowClass, WindowClass>();
            services.AddKeyedTransient<IWindowInternal, NativeWindow>(NativeWindow.Type);
            services.AddKeyedTransient<IWindowInternal, SubClassNativeWindow>(SubClassNativeWindow.Type);
            services.AddKeyedTransient<IWindowInternal, SubClassNativeWindow2>(SubClassNativeWindow2.Type);

        });
    }

}