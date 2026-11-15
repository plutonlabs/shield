using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlutonShield.Core.Settings;
using PlutonShield.Core.Update;

/// <summary>Tests of the app's own update: versions, the release answer, the download and the service around them.</summary>
static class UpdateTests
{
    private const string Base = "https://github.com/plutonlabs/shield/releases/download/";

    public static async Task<int> RunAsync()
    {
        var fail = 0;
        void Eq<T>(T exp, T act, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(exp, act)) { fail++; Console.WriteLine($"FAIL {name}: expected [{exp}] got [{act}]"); }
            else Console.WriteLine($"ok   {name}");
        }

        SemVer V(string s) => SemVer.TryParse(s, out var v) ? v : throw new Exception("bad version " + s);

        // ---- versions ----
        Eq(true, V("0.4.1") > V("0.4.0"), "semver patch");
        Eq(true, V("0.10.0") > V("0.9.0"), "semver numeric, not text");
        Eq(true, V("1.0.0") > V("0.99.99"), "semver major");
        Eq(true, V("0.4.0") > V("0.4.0-rc.1"), "release is newer than its rc");
        Eq(true, V("0.4.0-rc.2") > V("0.4.0-rc.1"), "rc.2 > rc.1");
        Eq(true, V("0.4.0-rc.10") > V("0.4.0-rc.2"), "rc.10 > rc.2");
        Eq(true, V("0.4.0-alpha.1") > V("0.4.0-alpha"), "longer pre-release is newer");
        Eq(true, V("0.4.0-rc.1") > V("0.4.0-beta.9"), "rc after beta (text order)");
        Eq(true, V("0.4.0-1") < V("0.4.0-a"), "number before word");
        Eq(0, V("v1.2.3").CompareTo(V("1.2.3+abc123")), "v prefix and build part ignored");
        Eq("1.2.3-rc.1", V("v1.2.3-rc.1+x").ToString(), "ToString");
        Eq(true, V("0.4.0-rc.1").IsPrerelease && !V("0.4.0").IsPrerelease, "IsPrerelease");
        Eq(false, SemVer.TryParse("1.2", out _), "two parts is no version");
        Eq(false, SemVer.TryParse("latest", out _), "word is no version");
        Eq(false, SemVer.TryParse("", out _), "empty is no version");
        Eq(false, SemVer.TryParse("1.2.3-", out _), "dash without pre-release");
        Eq(true, AppVersion.Current.Major >= 0, "AppVersion.Current reads");
        Environment.SetEnvironmentVariable(AppVersion.FakeVersionVariable, "0.0.1");
        Eq("0.0.1", AppVersion.Current.ToString(), "fake version for trying the feature");
        Environment.SetEnvironmentVariable(AppVersion.FakeVersionVariable, null);

        // ---- the release answer ----
        var installer = Encoding.ASCII.GetBytes(new string('x', 5000) + "setup");
        var sha = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();

        string Release(string tag = "v0.5.0", bool draft = false, bool pre = false, string arch = "x64", string? host = null,
            bool withSha = true, long? size = null, string body = "## What's Changed\r\n* a fix", bool withSetup = true)
        {
            var name = $"PlutonShield-{tag}-win-{arch}-Setup.exe";
            var prefix = host ?? Base;
            var assets = new List<object>
            {
                new { name = $"PlutonShield-{tag}-win-x64.zip", browser_download_url = prefix + tag + "/x.zip", size = 10 },
                new { name = $"PlutonShield-{tag}-win-arm64-Setup.exe", browser_download_url = prefix + tag + "/arm.exe", size = 99 },
            };
            if (withSetup) assets.Add(new { name, browser_download_url = prefix + tag + "/" + name, size = size ?? installer.Length });
            if (withSha) assets.Add(new { name = name + ".sha256", browser_download_url = prefix + tag + "/" + name + ".sha256", size = 90 });
            return JsonSerializer.Serialize(new
            {
                tag_name = tag, draft, prerelease = pre, body,
                html_url = "https://github.com/plutonlabs/shield/releases/tag/" + tag,
                assets,
            });
        }

