using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PlutonShield.Core.Proxy;
using PlutonShield.Core.Runtime;
using PlutonShield.Core.Settings;

namespace PlutonShield.App.Pages;

/// <summary>
/// The programs that go through Pluton Shield, each with a switch. A program whose switch is off is left alone (see
/// <see cref="AppSettings.SkippedApps"/>). The list is the programs seen during this run plus the ones left alone, so a
/// program that was switched off can always be switched on again, even when it has not run yet.
/// Rows are rebuilt only when the set of programs changes; the numbers on them are refreshed in place.
/// </summary>
public sealed partial class AppsPage : Page
{
    private sealed record Row(TextBlock Detail, ToggleSwitch Toggle);

    private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _suppress; // true while switches are set by the code: that must not save anything
    private string _shown = ""; // the programs the rows were built for
    private readonly Dictionary<string, ClientApp> _known = new(StringComparer.Ordinal); // every program shown since the page opened: a row stays when its switch is flipped

    public AppsPage()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Refresh(rebuildIfChanged: true);
    }

    private static ShieldRuntime Rt => App.Runtime;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Rt.Proxy.Apps.Changed += OnChanged;
        Rt.Settings.Changed += OnChanged;
        Refresh(rebuildIfChanged: true);
        _timer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Rt.Proxy.Apps.Changed -= OnChanged;
        Rt.Settings.Changed -= OnChanged;
    }

    // May come from a background thread (a new program was seen) or from this page's own change of the settings.
    private void OnChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => Refresh(rebuildIfChanged: true));

    /// <summary>The programs to list: those seen in this run, those left alone and those shown before, one row per file name, by name.</summary>
    private List<(ClientApp App, long Requests, bool Seen, bool Skipped)> Collect()
    {
        var skipped = Rt.Settings.Current.SkippedApps;
        var seen = Rt.Proxy.Apps.Snapshot().ToDictionary(x => x.App.Exe, StringComparer.Ordinal);
        var all = new Dictionary<string, (ClientApp App, long Requests, bool Seen, bool Skipped)>(StringComparer.Ordinal);

        foreach (var (exe, entry) in seen) all[exe] = (entry.App, entry.Requests, true, false);
        foreach (var rule in skipped)
        {
            if (!all.ContainsKey(rule.Exe)) all[rule.Exe] = (new ClientApp(rule.Exe, rule.Path), 0, false, true);
        }

        foreach (var (exe, app) in all.ToList()) _known[exe] = app.App;
        foreach (var (exe, app) in _known)
        {
            if (!all.ContainsKey(exe)) all[exe] = (app, 0, false, false);
        }

        // The switch is read from the settings, which are what the proxy follows.
        return all.Values
            .Select(x => (x.App, x.Requests, x.Seen, Skipped: skipped.Exists(a => a.Exe == x.App.Exe)))
            .OrderBy(x => x.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.App.Exe, StringComparer.Ordinal)
            .ToList();
    }

    private void Refresh(bool rebuildIfChanged)
    {
        var apps = Collect();
        var signature = string.Join("|", apps.Select(a => a.App.Exe));
        EmptyText.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResetButton.IsEnabled = Rt.Settings.Current.SkippedApps.Count > 0;

        if (rebuildIfChanged && signature != _shown)
        {
            Build(apps);
            _shown = signature;
            return;
        }

        // The same programs: only the numbers and the switches (when they were changed elsewhere, e.g. "Filter all again") change.
        _suppress = true;
        try
        {
            foreach (var a in apps)
            {
                if (!_rows.TryGetValue(a.App.Exe, out var row)) continue;
                row.Detail.Text = DetailOf(a.App, a.Requests, a.Seen);
                if (row.Toggle.IsOn == a.Skipped) row.Toggle.IsOn = !a.Skipped;
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    private static string DetailOf(ClientApp app, long requests, bool seen)
    {
        var activity = seen ? Loc.T("apps.requests", requests.ToString("N0", Loc.Culture)) : Loc.T("apps.notseen");
        return app.Path.Length > 0 ? app.Path + "  ·  " + activity : activity;
    }

    private void Build(List<(ClientApp App, long Requests, bool Seen, bool Skipped)> apps)
    {
        Rows.Children.Clear();
        _rows.Clear();
        _suppress = true;
        try
        {
            foreach (var a in apps)
            {
                var name = new TextBlock { Text = a.App.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                var detail = new TextBlock
                {
                    Text = DetailOf(a.App, a.Requests, a.Seen),
                    Style = (Style)Resources["SecondaryText"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                };
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(name);
                texts.Children.Add(detail);

                var toggle = new ToggleSwitch
                {
                    IsOn = !a.Skipped,
                    OnContent = Loc.T("apps.on"),
                    OffContent = Loc.T("apps.off"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                AutomationProperties.SetName(toggle, a.App.Name);
                var app = a.App;
                toggle.Toggled += (_, _) =>
                {
                    if (!_suppress) SetFiltered(app, toggle.IsOn);
                };

                var grid = new Grid { Padding = new Thickness(0, 8, 0, 8), ColumnSpacing = 12 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(texts, 0);
                Grid.SetColumn(toggle, 1);
                grid.Children.Add(texts);
                grid.Children.Add(toggle);
                ToolTipService.SetToolTip(grid, a.App.Path.Length > 0 ? a.App.Path : a.App.Exe);
                Rows.Children.Add(grid);
                _rows[a.App.Exe] = new Row(detail, toggle);
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    // ---- changes ----

    /// <summary>On: Pluton Shield filters the program (it is taken off the list of programs left alone). Off: it is left alone.</summary>
    private void SetFiltered(ClientApp app, bool filtered)
    {
        // A switch can also report a change that is not one (when it is first shown): the settings are only written when they differ.
        if (Rt.Settings.Current.SkippedApps.Exists(a => a.Exe == app.Exe) == !filtered) return;
        Rt.Settings.Update(s =>
        {
            s.SkippedApps.RemoveAll(a => a.Exe == app.Exe);
            if (!filtered) s.SkippedApps.Add(new SkippedApp { Exe = app.Exe, Path = app.Path });
        });
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");
            if ((Application.Current as App)?.MainWindow is { } window)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
            }

            var file = await picker.PickSingleFileAsync();
            if (file is null || ClientApp.FromPath(file.Path) is not { } app) return;
            Rt.Settings.Update(s =>
            {
                s.SkippedApps.RemoveAll(a => a.Exe == app.Exe);
                s.SkippedApps.Add(new SkippedApp { Exe = app.Exe, Path = app.Path });
            });
        }
        catch (Exception ex)
        {
            PlutonShield.Core.Logging.Log.Warn($"adding a program: {ex.Message}");
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Rt.Settings.Update(s => s.SkippedApps = new List<SkippedApp>());
}
