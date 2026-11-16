using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlutonShield.Core;
using PlutonShield.Core.Adult;
using PlutonShield.Core.Proxy;
using PlutonShield.Core.Settings;

/// <summary>Tests of the adult-content filter: the list and its download, SafeSearch, the block page and the settings.</summary>
static class AdultTests
{
    private const string Base = "https://github.com/plutonlabs/neat-filters/releases/latest/download/";
    private const string OptionalUrl = Base + "optional.json";
    private const string PackUrl = Base + "optional-adult.txt";

    public static async Task<int> RunAsync()
    {
        var fail = 0;
        void Eq<T>(T exp, T act, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(exp, act)) { fail++; Console.WriteLine($"FAIL {name}: expected [{exp}] got [{act}]"); }
            else Console.WriteLine($"ok   {name}");
        }

        var root = Path.Combine(Path.GetTempPath(), "pluton-adult-" + Guid.NewGuid().ToString("N"));
        string NewDir() => Path.Combine(root, Guid.NewGuid().ToString("N"));

        // ---- a pack as neat-filters publishes it ----
        var list = string.Join('\n', new[]
        {
            "# HaGeZi NSFW", "", "porn.example", "xxx.test", "adult-site.net", "a.b.c.example.org", "UPPER.CASE.COM",
            "com", ".dot.com", "trailing.dot.", "bad domain.com", "under_score.com", "x.y", "-start.com", "mid.-dash.com", "dup.example", "dup.example",
            "ünicode.com", "sub..empty.com", "! comment", "ok-123.example",
        }) + "\n";
        var listBytes = Encoding.UTF8.GetBytes(list);
        // porn.example xxx.test adult-site.net a.b.c.example.org upper.case.com dup.example ok-123.example
        const int goodDomains = 7;

        string Json(byte[] file, string? sha = null, long? size = null, string id = "adult", string name = "optional-adult.txt",
            string kind = "domains", int schema = 1, string version = "2026.11.16.1", long domains = 7)
        {
            sha ??= Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
            return JsonSerializer.Serialize(new
            {
                schema, version, generated = "2026-11-16T00:00:00Z",
                packs = new[] { new { id, title = "Adult", kind, file = name, sha256 = sha, size = size ?? file.Length, domains, source = "x", license = "x" } },
            });
        }

        FakeServer Server(byte[]? file = null, string? json = null)
        {
            file ??= listBytes;
            var s = new FakeServer();
            s.Add(OptionalUrl, Encoding.UTF8.GetBytes(json ?? Json(file)));
            s.Add(PackUrl, file);
            return s;
        }

        HttpClient Client(FakeServer s) => new(s);
        FakeServer Tweak(FakeServer s, Action<FakeServer> change)
        {
            change(s);
            return s;
        }

        // ---- nothing loaded ----
        var idle = new AdultBlocklist();
        Eq(AdultListState.Off, idle.State, "starts off");
        Eq(false, idle.IsBlocked("porn.example"), "nothing blocked without a list");

        // ---- download, verify, use ----
        var dir = NewDir();
        var server = Server();
        var adult = new AdultBlocklist(dir);
        var changes = 0;
        adult.Changed += (_, _) => changes++;
        Eq(true, await adult.UpdateAsync(Client(server), Base, CancellationToken.None), "update: a good pack is taken");
        Eq(AdultListState.Ready, adult.State, "update: ready");
        Eq(null, adult.Error, "update: no error");
        Eq("2026.11.16.1", adult.Version, "update: version of the release");
        Eq(goodDomains, adult.DomainCount, "update: only real host names are kept (comments, TLDs, junk, duplicates dropped)");
        Eq(true, changes >= 2, "update: Changed is raised (downloading, then ready)");
        Eq(true, File.Exists(Path.Combine(dir, "optional-adult.txt")) && File.Exists(Path.Combine(dir, "adult.json")), "update: list and its record are kept on disk");
        Eq(0, Directory.GetFiles(dir, ".tmp-*").Length, "update: no temporary file left");

