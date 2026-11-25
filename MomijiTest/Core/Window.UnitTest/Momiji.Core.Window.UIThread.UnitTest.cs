using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Core.Window;
using Momiji.Interop.User32;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class UIThreadUnitTest
{
    public UIThreadUnitTest()
    {
    }

    [TestCleanup]
    public void TestCleanup()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public class WindowMapDummy : IWindowMap
    {
        public bool IsEmpty => true;

        public void CloseAll()
        {
        }
        public void ForceClose()
        {
        }
        public nuint GenerateSubclassId() => throw new NotImplementedException();
        void IWindowMap.Add(IWindowInternal window) => throw new NotImplementedException();
        ValueTask<TResult> IWindowMap.EnqueueAsync<TResult>(Func<IWindowInternal, TResult> item, IWindowInternal window) => throw new NotImplementedException();
        bool IWindowMap.FindWindow(NativeMethods.HWND hwnd, out IWindowInternal window) => throw new NotImplementedException();
        int IWindowMap.GenerateChildId(IWindowInternal window) => throw new NotImplementedException();
        void IWindowMap.Remove(IWindowInternal window) => throw new NotImplementedException();
        bool IWindowMap.TryGetWindow(NativeMethods.HWND hwnd, out IWindowInternal window) => throw new NotImplementedException();
    }

    public class WindowMapDummyNoEmpty : IWindowMap
    {
        public bool IsEmpty => false;

        public void CloseAll()
        {
        }
        public void ForceClose()
        {
        }
        public nuint GenerateSubclassId() => throw new NotImplementedException();
        void IWindowMap.Add(IWindowInternal window) => throw new NotImplementedException();
        ValueTask<TResult> IWindowMap.EnqueueAsync<TResult>(Func<IWindowInternal, TResult> item, IWindowInternal window) => throw new NotImplementedException();
        bool IWindowMap.FindWindow(NativeMethods.HWND hwnd, out IWindowInternal window) => throw new NotImplementedException();
        int IWindowMap.GenerateChildId(IWindowInternal window) => throw new NotImplementedException();
        void IWindowMap.Remove(IWindowInternal window) => throw new NotImplementedException();
        bool IWindowMap.TryGetWindow(NativeMethods.HWND hwnd, out IWindowInternal window) => throw new NotImplementedException();
    }

    public class WindowFunctionDummy0 : IWindowFunction
    {
        public uint MsgWaitForMultipleObjectsEx(uint handleCount, PinnedBuffer<nint[]> waitHandlesPin)
        {
            Task.Delay(1000).Wait();
            return 0;
        }
        public void Setup()
        {
        }
        NativeMethods.HWND IWindowFunction.CreateWindow(NativeMethods.WS_EX dwExStyle, nint lpszClassName, string windowTitle, NativeMethods.WS style, int x, int y, int width, int height, NativeMethods.HWND hwndParent, nint hMenu, nint hInst) => throw new NotImplementedException();
        bool IWindowFunction.MoveWindow(NativeMethods.HWND hwnd, int x, int y, int width, int height, bool repaint) => throw new NotImplementedException();
        void IWindowFunction.PostMessage(NativeMethods.HWND hwnd, uint nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        nint IWindowFunction.SendMessage(NativeMethods.HWND hwnd, uint nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        (nint, Win32Exception) IWindowFunction.SetWindowLong(NativeMethods.HWND hwnd, int nIndex, nint dwNewLong) => throw new NotImplementedException();
        bool IWindowFunction.SetWindowStyle(NativeMethods.HWND hwnd, int style) => throw new NotImplementedException();
        bool IWindowFunction.ShowWindow(NativeMethods.HWND hwnd, int cmdShow) => throw new NotImplementedException();
    }

    public class WindowFunctionDummy1 : IWindowFunction
    {
        public uint MsgWaitForMultipleObjectsEx(uint handleCount, PinnedBuffer<nint[]> waitHandlesPin)
        {
            Task.Delay(1000).Wait();
            return 1;
        }
        public void Setup()
        {
        }
        NativeMethods.HWND IWindowFunction.CreateWindow(NativeMethods.WS_EX dwExStyle, nint lpszClassName, string windowTitle, NativeMethods.WS style, int x, int y, int width, int height, NativeMethods.HWND hwndParent, nint hMenu, nint hInst) => throw new NotImplementedException();
        bool IWindowFunction.MoveWindow(NativeMethods.HWND hwnd, int x, int y, int width, int height, bool repaint) => throw new NotImplementedException();
        void IWindowFunction.PostMessage(NativeMethods.HWND hwnd, uint nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        nint IWindowFunction.SendMessage(NativeMethods.HWND hwnd, uint nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        (nint, Win32Exception) IWindowFunction.SetWindowLong(NativeMethods.HWND hwnd, int nIndex, nint dwNewLong) => throw new NotImplementedException();
        bool IWindowFunction.SetWindowStyle(NativeMethods.HWND hwnd, int style) => throw new NotImplementedException();
        bool IWindowFunction.ShowWindow(NativeMethods.HWND hwnd, int cmdShow) => throw new NotImplementedException();
    }

    public class WindowFunctionDummy2 : IWindowFunction
    {
        public uint MsgWaitForMultipleObjectsEx(uint handleCount, PinnedBuffer<nint[]> waitHandlesPin)
        {
            Task.Delay(1000).Wait();
            return 2;
        }
        public void Setup()
        {
        }
        NativeMethods.HWND IWindowFunction.CreateWindow(NativeMethods.WS_EX dwExStyle, nint lpszClassName, string windowTitle, NativeMethods.WS style, int x, int y, int width, int height, NativeMethods.HWND hwndParent, nint hMenu, nint hInst) => throw new NotImplementedException();
        bool IWindowFunction.MoveWindow(NativeMethods.HWND hwnd, int x, int y, int width, int height, bool repaint) => throw new NotImplementedException();
        void IWindowFunction.PostMessage(NativeMethods.HWND hwnd, uint nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        nint IWindowFunction.SendMessage(NativeMethods.HWND hwnd, uint nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        (nint, Win32Exception) IWindowFunction.SetWindowLong(NativeMethods.HWND hwnd, int nIndex, nint dwNewLong) => throw new NotImplementedException();
        bool IWindowFunction.SetWindowStyle(NativeMethods.HWND hwnd, int style) => throw new NotImplementedException();
        bool IWindowFunction.ShowWindow(NativeMethods.HWND hwnd, int cmdShow) => throw new NotImplementedException();
    }

    public class WindowFunctionDummy3 : IWindowFunction
    {
        public uint MsgWaitForMultipleObjectsEx(uint handleCount, PinnedBuffer<nint[]> waitHandlesPin)
        {
            Task.Delay(1000).Wait();
            return 3;
        }
        public void Setup()
        {
        }
        NativeMethods.HWND IWindowFunction.CreateWindow(NativeMethods.WS_EX dwExStyle, nint lpszClassName, string windowTitle, NativeMethods.WS style, int x, int y, int width, int height, NativeMethods.HWND hwndParent, nint hMenu, nint hInst) => throw new NotImplementedException();
        bool IWindowFunction.MoveWindow(NativeMethods.HWND hwnd, int x, int y, int width, int height, bool repaint) => throw new NotImplementedException();
        void IWindowFunction.PostMessage(NativeMethods.HWND hwnd, uint nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        nint IWindowFunction.SendMessage(NativeMethods.HWND hwnd, uint nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        (nint, Win32Exception) IWindowFunction.SetWindowLong(NativeMethods.HWND hwnd, int nIndex, nint dwNewLong) => throw new NotImplementedException();
        bool IWindowFunction.SetWindowStyle(NativeMethods.HWND hwnd, int style) => throw new NotImplementedException();
        bool IWindowFunction.ShowWindow(NativeMethods.HWND hwnd, int cmdShow) => throw new NotImplementedException();
    }

    public class WindowFunctionDummy258 : IWindowFunction
    {
        public uint MsgWaitForMultipleObjectsEx(uint handleCount, PinnedBuffer<nint[]> waitHandlesPin)
        {
            Task.Delay(1000).Wait();
            return 258;
        }
        public void Setup()
        {
        }
        NativeMethods.HWND IWindowFunction.CreateWindow(NativeMethods.WS_EX dwExStyle, nint lpszClassName, string windowTitle, NativeMethods.WS style, int x, int y, int width, int height, NativeMethods.HWND hwndParent, nint hMenu, nint hInst) => throw new NotImplementedException();
        bool IWindowFunction.MoveWindow(NativeMethods.HWND hwnd, int x, int y, int width, int height, bool repaint) => throw new NotImplementedException();
        void IWindowFunction.PostMessage(NativeMethods.HWND hwnd, uint nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        nint IWindowFunction.SendMessage(NativeMethods.HWND hwnd, uint nMsg, nint wParam, nint lParam) => throw new NotImplementedException();
        (nint, Win32Exception) IWindowFunction.SetWindowLong(NativeMethods.HWND hwnd, int nIndex, nint dwNewLong) => throw new NotImplementedException();
        bool IWindowFunction.SetWindowStyle(NativeMethods.HWND hwnd, int style) => throw new NotImplementedException();
        bool IWindowFunction.ShowWindow(NativeMethods.HWND hwnd, int cmdShow) => throw new NotImplementedException();
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
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<IWindowMap, WindowMapDummy>();
            services.AddSingleton<IWindowProcedure, WindowProcedure>();
            services.AddSingleton<IWindowFunction, WindowFunctionDummy0>();
            services.AddSingleton<IWindowFactory, WindowFactory>();
            services.AddSingleton<IWindowClassManager, WindowClassManager>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
            services.AddSingleton<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
            services.AddSingleton<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
        });

        return builder.Build();
    }

    private static IHost Setup1()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IUIThreadActivator, UIThreadActivator>();
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<IWindowMap, WindowMapDummy>();
            services.AddSingleton<IWindowProcedure, WindowProcedure>();
            services.AddSingleton<IWindowFunction, WindowFunctionDummy1>();
            services.AddSingleton<IWindowFactory, WindowFactory>();
            services.AddSingleton<IWindowClassManager, WindowClassManager>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
            services.AddSingleton<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
            services.AddSingleton<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
        });

        return builder.Build();
    }

    private static IHost Setup2()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IUIThreadActivator, UIThreadActivator>();
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<IWindowMap, WindowMapDummy>();
            services.AddSingleton<IWindowProcedure, WindowProcedure>();
            services.AddSingleton<IWindowFunction, WindowFunctionDummy2>();
            services.AddSingleton<IWindowFactory, WindowFactory>();
            services.AddSingleton<IWindowClassManager, WindowClassManager>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
            services.AddSingleton<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
            services.AddSingleton<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
        });

        return builder.Build();
    }

    private static IHost Setup3()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IUIThreadActivator, UIThreadActivator>();
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<IWindowMap, WindowMapDummy>();
            services.AddSingleton<IWindowProcedure, WindowProcedure>();
            services.AddSingleton<IWindowFunction, WindowFunctionDummy3>();
            services.AddSingleton<IWindowFactory, WindowFactory>();
            services.AddSingleton<IWindowClassManager, WindowClassManager>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
            services.AddSingleton<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
            services.AddSingleton<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
        });

        return builder.Build();
    }

    private static IHost Setup258()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IUIThreadActivator, UIThreadActivator>();
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<IWindowMap, WindowMapDummy>();
            services.AddSingleton<IWindowProcedure, WindowProcedure>();
            services.AddSingleton<IWindowFunction, WindowFunctionDummy258>();
            services.AddSingleton<IWindowFactory, WindowFactory>();
            services.AddSingleton<IWindowClassManager, WindowClassManager>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
            services.AddSingleton<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
            services.AddSingleton<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
        });

        return builder.Build();
    }

    private static IHost SetupNoEmpty()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IUIThreadActivator, UIThreadActivator>();
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<IWindowMap, WindowMapDummyNoEmpty>();
            services.AddSingleton<IWindowProcedure, WindowProcedure>();
            services.AddSingleton<IWindowFunction, WindowFunctionDummy0>();
            services.AddSingleton<IWindowFactory, WindowFactory>();
            services.AddSingleton<IWindowClassManager, WindowClassManager>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
            services.AddSingleton<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
            services.AddSingleton<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
        });

        return builder.Build();
    }

    [TestMethod]
    public async Task TestConstructor()
    {
        using var host = Setup();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        thread.WindowManager.ToString();
    }

    [TestMethod]
    public async Task TestDisposeAsync()
    {
        using var host = Setup();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        await using var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );
    }

    [TestMethod]
    public async Task TestCancelAsync()
    {
        using var host = Setup();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        await using var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        await thread.CancelAsync();
        await thread.CancelAsync();
    }

    [TestMethod]
    public async Task TestFinalizer()
    {
        {
            using var host = Setup();
            var logger = host.Services.GetRequiredService<ILogger<UIThread>>();

            void Run()
            {
                var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
                var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
                var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
                var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
                var windowManager = host.Services.GetRequiredService<IWindowManager>();
                var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
                var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
                var windowMap = host.Services.GetRequiredService<IWindowMap>();
                var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

                var _ = new UIThread(
                    logger,
                    uiThreadActivator,
                    dispatcherQueue,
                    dispatcherQueueTaskExecutor,
                    context,
                    windowManager,
                    windowProcedure,
                    windowFunction,
                    windowMap,
                    onUnhandledExceptionHandler
                );
            }

            Run();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [TestMethod]
    public async Task TestDoubleDestruct()
    {
        using var host = Setup();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );
        thread.Dispose();
        thread.Dispose();
    }

    [TestMethod]
    public async Task TestDispatchAsyncOK()
    {
        using var host = Setup();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TestTool.RunThread(() =>
        {
            thread.RunMessageLoop(tcs);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task.ConfigureAwait(false);

        var result = await await await TestTool.RunThread(async () =>
        {
            return await uiThread.EnqueueAsync(async () =>
            {
                //immidiate mode
                var result = await uiThread.EnqueueAsync(() => 1);
                return 1 + result;
            }).ConfigureAwait(false);
        }).ConfigureAwait(false);

        Assert.AreEqual(2, result);

        await thread.CancelAsync().ConfigureAwait(false);
        await task.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TestRunMessageLoop1()
    {
        using var host = Setup1();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TestTool.RunThread(() =>
        {
            thread.RunMessageLoop(tcs);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task.ConfigureAwait(false);

        await thread.CancelAsync().ConfigureAwait(false);
        await task.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TestRunMessageLoop2()
    {
        using var host = Setup2();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TestTool.RunThread(() =>
        {
            thread.RunMessageLoop(tcs);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task.ConfigureAwait(false);

        await thread.CancelAsync().ConfigureAwait(false);
        await task.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TestRunMessageLoop3()
    {
        using var host = Setup3();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TestTool.RunThread(() =>
        {
            thread.RunMessageLoop(tcs);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task.ConfigureAwait(false);

        try
        {
            await thread.CancelAsync().ConfigureAwait(false);
            Assert.Fail();
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "thread error");
        }
        await task.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TestRunMessageLoop258()
    {
        using var host = Setup258();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TestTool.RunThread(() =>
        {
            thread.RunMessageLoop(tcs);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task.ConfigureAwait(false);

        await thread.CancelAsync().ConfigureAwait(false);
        await task.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TestRunMessageLoopNoEmptyCancel()
    {
        using var host = SetupNoEmpty();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TestTool.RunThread(() =>
        {
            thread.RunMessageLoop(tcs);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task.ConfigureAwait(false);

        await thread.CancelAsync().ConfigureAwait(false);
        await task.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TestRunMessageLoopNoEmptyDispose()
    {
        using var host = SetupNoEmpty();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TestTool.RunThread(() =>
        {
            thread.RunMessageLoop(tcs);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task.ConfigureAwait(false);

        thread.Dispose();

        await task.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TestCancelErrorInDispose()
    {
        using var host = Setup3();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TestTool.RunThread(() =>
        {
            thread.RunMessageLoop(tcs);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task.ConfigureAwait(false);

        thread.Dispose();

        await task.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TestDispatchAsyncFail()
    {
        using var host = Setup();
        var logger = host.Services.GetRequiredService<ILogger<UIThread>>();

        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var windowManager = host.Services.GetRequiredService<IWindowManager>();
        var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
        var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
        var windowMap = host.Services.GetRequiredService<IWindowMap>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var thread = new UIThread(
            logger,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            windowProcedure,
            windowFunction,
            windowMap,
            onUnhandledExceptionHandler
        );

        thread.OnInactivated += () =>
        {
            logger.LogInformation("Åöthread:[{threadId:X}] OnInactivated", Environment.CurrentManagedThreadId);
        };

        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TestTool.RunThread(() =>
        {
            thread.RunMessageLoop(tcs);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task.ConfigureAwait(false);

        await thread.CancelAsync().ConfigureAwait(false);
        await task.ConfigureAwait(false);

        try
        {
            var result = await await TestTool.RunThread(async () =>
            {
                return await uiThread.EnqueueAsync(() => 1);
            });

            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            logger.LogInformation(e, $"Åöthread:[{Environment.CurrentManagedThreadId:X}] error end");
        }
    }

    [TestMethod]
    public async Task TestMessageLoopForceEnd()
    {
        Task? task = null;

        {
            using var host = Setup();
            var logger = host.Services.GetRequiredService<ILogger<UIThread>>();
            var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
            var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
            var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
            var windowManager = host.Services.GetRequiredService<IWindowManager>();
            var windowProcedure = host.Services.GetRequiredService<IWindowProcedure>();
            var windowFunction = host.Services.GetRequiredService<IWindowFunction>();
            var windowMap = host.Services.GetRequiredService<IWindowMap>();
            var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
            var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

            using var thread = new UIThread(
                logger,
                uiThreadActivator,
                dispatcherQueue,
                dispatcherQueueTaskExecutor,
                context,
                windowManager,
                windowProcedure,
                windowFunction,
                windowMap,
                onUnhandledExceptionHandler
            );

            var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

            task = TestTool.RunThread(() =>
            {
                thread.RunMessageLoop(tcs, default);
                logger.LogInformation("loop end");
            });

            var uiThread = await tcs.Task.ConfigureAwait(false);
            thread.Dispose();
        }

        if (task != default)
        {
            await task.ConfigureAwait(false);
        }
    }
}
