using System.Runtime.InteropServices;
using System.Text.Json;

namespace PlutonShield.Core.Update;

/// <summary>The newer release that was found: what to show and what to download.</summary>
public sealed record AppUpdateInfo(
    SemVer Version,
    string Tag,
    string Notes,
    string PageUrl,
    string SetupName,
    string SetupUrl,
    long SetupSize,
    string Sha256Url);

/// <summary>Reads GitHub's "latest release" answer for this project.</summary>
public static class ReleaseFeed
{
    public const string Repo = "plutonlabs/shield";
    public const string ProjectUrl = "https://github.com/" + Repo;
    public const string LatestUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";

    /// <summary>Only files of this project's own releases are ever downloaded.</summary>
    private const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";

    private const string PagePrefix = "https://github.com/" + Repo + "/releases";
    private const long MaxSetupBytes = 400L * 1024 * 1024;
    private const int MaxNotesChars = 4000;

    /// <summary>"x64" or "arm64": the part of the installer's file name that tells the processor.</summary>
    public static string ArchName(Architecture architecture) => architecture == Architecture.Arm64 ? "arm64" : "x64";

    /// <summary>
    /// The update in a <c>/releases/latest</c> answer, or null (with the reason) when it holds none that can be installed:
    /// a draft or pre-release, a tag that is no version, or an installer for this processor that is not attached (yet).
    /// </summary>
    public static AppUpdateInfo? Parse(string json, string arch, out string? reason)
    {
        reason = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reason = "the answer is not a release";
                return null;
            }

            if (Bool(root, "draft") || Bool(root, "prerelease"))
            {
                reason = "the newest release is a draft or a pre-release";
                return null;
            }

            var tag = Str(root, "tag_name");
            if (!SemVer.TryParse(tag, out var version) || version.IsPrerelease)
            {
                reason = $"the tag '{tag}' is not a release version";
                return null;
            }

            var setupName = $"PlutonShield-v{version}-win-{arch}-Setup.exe";
            string? setupUrl = null;
            string? shaUrl = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Str(asset, "name");
                    var url = Str(asset, "browser_download_url");
                    if (name == setupName)
                    {
                        setupUrl = url;
                        size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                    }
                    else if (name == setupName + ".sha256")
                    {
                        shaUrl = url;
                    }
                }
            }

            if (setupUrl is null || shaUrl is null)
            {
                reason = $"{setupName} and its .sha256 are not attached to the release (yet)";
                return null;
            }

            if (!setupUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !shaUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal))
            {
                reason = "the installer is not hosted by this project's releases";
                return null;
            }

            if (size is <= 0 or > MaxSetupBytes)
            {
                reason = "the installer's size is not plausible";
                return null;
            }

            var page = Str(root, "html_url");
            if (!page.StartsWith(PagePrefix, StringComparison.Ordinal)) page = PagePrefix;

            return new AppUpdateInfo(version, tag, CleanNotes(Str(root, "body")), page, setupName, setupUrl, size, shaUrl);
        }
        catch (JsonException ex)
        {
            reason = "the answer is not valid JSON: " + ex.Message;
            return null;
        }
    }

    private static string CleanNotes(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return text.Length > MaxNotesChars ? text[..MaxNotesChars].TrimEnd() + "…" : text;
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
