using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PlutonShield.Core.Filters;
using PlutonShield.Core.Logging;
using PlutonShield.Core.Proxy;

namespace PlutonShield.Core.Adult;

public enum AdultListState
{
    /// <summary>The adult-content filter is switched off: nothing is loaded.</summary>
    Off,
    /// <summary>It is on, but no list is available yet (not downloaded, or the download failed: see <see cref="AdultBlocklist.Error"/>).</summary>
    Missing,
    Downloading,
    /// <summary>The list is loaded and is being used.</summary>
    Ready,
}

/// <summary>
/// The domains of the adult-content filter. neat-filters publishes them as an <em>optional pack</em> (<c>optional.json</c>
/// and <c>optional-adult.txt</c>, one domain per line) that is fetched only when the person switches the filter on.
/// A domain blocks itself and every subdomain. The check is a hash lookup, so it costs nothing per request.
/// </summary>
/// <remarks>
/// Like the filters themselves, the sha256 comes from the same release as the file: it protects against a damaged or
/// cut-off download, not against a hijacked release.
/// </remarks>
public sealed class AdultBlocklist
{
    public const string PackId = "adult";

    private const long MaxManifestBytes = 1024 * 1024;
    private const long MaxListBytes = 30L * 1024 * 1024;
    private const int MaxDomains = 3_000_000;
    private const string MetaFile = "adult.json";

    private readonly string? _dir;
    private readonly object _gate = new();
    private volatile HashSet<string>? _set;

    /// <param name="dir">Where the list is kept between runs; null keeps nothing on disk (tests).</param>
    public AdultBlocklist(string? dir = null)
    {
        _dir = dir;
    }

    public AdultListState State { get; private set; } = AdultListState.Off;

    /// <summary>Version of the filters release the list came from.</summary>
    public string? Version { get; private set; }

    public int DomainCount { get; private set; }

    /// <summary>Why the last download failed; null when it did not.</summary>
    public string? Error { get; private set; }

    public DateTime? LastAttempt { get; private set; }

    public event EventHandler? Changed;

    /// <summary>True when the host, or a parent domain of it, is on the list. False while no list is loaded.</summary>
    public bool IsBlocked(string host)
    {
        var set = _set;
        if (set is null) return false;
        return Contains(set, host);
    }

    internal static bool Contains(HashSet<string> set, string host)
    {
        host = HostRules.Normalize(host);
        if (host.Length == 0 || IPAddress.TryParse(host, out _)) return false;

        var candidate = host;
        while (true)
        {
            if (set.Contains(candidate)) return true;
            var dot = candidate.IndexOf('.');
            if (dot < 0) return false;
            candidate = candidate[(dot + 1)..];
            if (!candidate.Contains('.')) return false; // never a bare top-level domain
        }
    }

