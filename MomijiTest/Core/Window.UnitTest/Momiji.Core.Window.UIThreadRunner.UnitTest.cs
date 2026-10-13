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
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var uiThreadRunner = new UIThreadRunner(
            loggerFactory.CreateLogger<UIThreadRunner>(),
            factory, 
            onUnhandledExceptionHandler
        );
        var uiThread = await uiThreadRunner.StartAsync();

        var result = await uiThread.EnqueueAsync((manager) => { return 999; });
        Assert.AreEqual(999, result);
    }

    [TestMethod]
    public async Task TestStartAsync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadRunnerTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var mre = new ManualResetEventSlim(false);

        using var uiThreadRunner = new UIThreadRunner(
            loggerFactory.CreateLogger<UIThreadRunner>(),
            factory, 
            onUnhandledExceptionHandler
        );
        
        uiThreadRunner.OnStop += (sender, e) => {
            logger.LogInformation(e, "on stop");
            mre.Set();
        };

        try
        {
            var uiThread = await uiThreadRunner.StartAsync();
            try
            {
                //２重起動はさせない
                var uiThread2 = await uiThreadRunner.StartAsync();
                Assert.Fail();
            }
            finally
            {
                await uiThread.CancelAsync();
            }
        }
        catch (InvalidOperationException e)
        {
            //OK
            logger.LogInformation(e, "error occurred");
        }

        logger.LogInformation("mre wait");
        mre.Wait();
        logger.LogInformation("mre end");


        try
        {
            //UIスレッドが終わっていれば、もう一度起動できる
            var uiThread = await uiThreadRunner.StartAsync();
        }
        catch (InvalidOperationException e)
        {
            //OK
            logger.LogInformation(e, "error occurred");
        }

    }


    [TestMethod]
    public async Task TestErrorOnStop()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadRunnerTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var uiThreadRunner = new UIThreadRunner(
            loggerFactory.CreateLogger<UIThreadRunner>(),
            factory, 
            onUnhandledExceptionHandler
        );

        uiThreadRunner.OnStop += (sender, e) => {
            throw new Exception("ON STOP ERROR", e);
        };

        var uiThread = await uiThreadRunner.StartAsync();

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
            var window = uiThread.CreateWindow(new()
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
    }

    [TestMethod]
    public async Task TestAccessDisposedObject()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadRunnerTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        IUIThread? uiThread = default;
        {
            using var uiThreadRunner = new UIThreadRunner(
                loggerFactory.CreateLogger<UIThreadRunner>(),
                factory, 
                onUnhandledExceptionHandler
            );
            uiThread = await uiThreadRunner.StartAsync();
        }

        try
        {
            await uiThread.EnqueueAsync((manager) => {
                return 0;
            });
            Assert.Fail();
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "dispatch error occurred");
        }
    }

}
