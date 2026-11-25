using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

public static class IUIThreadExtensions
{
    public static async ValueTask<IWindow> CreateWindowAsync(
        this IUIThread uiThread,
        IWindowManager.CreateWindowParameter parameter
    )
    {
        return await uiThread.WindowManager.CreateWindowAsync(parameter);
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
            ((IWindowInternal)window).WindowFunction.MoveWindow(
                ((IWindowInternal)window).HWND,
                x,
                y,
                width,
                height,
                repaint
            )
        );
    }


    public static async ValueTask<bool> ShowAsync(
        this IWindow window,
        int cmdShow
    )
    {
        return await window.EnqueueAsync(
            (window) =>
            ((IWindowInternal)window).WindowFunction.ShowWindow(
                ((IWindowInternal)window).HWND,
                cmdShow
            )
        );
    }


    public static async ValueTask<bool> SetWindowStyleAsync(
        this IWindow window,
        int style
    )
    {
        return await window.EnqueueAsync(
            (window) =>
            ((IWindowInternal)window).WindowFunction.SetWindowStyle(
                ((IWindowInternal)window).HWND,
                style
            )
        );
    }
}

internal static class IOnUnhandledExceptionHandlerExtensions
{
    internal static void DoDisposeAsyncCoreSyncWait(
        this IOnUnhandledExceptionHandler onUnhandledExceptionHandler,
        ValueTask valueTask,
        ILogger logger
    )
    {
        var task = valueTask.AsTask();

        try
        {
            var retry = 5;
            while (retry-- > 0)
            {
                if (task.Wait(1000))
                {
                    break;
                }
            }

            if (!task.IsCompleted)
            {
                throw new InvalidOperationException("process task is not completed.");
            }

            task.GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            logger.LogWithLine(LogLevel.Error, e, "failed.", Environment.CurrentManagedThreadId);
            if (!onUnhandledExceptionHandler.DoUnhandledException(e))
            {
                logger.LogWithLine(LogLevel.Error, e, "onUnhandledException failed.", Environment.CurrentManagedThreadId);
            }
        }
    }


    internal static async ValueTask DoDisposeAsyncCore(
        this IOnUnhandledExceptionHandler onUnhandledExceptionHandler,
        ValueTask valueTask,
        ILogger logger
    )
    {
        try
        {
            await valueTask.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWithLine(LogLevel.Error, e, "failed.", Environment.CurrentManagedThreadId);
            if (!onUnhandledExceptionHandler.DoUnhandledException(e))
            {
                logger.LogWithLine(LogLevel.Error, e, "onUnhandledException failed.", Environment.CurrentManagedThreadId);
            }
        }
    }
}


public static class IHostBuilderExtensions
{
    public static IHostBuilder UseMomijiWindow(this IHostBuilder hostBuilder)
    {
        return hostBuilder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IUIThreadFactory, UIThreadFactory>();
            services.AddSingleton<IUIThreadMap, UIThreadMap>();
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();

            services.AddScoped<IUIThreadInternal, UIThread>();
            services.AddScoped<IUIThreadActivator, UIThreadActivator>();
            services.AddScoped<IDispatcherQueue, DispatcherQueue>();
            services.AddScoped<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
            services.AddScoped<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
            services.AddScoped<IWindowMap, WindowMap>();
            services.AddScoped<IWindowManager, WindowManager>();
            services.AddScoped<IWindowProcedure, WindowProcedure>();
            services.AddScoped<IWindowFunction, WindowFunction>();
            services.AddScoped<IWindowFactory, WindowFactory>();
            services.AddScoped<IWindowClassManager, WindowClassManager>();

            services.AddTransient<IWindowClass, WindowClass>();
        });
    }

}