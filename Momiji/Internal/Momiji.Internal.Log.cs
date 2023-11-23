using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Internal.Log;

internal static partial class LogDefine
{
    [LoggerMessage(
        Message = "thread:[{threadId:X}] {message} ({file}:{line} {member})"
    )]
    internal static partial void LogWithLine(this ILogger logger, LogLevel logLevel, string message, int threadId,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [LoggerMessage(
        Message = "thread:[{threadId:X}] {message} ({file}:{line} {member})"
    )]
    internal static partial void LogWithLine(this ILogger logger, LogLevel logLevel, Exception exception, string message, int threadId,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [LoggerMessage(
        Message = "thread:[{threadId:X}] {message} error:[{errorId} {errorMessage}] ({file}:{line} {member})"
    )]
    internal static partial void LogWithErrorId(this ILogger logger, LogLevel logLevel, string message, int errorId, string errorMessage, int threadId,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [LoggerMessage(
        Message = "thread:[{threadId:X}] hwnd:[{hwnd}] {message} msg:[{msg:X}] wParam:[{wParam:X}] lParam:[{lParam:X}] ({file}:{line} {member})"
    )]
    internal static partial void LogWithMsg(this ILogger logger, LogLevel logLevel, string message, User32.HWND hwnd, uint msg, nint wParam, nint lParam, int threadId,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [LoggerMessage(
        Message = "thread:[{threadId:X}] hwnd:[{hwnd}] {message} ({file}:{line} {member})"
    )]
    internal static partial void LogWithHWnd(this ILogger logger, LogLevel logLevel, string message, User32.HWND hwnd, int threadId,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [LoggerMessage(
        Message = "thread:[{threadId:X}] hwnd:[{hwnd}] {message} error:[{errorId} {errorMessage}] ({file}:{line} {member})"
    )]
    internal static partial void LogWithHWndAndErrorId(this ILogger logger, LogLevel logLevel, string message, User32.HWND hwnd, int errorId, string errorMessage, int threadId,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

}
