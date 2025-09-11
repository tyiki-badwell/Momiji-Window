using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class WindowProcedureUnitTest
{
    public WindowProcedureUnitTest()
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

        using var executor = new WindowProcedure(
            loggerFactory.CreateLogger<WindowProcedure>(),
            uiThreadActivator
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

        var executor = new WindowProcedure(
            loggerFactory.CreateLogger<WindowProcedure>(),
            uiThreadActivator
        );
        executor.Dispose();
        executor.Dispose();
    }

    [TestMethod]
    public async Task TestOnInactivated()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        
        var active = uiThreadActivator.Activate();

        using var executor = new WindowProcedure(
            loggerFactory.CreateLogger<WindowProcedure>(),
            uiThreadActivator
        );

        active.Dispose();
    }


}