        var info = ReleaseFeed.Parse(Release(), "x64", out var why);
        Eq(true, info is not null, "feed: a normal release parses");
        Eq("0.5.0", info?.Version.ToString(), "feed: version");
        Eq("PlutonShield-v0.5.0-win-x64-Setup.exe", info?.SetupName, "feed: installer name for x64");
        Eq(installer.Length, (int)(info?.SetupSize ?? 0), "feed: size");
        Eq("## What's Changed\n* a fix", info?.Notes, "feed: notes use \\n");
        Eq("https://github.com/plutonlabs/shield/releases/tag/v0.5.0", info?.PageUrl, "feed: page");
        Eq("PlutonShield-v0.5.0-win-arm64-Setup.exe", ReleaseFeed.Parse(Release(arch: "arm64"), "arm64", out _)?.SetupName, "feed: arm64 installer for arm64");
        Eq(true, ReleaseFeed.Parse(Release(), "arm64", out _) is null, "feed: no arm64 installer when only x64 has a .sha256");
        Eq(true, ReleaseFeed.Parse(Release(draft: true), "x64", out why) is null && why!.Contains("draft"), "feed: draft ignored");
        Eq(true, ReleaseFeed.Parse(Release(pre: true), "x64", out _) is null, "feed: pre-release ignored");
        Eq(true, ReleaseFeed.Parse(Release(tag: "v0.5.0-rc.1"), "x64", out _) is null, "feed: rc tag ignored");
        Eq(true, ReleaseFeed.Parse(Release(tag: "nightly"), "x64", out _) is null, "feed: odd tag ignored");
        Eq(true, ReleaseFeed.Parse(Release(withSha: false), "x64", out why) is null && why!.Contains("sha256"), "feed: no .sha256 yet");
        Eq(true, ReleaseFeed.Parse(Release(withSetup: false), "x64", out _) is null, "feed: no installer yet");
        Eq(true, ReleaseFeed.Parse(Release(host: "https://evil.example/plutonlabs/shield/releases/download/"), "x64", out why) is null, "feed: other host refused");
        Eq(true, ReleaseFeed.Parse(Release(host: "https://github.com/someone/else/releases/download/"), "x64", out _) is null, "feed: other repo refused");
        Eq(true, ReleaseFeed.Parse(Release(host: "http://github.com/plutonlabs/shield/releases/download/"), "x64", out _) is null, "feed: plain http refused");
        Eq(true, ReleaseFeed.Parse(Release(size: 0), "x64", out _) is null, "feed: zero size refused");
        Eq(true, ReleaseFeed.Parse(Release(size: 500L * 1024 * 1024), "x64", out _) is null, "feed: huge size refused");
        Eq(true, ReleaseFeed.Parse("not json", "x64", out why) is null && why!.Contains("JSON"), "feed: invalid JSON");
        Eq(true, ReleaseFeed.Parse("[]", "x64", out _) is null, "feed: not an object");
        Eq(4001, ReleaseFeed.Parse(Release(body: new string('n', 9000)), "x64", out _)!.Notes.Length, "feed: long notes cut (4000 + ellipsis)");
        Eq("x64", ReleaseFeed.ArchName(System.Runtime.InteropServices.Architecture.X64), "arch x64");
        Eq("arm64", ReleaseFeed.ArchName(System.Runtime.InteropServices.Architecture.Arm64), "arch arm64");

