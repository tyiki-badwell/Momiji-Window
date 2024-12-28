using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Window;

[TestClass]
public partial class UIThreadTest : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public UIThreadTest()
    {
        var configuration = CreateConfiguration();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Window", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.Window.UIThreadTest", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<UIThreadTest>();
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static IConfiguration CreateConfiguration()
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        return configuration;
    }

    private partial class DummyWindowManager(bool empty) : IWindowManagerInternal
    {
        public IWindowClassManager WindowClassManager => throw new NotImplementedException();
        public IWindowProcedure WindowProcedure => throw new NotImplementedException();
        public bool IsEmpty => empty;
        public void CloseAll() {}
        public IWindow CreateWindow(IWindowManager.CreateWindowParameter parameter) => throw new NotImplementedException();
        public ValueTask<TResult> DispatchAsync<TResult>(Func<IWindow, TResult> item, IWindowInternal window) => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
        public int GenerateChildId(IWindowInternal window) => throw new NotImplementedException();
    }

    [TestMethod]
    public async Task TestDispatchAsyncOK()
    {
        var uiThreadChecker = new UIThreadActivator(_loggerFactory);
        using var thread = new UIThread(
            _loggerFactory,
            uiThreadChecker,
            new DispatcherQueue(_loggerFactory, uiThreadChecker),
            new DummyWindowManager(true)
        );

        using var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = Task.Run(() =>
        {
            thread.RunMessageLoop(tcs, cts.Token);
            _logger.LogInformation("loop end");
        });

        using var uiThread = await tcs.Task;

        var result = await await Task.Run(async () =>
        {
            return await uiThread.DispatchAsync(async (manager) =>
            {
                //immidiate mode
                var result = await uiThread.DispatchAsync((manager) => 1);
                return 1 + result;
            });
        });

        Assert.AreEqual(2, result);

        cts.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestDispatchAsyncFail()
    {
        var uiThreadChecker = new UIThreadActivator(_loggerFactory);
        using var thread = new UIThread(
            _loggerFactory,
            uiThreadChecker,
            new DispatcherQueue(_loggerFactory, uiThreadChecker),
            new DummyWindowManager(true)
        );

        try
        {
            using var cts = new CancellationTokenSource();
            var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

            var task = Task.Run(() =>
            {
                thread.RunMessageLoop(tcs, cts.Token);
                _logger.LogInformation("loop end");
            });

            using var uiThread = await tcs.Task;

            cts.Cancel();
            await task;

            var result = await Task.Run(async () =>
            {
                return await uiThread.DispatchAsync((manager) => 1);
            });

            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            _logger.LogInformation(e, $"Åöthread:[{Environment.CurrentManagedThreadId:X}] error end");
        }
    }

    [TestMethod]
    public async Task TestMessageLoopForceEnd()
    {
        Task? task = null;

        {
            var uiThreadChecker = new UIThreadActivator(_loggerFactory);
            using var thread = new UIThread(
                _loggerFactory,
                uiThreadChecker,
                new DispatcherQueue(_loggerFactory, uiThreadChecker),
                new DummyWindowManager(true)
            );

            var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

            task = Task.Run(() =>
            {
                thread.RunMessageLoop(tcs, default);
                _logger.LogInformation("loop end");
            });

            using var uiThread = await tcs.Task;
            thread.Dispose();
        }

        if (task != default)
        {
            await task;
        }
    }
}
