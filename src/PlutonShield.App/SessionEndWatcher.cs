using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace PlutonShield.App;

/// <summary>
/// Tells the app that Windows is shutting down, restarting or signing the user out (WM_ENDSESSION).
/// Windows ends the process right after that without running its exit code, so this is the last moment to give the proxy
/// setting back. Without it the next start of Windows begins with a proxy that nobody answers, and the browser says
/// "no Internet" until PlutonShield is running again.
/// </summary>
internal sealed class SessionEndWatcher
{
    private const uint WM_ENDSESSION = 0x0016;
    private const int GWLP_WNDPROC = -4;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly WndProc _proc; // a field so that the delegate is not collected while Windows can still call it
    private readonly Action _sessionEnding;
    private readonly IntPtr _previous;

    public SessionEndWatcher(Window window, Action sessionEnding)
    {
        _sessionEnding = sessionEnding;
        _proc = Callback;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _previous = SetWindowLongPtr(hwnd, GWLP_WNDPROC, _proc);
    }

    private IntPtr Callback(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // wParam is TRUE when the session really ends (it is FALSE when another program cancelled the shutdown).
        if (msg == WM_ENDSESSION && wParam != IntPtr.Zero)
        {
            try { _sessionEnding(); } catch (Exception) { /* nothing more can be done at this point */ }
        }

        return CallWindowProc(_previous, hWnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProc newProc);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
