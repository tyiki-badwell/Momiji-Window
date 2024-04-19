using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

[TestClass]
public class WindowManagerTest : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public WindowManagerTest()
    {
        var configuration = CreateConfiguration();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Window", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.Window.WindowManagerTest", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<WindowManagerTest>();
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

    private class DummyUIThreadChecker : IUIThreadChecker
    {
        public bool IsActivatedThread => throw new NotImplementedException();

        public uint NativeThreadId => throw new NotImplementedException();

        public event IUIThreadChecker.InactivatedEventHandler? OnInactivated = () => { };

        public void ThrowIfCalledFromOtherThread() {}
        public void ThrowIfNoActive() => throw new NotImplementedException();
    }

    private class DummyDispatcherQueue : IDispatcherQueue
    {
        public void Dispatch(SendOrPostCallback callback, object? param) => throw new NotImplementedException();
#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
        public async ValueTask<TResult> DispatchAsync<TResult>(Func<TResult> func) => func();
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
    }

    private class DummyWindow : IWindowInternal
    {
        public nint Handle => 0;

        User32.HWND IWindowInternal.HWND
        {
            get => User32.HWND.None;
            set => throw new NotImplementedException();
        }

        public bool Close() => throw new NotImplementedException();
        public ValueTask<T> DispatchAsync<T>(Func<IWindow, T> func) => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
        public void OnMessage(IWindowManager.IMessage message) => throw new NotImplementedException();
        public void PostMessage(int nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        public bool ReplyMessage(nint lResult) => throw new NotImplementedException();
        public nint SendMessage(int nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
    }

    private class TestException : Exception
    {

    }

    [TestMethod]
    public void TestConstruct()
    {
        using var windowManager =
            new WindowManager(
                _loggerFactory,
                new DummyUIThreadChecker(),
                new DummyDispatcherQueue()
            );

        Assert.IsNotNull(windowManager.WindowClassManager);
        Assert.IsNotNull(windowManager.WindowProcedure);
    }

    [TestMethod]
    public async Task TestDispatchFail()
    {
        using var windowManager = 
            new WindowManager(
                _loggerFactory, 
                new DummyUIThreadChecker(), 
                new DummyDispatcherQueue()
            );

        try
        {
#pragma warning disable CS0162 // 到達できないコードが検出されました
            var result = await windowManager.DispatchAsync((window) => { throw new TestException(); return 0; }, new DummyWindow());
#pragma warning restore CS0162 // 到達できないコードが検出されました
            Assert.Fail();
        }
        catch (TestException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }

    }

    [TestMethod]
    public void TestCloseAll()
    {
        using var windowManager =
            new WindowManager(
                _loggerFactory,
                new DummyUIThreadChecker(),
                new DummyDispatcherQueue()
            );

        windowManager.CloseAll();

    }


    [TestMethod]
    public void TestCreateWindow()
    {
        using var windowManager =
            new WindowManager(
                _loggerFactory,
                new DummyUIThreadChecker(),
                new DummyDispatcherQueue()
            );

        //TODO メッセージポンプが無いので、応答待ちになる
        var window = windowManager.CreateWindow(new());

        window.Close();
    }



}
