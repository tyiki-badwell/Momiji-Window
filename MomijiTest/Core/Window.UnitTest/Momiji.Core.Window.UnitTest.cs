using Momiji.Core.Window;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public class WindowExceptionUnitTest
{
    [TestMethod]
    public void Test1()
    {
        var test = new WindowException("test1");
        Assert.IsNotNull(test.Message);
    }

    [TestMethod]
    public void Test2()
    {
        var test = new WindowException("test2", new Exception("inner"));
        Assert.IsNotNull(test.Message);
    }
}

public static class TestTool
{
    public static async Task RunThread(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(async () =>
        {
            action();
            tcs.TrySetResult();
        })
        {
            IsBackground = false
        };
        thread.Start();

        await tcs.Task;
        thread.Join();
    }

    public static async Task<T> RunThread<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(async () =>
        {
            tcs.TrySetResult(action());
        })
        {
            IsBackground = false
        };
        thread.Start();

        var task = await tcs.Task;
        thread.Join();
        return task;
    }
}
