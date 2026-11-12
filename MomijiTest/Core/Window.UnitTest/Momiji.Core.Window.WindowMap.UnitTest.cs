using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class WindowMapUnitTest
{
    public WindowMapUnitTest()
    {
    }

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IUIThreadActivator, UIThreadActivator>();
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
            services.AddSingleton<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
        });

        return builder.Build();
    }

    [TestMethod]
    public async Task TestConstructor()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();

        using var map = new WindowMap(
            loggerFactory.CreateLogger<WindowMap>(),
            uiThreadActivator,
            dispatcherQueue
        );
    }

    [TestMethod]
    public async Task TestFinalizer()
    {
        {
            using var host = Setup();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

            void run()
            {
                var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
                var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();

                var _ = new WindowMap(
                    loggerFactory.CreateLogger<WindowMap>(),
                    uiThreadActivator,
                    dispatcherQueue
                );
            }

            run();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [TestMethod]
    public async Task TestDoubleDestruct()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();

        var map = new WindowMap(
            loggerFactory.CreateLogger<WindowMap>(),
            uiThreadActivator,
            dispatcherQueue
        );
        map.Dispose();
        map.Dispose();
    }

    private partial class DummyWindow : IWindowInternal
    {
        public nint Handle => 0;

        User32.HWND IWindowInternal.HWND
        {
            get => User32.HWND.None;
            set => throw new NotImplementedException();
        }

        public ILogger Logger => throw new NotImplementedException();

        public IWindowFunction WindowFunction => throw new NotImplementedException();

        public bool Close() => throw new NotImplementedException();
        public ValueTask<T> EnqueueAsync<T>(Func<IWindow, T> func) => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
        public void Initialize(IWindowInternal.IParameter parameter) => throw new NotImplementedException();
        public void OnMessage(IWindowManager.IMessage message) => throw new NotImplementedException();
        public void PostMessage(int nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        public bool ReplyMessage(nint lResult) => throw new NotImplementedException();
        public nint SendMessage(int nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
    }

    [TestMethod]
    public async Task TestDispatchFail()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();

        using var map = new WindowMap(
            loggerFactory.CreateLogger<WindowMap>(),
            uiThreadActivator,
            dispatcherQueue
        );

        try
        {
            var result = await map.EnqueueAsync<int>((window) => { throw new Exception("TEST"); }, new DummyWindow());
            Assert.Fail();
        }
        catch (Exception)
        {
            //OK
        }
    }


}
