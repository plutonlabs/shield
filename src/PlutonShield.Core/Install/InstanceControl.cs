namespace PlutonShield.Core.Install;

/// <summary>
/// How the running app is recognised, and how the installer asks it to close (so that files can be replaced or removed
/// and the Windows proxy setting is given back properly, which a forced kill would not do).
/// </summary>
public static class InstanceControl
{
    public const string MutexName = @"Local\PlutonShield.SingleInstance";
    public const string ShowEventName = @"Local\PlutonShield.Show";
    public const string QuitEventName = @"Local\PlutonShield.Quit";

    /// <summary>True while a copy of the app is running (it holds the single-instance mutex).</summary>
    public static bool IsRunning(string mutexName = MutexName)
    {
        try
        {
            if (!Mutex.TryOpenExisting(mutexName, out var mutex)) return false;
            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // it exists, we just may not open it
        }
    }

    /// <summary>
    /// Asks a running copy to close itself and waits until it has. Returns true when no copy is running any more
    /// (also when none was running). Returns false if it is still running after <paramref name="timeout"/>.
    /// </summary>
    public static bool RequestQuit(TimeSpan timeout, string mutexName = MutexName, string quitEventName = QuitEventName) =>
        RequestQuit(timeout, mutexName, () => TrySignal(quitEventName));

    /// <param name="trySignal">Tells the copy to close; returns true once that worked (it is not called again then).</param>
    internal static bool RequestQuit(TimeSpan timeout, string mutexName, Func<bool> trySignal)
    {
        var signalled = false;
        var end = DateTime.UtcNow + timeout;
        do
        {
            if (!IsRunning(mutexName)) return true;

            // A copy that is still starting has its mutex a moment before its quit listener: keep trying.
            if (!signalled) signalled = trySignal();

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < end);

        return !IsRunning(mutexName);
    }

    /// <summary>Waits until no copy is running (a restart: the new copy starts while the old one is still closing).</summary>
    public static bool WaitUntilGone(TimeSpan timeout, string mutexName = MutexName)
    {
        var end = DateTime.UtcNow + timeout;
        while (IsRunning(mutexName))
        {
            if (DateTime.UtcNow >= end) return false;
            Thread.Sleep(100);
        }

        return true;
    }

    private static bool TrySignal(string quitEventName)
    {
        if (!OperatingSystem.IsWindows()) return false; // named events exist on Windows only, and so does the app

        try
        {
            using var quit = EventWaitHandle.OpenExisting(quitEventName);
            return quit.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
