using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace MomijiTest.Core.Window.IntegrationTest;

[TestClass]
public partial class WindowManagerIntegrationTest
{
    public WindowManagerIntegrationTest()
    {
    }

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.UseMomijiWindow();

        return builder.Build();
    }

    private class TestException : Exception
    {

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
    public void TestCloseAll()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<WindowManagerIntegrationTest>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();

        windowMap.CloseAll();

    }


    [TestMethod]
    public async Task TestCreateWindow()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<WindowManagerIntegrationTest>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();

        //起動
        using var active = uiThreadActivator.Activate();

        var window = await windowManager.CreateWindowAsync(new() { 
            onMessage = (sender, message) => {
                if (message.Msg == 0)
                {
                    if (message.WParam == 1)
                    {
                        message.Result = message.LParam;
                        message.Handled = true;
                    }
                
                }
            }
        });

        {
            var result = window.SendMessage(0, 1, 2);
            Assert.AreEqual(2, result);
        }

        //メッセージポンプが無いので動作しない
        window.PostMessage(0, 0, 0);

        //全部クローズ
        windowMap.CloseAll();

        //個別のcloseは失敗するが許している
        window.Close();
    }
}
