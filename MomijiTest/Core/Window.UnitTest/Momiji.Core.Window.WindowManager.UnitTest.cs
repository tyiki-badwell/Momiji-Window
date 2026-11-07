using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class WindowManagerUnitTest
{
    public WindowManagerUnitTest()
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
            services.AddSingleton<IDispatcherQueue, DummyDispatcherQueue>();
            services.AddSingleton<IDispatcherQueueSynchronizationContext, DummyDispatcherQueueSynchronizationContext>();
            services.AddSingleton<IWindowProcedure, DummyWindowProcedure>();
            services.AddSingleton<IWindowFunction, DummyWindowFunction>();
            services.AddSingleton<IWindowClass, DummyWindowClass>();
            services.AddSingleton<IWindowClassManager, DummyWindowClassManager>();
            services.AddSingleton<IWindowManagerInternal, WindowManager>();
            services.AddSingleton<IWindowMap, WindowMap>();
            services.AddKeyedTransient<IWindowInternal, NativeWindow>(NativeWindow.Type);

        });

        return builder.Build();
    }

    private partial class DummyDispatcherQueue : IDispatcherQueue
    {
        public WaitHandle WaitHandle => throw new NotImplementedException();

        public int Count => throw new NotImplementedException();

        public event IDispatcherQueue.EnqueueHandler? OnEnqueue;

        public void Dispatch(SendOrPostCallback callback, object? param) => throw new NotImplementedException();
#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
        public async ValueTask<TResult> EnqueueAsync<TResult>(Func<TResult> func) => func();
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
        public void DispatchQueue() => throw new NotImplementedException();
        public void Dispose()
        {
        }

        public void Enqueue(SendOrPostCallback callback, object? param) => throw new NotImplementedException();
        public void Enqueue(Action action) => throw new NotImplementedException();
        public void ResetEvent() => throw new NotImplementedException();
        public bool TryDequeue([MaybeNullWhen(false)] out object item) => throw new NotImplementedException();
    }

    private partial class DummyWindowProcedure : IWindowProcedure
    {
        public nint FunctionPointer => throw new NotImplementedException();

        public nint SubFunctionPointer => throw new NotImplementedException();

        public event IWindowProcedure.WindowMessageEventHandler? OnWindowMessage;
        public event IWindowProcedure.OnThreadMessageEventHandler? OnThreadMessage;

        public void DispatchMessage() => throw new NotImplementedException();
        public void Dispose()
        {
        }

        public void ThrowIfOccurredInWndProc() => throw new NotImplementedException();
    }

    private partial class DummyWindowFunction : IWindowFunction
    {
        public void PostMessage(User32.HWND hwnd, uint nMsg, nint wParam, nint lParam)
        {
        }
        public nint SendMessage(User32.HWND hwnd, uint nMsg, nint wParam, nint lParam)
        {
            return lParam;
        }
        public User32.HWND CreateWindow(User32.WS_EX dwExStyle, nint lpszClassName, string windowTitle, User32.WS style, int x, int y, int width, int height, User32.HWND hwndParent, nint hMenu, nint hInst)
        {
            return User32.HWND.None;
        }

        public bool MoveWindow(User32.HWND hwnd, int x, int y, int width, int height, bool repaint) => throw new NotImplementedException();
        public bool ShowWindow(User32.HWND hwnd, int cmdShow) => throw new NotImplementedException();
        public bool SetWindowStyle(User32.HWND hwnd, int style) => throw new NotImplementedException();
        public (nint, Win32Exception) SetWindowLong(User32.HWND hwnd, int nIndex, nint dwNewLong) => throw new NotImplementedException();
    }

    private partial class DummyWindowClass : IWindowClass
    {
        public nint ClassName => nint.Zero;

        public nint HInstance => nint.Zero;

        public void CallOriginalWindowProc(User32.HWND hwnd, IWindowManager.IMessage message) => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
        public void Initialize(User32.WNDCLASSEX.CS classStyle, string className) => throw new NotImplementedException();
    }

    private partial class DummyWindowClassManager : IWindowClassManager
    {
        public void Dispose()
        {
        }
        public IWindowClass QueryWindowClass(string className, User32.WNDCLASSEX.CS classStyle) => new DummyWindowClass();
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

    private partial class DummyDispatcherQueueSynchronizationContext : IDispatcherQueueSynchronizationContext
    {
        public SynchronizationContext SynchronizationContext => throw new NotImplementedException();
    }

    private class TestException : Exception
    {

    }

    [TestMethod]
    public void TestConstruct()
    {
        using var host = Setup();
        var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();

    }

    [TestMethod]
    public async Task TestDispatchFail()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<WindowManagerUnitTest>();
        var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();

        try
        {
            var result = await windowManager.EnqueueAsync<int>((window) => { throw new TestException(); }, new DummyWindow());
            Assert.Fail();
        }
        catch (TestException e)
        {
            //OK
            logger.LogInformation(e, "error occurred");
        }

    }

    [TestMethod]
    public async Task TestCreateWindow()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<WindowManagerUnitTest>();
        var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();

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
