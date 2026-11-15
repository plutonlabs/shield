using System.Diagnostics;
using Microsoft.UI.Xaml;
using PlutonShield.Core.Install;
using PlutonShield.Core.Logging;
using PlutonShield.Core.Runtime;

namespace PlutonShield.App;

public partial class App : Application
{
    private const string MutexName = InstanceControl.MutexName;
    private const string ShowEventName = InstanceControl.ShowEventName;

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private MainWindow? _window;
    private SessionEndWatcher? _sessionEnd;
    private TrayController? _tray;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("unhandled: " + e.Exception);
            // Whatever happened, do not leave the user with a dead proxy setting.
            try { Runtime?.SystemProxy.Restore(); } catch (Exception) { /* nothing more to do */ }
        };
    }

    public static ShieldRuntime Runtime { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Commands for the installer. Nothing is shown and no copy of the app is started.
        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Contains("--quit", StringComparer.OrdinalIgnoreCase))
        {
            // Close the running copy (which gives the Windows proxy setting back) and wait until it is gone.
            Environment.Exit(InstanceControl.RequestQuit(TimeSpan.FromSeconds(20)) ? 0 : 1);
            return;
        }

        if (commandLine.Contains("--cleanup", StringComparer.OrdinalIgnoreCase))
        {
            // Uninstall: proxy setting and trusted certificate.
            UninstallCleanup.Run();
            Environment.Exit(0);
            return;
        }

        // Started by "Restart" in the settings: the old copy is still closing, wait until it is gone.
        var restarted = commandLine.Contains("--restarted", StringComparer.OrdinalIgnoreCase);
        if (restarted) InstanceControl.WaitUntilGone(TimeSpan.FromSeconds(15));

        // One copy only: a second start just asks the first one to show its window.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (Exception) { /* first copy is closing */ }
            Environment.Exit(0);
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceControl.QuitEventName);

        var bundled = Path.Combine(AppContext.BaseDirectory, "filters");
        Runtime = new ShieldRuntime(bundledFilters: bundled);
        Loc.Language = Runtime.Settings.Current.EffectiveLanguage;
        Log.Info($"PlutonShield {typeof(App).Assembly.GetName().Version} started; arguments: {string.Join(' ', commandLine.Skip(1))}");

        // The Windows entry for "Start with Windows" holds a path: keep it pointing at the copy that is running now.
        if (Environment.ProcessPath is { } exe && string.Equals(Path.GetFileName(exe), "PlutonShield.exe", StringComparison.OrdinalIgnoreCase))
        {
            StartupRegistration.Sync(Runtime.Settings.Current.StartWithWindows, exe);
        }

        _window = new MainWindow();
        // Windows is shutting down or signing out: give the proxy setting back first (the process is ended right after).
        _sessionEnd = new SessionEndWatcher(_window, () => Runtime.SystemProxy.Restore());
        _tray = new TrayController(_window, Runtime, ExitApp);
        _window.CloseToTray = () => !_exiting;

        // "Start minimized to the tray" is for the start with Windows (--background). Opening the app yourself, or
        // restarting it from the settings, always shows the window.
        var background = commandLine.Contains("--background", StringComparer.OrdinalIgnoreCase);
        if (restarted || !background || !Runtime.Settings.Current.StartMinimized)
        {
            _window.Activate();
        }

        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                _window.DispatcherQueue.TryEnqueue(_window.ShowAndFocus);
            }
        })
        { IsBackground = true, Name = "show-window-listener" };
        thread.Start();

        // The installer asks the app to close (update or uninstall): leave the same way as the tray's "Exit".
        var window = _window;
        var quitThread = new Thread(() =>
        {
            quitEvent.WaitOne();
            window.DispatcherQueue.TryEnqueue(ExitApp);
        })
        { IsBackground = true, Name = "quit-listener" };
        quitThread.Start();

        _ = Runtime.StartAsync();
    }

    private bool _exiting;

    /// <summary>Starts a new copy and closes this one (protection comes back on by itself: the setting is kept).</summary>
    public void RestartApp()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            Process.Start(new ProcessStartInfo(exe, "--restarted") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Error("restart: " + ex.Message);
            return; // keep running: a restart that cannot start the new copy must not close the app
        }

        ExitApp();
    }

    private void ExitApp()
    {
        _exiting = true;
        try { Runtime.Shutdown(); } catch (Exception ex) { Log.Error("shutdown: " + ex); }
        _tray?.Dispose();
        Runtime.Dispose();
        Exit();
    }
}
