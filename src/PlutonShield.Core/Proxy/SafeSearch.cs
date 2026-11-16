using System.Text.RegularExpressions;

namespace PlutonShield.Core.Proxy;

/// <summary>What to change in a request so that a search engine or video site keeps its "safe" mode on.</summary>
/// <param name="NewUrl">The address to use instead (same site, other query), or null when the address stays.</param>
/// <param name="HeaderName">A header to set, or null.</param>
public readonly record struct SafeSearchChange(string? NewUrl, string? HeaderName, string? HeaderValue);

/// <summary>
/// Keeps SafeSearch on for the big search engines while the adult-content filter is on. The proxy sees the decrypted
/// request, so it sets what each service documents for this: a query parameter for Google (<c>safe=active</c>), Bing
/// (<c>adlt=strict</c>) and DuckDuckGo (<c>kp=1</c>), and the <c>YouTube-Restrict</c> header for YouTube.
/// The person cannot switch it off in the page, because the parameter is put back on every request.
/// </summary>
/// <remarks>Best effort: it works for the normal search pages of these sites, and only for HTTPS pages the proxy opens.</remarks>
public static partial class SafeSearch
{
    public const string YouTubeHeader = "YouTube-Restrict";
    public const string YouTubeValue = "Moderate";

    private static readonly HashSet<string> YouTubeHosts = new(StringComparer.Ordinal)
    {
        "youtube.com", "www.youtube.com", "m.youtube.com", "youtubei.googleapis.com", "youtube.googleapis.com",
        "youtube-nocookie.com", "www.youtube-nocookie.com",
    };

    private static readonly HashSet<string> BingPaths = new(StringComparer.Ordinal)
    {
        "/search", "/images/search", "/videos/search", "/news/search",
    };

    private static readonly HashSet<string> DuckPaths = new(StringComparer.Ordinal)
    {
        "/", "/html", "/html/", "/lite", "/lite/", "/d.js", "/i.js", "/v.js", "/news.js",
    };

    /// <returns>The change for this address, or null when the site is not one of these or nothing needs to change.</returns>
    public static SafeSearchChange? For(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return null;
        var host = uri.IdnHost.ToLowerInvariant();
        var path = uri.AbsolutePath;

        if (YouTubeHosts.Contains(host)) return new SafeSearchChange(null, YouTubeHeader, YouTubeValue);

        if (GoogleHost().IsMatch(host) && path == "/search") return WithParameter(uri, "safe", "active");
        if (BingHost().IsMatch(host) && BingPaths.Contains(path)) return WithParameter(uri, "adlt", "strict");
        if (DuckHost().IsMatch(host) && DuckPaths.Contains(path) && HasParameter(uri, "q")) return WithParameter(uri, "kp", "1");
        return null;
    }

    private static bool HasParameter(Uri uri, string name) =>
        Pairs(uri).Any(p => string.Equals(KeyOf(p), name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Pairs(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);

    private static string KeyOf(string pair)
    {
        var eq = pair.IndexOf('=');
        return eq < 0 ? pair : pair[..eq];
    }

    private static SafeSearchChange? WithParameter(Uri uri, string name, string value)
    {
        var wanted = name + "=" + value;
        var pairs = Pairs(uri).ToList();
        var others = pairs.Where(p => !string.Equals(KeyOf(p), name, StringComparison.OrdinalIgnoreCase)).ToList();

        // Already exactly right (one copy, the right value): leave the request alone.
        if (others.Count == pairs.Count - 1 && pairs.Any(p => p == wanted)) return null;

        others.Add(wanted);
        return new SafeSearchChange(uri.GetLeftPart(UriPartial.Path) + "?" + string.Join('&', others), null, null);
    }

    [GeneratedRegex(@"^(?:www\.)?google\.(?:com|co\.[a-z]{2}|com\.[a-z]{2}|[a-z]{2})$")]
    private static partial Regex GoogleHost();

    [GeneratedRegex(@"^(?:www\.)?bing\.com$")]
    private static partial Regex BingHost();

    [GeneratedRegex(@"^(?:www\.|html\.|lite\.)?duckduckgo\.com$")]
    private static partial Regex DuckHost();
}
