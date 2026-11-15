using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PlutonShield.Core.Update;

/// <summary>Downloads the installer of a release and checks it against the release's .sha256 file.</summary>
/// <remarks>
/// The hash comes from the same release as the installer, so this protects against a damaged or cut-off download, not
/// against a hijacked release. The address is limited to this project's releases (see <see cref="ReleaseFeed"/>).
/// </remarks>
public static partial class AppUpdateDownloader
{
    private const long MaxShaFileBytes = 4096;

    /// <returns>The path of the verified installer (the file is kept in <paramref name="dir"/>).</returns>
    public static async Task<string> DownloadAsync(HttpClient http, AppUpdateInfo info, string dir, Action<double>? progress, CancellationToken ct)
    {
        if (Path.GetFileName(info.SetupName) != info.SetupName || info.SetupName.Length == 0)
        {
            throw new InvalidDataException("the installer's file name is not safe");
        }

        Directory.CreateDirectory(dir);
        var expected = await ReadExpectedHashAsync(http, info, ct).ConfigureAwait(false);
        var dest = Path.Combine(dir, info.SetupName);

        // Downloaded already (an earlier run): use it when it is still the right file.
        if (File.Exists(dest))
        {
            if (new FileInfo(dest).Length == info.SetupSize &&
                string.Equals(await HashFileAsync(dest, ct).ConfigureAwait(false), expected, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Invoke(1);
                return dest;
            }

            File.Delete(dest);
        }

        var part = dest + ".part";
        try
        {
            using (var response = await http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long total = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > info.SetupSize) throw new InvalidDataException("the download is larger than the release says");
                    hash.AppendData(buffer, 0, n);
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    progress?.Invoke((double)total / info.SetupSize);
                }

                if (total != info.SetupSize)
                {
                    throw new InvalidDataException($"the download is {total} bytes, the release says {info.SetupSize}");
                }

                var actual = Convert.ToHexString(hash.GetHashAndReset());
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("the download does not match its sha256");
                }
            }

            File.Move(part, dest, overwrite: true);
            return dest;
        }
        finally
        {
            try { File.Delete(part); } catch (Exception) { /* cleaned up at the next start */ }
        }
    }

    private static async Task<string> ReadExpectedHashAsync(HttpClient http, AppUpdateInfo info, CancellationToken ct)
    {
        using var response = await http.GetAsync(info.Sha256Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxShaFileBytes) throw new InvalidDataException("the .sha256 file is too large");

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[MaxShaFileBytes + 1];
        var read = 0;
        int n;
        while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false)) > 0) read += n;
        if (read > MaxShaFileBytes) throw new InvalidDataException("the .sha256 file is too large");

        // "<hash>  <file name>" (what tools\make-installer.ps1 writes).
        var m = ShaLine().Match(System.Text.Encoding.ASCII.GetString(buffer, 0, read));
        if (!m.Success) throw new InvalidDataException("the .sha256 file is not in the expected format");
        var named = m.Groups[2].Value.Trim().TrimStart('*');
        if (named.Length > 0 && !string.Equals(named, info.SetupName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("the .sha256 file is for another file");
        }

        return m.Groups[1].Value;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    [GeneratedRegex(@"^\s*([0-9a-fA-F]{64})(?:[ \t]+(.*?))?\s*$", RegexOptions.Singleline)]
    private static partial Regex ShaLine();
}
