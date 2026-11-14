using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Momiji.Core.Window;

public static class IUIThreadExtensions
{
    public static async ValueTask<IWindow> CreateWindowAsync(
        this IUIThread uiThread,
        IWindowManager.CreateWindowParameter parameter
    )
    {
        return await await uiThread.EnqueueAsync(
            (manager) => 
            manager.CreateWindowAsync(
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
            services.AddScoped<IWindowClassManager, WindowClassManager>();

            services.AddTransient<IWindowClass, WindowClass>();
            services.AddKeyedTransient<IWindowInternal, NativeWindow>(NativeWindow.Type);
            services.AddKeyedTransient<IWindowInternal, SubClassNativeWindow>(SubClassNativeWindow.Type);
            services.AddKeyedTransient<IWindowInternal, SubClassNativeWindow2>(SubClassNativeWindow2.Type);

        });
    }

}