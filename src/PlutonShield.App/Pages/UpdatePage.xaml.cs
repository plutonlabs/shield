using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PlutonShield.Core.Update;

namespace PlutonShield.App.Pages;

/// <summary>The page behind the bell: what the new version is, and the button that installs it.</summary>
public sealed partial class UpdatePage : Page
{
    private bool _installing;

    public UpdatePage()
    {
        InitializeComponent();
        ErrorText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
    }

    private static AppUpdateService Updates => App.Runtime.AppUpdates;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Updates.Changed -= OnChanged;
        Updates.Changed += OnChanged;
        Refresh();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => Updates.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Refresh);

    private void Refresh()
    {
        var info = Updates.Info;
        if (info is null)
        {
            // Nothing newer is known (for example a check just said so).
            Headline.Text = Loc.T("update.none");
            VersionsText.Text = "";
            NotesPanel.Visibility = Visibility.Collapsed;
            Bar.Visibility = Visibility.Collapsed;
            StatusText.Text = "";
            ErrorText.Text = "";
            Buttons.Visibility = Visibility.Collapsed;
            HintText.Visibility = Visibility.Collapsed;
            return;
        }

        Headline.Text = Loc.T("nav.update");
        VersionsText.Text = Loc.T("update.versions", Updates.Current.ToString(), info.Version.ToString());
        NotesText.Text = info.Notes.Length > 0 ? info.Notes : Loc.T("update.nonotes");
        NotesPanel.Visibility = Visibility.Visible;
        Buttons.Visibility = Visibility.Visible;
        HintText.Visibility = Visibility.Visible;

        var downloading = Updates.State == AppUpdateState.Downloading;
        Bar.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        Bar.Value = Updates.Progress * 100;
        StatusText.Text = _installing ? Loc.T("update.installing")
            : downloading ? Loc.T("update.downloading", (int)(Updates.Progress * 100))
            : Updates.State == AppUpdateState.Ready ? Loc.T("update.ready")
            : Loc.T("update.waiting");
        ErrorText.Text = Updates.Error is { Length: > 0 } err && !_installing && !downloading ? Loc.T("update.fail", err) : "";
        UpdateButton.IsEnabled = !_installing && !downloading;
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        _installing = true;
        UpdateButton.IsEnabled = false;
        try
        {
            // The background download may be running already: this then just waits for it.
            if (!await Updates.DownloadAsync())
            {
                _installing = false;
                Refresh();
                return;
            }

            Refresh();
            if (!((App)Application.Current).InstallUpdateAndExit())
            {
                _installing = false;
                Refresh();
                ErrorText.Text = Loc.T("update.installfail", Updates.Error ?? "?");
            }
        }
        catch (Exception ex)
        {
            _installing = false;
            Refresh();
            ErrorText.Text = Loc.T("update.fail", ex.Message);
        }
    }

    // "Later": back to the home page. The bell stays, and the downloaded installer is kept.
    private void Later_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).MainWindow?.ShowHome();

    private void ViewOnGitHub_Click(object sender, RoutedEventArgs e)
    {
        var url = Updates.Info?.PageUrl ?? ReleaseFeed.ProjectUrl;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorText.Text = Loc.T("update.fail", ex.Message);
        }
    }
}
