using System.Text;
using System.Text.RegularExpressions;

namespace PlutonShield.Core.Filters;

public enum FilterCategory
{
    Ads,
    Privacy,
    Security,
    Annoyances,
    Fixes,
    Regional,
    /// <summary>Pop-up and whole-page rules: the lists that the app itself needs for those features.</summary>
    System,
    Other,
}

/// <summary>What the "Filter lists" page shows about a release's lists: group, readable name and the counts of what is on.</summary>
public static partial class FilterCatalog
{
    /// <summary>The order in which the groups are shown.</summary>
    public static readonly FilterCategory[] Order =
    {
        FilterCategory.Ads, FilterCategory.Privacy, FilterCategory.Security, FilterCategory.Annoyances,
        FilterCategory.Fixes, FilterCategory.Regional, FilterCategory.System, FilterCategory.Other,
    };

    /// <summary>The filter lists of a release (not the resources file), in a stable order by readable name.</summary>
    public static List<FilterFile> ListsOf(FilterManifest manifest) =>
        manifest.Files
            .Where(f => f.Kind == "list")
            .OrderBy(f => DisplayTitle(f), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// A group for the list. neat-filters does not say what a list is for, so this goes by the role, the flag in the title
    /// (country lists) and words in the file name; a list that matches nothing is "Other" and still works the same.
    /// </summary>
    public static FilterCategory CategoryOf(FilterFile f)
    {
        if (f.Role is "popup" or "document") return FilterCategory.System;
        if (HasFlag(f.Title)) return FilterCategory.Regional;

        var n = f.Name.ToLowerInvariant();
        if (ContainsAny(n, "badware", "malware", "phishing", "urlhaus", "abuse", "scam")) return FilterCategory.Security;
        if (ContainsAny(n, "privacy", "tracking", "tracker", "plowe", "hagezi")) return FilterCategory.Privacy;
        if (ContainsAny(n, "unbreak", "quick-fix", "quickfix")) return FilterCategory.Fixes;
        if (ContainsAny(n, "cookie", "social", "newsletter", "notification", "chat", "ai-", "annoyance", "popup", "overlay", "widget", "banner"))
        {
            return FilterCategory.Annoyances;
        }

        if (n is "easylist.txt" or "ublock-filters.txt" || ContainsAny(n, "adservers", "-ads", "adblock")) return FilterCategory.Ads;
        return FilterCategory.Other;
    }

    /// <summary>
    /// The name to show. The flags in country lists' titles are left out (Windows draws a flag as two letters, which only
    /// looks like a typo), and so is the list's id in brackets at the end: "🇻🇳vn: ABPVN List [VIE-1]" becomes "ABPVN List (VN)".
    /// </summary>
    public static string DisplayTitle(FilterFile f)
    {
        var title = (f.Title ?? "").Trim();
        if (title.Length == 0) return f.Name;

        var hadFlag = HasFlag(title);
        var sb = new StringBuilder();
        foreach (var rune in title.EnumerateRunes())
        {
            if (rune.Value is < 0x1F1E6 or > 0x1F1FF) sb.Append(rune.ToString());
        }

        title = TrailingId().Replace(sb.ToString(), "").Trim();
        if (hadFlag && CountryPrefix().Match(title) is { Success: true } m)
        {
            var countries = string.Join(", ", m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(c => c.ToUpperInvariant()));
            title = m.Groups[2].Value.Trim() + " (" + countries + ")";
        }

        return title.Length == 0 ? f.Name : title;
    }

    /// <summary>Is the list on? A list is on unless its file name is among <paramref name="disabled"/>.</summary>
    public static bool IsOn(FilterFile f, ISet<string> disabled) => !disabled.Contains(f.Name);

    /// <summary>A case-insensitive set of the names, for <see cref="IsOn"/>.</summary>
    public static HashSet<string> DisabledSet(IEnumerable<string>? names) =>
        new(names ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

    /// <summary>How many lists are on, and what their rules add up to.</summary>
    public static (int ListsOn, int ListsTotal, int NetworkRules, int CosmeticRules) Count(FilterManifest manifest, IEnumerable<string>? disabledNames)
    {
        var disabled = DisabledSet(disabledNames);
        var lists = manifest.Files.Where(f => f.Kind == "list").ToList();
        var on = lists.Where(f => IsOn(f, disabled)).ToList();
        return (on.Count, lists.Count, on.Sum(f => f.Rules?.Network ?? 0), on.Sum(f => f.Rules?.Cosmetic ?? 0));
    }

    /// <summary>Does the list match what the user typed in the search box (name, file name or country code)?</summary>
    public static bool Matches(FilterFile f, string query)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) return true;
        foreach (var word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var found = DisplayTitle(f).Contains(word, StringComparison.CurrentCultureIgnoreCase)
                        || f.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                        || (f.Title ?? "").Contains(word, StringComparison.CurrentCultureIgnoreCase);
            if (!found) return false;
        }

        return true;
    }

    private static bool HasFlag(string? title)
    {
        if (string.IsNullOrEmpty(title)) return false;
        foreach (var rune in title.EnumerateRunes())
        {
            if (rune.Value is >= 0x1F1E6 and <= 0x1F1FF) return true;
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] words)
    {
        foreach (var w in words)
        {
            if (text.Contains(w, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    [GeneratedRegex(@"\s*\[[^\]]*\]\s*$")]
    private static partial Regex TrailingId();

    // "eg sa ma dz: Liste AR": two-letter country codes, a colon, then the name.
    [GeneratedRegex(@"^((?:[a-zA-Z]{2}\s+)*[a-zA-Z]{2})\s*:\s*(.+)$")]
    private static partial Regex CountryPrefix();
}