        // ---- the check itself ----
        Eq(true, adult.IsBlocked("porn.example"), "blocked: the domain");
        Eq(true, adult.IsBlocked("www.porn.example"), "blocked: a subdomain");
        Eq(true, adult.IsBlocked("a.b.c.porn.example"), "blocked: deep subdomain");
        Eq(true, adult.IsBlocked("PORN.Example"), "blocked: any case");
        Eq(true, adult.IsBlocked("porn.example."), "blocked: trailing dot");
        Eq(true, adult.IsBlocked("porn.example:443"), "blocked: with port");
        Eq(false, adult.IsBlocked("notporn.example"), "not blocked: only a suffix of the text");
        Eq(false, adult.IsBlocked("porn.example.evil.com"), "not blocked: the domain as a prefix");
        Eq(false, adult.IsBlocked("example"), "not blocked: a bare name");
        Eq(false, adult.IsBlocked("other.example"), "not blocked: a sibling");
        Eq(false, adult.IsBlocked("c.example.org"), "not blocked: a parent of a listed deep name");
        Eq(true, adult.IsBlocked("x.a.b.c.example.org"), "blocked: below a listed deep name");
        Eq(true, adult.IsBlocked("upper.case.com"), "blocked: list entries are lower-cased");
        Eq(false, adult.IsBlocked("case.com"), "not blocked: parent of a listed name");
        Eq(false, adult.IsBlocked("com"), "not blocked: a top-level domain");
        Eq(false, adult.IsBlocked("1.2.3.4"), "not blocked: an IP address");
        Eq(false, adult.IsBlocked("[::1]"), "not blocked: an IPv6 address");
        Eq(false, adult.IsBlocked(""), "not blocked: empty");

        // ---- already the newest: not downloaded again ----
        Eq(1, server.Requests(PackUrl), "pack downloaded once so far");
        Eq(true, await adult.UpdateAsync(Client(server), Base, CancellationToken.None), "update: same pack again is fine");
        Eq(1, server.Requests(PackUrl), "update: an unchanged pack is not downloaded again");
        Eq(2, server.Requests(OptionalUrl), "update: optional.json is asked every time");

        // ---- a newer pack ----
        var list2 = Encoding.UTF8.GetBytes("porn.example\nnew-adult.example\n");
        var server2 = Server(list2, Json(list2, version: "2026.11.17.1", domains: 2));
        Eq(true, await adult.UpdateAsync(Client(server2), Base, CancellationToken.None), "update: a newer pack is taken");
        Eq(2, adult.DomainCount, "update: new count");
        Eq("2026.11.17.1", adult.Version, "update: new version");
        Eq(true, adult.IsBlocked("new-adult.example") && !adult.IsBlocked("xxx.test"), "update: the new list is the one in use");

        // ---- the saved copy is used at the next start ----
        var restarted = new AdultBlocklist(dir);
        Eq(true, restarted.LoadFromDisk(), "disk: the saved list loads");
        Eq(AdultListState.Ready, restarted.State, "disk: ready");
        Eq(2, restarted.DomainCount, "disk: count");
        Eq("2026.11.17.1", restarted.Version, "disk: version");
        Eq(true, restarted.IsBlocked("new-adult.example"), "disk: blocks");

        // a changed file is not trusted
        File.AppendAllText(Path.Combine(dir, "optional-adult.txt"), "added-by-someone.example\n");
        var tampered = new AdultBlocklist(dir);
        Eq(false, tampered.LoadFromDisk(), "disk: a file that does not match its checksum is refused");
        Eq(false, tampered.IsBlocked("added-by-someone.example"), "disk: nothing from a refused file is used");
        Eq(AdultListState.Off, tampered.State, "disk: refusing leaves the state alone");
        // ...and a record pointing outside the folder is refused as well
        File.WriteAllText(Path.Combine(dir, "adult.json"), "{\"File\":\"..\\\\..\\\\x.txt\",\"Sha256\":\"" + new string('a', 64) + "\",\"Version\":\"1\",\"Domains\":1}");
        Eq(false, new AdultBlocklist(dir).LoadFromDisk(), "disk: a record with an unsafe file name is refused");
        File.WriteAllText(Path.Combine(dir, "adult.json"), "not json");
        Eq(false, new AdultBlocklist(dir).LoadFromDisk(), "disk: a broken record is refused");
        Eq(false, new AdultBlocklist(NewDir()).LoadFromDisk(), "disk: nothing saved yet");
        // after tampering, the next update puts the right file back
        var healed = new AdultBlocklist(dir);
        Eq(true, await healed.UpdateAsync(Client(server2), Base, CancellationToken.None), "disk: update heals a changed file");
        Eq(true, healed.IsBlocked("porn.example") && !healed.IsBlocked("added-by-someone.example"), "disk: healed list is the verified one");

