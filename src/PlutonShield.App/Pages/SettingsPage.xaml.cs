using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PlutonShield.Core.Runtime;
using PlutonShield.Core.Update;

namespace PlutonShield.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage() => InitializeComponent();

    private static ShieldRuntime Rt => App.Runtime;

    // Filling the controls from the saved settings must not save anything (it would also re-draw the page).
    private bool _loading = true;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            var s = Rt.Settings.Current;
            LanguageBox.SelectedIndex = s.Language switch { "vi" => 1, "en" => 2, _ => 0 };
            StartWithWindows.IsOn = s.StartWithWindows;
            StartMinimized.IsOn = s.StartMinimized;
            AutoAppUpdate.IsOn = s.AutoAppUpdate;
            AutoUpdate.IsOn = s.AutoUpdate;
            Interval.Value = s.UpdateIntervalHours;
            Cosmetic.IsOn = s.CosmeticFiltering;
            BlockPopups.IsOn = s.BlockPopups;
            FilterHttps.IsOn = s.FilterHttps;
            Excluded.Text = string.Join(Environment.NewLine, s.ExcludedHosts);
            Port.Value = s.Port;
            RefreshCert();
            VersionText.Text = Loc.T("settings.version", Rt.AppUpdates.Current.ToString());
        }
        finally
        {
            _loading = false;
        }

        Rt.AppUpdates.Changed -= OnUpdatesChanged;
        Rt.AppUpdates.Changed += OnUpdatesChanged;
        RefreshUpdateStatus(null);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => Rt.AppUpdates.Changed -= OnUpdatesChanged;

    private void OnUpdatesChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => RefreshUpdateStatus(null));

    /// <summary>The line under the version: what the last check found (<paramref name="checking"/> is the line to show while one runs).</summary>
    private void RefreshUpdateStatus(string? checking)
    {
        var u = Rt.AppUpdates;
        ViewUpdateButton.Visibility = u.Info is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateStatusText.Text = checking
            ?? (u.Info is { } info ? Loc.T("settings.update.found", info.Version.ToString())
                : u.Error is { Length: > 0 } err ? Loc.T("settings.update.fail", err)
                : u.LastCheck is { } at ? Loc.T("settings.update.uptodate", at.ToString("HH:mm:ss"))
                : "");
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        RefreshUpdateStatus(Loc.T("settings.update.checking"));
        try
        {
            await Rt.AppUpdates.CheckAsync();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = Loc.T("settings.update.fail", ex.Message);
            return;
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }

        RefreshUpdateStatus(null);
    }

    private void ViewUpdate_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).MainWindow?.ShowUpdatePage();

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ReleaseFeed.ProjectUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = Loc.T("settings.update.fail", ex.Message);
        }
    }

    // Every change is saved at once: there is no Save button to forget (it used to sit far below the visible part).
    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => AutoSave();

    private void Toggle_Toggled(object sender, RoutedEventArgs e) => AutoSave();

    private void Number_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => AutoSave();

    private void Excluded_LostFocus(object sender, RoutedEventArgs e) => AutoSave();

    private void AutoSave()
    {
        if (_loading) return;
        Apply();
    }

    private void Apply()
    {
        var hosts = Excluded.Text
            .Split(['\r', '\n', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.Trim())
            .ToList();
        var interval = double.IsNaN(Interval.Value) ? 6 : (int)Interval.Value;
        var port = double.IsNaN(Port.Value) ? 8877 : (int)Port.Value;

        var language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var turnedOnStartup = StartWithWindows.IsOn && !Rt.Settings.Current.StartWithWindows;
        Rt.Settings.Update(s =>
        {
            s.Language = language;
            s.StartWithWindows = StartWithWindows.IsOn;
            s.StartMinimized = StartMinimized.IsOn;
            s.AutoAppUpdate = AutoAppUpdate.IsOn;
            s.AutoUpdate = AutoUpdate.IsOn;
            s.UpdateIntervalHours = interval;
            s.CosmeticFiltering = Cosmetic.IsOn;
            s.BlockPopups = BlockPopups.IsOn;
            s.FilterHttps = FilterHttps.IsOn;
            s.ExcludedHosts = hosts;
            s.Port = port;
        });

        // Just turned on here: this also undoes an "off" for PlutonShield in Windows' own Startup list.
        StartupRegistration.Set(StartWithWindows.IsOn, Environment.ProcessPath ?? "", clearWindowsOff: turnedOnStartup);

        var changedLanguage = Loc.Language != Rt.Settings.Current.EffectiveLanguage;
        Loc.Language = Rt.Settings.Current.EffectiveLanguage; // re-draws the window (and this page) when it changed
        if (changedLanguage) return;
        SavedText.Text = Loc.T("settings.saved", DateTime.Now.ToString("HH:mm:ss"));
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).RestartApp();

    private void RefreshCert()
    {
        var c = Rt.Certificate.GetStatus();
        CertText.Text = !c.Exists
            ? Loc.T("settings.cert.none")
            : Loc.T(c.Trusted ? "settings.cert.trusted" : "settings.cert.untrusted", c.Expires?.ToString("d", Loc.Culture) ?? "?");
    }

    private async void InstallCert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(() => Rt.Certificate.Trust()); // Windows shows its own confirmation
        }
        catch (Exception ex)
        {
            CertText.Text = Loc.T("settings.cert.installfail", ex.Message);
            return;
        }

        RefreshCert();
    }

    private async void RemoveCert_Click(object sender, RoutedEventArgs e)
    {
        if (Rt.State == ProtectionState.On) await Rt.SetProtectionAsync(false);
        try
        {
            await Task.Run(() =>
            {
                Rt.Certificate.EnsureCreated();
                Rt.Certificate.Untrust();
            });
        }
        catch (Exception ex)
        {
            CertText.Text = Loc.T("settings.cert.removefail", ex.Message);
            return;
        }

        RefreshCert();
    }

    private void OpenCertFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Rt.Certificate.EnsureCreated();
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Rt.Paths.CaCertFile}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CertText.Text = Loc.T("settings.cert.folderfail", ex.Message);
        }
    }

    private async void ResetProxy_Click(object sender, RoutedEventArgs e)
    {
        await Rt.SetProtectionAsync(false);
        Rt.SystemProxy.Restore();
        ResetText.Text = Loc.T("settings.resetdone");
    }

    private void CopyDiag_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(Diagnostics.Report(Rt));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            DiagText.Text = Loc.T("settings.diag.copied");
        }
        catch (Exception ex)
        {
            DiagText.Text = Loc.T("settings.diag.fail", ex.Message);
        }
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Rt.Paths.LogDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagText.Text = Loc.T("settings.diag.fail", ex.Message);
        }
    }
}
