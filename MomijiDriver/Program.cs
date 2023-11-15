using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);

        builder.UseContentRoot(AppContext.BaseDirectory);

        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddHostedService<Worker>();
        });

        var host = builder.Build();

        var task = host.RunAsync().ConfigureAwait(false);

        var manager = (IWindowManager?)host.Services.GetService(typeof(IWindowManager));
        ArgumentNullException.ThrowIfNull(manager, nameof(manager));

        var window = manager.CreateWindow();

        //TODO 他のコンテンツを挿入する

        window.Show(1);

        await task;
    }
}

public class Worker : BackgroundService
{
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