        // ---- switching off ----
        healed.Unload();
        Eq(AdultListState.Off, healed.State, "unload: off");
        Eq(false, healed.IsBlocked("porn.example"), "unload: nothing blocked");
        Eq(true, File.Exists(Path.Combine(dir, "optional-adult.txt")), "unload: the file stays for next time");
        Eq(true, new AdultBlocklist(dir).LoadFromDisk(), "unload: ...and loads again");

        // ---- things that go wrong: nothing is used, the reason is kept ----
        async Task<AdultBlocklist> Fails(string what, FakeServer s, string expectedPart, string baseUrl = Base)
        {
            var d = NewDir();
            var a = new AdultBlocklist(d);
            var ok = await a.UpdateAsync(Client(s), baseUrl, CancellationToken.None);
            Eq(false, ok, $"fail: {what}: not in use");
            Eq(AdultListState.Missing, a.State, $"fail: {what}: state is missing");
            Eq(true, a.Error?.Contains(expectedPart, StringComparison.OrdinalIgnoreCase) == true, $"fail: {what}: reason says '{expectedPart}' (was: {a.Error})");
            Eq(false, a.IsBlocked("porn.example"), $"fail: {what}: nothing blocked");
            Eq(false, File.Exists(Path.Combine(d, "adult.json")), $"fail: {what}: nothing recorded");
            Eq(0, Directory.Exists(d) ? Directory.GetFiles(d, ".tmp-*").Length : 0, $"fail: {what}: no temporary file left");
            return a;
        }

        await Fails("release without optional.json", new FakeServer(), "no adult-content list");
        await Fails("pack not in optional.json", Server(json: Json(listBytes, id: "other")), "no adult-content list");
        await Fails("pack of another kind", Server(json: Json(listBytes, kind: "rules")), "unknown kind");
        await Fails("unknown schema", Server(json: Json(listBytes, schema: 2)), "unknown format");
        await Fails("checksum that is not hex", Server(json: Json(listBytes, sha: new string('z', 64))), "sha256");
        await Fails("short checksum", Server(json: Json(listBytes, sha: "abcd")), "sha256");
        await Fails("file that does not match the checksum", Server(json: Json(listBytes, sha: new string('0', 64))), "sha256 does not match");
        await Fails("file bigger than optional.json says", Server(json: Json(listBytes, size: listBytes.Length - 5)), "larger");
        await Fails("file smaller than optional.json says", Server(json: Json(listBytes, size: listBytes.Length + 5)), "size");
        await Fails("absurd size", Server(json: Json(listBytes, size: 500L * 1024 * 1024)), "size");
        await Fails("unsafe file name", Server(json: Json(listBytes, name: "../evil.txt")), "unsafe");
        await Fails("file name with a folder", Server(json: Json(listBytes, name: "a/b.txt")), "unsafe");
        await Fails("pack without domains", Server(json: Json(listBytes, domains: 0)), "domain count");
        await Fails("optional.json that is not json", Server(json: "<html>nope"), "");
        await Fails("pack file missing", Tweak(new FakeServer(), s => s.Add(OptionalUrl, Encoding.UTF8.GetBytes(Json(listBytes)))), "404");
        await Fails("pack download fails", Tweak(Server(), s => s.Add(PackUrl, Array.Empty<byte>(), HttpStatusCode.InternalServerError)), "500");
        var empty = Encoding.UTF8.GetBytes("# only a comment\ncom\n\n");
        await Fails("pack with no domains in it", Server(empty), "no domains");
        await Fails("address that is not https", Server(), "https", "http://github.com/plutonlabs/neat-filters/releases/latest/download/");
        await Fails("address that is not an address", Server(), "https", "not a url");
        Eq(false, Directory.Exists(Path.Combine(root, "x.txt")) || File.Exists(Path.Combine(root, "evil.txt")), "fail: nothing was written outside the folder");

