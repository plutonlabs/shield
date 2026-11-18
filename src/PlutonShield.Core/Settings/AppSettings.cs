using System.Text.Json;
using PlutonShield.Core.Filters;

namespace PlutonShield.Core.Settings;

public sealed class AppSettings
{
    public const string DefaultFiltersBaseUrl = "https://github.com/plutonlabs/neat-filters/releases/latest/download/";

    /// <summary>Port of the local proxy (127.0.0.1 only).</summary>
    public int Port { get; set; } = 8877;

    /// <summary>Decrypt HTTPS (needs the local root certificate) so that requests inside it can be filtered.</summary>
    public bool FilterHttps { get; set; } = true;

    /// <summary>Hide page elements and inject scriptlets into HTML pages.</summary>
    public bool CosmeticFiltering { get; set; } = true;

    /// <summary>Block pop-up windows that the filters' $popup rules name.</summary>
    public bool BlockPopups { get; set; } = true;

    public bool AutoUpdate { get; set; } = true;

    /// <summary>Check for a new version of the app now and then and download it ahead; installing is always the user's click.</summary>
    public bool AutoAppUpdate { get; set; } = true;

    public int UpdateIntervalHours { get; set; } = 6;

    public bool StartWithWindows { get; set; }

    public bool StartMinimized { get; set; }

    /// <summary>Protection was on when the app last ran; it is switched on again at the next start.</summary>
    public bool ProtectionEnabled { get; set; }

    public string FiltersBaseUrl { get; set; } = DefaultFiltersBaseUrl;

    /// <summary>
    /// Block adult websites (the HaGeZi NSFW list, downloaded only when this is switched on). Off by default; anyone can
    /// switch it on or off, there is no password.
    /// </summary>
    public bool AdultFilter { get; set; }

    /// <summary>While <see cref="AdultFilter"/> is on, keep SafeSearch on in Google, Bing, DuckDuckGo and YouTube.</summary>
    public bool ForceSafeSearch { get; set; } = true;

    /// <summary>Hosts that are never decrypted or filtered. "example.com" also covers its subdomains; "*.example.com" is the same.</summary>
    public List<string> ExcludedHosts { get; set; } = new();

    /// <summary>
    /// Programs that are left alone: their connections pass through untouched (HTTPS is not opened, nothing is blocked,
    /// SafeSearch is not applied). Every other program is filtered as before.
    /// </summary>
    public List<SkippedApp> SkippedApps { get; set; } = new();

    /// <summary>The user's own filter rules (Adblock Plus / uBlock Origin syntax), one per line.</summary>
    public string CustomFilters { get; set; } = "";

    /// <summary>
    /// File names (for example "easylist.txt") of the filter lists the user turned off. Lists that are not named here are
    /// on, so a list that a later release adds is on from the start.
    /// </summary>
    public List<string> DisabledLists { get; set; } = new();

    /// <summary>"vi", "en", or empty for the language of Windows.</summary>
    public string Language { get; set; } = "";

    /// <summary>The language in use: the chosen one, or Vietnamese when Windows is in Vietnamese, else English.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveLanguage =>
        Language is "vi" or "en"
            ? Language
            : string.Equals(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "vi", StringComparison.OrdinalIgnoreCase) ? "vi" : "en";

    public const int MaxCustomFiltersLength = 256 * 1024;

    /// <summary>Password of the root CA's key file, protected with DPAPI.</summary>
    public string? CaSecret { get; set; }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this)) ?? new AppSettings();

    /// <summary>Puts every value back in its allowed range.</summary>
    public void Normalize()
    {
        if (Port is < 1024 or > 65535) Port = 8877;
        if (UpdateIntervalHours is < 1 or > 168) UpdateIntervalHours = 6;
        // The GitHub account was renamed (neatbrowser -> plutonlabs): a saved address still holds the old name.
        const string oldOwner = "https://github.com/neatbrowser/";
        if (FiltersBaseUrl.StartsWith(oldOwner, StringComparison.OrdinalIgnoreCase))
        {
            FiltersBaseUrl = "https://github.com/plutonlabs/" + FiltersBaseUrl[oldOwner.Length..];
        }

        if (!Uri.TryCreate(FiltersBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            FiltersBaseUrl = DefaultFiltersBaseUrl;
        }
        else if (!FiltersBaseUrl.EndsWith('/'))
        {
            FiltersBaseUrl += "/";
        }

        CustomFilters = (CustomFilters ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        if (CustomFilters.Length > MaxCustomFiltersLength) CustomFilters = CustomFilters[..MaxCustomFiltersLength];
        if (Language is not ("vi" or "en")) Language = "";

        DisabledLists = (DisabledLists ?? new List<string>())
            .Select(n => (n ?? "").Trim())
            .Where(n => n.Length is > 0 and <= 128 && FilterManifest.IsSafeName(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(1000)
            .ToList();

        SkippedApps = (SkippedApps ?? new List<SkippedApp>())
            .Where(a => a is not null)
            .Select(a => new SkippedApp { Exe = (a.Exe ?? "").Trim().ToLowerInvariant(), Path = (a.Path ?? "").Trim() })
            .Where(a => Proxy.ClientApp.IsValidExe(a.Exe))
            .Select(a => { if (a.Path.Length > 520 || a.Path.Any(char.IsControl)) a.Path = ""; return a; })
            .DistinctBy(a => a.Exe)
            .OrderBy(a => a.Exe, StringComparer.Ordinal)
            .Take(500)
            .ToList();

        ExcludedHosts = (ExcludedHosts ?? new List<string>())
            .Select(h => h.Trim().ToLowerInvariant())
            .Where(h => h.Length > 0)
            .Distinct()
            .ToList();
    }
}
