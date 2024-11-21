using Microsoft.UI;
using Microsoft.UI.Xaml.Hosting;
using Momiji.Core.Window;

namespace Momiji.Driver;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Worker.CreateHost(args);
        await host.RunAsync();
    }
}

public partial class Worker : BackgroundService
{
    public static IHost CreateHost(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();

        builder.UseMomijiWindow();

        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddHostedService<Worker>();
        });

        var host = builder.Build();

        return host;
    }

    private readonly ILogger _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public Worker(
        ILogger<Worker> logger,
        IHostApplicationLifetime hostApplicationLifetime,
        IServiceScopeFactory serviceScopeFactory
    )
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;

        hostApplicationLifetime.ApplicationStarted.Register(() =>
        {
            _logger.LogInformation("ApplicationStarted");
        });
        hostApplicationLifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("ApplicationStopping");
        });
        hostApplicationLifetime.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation("ApplicationStopped");
        });
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExecuteAsync");

        using var scope = _serviceScopeFactory.CreateScope();

        var a = scope.ServiceProvider.GetRequiredService<IUIThread>();
        var b = scope.ServiceProvider.GetRequiredService<IUIThread>();

        var classStyle = 0;
        classStyle |= 0x00000020; //CS_OWNDC

        var windowStyle = 0;
        windowStyle |= 0x10000000; //WS_VISIBLE
        //windowStyle |= 0x80000000U; //WS_POPUP
        windowStyle |= 0x00C00000; //WS_CAPTION
        windowStyle |= 0x00080000; //WS_SYSMENU
        windowStyle |= 0x00040000; //WS_THICKFRAME
        windowStyle |= 0x00020000; //WS_MINIMIZEBOX
        windowStyle |= 0x00010000; //WS_MAXIMIZEBOX

        var exWindowStyle = 0;
        //exWindowStyle |= 0x00200000; //WS_EX_NOREDIRECTIONBITMAP

        var childStyle = 0;
        childStyle |= 0x40000000; //WS_CHILD
        childStyle |= 0x10000000; //WS_VISIBLE

        var dispatcherQueueController = await a.DispatchAsync((manager) => {
            _logger.LogInformation("thread A: DispatcherQueueController.CreateOnCurrentThread");
            return Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnCurrentThread();
        });

        a.OnInactivated += () => {
            _logger.LogInformation("thread A: DispatcherQueueController.ShutdownQueue");
            dispatcherQueueController.ShutdownQueue();
        };

        //TODO IWindowにプロパティ増やす
        DesktopWindowXamlSource? xamlSource = default;

        var windowA = a.CreateWindow(new()
        {
            windowTitle = "windowA",
            classStyle = classStyle, 
            style = windowStyle,
            exStyle = exWindowStyle,
            onMessage = async (sender, message) =>
            {
                _logger.LogInformation($"   windowA:{message}");
                switch (message.Msg)
                {
                    case 0x0082: //WM_NCDESTROY
                        _logger.LogInformation($"       windowA:xamlSource.Dispose");
                        xamlSource?.Dispose();

                        break;

                    case 0x0210: //WM_PARENTNOTIFY
                        //message.WParam 子のウインドウメッセージ
                        //message.LParam 下位ワード　X座標／上位ワード　Y座標
                        break;

                    case 0x0111: //WM_COMMAND
                        //message.WParam 上位ワード　0：メニュー／1：アクセラレータ／その他：ボタン識別子　下位ワード　識別子
                        //message.LParam ウインドウハンドル

                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}][{message}] delay start ===============================");
                        await Task.Delay(1000);
                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] delay end ===============================");

                        a.CreateWindow(new()
                        {
                            windowTitle = $"window{Guid.NewGuid()}",
                            classStyle = classStyle,
                            style = windowStyle,
                            exStyle = exWindowStyle,
                        });

                        break;

                }
            }
        });

        var windowB = b.CreateWindow(new() { 
            windowTitle = "windowB",
            style = windowStyle,
        });

        var buttonA = a.CreateWindow(new()
        {
            windowTitle = "buttonA",
            parent = windowA,
            style = childStyle,
            className = "BUTTON",
            onMessage = (sender, message) =>
            {
                _logger.LogInformation($"       buttonA:{message}");
                switch (message.Msg)
                {
                    case 0x0084: //WM_NCHITTEST
                        //message.LParam 下位ワード　X座標／上位ワード　Y座標
                        break;
                    case 0x0020: //WM_SETCURSOR
                        //message.WParam カーソルを含むウィンドウへのハンドル
                        //message.LParam 下位ワード　WM_NCHITTESTの戻り値／上位ワード　マウスウインドウメッセージ
                        break;
                    case 0x0200: //WM_MOUSEMOVE
                        //message.WParam 仮想キー
                        //message.LParam 下位ワード　X座標／上位ワード　Y座標
                        break;
                    case 0x0021: //WM_MOUSEACTIVATE
                        //message.WParam トップレベルウインドウハンドル
                        //message.LParam 下位ワード　WM_NCHITTESTの戻り値／上位ワード　マウスウインドウメッセージ
                        break;
                    case 0x0201: //WM_LBUTTONDOWN
                        //message.WParam 仮想キー
                        //message.LParam 下位ワード　X座標／上位ワード　Y座標
                        break;
                    case 0x0281: //WM_IME_SETCONTEXT
                        //message.WParam ウインドウがアクティブなら1
                        //message.LParam 表示オプション
                        break;
                    case 0x0007: //WM_SETFOCUS
                        //message.WParam フォーカスが移る前にフォーカスを持っていたウインドウハンドル
                        break;
                    case 0x00F3: //BM_SETSTATE
                        //message.WParam ボタンを強調表示するならtrue
                        break;
                    case 0x0202: //WM_LBUTTONUP
                        //message.WParam 仮想キー
                        //message.LParam 下位ワード　X座標／上位ワード　Y座標
                        break;
                    case 0x0215: //WM_CAPTURECHANGED
                        //message.LParam マウスキャプチャしているウインドウハンドル
                        break;

                }
            }
        });

        var textA = a.CreateWindow(new()
        {
            windowTitle = "textA",
            parent = windowA,
            style = childStyle,
            className = "EDIT",
            onMessage = (sender, message) =>
            {
                _logger.LogInformation($"       textA:{message}");
            }
        });

        var buttonB = b.CreateWindow(new()
        {
            windowTitle = "buttonB",
            parent = windowB,
            style = childStyle,
            className = "BUTTON",
            onMessage = (sender, message) =>
            {
                _logger.LogInformation($"       buttonB:{message}");
            }
        });
        var textB = b.CreateWindow(new()
        {
            windowTitle = "textB",
            parent = windowB,
            style = childStyle,
            className = "EDIT",
            onMessage = (sender, message) =>
            {
                _logger.LogInformation($"       textB:{message}");
            }
        });

        //BスレッドからAウインドウにボタン追加
        var buttonC = b.CreateWindow(new()
        {
            windowTitle = "buttonC",
            parent = windowA,
            style = childStyle,
            className = "BUTTON",
            onMessage = (sender, message) =>
            {
                _logger.LogInformation($"       buttonC:{message}");
            }
        });


        await windowA.DispatchAsync((window) => 
        {
            //WinUI3コンテンツを挿入する
            xamlSource = new DesktopWindowXamlSource();

            var id = Win32Interop.GetWindowIdFromWindow(window.Handle);

            _logger.LogInformation($"       windowA:windowId:{id.Value:X}");

            _logger.LogInformation($"       windowA:xamlSource.Initialize");
            xamlSource.Initialize(id);

            _logger.LogInformation($"       windowA:xamlSource.SiteBridge.WindowId {xamlSource.SiteBridge.WindowId.Value:X}");

            xamlSource.Content = new TestPage();

            xamlSource.TakeFocusRequested += (sender, args) => {
                _logger.LogInformation($"       windowA:xamlSource.TakeFocusRequested {args.Request}");
            };

            /*

            var style = 0U;
            style = 0x10000000U; //WS_VISIBLE
            style |= 0x40000000U; //WS_CHILD
            var page = new TestPage();
            var param = new HwndSourceParameters("TestPage", 500, 500);
            param.SetPosition(200, 200);
            param.WindowStyle = (int)style;
            param.ParentWindow = window.Handle;

            var hwndSource = new HwndSource(param)
            {
                RootVisual = page
            };
            */

            return 0;
        });

        buttonA.Move(10, 10, 200, 80, true);
        textA.Move(10, 300, 200, 80, true);

        buttonC.Move(300, 100, 200, 80, true);

        windowA.Show(1);

        buttonB.Move(10, 10, 200, 80, true);
        textB.Move(10, 300, 200, 80, true);
        windowB.Show(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public async override Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StopAsync");
        await base.StopAsync(stoppingToken);
    }
}