    /// <summary>The domains in a pack file: one per line, lower case. Lines that are not host names are skipped.</summary>
    internal static HashSet<string> Parse(IEnumerable<string> lines)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            var line = raw.Trim().ToLowerInvariant();
            if (line.Length is < 4 or > 253 || line[0] is '#' or '!' || !line.Contains('.')) continue;
            if (line.Any(c => !(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c is '.' or '-'))) continue;
            if (line.StartsWith('.') || line.EndsWith('.') || line.Contains("..")) continue;
            if (line.Split('.').Any(label => label[0] == '-' || label[^1] == '-')) continue;
            set.Add(line);
        }

        return set;
    }

    /// <summary>Switches the filter off and frees the list (the file stays on disk for the next time).</summary>
    public void Unload()
    {
        lock (_gate)
        {
            _set = null;
            DomainCount = 0;
            Error = null;
            State = AdultListState.Off;
        }

        Raise();
    }

    /// <summary>
    /// Loads the list kept on disk, if there is one and it is still the file that was verified. Returns true when it is in use.
    /// When there is none (or it was changed), nothing happens: <see cref="UpdateAsync"/> fetches it.
    /// </summary>
    public bool LoadFromDisk()
    {
        if (_dir is null) return false;
        try
        {
            var meta = ReadMeta();
            var file = meta is null ? null : Path.Combine(_dir, meta.File);
            if (meta is null || file is null || !File.Exists(file)) return false;

            using (var stream = File.OpenRead(file))
            {
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), meta.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn("adult list: the saved copy does not match its checksum; it will be downloaded again");
                    return false;
                }
            }

            Use(Parse(File.ReadLines(file)), meta.Version);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"adult list: could not load the saved copy: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads <c>optional.json</c> from the filters address, and downloads and verifies the adult pack when it is new.
    /// Returns true when a list is in use afterwards (also when the saved one was already the newest).
    /// </summary>
    public async Task<bool> UpdateAsync(HttpClient http, string baseUrl, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_set is null) State = AdultListState.Downloading;
            Error = null;
        }

        Raise();
        string? tmp = null;
        try
        {
            if (!Uri.TryCreate(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("the filter address must start with https://");
            }

            var pack = ParsePack(await ReadSmallAsync(http, new Uri(baseUri, "optional.json"), ct).ConfigureAwait(false), out var version);

            var meta = ReadMeta();
            if (_set is not null && meta is not null && string.Equals(meta.Sha256, pack.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                lock (_gate) { LastAttempt = DateTime.Now; }
                Raise();
                return true; // already the newest
            }

            if (_dir is null) throw new InvalidOperationException("no folder to keep the list in");
            Directory.CreateDirectory(_dir);
            tmp = Path.Combine(_dir, ".tmp-" + Guid.NewGuid().ToString("N"));
            await DownloadVerifiedAsync(http, new Uri(baseUri, Uri.EscapeDataString(pack.File)), tmp, pack, ct).ConfigureAwait(false);

            var set = Parse(File.ReadLines(tmp));
            if (set.Count == 0) throw new InvalidDataException("the list holds no domains");

            var dest = Path.Combine(_dir, pack.File);
            File.Move(tmp, dest, overwrite: true);
            tmp = null;
            File.WriteAllText(Path.Combine(_dir, MetaFile), JsonSerializer.Serialize(new Meta(pack.File, pack.Sha256, version, set.Count)));

            Use(set, version);
            Log.Info($"adult list: {set.Count} domains (filters release {version})");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (_gate) { if (_set is null) State = AdultListState.Missing; }
            Raise();
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"adult list: update failed: {ex.Message}");
            lock (_gate)
            {
                Error = ex.Message;
                if (_set is null) State = AdultListState.Missing; // a list already in use keeps working
            }

            Raise();
            return _set is not null;
        }
        finally
        {
            lock (_gate) { LastAttempt = DateTime.Now; }
            if (tmp is not null)
            {
                try { File.Delete(tmp); } catch (Exception) { /* next time */ }
            }
        }
    }

    private void Use(HashSet<string> set, string? version)
    {
        lock (_gate)
        {
            _set = set;
            DomainCount = set.Count;
            Version = version;
            Error = null;
            State = AdultListState.Ready;
        }

        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    // ---- optional.json ----

    internal sealed record Pack(string File, string Sha256, long Size, long Domains);

    private sealed record Meta(string File, string Sha256, string? Version, int Domains);

    private Meta? ReadMeta()
    {
        try
        {
            var path = _dir is null ? null : Path.Combine(_dir, MetaFile);
            var meta = path is not null && File.Exists(path) ? JsonSerializer.Deserialize<Meta>(File.ReadAllText(path)) : null;
            // The file name was checked before it was saved; check again, because adult.json is only a file on disk.
            return meta is not null && FilterManifest.IsSafeName(meta.File) && meta.Sha256?.Length == 64 ? meta : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The adult pack of an <c>optional.json</c>; throws when there is none or it is not usable.</summary>
    internal static Pack ParsePack(byte[] json, out string? version)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        version = Str(root, "version");
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) || !schema.TryGetInt32(out var s) || s != 1)
        {
            throw new InvalidDataException("optional.json has an unknown format");
        }

        if (root.TryGetProperty("packs", out var packs) && packs.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in packs.EnumerateArray())
            {
                if (Str(p, "id") != PackId) continue;
                var file = Str(p, "file") ?? "";
                var sha = Str(p, "sha256") ?? "";
                var size = p.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var n) ? n : 0;
                var domains = p.TryGetProperty("domains", out var dm) && dm.TryGetInt64(out var d) ? d : 0;
                if (Str(p, "kind") != "domains") throw new InvalidDataException("the adult pack has an unknown kind");
                if (!FilterManifest.IsSafeName(file)) throw new InvalidDataException("the adult pack has an unsafe file name");
                if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) throw new InvalidDataException("the adult pack has no valid sha256");
                if (size is <= 0 or > MaxListBytes) throw new InvalidDataException("the adult pack has an implausible size");
                if (domains is <= 0 or > MaxDomains) throw new InvalidDataException("the adult pack has an implausible domain count");
                return new Pack(file, sha, size, domains);
            }
        }

        throw new InvalidDataException("this release of the filters has no adult-content list");
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static async Task<byte[]> ReadSmallAsync(HttpClient http, Uri uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidDataException("this release of the filters has no adult-content list");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxManifestBytes) throw new InvalidDataException("optional.json is too large");

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[16384];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > MaxManifestBytes) throw new InvalidDataException("optional.json is too large");
        }

        return ms.ToArray();
    }

    private static async Task DownloadVerifiedAsync(HttpClient http, Uri uri, string dest, Pack expected, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(dest);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += n;
            if (total > expected.Size) throw new InvalidDataException($"{expected.File} is larger than optional.json says");
            hash.AppendData(buffer, 0, n);
            await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
        }

        if (total != expected.Size) throw new InvalidDataException($"{expected.File}: size {total} differs from optional.json ({expected.Size})");
        if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{expected.File}: sha256 does not match optional.json");
        }
    }

    /// <summary>Text for the window: how many websites are blocked, or why none are.</summary>
    public string Describe() => State switch
    {
        AdultListState.Ready => string.Create(CultureInfo.InvariantCulture, $"ready, {DomainCount} domains (filters {Version})"),
        AdultListState.Downloading => "downloading",
        AdultListState.Missing => "no list" + (Error is null ? "" : ": " + Error),
        _ => "off",
    };
}