        // ---- the download ----
        var dir = Path.Combine(Path.GetTempPath(), "pluton-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var good = ReleaseFeed.Parse(Release(), "x64", out _)!;
            var server = new FakeServer();
            server.Add(good.Sha256Url, Encoding.ASCII.GetBytes($"{sha}  {good.SetupName}\n"));
            server.Add(good.SetupUrl, installer);
            using var http = new HttpClient(server);

            var seen = new List<double>();
            var path = await AppUpdateDownloader.DownloadAsync(http, good, dir, seen.Add, default);
            Eq(Path.Combine(dir, good.SetupName), path, "download: path");
            Eq(true, File.ReadAllBytes(path).AsSpan().SequenceEqual(installer), "download: content");
            Eq(false, File.Exists(path + ".part"), "download: no .part left");
            Eq(1.0, seen.Count > 0 ? seen[^1] : -1, "download: progress ends at 1");
            Eq(true, seen.Zip(seen.Skip(1), (a, b) => b >= a).All(x => x), "download: progress never goes back");

            var before = server.Requests(good.SetupUrl);
            await AppUpdateDownloader.DownloadAsync(http, good, dir, null, default);
            Eq(before, server.Requests(good.SetupUrl), "download: a good file already there is not downloaded again");

            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            await AppUpdateDownloader.DownloadAsync(http, good, dir, null, default);
            Eq(before + 1, server.Requests(good.SetupUrl), "download: a damaged copy is replaced");
            Eq(true, File.ReadAllBytes(path).AsSpan().SequenceEqual(installer), "download: replaced content");

            // wrong hash in the .sha256
            var dir2 = dir + "-b";
            var bad = new FakeServer();
            bad.Add(good.Sha256Url, Encoding.ASCII.GetBytes(new string('0', 64) + "  " + good.SetupName));
            bad.Add(good.SetupUrl, installer);
            var ex1 = await Throws(() => AppUpdateDownloader.DownloadAsync(new HttpClient(bad), good, dir2, null, default));
            Eq(true, ex1 is InvalidDataException && ex1.Message.Contains("sha256"), "download: hash mismatch refused");
            Eq(0, Directory.GetFiles(dir2).Length, "download: nothing left after a hash mismatch");

            // size differs from the release
            var shorter = new FakeServer();
            shorter.Add(good.Sha256Url, Encoding.ASCII.GetBytes($"{sha}  {good.SetupName}"));
            shorter.Add(good.SetupUrl, installer[..^10]);
            var ex2 = await Throws(() => AppUpdateDownloader.DownloadAsync(new HttpClient(shorter), good, dir2, null, default));
            Eq(true, ex2 is InvalidDataException, "download: cut-off download refused");
            var longer = new FakeServer();
            longer.Add(good.Sha256Url, Encoding.ASCII.GetBytes($"{sha}  {good.SetupName}"));
            longer.Add(good.SetupUrl, installer.Concat(new byte[10]).ToArray());
            var ex3 = await Throws(() => AppUpdateDownloader.DownloadAsync(new HttpClient(longer), good, dir2, null, default));
            Eq(true, ex3 is InvalidDataException, "download: too long download refused");
            Eq(0, Directory.GetFiles(dir2).Length, "download: nothing left after a bad size");

            // .sha256 problems
            foreach (var (text, label) in new[] { ("nothing useful", "no hash"), (sha + "  OTHER-Setup.exe", "hash for another file"), (new string('a', 63), "short hash") })
            {
                var s = new FakeServer();
                s.Add(good.Sha256Url, Encoding.ASCII.GetBytes(text));
                s.Add(good.SetupUrl, installer);
                var ex = await Throws(() => AppUpdateDownloader.DownloadAsync(new HttpClient(s), good, dir2, null, default));
                Eq(true, ex is InvalidDataException, "download: .sha256 refused: " + label);
            }

            var starFormat = new FakeServer();
            starFormat.Add(good.Sha256Url, Encoding.ASCII.GetBytes($"{sha.ToUpperInvariant()} *{good.SetupName}\r\n"));
            starFormat.Add(good.SetupUrl, installer);
            Eq(true, (await AppUpdateDownloader.DownloadAsync(new HttpClient(starFormat), good, dir2, null, default)).EndsWith(good.SetupName), "download: sha256sum style (upper case, *name) accepted");
            Directory.Delete(dir2, true);

            var unsafeName = good with { SetupName = "..\\evil.exe" };
            Eq(true, (await Throws(() => AppUpdateDownloader.DownloadAsync(http, unsafeName, dir, null, default))) is InvalidDataException, "download: unsafe file name refused");

            var missing = new FakeServer();
            Eq(true, (await Throws(() => AppUpdateDownloader.DownloadAsync(new HttpClient(missing), good, dir2, null, default))) is HttpRequestException, "download: HTTP error surfaces");
            if (Directory.Exists(dir2)) Directory.Delete(dir2, true);
            Directory.Delete(dir, true);

            // ---- the service ----
            await ServiceTests(dir, Release, installer, sha, Eq, V);
        }
        finally
        {
            foreach (var d in new[] { dir, dir + "-b" }) if (Directory.Exists(d)) Directory.Delete(d, true);
        }