        // a list that already works keeps working when the next update fails
        var keep = new AdultBlocklist(NewDir());
        await keep.UpdateAsync(Client(Server()), Base, CancellationToken.None);
        var bad = Server(json: Json(listBytes, sha: new string('0', 64), version: "2026.12.01.1"));
        Eq(true, await keep.UpdateAsync(Client(bad), Base, CancellationToken.None), "keep: a failed update leaves the list in use");
        Eq(AdultListState.Ready, keep.State, "keep: still ready");
        Eq(true, keep.Error?.Contains("sha256") == true, "keep: the error is remembered for the window");
        Eq(true, keep.IsBlocked("porn.example"), "keep: still blocks");
        Eq("2026.11.16.1", keep.Version, "keep: still the old version");

        // cancelling is not an error
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            var cancelled = new AdultBlocklist(NewDir());
            var threw = false;
            try { await cancelled.UpdateAsync(Client(Server()), Base, cts.Token); }
            catch (OperationCanceledException) { threw = true; }
            Eq(true, threw, "cancel: the caller sees the cancellation");
            Eq(null, cancelled.Error, "cancel: it is not reported as an error");
            Eq(false, cancelled.IsBlocked("porn.example"), "cancel: nothing in use");
        }

        // Describe() is only text for logs
        Eq(true, adult.Describe().Contains("ready") && new AdultBlocklist().Describe() == "off", "describe");

        // ---- SafeSearch ----
        string? Url(string u) => SafeSearch.For(u)?.NewUrl;
        string? Header(string u) => SafeSearch.For(u)?.HeaderName;

        Eq("https://www.google.com/search?q=cats&safe=active", Url("https://www.google.com/search?q=cats"), "safesearch google: parameter added");
        Eq(null, Url("https://www.google.com/search?q=cats&safe=active"), "safesearch google: already on, nothing to do");
        Eq("https://www.google.com/search?q=a&hl=en&safe=active", Url("https://www.google.com/search?q=a&safe=off&hl=en"), "safesearch google: 'off' is replaced");
        Eq("https://www.google.com/search?q=a&safe=active", Url("https://www.google.com/search?q=a&safe=active&safe=off"), "safesearch google: duplicates are folded into one");
        Eq("https://www.google.com/search?q=a&safe=active", Url("https://www.google.com/search?q=a&Safe=Images"), "safesearch google: the name in any case");
        Eq("https://www.google.com/search?safe=active", Url("https://www.google.com/search"), "safesearch google: no query at all");
        Eq("https://www.google.com/search?q=a%20b+c%26d&safe=active", Url("https://www.google.com/search?q=a%20b+c%26d"), "safesearch google: other parameters are kept as they are");
        Eq("https://google.com.vn/search?q=a&safe=active", Url("https://google.com.vn/search?q=a"), "safesearch google: google.com.vn");
        Eq("https://www.google.co.uk/search?q=a&safe=active", Url("https://www.google.co.uk/search?q=a"), "safesearch google: google.co.uk");
        Eq("https://www.google.de/search?q=a&safe=active", Url("https://www.google.de/search?q=a"), "safesearch google: google.de");
        Eq("http://www.google.com/search?q=a&safe=active", Url("http://www.google.com/search?q=a"), "safesearch google: plain http too");
        Eq(null, Url("https://www.google.com/maps?q=a"), "safesearch google: other services are left alone");
        Eq(null, Url("https://mail.google.com/search?q=a"), "safesearch google: other hosts are left alone");
        Eq(null, Url("https://google.evil.com/search?q=a"), "safesearch google: look-alike host");
        Eq(null, Url("https://notgoogle.com/search?q=a"), "safesearch google: look-alike host 2");
        Eq(null, Url("https://www.google.com.evil.example/search?q=a"), "safesearch google: look-alike host 3");

        Eq("https://www.bing.com/search?q=x&adlt=strict", Url("https://www.bing.com/search?q=x"), "safesearch bing: web");
        Eq("https://www.bing.com/images/search?q=x&adlt=strict", Url("https://www.bing.com/images/search?q=x"), "safesearch bing: images");
        Eq("https://www.bing.com/search?q=x&adlt=strict", Url("https://www.bing.com/search?q=x&adlt=off"), "safesearch bing: 'off' is replaced");
        Eq(null, Url("https://www.bing.com/search?q=x&adlt=strict"), "safesearch bing: already on");
        Eq(null, Url("https://www.bing.com/maps?q=x"), "safesearch bing: other pages are left alone");

        Eq("https://duckduckgo.com/?q=x&kp=1", Url("https://duckduckgo.com/?q=x"), "safesearch duckduckgo: parameter added");
        Eq("https://duckduckgo.com/?q=x&kp=1", Url("https://duckduckgo.com/?q=x&kp=-2"), "safesearch duckduckgo: 'off' is replaced");
        Eq(null, Url("https://duckduckgo.com/"), "safesearch duckduckgo: no search, nothing to do");
        Eq("https://html.duckduckgo.com/html/?q=x&kp=1", Url("https://html.duckduckgo.com/html/?q=x"), "safesearch duckduckgo: html version");
        Eq(null, Url("https://duckduckgo.com/assets/logo.png?q=x"), "safesearch duckduckgo: other paths are left alone");

        Eq("YouTube-Restrict", Header("https://www.youtube.com/watch?v=1"), "safesearch youtube: header on pages");
        Eq("Moderate", SafeSearch.For("https://www.youtube.com/")?.HeaderValue, "safesearch youtube: moderate");
        Eq(null, SafeSearch.For("https://www.youtube.com/watch?v=1")?.NewUrl, "safesearch youtube: the address stays");
        Eq("YouTube-Restrict", Header("https://m.youtube.com/"), "safesearch youtube: mobile site");
        Eq(null, Header("https://youtube.evil.com/"), "safesearch youtube: look-alike host");
        Eq(null, SafeSearch.For("https://example.com/search?q=x") is null ? null : "changed", "safesearch: other sites are left alone");
        Eq(null, SafeSearch.For("not a url") is null ? null : "changed", "safesearch: not an address");
        Eq(null, SafeSearch.For("ftp://www.google.com/search?q=a") is null ? null : "changed", "safesearch: only http and https");

        // ---- the block page ----
        var pageEn = PageText.AdultBlockedPage("https://porn.example/watch?q=secret&x=<script>", "en");
        var pageVi = PageText.AdultBlockedPage("https://porn.example/watch", "vi");
        Eq(true, pageEn.Contains("Adult website blocked") && pageEn.Contains("porn.example") && pageEn.Contains("Filter lists"), "page en: says what happened and where to switch it off");
        Eq(true, !pageEn.Contains("secret") && !pageEn.Contains("<script>"), "page en: shows the site only, not the address with its query");
        Eq(true, pageVi.Contains("nội dung người lớn") && pageVi.Contains("Quay lại") && pageVi.Contains("lang=\"vi\""), "page vi");
        Eq(true, pageEn.Contains("javascript:history.back()") && !pageEn.Contains("window.close"), "page: a way back, and the tab is not closed");

        // ---- settings ----
        var fresh = new AppSettings();
        Eq(false, fresh.AdultFilter, "settings: adult filter is off by default");
        Eq(true, fresh.ForceSafeSearch, "settings: SafeSearch is on by default");
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Port\":8877,\"DisabledLists\":[]}")!;
        Eq(false, old.AdultFilter, "settings: a settings file from before has the filter off");
        Eq(true, old.ForceSafeSearch, "settings: ...and SafeSearch on, for when it is switched on");
        fresh.AdultFilter = true;
        fresh.ForceSafeSearch = false;
        var copy = fresh.Clone();
        Eq(true, copy.AdultFilter && !copy.ForceSafeSearch, "settings: both survive a copy (what the store saves)");
        Eq(Path.Combine(root, "adult"), new AppPaths(root, Path.Combine(root, "nofilters")).AdultDir, "paths: the list lives in its own folder");

        try { Directory.Delete(root, recursive: true); } catch (Exception) { /* temp folder */ }
        return fail;
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
