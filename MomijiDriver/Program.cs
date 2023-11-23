using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace Momiji.Driver;

public class Program
{
    public static async Task Main(string[] args)
    {
        var a = Run(args);
        var b = Run(args);

        var set = new HashSet<Task>
        {
            a.Item2,
            b.Item2
        };

        var windowA = a.Item3.CreateWindow();
        var windowB = b.Item3.CreateWindow();

        var buttonA = a.Item3.CreateChildWindow(windowA, "BUTTON");
        var textA = a.Item3.CreateChildWindow(windowA, "EDIT");

        var buttonB = b.Item3.CreateChildWindow(windowB, "BUTTON");
        var textB = b.Item3.CreateChildWindow(windowB, "EDIT");

        //BスレッドからAにボタン追加
        //TODO handleを保存できてない
        var buttonC = b.Item3.CreateChildWindow(windowA, "BUTTON");

        //TODO 他のコンテンツを挿入する

        buttonA.Move(10, 10, 200, 80, true);
        textA.Move(10, 300, 200, 80, true);

        //TODO 失敗する
        buttonC.Move(300, 100, 200, 80, true);

        windowA.Show(1);

        buttonB.Move(10, 10, 200, 80, true);
        textB.Move(10, 300, 200, 80, true);
        windowB.Show(1);

        while (set.Count > 0)
        {
            var task = await Task.WhenAny(set).ConfigureAwait(false);
            set.Remove(task);
        }
    }

    private static (IHost, Task, IWindowManager) Run(string[] args)
    {
        var host = Worker.CreateHost(args);

        var task = host.RunAsync();

        var manager = (IWindowManager?)host.Services.GetService(typeof(IWindowManager));
        ArgumentNullException.ThrowIfNull(manager, nameof(manager));

        return (host, task, manager);
    }
}

public class Worker : BackgroundService
{
    public static IHost CreateHost(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();

        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddHostedService<Worker>();
        });

        var host = builder.Build();

        return host;
    }

    private readonly ILogger _logger;
    private readonly IWindowManager _windowManager;

    public Worker(
        ILogger<Worker> logger,
        IWindowManager windowManager,
        IHostApplicationLifetime hostApplicationLifetime
    )
    {
        _logger = logger;
        _windowManager = windowManager;

        hostApplicationLifetime?.ApplicationStarted.Register(() =>
        {
            _logger.LogInformation("ApplicationStarted");
        });
        hostApplicationLifetime?.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("ApplicationStopping");
        });
        hostApplicationLifetime?.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation("ApplicationStopped");
        });
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _windowManager.StartAsync(stoppingToken);
    }
}