        // ---- every text the window asks for exists, in both languages ----
        string? repo = null;
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            repo = start;
            while (repo is not null && !File.Exists(Path.Combine(repo, "PlutonShield.slnx"))) repo = Path.GetDirectoryName(repo);
            if (repo is not null) break;
        }

        if (repo is not null)
        {
            var used = new SortedSet<string>();
            var pattern = new System.Text.RegularExpressions.Regex(@"(?:L\.Key|L\.Header)=""([a-z0-9.]+)""|Loc\.T\(""([a-z0-9.]+)""");
            foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "src", "PlutonShield.App"), "*.*", SearchOption.AllDirectories)
                         .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            {
                // Comments are skipped, and so are the start of a key that is finished in code ("cat." + name).
                var code = string.Join("\n", File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("//")));
                foreach (System.Text.RegularExpressions.Match m in pattern.Matches(code))
                {
                    var key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (!key.EndsWith('.')) used.Add(key);
                }
            }

            var missing = new List<string>();
            foreach (var lang in new[] { "vi", "en" })
            {
                PlutonShield.App.Loc.Language = lang;
                missing.AddRange(used.Where(k => PlutonShield.App.Loc.T(k) == k).Select(k => lang + ":" + k));
            }

            Eq("", string.Join(", ", missing), $"window texts: all {used.Count} keys in use exist");
            PlutonShield.App.Loc.Language = "en";
        }

        // ---- settings ----
        var s2 = new AppSettings();
        Eq(true, s2.AutoAppUpdate, "settings: app updates are automatic by default");
        s2.AutoAppUpdate = false;
        Eq(false, s2.Clone().AutoAppUpdate, "settings: the choice survives a copy");
        Eq(true, JsonSerializer.Deserialize<AppSettings>("{\"Port\":8877}")!.AutoAppUpdate, "settings: an old file without it means automatic");

        return fail;
    }

    private static async Task ServiceTests(string root, Func<string, bool, bool, string, string?, bool, long?, string, bool, string> release,
        byte[] installer, string sha, Action<object?, object?, string> eq, Func<string, SemVer> v)
    {
        void Eq(object? e, object? a, string n) => eq(e, a, n);
        string Rel(string tag) => release(tag, false, false, "x64", null, true, null, "notes", true);

        var feed = "https://api.test/latest";
        var dir = Path.Combine(root, "svc");

        AppUpdateService Make(FakeServer fs, string current = "0.4.0") =>
            new(new HttpClient(fs), dir, v(current), "x64", feed);

        // newer release → Available
        var fs1 = new FakeServer();
        fs1.Add(feed, Encoding.UTF8.GetBytes(Rel("v0.5.0")));
        var svc = Make(fs1);
        var changes = 0;
        svc.Changed += (_, _) => changes++;
        Eq(AppUpdateState.Idle, svc.State, "service: starts idle");
        Eq(CheckOutcome.Available, await svc.CheckAsync(), "service: newer release is available");
        Eq(AppUpdateState.Available, svc.State, "service: state Available");
        Eq("0.5.0", svc.Info?.Version.ToString(), "service: Info");
        Eq(true, svc.LastCheck is not null && svc.LastAttempt is not null && !svc.LastAttemptFailed, "service: attempt noted");
        Eq(true, changes >= 2, "service: Changed raised");

        // download → Ready
        var info = svc.Info!;
        fs1.Add(info.Sha256Url, Encoding.ASCII.GetBytes($"{sha}  {info.SetupName}\n"));
        fs1.Add(info.SetupUrl, installer);
        Eq(true, await svc.DownloadAsync(), "service: download works");
        Eq(AppUpdateState.Ready, svc.State, "service: state Ready");
        Eq(true, svc.SetupPath is not null && File.Exists(svc.SetupPath), "service: SetupPath exists");
        Eq(true, await svc.DownloadAsync(), "service: download again is a no-op");
        Eq(CheckOutcome.Available, await svc.CheckAsync(), "service: a check while Ready keeps it");
        Eq(1, fs1.Requests(feed), "service: and does not ask GitHub again");

        // same / older / missing release → up to date
        foreach (var (current, label) in new[] { ("0.5.0", "same version"), ("0.6.0", "older release") })
        {
            var fs = new FakeServer();
            fs.Add(feed, Encoding.UTF8.GetBytes(Rel("v0.5.0")));
            var s = Make(fs, current);
            Eq(CheckOutcome.UpToDate, await s.CheckAsync(), "service: " + label + " is up to date");
            Eq(true, s.Info is null && s.State == AppUpdateState.Idle && s.LastCheck is not null, "service: " + label + " leaves nothing");
        }

        var rc = new FakeServer();
        rc.Add(feed, Encoding.UTF8.GetBytes(Rel("v0.5.0")));
        Eq(CheckOutcome.Available, await Make(rc, "0.5.0-rc.1").CheckAsync(), "service: running an rc, the release of it is an update");

        var none = new FakeServer();
        none.Add(feed, new byte[0], HttpStatusCode.NotFound);
        var s404 = Make(none);
        Eq(CheckOutcome.UpToDate, await s404.CheckAsync(), "service: no release published yet (404) is not an error");
        Eq(null, s404.Error, "service: 404 sets no error");

        var notReady = new FakeServer();
        notReady.Add(feed, Encoding.UTF8.GetBytes(release("v0.5.0", false, false, "x64", null, false, null, "n", true)));
        Eq(CheckOutcome.UpToDate, await Make(notReady).CheckAsync(), "service: files not uploaded yet: not an update (yet)");

        // errors
        var broken = new FakeServer();
        broken.Add(feed, new byte[0], HttpStatusCode.InternalServerError);
        var sf = Make(broken);
        Eq(CheckOutcome.Failed, await sf.CheckAsync(), "service: server error fails the check");
        Eq(true, sf.Error is { Length: > 0 } && sf.State == AppUpdateState.Idle && sf.LastAttemptFailed && sf.LastCheck is null, "service: error kept, still idle, failure noted");
        broken.Add(feed, Encoding.UTF8.GetBytes(Rel("v0.5.0")));
        Eq(CheckOutcome.Available, await sf.CheckAsync(), "service: next check works again");
        Eq(null, sf.Error, "service: error cleared by a good check");
        Eq(false, sf.LastAttemptFailed, "service: failure flag cleared");

        // a failed check while an update is known keeps it
        broken.Add(feed, new byte[0], HttpStatusCode.BadGateway);
        Eq(CheckOutcome.Failed, await sf.CheckAsync(), "service: failed check while Available");
        Eq(true, sf.Info is not null && sf.State == AppUpdateState.Available, "service: the known update stays");

        // bad download → back to Available with an error
        var badDl = new FakeServer();
        badDl.Add(feed, Encoding.UTF8.GetBytes(Rel("v0.5.0")));
        var sb = Make(badDl);
        await sb.CheckAsync();
        badDl.Add(sb.Info!.Sha256Url, Encoding.ASCII.GetBytes(new string('0', 64) + "  " + sb.Info.SetupName));
        badDl.Add(sb.Info.SetupUrl, installer);
        Eq(false, await sb.DownloadAsync(), "service: bad hash: download returns false");
        Eq(true, sb.State == AppUpdateState.Available && sb.Error is { Length: > 0 } && sb.LastDownloadFailure is not null, "service: bad hash: Available again, error and time kept");
        Eq(false, sb.StartSetup(), "service: cannot start a setup that is not ready");

        // cancel
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Eq(true, (await Throws(() => Make(badDl).CheckAsync(cts.Token))) is OperationCanceledException, "service: cancel surfaces");

        // cleaning old downloads
        var clean = Path.Combine(root, "clean");
        Directory.CreateDirectory(clean);
        foreach (var f in new[] { "PlutonShield-v0.3.0-win-x64-Setup.exe", "PlutonShield-v0.4.0-win-x64-Setup.exe", "PlutonShield-v0.4.0-win-x64-Setup.exe.sha256",
                     "PlutonShield-v0.5.0-win-x64-Setup.exe", "PlutonShield-v0.5.0-win-x64-Setup.exe.part", "other.txt" })
        {
            File.WriteAllText(Path.Combine(clean, f), "x");
        }

        var cleaner = new AppUpdateService(new HttpClient(new FakeServer()), clean, v("0.4.0"), "x64", feed);
        Eq(4, cleaner.CleanDownloads(), "clean: older and equal versions and .part files go");
        var left = Directory.GetFiles(clean).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Eq("PlutonShield-v0.5.0-win-x64-Setup.exe|other.txt", string.Join("|", left), "clean: a newer installer and other files stay");
        Eq(0, new AppUpdateService(new HttpClient(new FakeServer()), Path.Combine(root, "nowhere"), v("0.4.0"), "x64", feed).CleanDownloads(), "clean: no folder is fine");
        Eq("/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1", AppUpdateService.SetupArguments, "setup arguments");
    }

    private static async Task<Exception> Throws(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { return ex; }
        return new Exception("did not throw");
    }

    /// <summary>Answers requests from a table (address → bytes); anything else is a 404.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Dictionary<string, (byte[] Body, HttpStatusCode Status)> _routes = new();
        private readonly Dictionary<string, int> _counts = new();

        public void Add(string url, byte[] body, HttpStatusCode status = HttpStatusCode.OK) => _routes[url] = (body, status);

        public int Requests(string url) => _counts.GetValueOrDefault(url);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var url = request.RequestUri!.ToString();
            _counts[url] = _counts.GetValueOrDefault(url) + 1;
            if (!_routes.TryGetValue(url, out var r)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(r.Status) { Content = new ByteArrayContent(r.Body) });
        }
    }
}
