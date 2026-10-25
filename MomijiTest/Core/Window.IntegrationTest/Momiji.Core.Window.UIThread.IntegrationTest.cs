using Microsoft.Extensions.Hosting;
using Momiji.Core.Window;

namespace MomijiTest.Core.Window.IntegrationTest;

[TestClass]
public partial class UIThreadIntegrationTest
{
    public UIThreadIntegrationTest()
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


}
