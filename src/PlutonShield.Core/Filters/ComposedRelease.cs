using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PlutonShield.Core.Logging;

namespace PlutonShield.Core.Filters;

/// <summary>
/// The user's own rules are added to a release, and the lists the user turned off are left out, by building a second
/// folder that holds the same files plus <c>custom.txt</c> and a manifest that lists what is to be loaded. The files are
/// hard-linked (copied where that is not possible), so this is quick and takes no extra space. The release folder itself
/// is never changed.
/// </summary>
public static class ComposedRelease
{
    public const string CustomFileName = "custom.txt";
    private const string CompleteMarker = ".complete";

    /// <summary>
    /// Returns the folder to load. Same folder as <paramref name="releaseDir"/> when there are no custom rules and none of
    /// the <paramref name="disabledLists"/> is in the release.
    /// </summary>
    public static string Build(string releaseDir, string customRules, string composedRoot, IReadOnlyCollection<string>? disabledLists = null)
    {
        var manifestBytes = File.ReadAllBytes(Path.Combine(releaseDir, "manifest.json"));
        var text = (customRules ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var hasCustom = !string.IsNullOrWhiteSpace(text);
        var disabled = DisabledIn(manifestBytes, disabledLists);
        if (!hasCustom && disabled.Count == 0) return releaseDir;

        var id = Hash(manifestBytes, hasCustom ? text : "", disabled);
        Directory.CreateDirectory(composedRoot);
        var target = Path.Combine(composedRoot, id);

        if (!File.Exists(Path.Combine(target, CompleteMarker)))
        {
            var tmp = Path.Combine(composedRoot, ".tmp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                foreach (var file in Directory.GetFiles(releaseDir))
                {
                    var name = Path.GetFileName(file);
                    if (name == "manifest.json" || disabled.Contains(name)) continue;
                    LinkOrCopy(file, Path.Combine(tmp, name));
                }

                if (hasCustom) File.WriteAllText(Path.Combine(tmp, CustomFileName), text, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(tmp, "manifest.json"), ComposeManifest(manifestBytes, hasCustom ? text : null, disabled), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(tmp, CompleteMarker), "");

                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.Move(tmp, target);
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

        Prune(composedRoot, id);
        return target;
    }

    /// <summary>
    /// A text that is equal for equal sets of list names (case and order do not matter): to see whether the lists that are
    /// loaded are the lists that are wanted. Empty when nothing is turned off.
    /// </summary>
    public static string DisabledKey(IEnumerable<string>? names) =>
        string.Join('|', (names ?? Array.Empty<string>())
            .Select(n => (n ?? "").Trim().ToLowerInvariant())
            .Where(n => n.Length > 0)
            .Distinct()
            .Order(StringComparer.Ordinal));

    /// <summary>The names from <paramref name="wanted"/> that are filter lists in this manifest (resources cannot be turned off).</summary>
    internal static SortedSet<string> DisabledIn(byte[] manifestBytes, IReadOnlyCollection<string>? wanted)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        if (wanted is null || wanted.Count == 0) return result;

        var files = JsonNode.Parse(manifestBytes)?["files"]?.AsArray();
        if (files is null) return result;
        var want = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (f?["kind"]?.GetValue<string>() == "list" && f["name"]?.GetValue<string>() is { } name && want.Contains(name)) result.Add(name);
        }

        return result;
    }

    /// <summary>Original manifest plus a <c>custom.txt</c> entry. The text's hash is in the manifest so the engine cache key follows it.</summary>
    internal static string ComposeManifestForTest(byte[] manifestBytes, string customText, IReadOnlyCollection<string>? disabledLists = null) =>
        ComposeManifest(manifestBytes, customText, DisabledIn(manifestBytes, disabledLists));

    /// <param name="customText">The user's rules, or null for none.</param>
    /// <param name="disabled">Names of the lists to leave out.</param>
    internal static string ComposeManifest(byte[] manifestBytes, string? customText, ISet<string> disabled)
    {
        var root = JsonNode.Parse(manifestBytes)?.AsObject() ?? throw new InvalidDataException("manifest.json is not an object");
        var files = root["files"]?.AsArray() ?? throw new InvalidDataException("manifest.json has no files");
        for (var i = files.Count - 1; i >= 0; i--)
        {
            if (files[i]?["kind"]?.GetValue<string>() == "list" && files[i]?["name"]?.GetValue<string>() is { } name && disabled.Contains(name))
            {
                files.RemoveAt(i);
            }
        }

        if (customText is not null)
        {
            files.Add(new JsonObject
            {
                ["name"] = CustomFileName,
                ["kind"] = "list",
                ["role"] = "network",
                ["trusted"] = false,
            });
            root["customSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(customText))).ToLowerInvariant();
        }

        return root.ToJsonString();
    }

    private static string Hash(byte[] manifestBytes, string text, IEnumerable<string> disabled)
    {
        using var sha = SHA256.Create();
        sha.TransformBlock(manifestBytes, 0, manifestBytes.Length, null, 0);
        var sep = new byte[] { 0 };
        sha.TransformBlock(sep, 0, 1, null, 0);
        var t = Encoding.UTF8.GetBytes(text);
        sha.TransformBlock(t, 0, t.Length, null, 0);
        sha.TransformBlock(sep, 0, 1, null, 0);
        var d = Encoding.UTF8.GetBytes(string.Join('\n', disabled));
        sha.TransformFinalBlock(d, 0, d.Length);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant()[..20];
    }

    private static void Prune(string root, string keep)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (Path.GetFileName(dir) != keep) TryDelete(dir);
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Log.Warn($"could not delete {dir}: {ex.Message}"); }
    }

    private static void LinkOrCopy(string source, string destination)
    {
        if (OperatingSystem.IsWindows() && CreateHardLink(destination, source, IntPtr.Zero)) return;
        File.Copy(source, destination);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);
}
