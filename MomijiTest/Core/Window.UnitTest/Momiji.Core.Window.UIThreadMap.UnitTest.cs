using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class UIThreadMapUnitTest
{
    public UIThreadMapUnitTest()
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
        });

        return builder.Build();
    }

    [TestMethod]
    public void TestConstructor()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var handler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var map = new UIThreadMap(
            loggerFactory.CreateLogger<UIThreadMap>(),
            handler
        );
    }

    [TestMethod]
    public void TestFinalizer()
    {
        {
            using var host = Setup();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

            void run()
            {
                var handler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

                var _ = new UIThreadMap(
                    loggerFactory.CreateLogger<UIThreadMap>(),
                    handler
                );
            }

            run();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [TestMethod]
    public void TestDoubleDestruct()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var handler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var map = new UIThreadMap(
            loggerFactory.CreateLogger<UIThreadMap>(),
            handler
        );
        map.Dispose();
        map.Dispose();
    }

    [TestMethod]
    public async Task TestDoubleDestructAsync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var handler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var map = new UIThreadMap(
            loggerFactory.CreateLogger<UIThreadMap>(),
            handler
        );
        await map.DisposeAsync();
        await map.DisposeAsync();
    }

    internal class UIThreadDummy : IUIThreadInternal
    {
        public event IUIThread.InactivatedEventHandler? OnInactivated;

        public ValueTask CancelAsync() => ValueTask.CompletedTask;
        public void Dispose() => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<TResult> EnqueueAsync<TResult>(Func<IWindowManager, TResult> item) => throw new NotImplementedException();
        public void RunMessageLoop(TaskCompletionSource<IUIThread> startTcs, CancellationToken ct = default)
        {
            startTcs.TrySetResult(this);
        }
    }

    [TestMethod]
    public void TestAddRemove()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var handler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var map = new UIThreadMap(
            loggerFactory.CreateLogger<UIThreadMap>(),
            handler
        );

        var thread = new UIThreadDummy();
        var task = Task.CompletedTask;

        map.Add(thread, task);
        map.Remove(thread);
    }

    [TestMethod]
    public async Task TestCancelAllAsync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var handler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var map = new UIThreadMap(
            loggerFactory.CreateLogger<UIThreadMap>(),
            handler
        );

        var thread = new UIThreadDummy();
        var task = Task.CompletedTask;

        map.Add(thread, task);
        await map.CancelAllAsync();
    }

    internal class UIThreadDummyCancelAsyncError : IUIThreadInternal
    {
        public event IUIThread.InactivatedEventHandler? OnInactivated;

        public ValueTask CancelAsync() => ValueTask.FromException(new Exception("ERROR"));
        public void Dispose() => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<TResult> EnqueueAsync<TResult>(Func<IWindowManager, TResult> item) => throw new NotImplementedException();
        public void RunMessageLoop(TaskCompletionSource<IUIThread> startTcs, CancellationToken ct = default)
        {
            startTcs.TrySetResult(this);
        }
    }

    internal class UIThreadDummyCancelAsyncEndless : IUIThreadInternal
    {
        public event IUIThread.InactivatedEventHandler? OnInactivated;

        public ValueTask CancelAsync()
        {
            return new ValueTask(Task.Delay(1000000));
        }
        public void Dispose() => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<TResult> EnqueueAsync<TResult>(Func<IWindowManager, TResult> item) => throw new NotImplementedException();
        public void RunMessageLoop(TaskCompletionSource<IUIThread> startTcs, CancellationToken ct = default)
        {
            startTcs.TrySetResult(this);
        }
    }

    [TestMethod]
    public async Task TestCancelAllAsyncError()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var handler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var map = new UIThreadMap(
            loggerFactory.CreateLogger<UIThreadMap>(),
            handler
        );

        var thread = new UIThreadDummyCancelAsyncError();
        var task = Task.CompletedTask;

        map.Add(thread, task);
    }

    internal class UIThreadDummyCancelAsyncEternal : IUIThreadInternal
    {
        public event IUIThread.InactivatedEventHandler? OnInactivated;

        public async ValueTask CancelAsync()
        {
            await Task.Delay(1000000);
        }
        public void Dispose() => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<TResult> EnqueueAsync<TResult>(Func<IWindowManager, TResult> item) => throw new NotImplementedException();
        public void RunMessageLoop(TaskCompletionSource<IUIThread> startTcs, CancellationToken ct = default)
        {
            startTcs.TrySetResult(this);
        }
    }

    [TestMethod]
    public async Task TestCancelAllAsyncEndless()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var handler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var map = new UIThreadMap(
            loggerFactory.CreateLogger<UIThreadMap>(),
            handler
        );

        var thread = new UIThreadDummyCancelAsyncEndless();
        var task = Task.CompletedTask;

        map.Add(thread, task);
        //await map.CancelAllAsync();
    }

}
