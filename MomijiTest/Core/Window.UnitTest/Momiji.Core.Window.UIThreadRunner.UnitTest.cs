using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class UIThreadRunnerTest
{
    public UIThreadRunnerTest()
    {
    }

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();

            services.AddScoped<IUIThreadInternal, UIThread>();
            services.AddScoped<IUIThreadActivator, UIThreadActivator>();
            services.AddScoped<IDispatcherQueue, DispatcherQueue>();
            services.AddScoped<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
            services.AddScoped<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
            services.AddScoped<IWindowManagerInternal, WindowManager>();
            services.AddScoped<IWindowProcedure, WindowProcedure>();
            services.AddScoped<IWindowClassManager, WindowClassManager>();
        });

        return builder.Build();
    }

    private class TestException : Exception
    {
    
    }

    [TestMethod]
    public async Task TestConstruct()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadRunnerTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();

        var uiThreadRunner = new UIThreadRunner(
            loggerFactory.CreateLogger<UIThreadRunner>(),
            factory
        );
    }

    [TestMethod]
    public async Task TestStartAsync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadRunnerTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        
        using var mre = new ManualResetEventSlim(false);

        var uiThreadRunner = new UIThreadRunner(
            loggerFactory.CreateLogger<UIThreadRunner>(),
            factory
        );
        
        uiThreadRunner.OnStop += (sender, e) => {
            logger.LogInformation(e, "on stop");
            mre.Set();
        };

        try
        {
            var (uiThread1, processTask1) = await uiThreadRunner.StartAsync();
            var (uiThread2, processTask2) = await uiThreadRunner.StartAsync();

            await uiThread1.CancelAsync();
            await uiThread2.CancelAsync();
        }
        catch (InvalidOperationException e)
        {
            //OK
            logger.LogInformation(e, "error occurred");
        }

        logger.LogInformation("mre wait");
        mre.Wait();
        logger.LogInformation("mre end");

    }


    [TestMethod]
    public async Task TestErrorOnStop()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadRunnerTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();

        var uiThreadRunner = new UIThreadRunner(
            loggerFactory.CreateLogger<UIThreadRunner>(),
            factory
        );

        uiThreadRunner.OnStop += (sender, e) => {
            throw new Exception("ON STOP ERROR", e);
        };

        var (uiThread, processTask) = await uiThreadRunner.StartAsync();

        try
        {
            //UIスレッドが耐える
            var result = await uiThread.EnqueueAsync<int>((manager) => { throw new Exception("DISPATCH ERROR"); });
            Assert.Fail();
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "dispatch error occurred");
        }

        try
        {
            //UIスレッドが耐える
            var window = await uiThread.CreateWindowAsync(new()
            {
                onMessage = (sender, message) => {
                    throw new Exception($"ON MESSAGE ERROR {message}");
                }
            });
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "message error occurred");
        }

        await uiThread.CancelAsync();
    }

}
