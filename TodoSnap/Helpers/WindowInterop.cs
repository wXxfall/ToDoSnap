using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TodoSnap.Helpers;

/// <summary>Win32 helpers for making the floating bar non-activating and always-on-top.</summary>
internal static class WindowInterop
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080; // keep out of Alt-Tab
    private const int WS_EX_NOACTIVATE = 0x08000000; // clicks don't steal focus

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// Add WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW to a window so that interacting with
    /// it (clicking a to-do's complete button) never pulls focus away from the user's
    /// foreground app. Call from <c>OnSourceInitialized</c>.
    /// </summary>
    public static void MakeNonActivating(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        int style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }
}
