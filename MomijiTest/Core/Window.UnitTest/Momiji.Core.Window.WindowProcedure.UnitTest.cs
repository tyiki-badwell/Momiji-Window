using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class WindowProcedureUnitTest
{
    public WindowProcedureUnitTest()
    {
    }

    [TestCleanup]
    public void TestCleanup()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
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

        using var procedure = new WindowProcedure(
            loggerFactory.CreateLogger<WindowProcedure>(),
            uiThreadActivator
        );

        procedure.FunctionPointer.ToString();
        procedure.SubFunctionPointer.ToString();
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

                var _ = new WindowProcedure(
                    loggerFactory.CreateLogger<WindowProcedure>(),
                    uiThreadActivator
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

        var procedure = new WindowProcedure(
            loggerFactory.CreateLogger<WindowProcedure>(),
            uiThreadActivator
        );
        procedure.Dispose();
        procedure.FunctionPointer.ToString();
        procedure.SubFunctionPointer.ToString();

        procedure.Dispose();
        procedure.FunctionPointer.ToString();
        procedure.SubFunctionPointer.ToString();
    }

    [TestMethod]
    public async Task TestDispatchMessage()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        
        var active = uiThreadActivator.Activate();

        using var procedure = new WindowProcedure(
            loggerFactory.CreateLogger<WindowProcedure>(),
            uiThreadActivator
        );

        var windowFunction = new WindowFunction(
            loggerFactory.CreateLogger<WindowFunction>(),
            uiThreadActivator,
            procedure
        );

        windowFunction.PostMessage(User32.HWND.None, 0, 0, 0);
        windowFunction.PostMessage(User32.HWND.BROADCAST, 0, 0, 0);

        procedure.DispatchMessage();
    }


}
