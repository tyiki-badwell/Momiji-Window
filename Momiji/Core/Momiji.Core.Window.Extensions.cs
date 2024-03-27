using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

public static class IWindowExtensions
{

    public static ValueTask<bool> MoveAsync(
        this IWindow window,
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        return window.DispatchAsync((window) =>
        {
            return ((NativeWindow)window).MoveImpl(
                x,
                y,
                width,
                height,
                repaint
            );
        });
    }

    private static bool MoveImpl(
        this NativeWindow window,
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        window.Logger.LogWithHWnd(LogLevel.Information, $"MoveWindow x:[{x}] y:[{y}] width:[{width}] height:[{height}] repaint:[{repaint}]", window.HWindow, Environment.CurrentManagedThreadId);
        var result =
            User32.MoveWindow(
                window.HWindow,
                x,
                y,
                width,
                height,
                repaint
        );

        if (!result)
        {
            var error = new Win32Exception();
            window.Logger.LogWithHWndAndError(LogLevel.Error, "MoveWindow", window.HWindow, error.ToString(), Environment.CurrentManagedThreadId);
        }
        return result;
    }

    public static ValueTask<bool> ShowAsync(
        this IWindow window,
        int cmdShow
    )
    {
        return window.DispatchAsync((window) =>
        {
            return ((NativeWindow)window).ShowImpl(cmdShow);
        });
    }

    private static bool ShowImpl(
        this NativeWindow window,
        int cmdShow
    )
    {
        window.Logger.LogWithHWnd(LogLevel.Information, $"ShowWindow cmdShow:[{cmdShow}]", window.HWindow, Environment.CurrentManagedThreadId);
        var result =
            User32.ShowWindow(
                window.HWindow,
                cmdShow
            );

        var error = new Win32Exception();

        //result=0: 実行前は非表示だった/ <>0:実行前から表示されていた
        window.Logger.LogWithHWndAndError(LogLevel.Information, $"ShowWindow result:[{result}]", window.HWindow, error.ToString(), Environment.CurrentManagedThreadId);

        if (error.NativeErrorCode == 1400) // ERROR_INVALID_WINDOW_HANDLE
        {
            throw error;
        }

        {
            var wndpl = new User32.WINDOWPLACEMENT()
            {
                length = Marshal.SizeOf<User32.WINDOWPLACEMENT>()
            };
            User32.GetWindowPlacement(window.HWindow, ref wndpl);

            window.Logger.LogWithHWnd(LogLevel.Information, $"GetWindowPlacement result cmdShow:[{cmdShow}] -> wndpl:[{wndpl}]", window.HWindow, Environment.CurrentManagedThreadId);
        }

        return result;
    }

    public static ValueTask<bool> SetWindowStyleAsync(
        this IWindow window,
        int style
    )
    {
        return window.DispatchAsync((window) =>
        {
            return ((NativeWindow)window).SetWindowStyleImpl(style);
        });
    }

    private static bool SetWindowStyleImpl(
        this NativeWindow window,
        int style
    )
    {
        //TODO DPI対応
        var clientRect = new User32.RECT();

        {
            var result = User32.GetClientRect(window.HWindow, ref clientRect);
            if (!result)
            {
                var error = new Win32Exception();
                window.Logger.LogWithHWndAndError(LogLevel.Error, "GetClientRect failed", window.HWindow, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var result = User32.AdjustWindowRectExForDpi(ref clientRect, style, false, 0, 96);
            if (!result)
            {
                var error = new Win32Exception();
                window.Logger.LogWithHWndAndError(LogLevel.Error, "AdjustWindowRectExForDpi failed", window.HWindow, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var (result, error) = window.SetWindowLong(-16, new nint(style)); //GWL_STYLE
            if (result == nint.Zero && error.NativeErrorCode != 0)
            {
                window.Logger.LogWithHWndAndError(LogLevel.Error, "SetWindowLong failed", window.HWindow, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        {
            var width = clientRect.right - clientRect.left;
            var height = clientRect.bottom - clientRect.top;

            window.Logger.LogWithHWnd(LogLevel.Information, $"SetWindowPos", window.HWindow, Environment.CurrentManagedThreadId);
            var result =
                User32.SetWindowPos(
                        window.HWindow,
                        User32.HWND.None,
                        0,
                        0,
                        width,
                        height,
                        0x0002 //SWP_NOMOVE
                               //| 0x0001 //SWP_NOSIZE
                        | 0x0004 //SWP_NOZORDER
                        | 0x0020 //SWP_FRAMECHANGED
                    );

            if (!result)
            {
                var error = new Win32Exception();
                window.Logger.LogWithHWndAndError(LogLevel.Error, "SetWindowPos failed", window.HWindow, error.ToString(), Environment.CurrentManagedThreadId);
                return false;
            }
        }

        return true;
    }

}
