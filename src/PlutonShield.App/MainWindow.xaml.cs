using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PlutonShield.App.Pages;
using WinRT.Interop;

namespace PlutonShield.App;

public sealed partial class MainWindow : Window
{
    /// <summary>Returns true while closing the window should only hide it (the app keeps running in the tray).</summary>
    public Func<bool> CloseToTray { get; set; } = () => true;

    public MainWindow()
    {
        InitializeComponent();

        // The title text is not drawn any more, but the title still names the window in the taskbar and Alt+Tab.
        Title = "Pluton Shield";

        // No Windows title bar (no icon, no text): the content reaches the top edge, and Windows draws only the
        // standard minimize, maximize and close buttons on the right.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = AppWindow.TitleBar;
            bar.PreferredHeightOption = TitleBarHeightOption.Standard;
            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 700));
        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "plutonshield.ico"));
        }
        catch (Exception)
        {
            // the icon is cosmetic
        }

        AppWindow.Closing += OnClosing;
        Nav.SelectedItem = Nav.MenuItems[0];
        Loc.Changed += (_, _) => DispatcherQueue.TryEnqueue(Relocalize);
    }

    /// <summary>The language was changed: new texts in the menu, and the open page is built again.</summary>
    private void Relocalize()
    {
        foreach (var item in Nav.MenuItems.Concat(Nav.FooterMenuItems))
        {
            if (item is DependencyObject d) L.Apply(d);
        }

        if (ContentFrame.CurrentSourcePageType is { } page) ContentFrame.Navigate(page);
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (CloseToTray())
        {
            args.Cancel = true;
            sender.Hide();
        }
    }

    public void ShowAndFocus()
    {
        AppWindow.Show();
        Activate();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;
        var page = tag switch
        {
            "filters" => typeof(FiltersPage),
            "lists" => typeof(FilterListsPage),
            "custom" => typeof(CustomFiltersPage),
            "stats" => typeof(StatsPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage),
        };
        ContentFrame.Navigate(page);
    }
}
