using Microsoft.Extensions.Logging;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Internal.Log;

internal static partial class Log
{
    [LoggerMessage(
        EventId = 1,
        Message = "{message} thread:[{threadId:X}]"
    )]
    internal static partial void LogWithThreadId(this ILogger logger, LogLevel logLevel, string message, int threadId);

    [LoggerMessage(
        EventId = 2,
        Message = "{message} hwnd:[{hwnd}] thread:[{threadId:X}]"
    )]
    internal static partial void LogWithHWndAndThreadId(this ILogger logger, LogLevel logLevel, string message, User32.HWND hwnd, int threadId);

    [LoggerMessage(
        EventId = 3,
        Message = "{message} hwnd:[{hwnd}] error:[{errorId}]"
    )]
    internal static partial void LogWithHWndAndErrorId(this ILogger logger, LogLevel logLevel, string message, User32.HWND hwnd, int errorId);

    [LoggerMessage(
        EventId = 4,
        Message = "{message} hwnd:[{hwnd}] msg:[{msg:X}] wParam:[{wParam:X}] lParam:[{lParam:X}] / thread:[{threadId:X}]"
    )]
    internal static partial void LogMsgWithThreadId(this ILogger logger, LogLevel logLevel, string message, User32.HWND hwnd, uint msg, nint wParam, nint lParam, int threadId);
}
