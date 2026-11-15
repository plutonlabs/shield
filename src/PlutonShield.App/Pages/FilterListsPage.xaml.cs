using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PlutonShield.Core.Filters;
using PlutonShield.Core.Runtime;

namespace PlutonShield.App.Pages;

/// <summary>
/// The lists of the filter release in use, grouped by what they are for, each with its own switch. A list that is
/// switched off is left out when the engine is built (see <see cref="ComposedRelease"/>); the choice is kept in the
/// settings by file name, so it also holds for the next release of the filters.
/// The rows are built here in code (about 70 of them) instead of a data template: the groups and the search change them often.
/// </summary>
public sealed partial class FilterListsPage : Page
{
    private readonly Dictionary<string, ToggleSwitch> _switches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FilterCategory, bool> _expanded = new();
    private readonly List<Action> _headerRefreshers = new();
    private FilterManifest? _manifest;
    private bool _suppress; // true while switches are set by the code (a group at once): they must not save one by one

    public FilterListsPage()
    {
        InitializeComponent();
        SearchBox.PlaceholderText = Loc.T("lists.search");
    }

    private static ShieldRuntime Rt => App.Runtime;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Rt.Engine.EngineChanged += OnEngineChanged;
        Rt.Settings.Changed += OnSettingsChanged;
        _manifest = LoadManifest();
        Build();
        UpdateSummary();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Rt.Engine.EngineChanged -= OnEngineChanged;
        Rt.Settings.Changed -= OnSettingsChanged;
    }

    private static FilterManifest? LoadManifest()
    {
        var dir = Rt.Engine.LoadedDir ?? Rt.Filters.BestDir;
        return dir is null ? null : Rt.Filters.ReadManifest(dir);
    }

    // Both events may come from a background thread.
    private void OnSettingsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(UpdateSummary);

    private void OnEngineChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        var latest = LoadManifest();
        if (latest?.Version != _manifest?.Version)
        {
            // A new release of the filters was installed while this page was open: it may have other lists.
            _manifest = latest;
            Build();
        }

        UpdateSummary();
    });

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Build();

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        Rt.Settings.Update(s => s.DisabledLists = new List<string>());
        Build();
        UpdateSummary();
    }

    // ---- what is shown ----

    private void UpdateSummary()
    {
        if (_manifest is null)
        {
            SummaryText.Text = Loc.T("filters.none");
            RulesText.Text = "";
            Ring.IsActive = false;
            StatusText.Text = "";
            WarnBar.IsOpen = false;
            return;
        }

        var disabled = Rt.Settings.Current.DisabledLists;
        var c = FilterCatalog.Count(_manifest, disabled);
        SummaryText.Text = Loc.T("lists.summary", c.ListsOn.ToString("N0", Loc.Culture), c.ListsTotal.ToString("N0", Loc.Culture));
        RulesText.Text = Loc.T("filters.rules", c.NetworkRules.ToString("N0", Loc.Culture), c.CosmeticRules.ToString("N0", Loc.Culture));
        WarnBar.Message = Loc.T("lists.warn.none");
        WarnBar.IsOpen = c.ListsOn == 0;

        // The engine is rebuilt a moment after a switch (in the background; the old one keeps working meanwhile).
        var applied = Rt.Engine.LoadedDisabledKey == ComposedRelease.DisabledKey(disabled);
        Ring.IsActive = !applied;
        StatusText.Text = Loc.T(applied ? "lists.applied" : "lists.applying");
    }

    private void Build()
    {
        GroupsPanel.Children.Clear();
        _switches.Clear();
        _headerRefreshers.Clear();

        if (_manifest is null)
        {
            GroupsPanel.Children.Add(new TextBlock { Text = Loc.T("filters.none") });
            return;
        }

        var query = SearchBox.Text;
        var lists = FilterCatalog.ListsOf(_manifest).Where(f => FilterCatalog.Matches(f, query)).ToList();
        if (lists.Count == 0)
        {
            GroupsPanel.Children.Add(new TextBlock { Text = Loc.T("lists.nomatch"), Style = (Style)Resources["SecondaryText"] });
            return;
        }

        foreach (var category in FilterCatalog.Order)
        {
            var inGroup = lists.Where(f => FilterCatalog.CategoryOf(f) == category).ToList();
            if (inGroup.Count > 0) GroupsPanel.Children.Add(BuildGroup(category, inGroup, expandAll: query.Trim().Length > 0));
        }
    }

    private UIElement BuildGroup(FilterCategory category, List<FilterFile> files, bool expandAll)
    {
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        void RefreshHeader()
        {
            var off = FilterCatalog.DisabledSet(Rt.Settings.Current.DisabledLists);
            var on = files.Count(f => FilterCatalog.IsOn(f, off));
            title.Text = $"{Loc.T("cat." + category.ToString().ToLowerInvariant())}  ·  {on}/{files.Count}";
        }

        RefreshHeader();
        _headerRefreshers.Add(RefreshHeader);

        var content = new StackPanel { Spacing = 2 };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var allOn = new HyperlinkButton { Content = Loc.T("lists.group.on") };
        var allOff = new HyperlinkButton { Content = Loc.T("lists.group.off") };
        allOn.Click += (_, _) => SetMany(files, on: true);
        allOff.Click += (_, _) => SetMany(files, on: false);
        actions.Children.Add(allOn);
        actions.Children.Add(allOff);
        content.Children.Add(actions);

        var off0 = FilterCatalog.DisabledSet(Rt.Settings.Current.DisabledLists);
        foreach (var f in files) content.Children.Add(BuildRow(f, FilterCatalog.IsOn(f, off0)));

        var expander = new Expander
        {
            Header = title,
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            // The country lists are the long ones: closed at first, opened by the search.
            IsExpanded = expandAll || (_expanded.TryGetValue(category, out var open) ? open : category != FilterCategory.Regional),
        };
        expander.Expanding += (_, _) => _expanded[category] = true;
        expander.Collapsed += (_, _) => _expanded[category] = false;
        return expander;
    }

    private UIElement BuildRow(FilterFile f, bool on)
    {
        var name = f.Role switch
        {
            "popup" => Loc.T("lists.role.popup"),
            "document" => Loc.T("lists.role.document"),
            _ => FilterCatalog.DisplayTitle(f),
        };

        var detail = Loc.T("lists.detail",
            (f.Rules?.Network ?? 0).ToString("N0", Loc.Culture),
            (f.Rules?.Cosmetic ?? 0).ToString("N0", Loc.Culture),
            FormatSize(f.Size));
        if (string.Equals(f.Tier, "strong", StringComparison.OrdinalIgnoreCase)) detail = Loc.T("lists.strong") + "  ·  " + detail;

        var titleText = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        var detailText = new TextBlock { Text = detail, Style = (Style)Resources["SecondaryText"] };
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(titleText);
        texts.Children.Add(detailText);

        // Set before the handler is attached, so that filling the page does not count as a change.
        var toggle = new ToggleSwitch { IsOn = on, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, name);
        toggle.Toggled += (_, _) =>
        {
            if (!_suppress) SetOne(f.Name, toggle.IsOn);
        };
        _switches[f.Name] = toggle;

        var row = new Grid { Padding = new Thickness(0, 6, 0, 6), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 0);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(texts);
        row.Children.Add(toggle);
        ToolTipService.SetToolTip(row, f.Name);
        return row;
    }

    // ---- changes ----

    private void SetOne(string fileName, bool on)
    {
        Rt.Settings.Update(s =>
        {
            var set = new HashSet<string>(s.DisabledLists, StringComparer.OrdinalIgnoreCase);
            if (on) set.Remove(fileName);
            else set.Add(fileName);
            s.DisabledLists = set.Order(StringComparer.OrdinalIgnoreCase).ToList();
        });
        AfterChange();
    }

    private void SetMany(IEnumerable<FilterFile> files, bool on)
    {
        var names = files.Select(f => f.Name).ToList();
        Rt.Settings.Update(s =>
        {
            var set = new HashSet<string>(s.DisabledLists, StringComparer.OrdinalIgnoreCase);
            foreach (var n in names)
            {
                if (on) set.Remove(n);
                else set.Add(n);
            }

            s.DisabledLists = set.Order(StringComparer.OrdinalIgnoreCase).ToList();
        });

        _suppress = true;
        try
        {
            foreach (var n in names)
            {
                if (_switches.TryGetValue(n, out var toggle)) toggle.IsOn = on;
            }
        }
        finally
        {
            _suppress = false;
        }

        AfterChange();
    }

    private void AfterChange()
    {
        foreach (var refresh in _headerRefreshers) refresh();
        UpdateSummary();
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / 1024.0 / 1024.0).ToString("N1", Loc.Culture) + " MB"
            : Math.Max(1, bytes / 1024).ToString("N0", Loc.Culture) + " KB";
}
