using System.Text.Json;
using PlutonShield.Core.Proxy;
using PlutonShield.Core.Settings;

/// <summary>Tests of the per-program exceptions: names of programs, the process lookup, the list of programs seen, the settings.</summary>
static class AppRuleTests
{
    public static int Run()
    {
        var fail = 0;
        void Eq<T>(T exp, T act, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(exp, act)) { fail++; Console.WriteLine($"FAIL {name}: expected [{exp}] got [{act}]"); }
            else Console.WriteLine($"ok   {name}");
        }

        // ---- file names ----
        Eq("chrome.exe", ClientApp.FromPath(@"C:\Program Files\Google\Chrome\Application\Chrome.EXE")?.Exe, "name: Windows path, lower-cased");
        Eq(@"C:\Program Files\Google\Chrome\Application\Chrome.EXE", ClientApp.FromPath(@"  C:\Program Files\Google\Chrome\Application\Chrome.EXE ")?.Path, "name: the path is kept as it was seen (trimmed)");
        Eq("curl", ClientApp.FromPath("/usr/bin/curl")?.Exe, "name: Linux path");
        Eq("discord.exe", ClientApp.FromPath("discord.exe")?.Exe, "name: a bare file name");
        Eq("discord.exe", ClientApp.FromPath(@"C:\Users\a\AppData\Local\Discord\app-1.0.9100\Discord.exe")?.Exe, "name: a versioned folder does not matter");
        Eq(null, ClientApp.FromPath(null), "name: nothing");
        Eq(null, ClientApp.FromPath("   "), "name: blank");
        Eq(null, ClientApp.FromPath(@"C:\folder\"), "name: a folder is not a program");
        Eq("Discord", ClientApp.FromPath(@"C:\x\Discord.exe")?.Name, "name: shown without .exe");
        Eq("curl", ClientApp.FromPath("/usr/bin/curl")?.Name, "name: shown as it is without .exe");
        Eq(true, ClientApp.IsValidExe("a b.exe") && ClientApp.IsValidExe("ünï.exe"), "valid: spaces and letters are fine");
        Eq(false, ClientApp.IsValidExe("a\\b.exe") || ClientApp.IsValidExe("a/b.exe") || ClientApp.IsValidExe("c:x.exe"), "valid: no folders or drives");
        Eq(false, ClientApp.IsValidExe("..") || ClientApp.IsValidExe("") || ClientApp.IsValidExe(null) || ClientApp.IsValidExe("a*.exe") || ClientApp.IsValidExe("a\u0001.exe"), "valid: no dots only, empty, wildcards or control characters");
        Eq(false, ClientApp.IsValidExe(new string('a', 129)), "valid: not too long");

        // ---- the process lookup is asked once a minute, not per request ----
        var asked = new List<int>();
        var resolver = new ClientAppResolver(pid =>
        {
            asked.Add(pid);
            return pid switch { 100 => new ClientApp("app.exe", @"C:\a\app.exe"), 200 => throw new InvalidOperationException("access denied"), _ => null };
        });
        Eq("app.exe", resolver.Resolve(100)?.Exe, "resolver: a known process");
        Eq("app.exe", resolver.Resolve(100)?.Exe, "resolver: asked again");
        Eq(1, asked.Count(p => p == 100), "resolver: the answer is remembered");
        Eq(null, resolver.Resolve(200), "resolver: a lookup that fails is just 'unknown'");
        Eq(null, resolver.Resolve(200), "resolver: ...and that is remembered too");
        Eq(1, asked.Count(p => p == 200), "resolver: a failing lookup is not repeated for every request");
        Eq(null, resolver.Resolve(-1), "resolver: unknown process id");
        Eq(null, resolver.Resolve(0), "resolver: process 0");
        Eq(null, resolver.Resolve(4), "resolver: the Windows System process");
        Eq(false, asked.Contains(-1) || asked.Contains(0) || asked.Contains(4), "resolver: those are not even looked up");
        for (var pid = 1000; pid < 1700; pid++) resolver.Resolve(pid);
        Eq(true, resolver.Resolve(1699) is null, "resolver: many processes do not break it");

        // the real lookup, for this very process
        var self = new ClientAppResolver().Resolve(Environment.ProcessId);
        Eq(true, self is not null && ClientApp.IsValidExe(self.Exe), "resolver: the operating system finds this test program");

        // ---- programs seen ----
        var seen = new ObservedApps();
        var changed = 0;
        seen.Changed += (_, _) => changed++;
        var a = new ClientApp("a.exe", @"C:\x\a.exe");
        seen.Record(a);
        seen.Record(a);
        Thread.Sleep(5);
        seen.Record(new ClientApp("b.exe", @"C:\x\b.exe"));
        Eq(2, changed, "seen: Changed only when a new program appears");
        var list = seen.Snapshot();
        Eq(2, list.Count, "seen: two programs");
        Eq("b.exe", list[0].App.Exe, "seen: the latest first");
        Eq(2L, list.First(e => e.App.Exe == "a.exe").Requests, "seen: requests are counted");
        seen.Record(new ClientApp("a.exe", @"C:\y\a.exe"));
        Eq(@"C:\y\a.exe", seen.Snapshot().First(e => e.App.Exe == "a.exe").App.Path, "seen: a program that moved shows where it is now");
        seen.Record(new ClientApp("a.exe", ""));
        Eq(@"C:\y\a.exe", seen.Snapshot().First(e => e.App.Exe == "a.exe").App.Path, "seen: an unknown path does not wipe a known one");
        for (var i = 0; i < 400; i++) seen.Record(new ClientApp($"p{i}.exe", ""));
        Eq(true, seen.Snapshot().Count <= 301, "seen: the list is kept small");

        // ---- settings ----
        var s = new AppSettings();
        Eq(0, s.SkippedApps.Count, "settings: no program is left alone by default");
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Port\":8877}")!;
        Eq(0, old.SkippedApps.Count, "settings: a file from before has none");
        s.SkippedApps = new List<SkippedApp>
        {
            new() { Exe = "  Discord.EXE ", Path = @"C:\d\Discord.exe" },
            new() { Exe = "discord.exe", Path = "duplicate" },
            new() { Exe = "..\\evil.exe" },
            new() { Exe = "" },
            new() { Exe = "a/b.exe" },
            new() { Exe = "steam.exe", Path = new string('x', 600) },
            new() { Exe = "alpha.exe", Path = "p\u0001q" },
        };
        s.Normalize();
        Eq("alpha.exe,discord.exe,steam.exe", string.Join(",", s.SkippedApps.Select(a => a.Exe)), "normalize: tidy, lower-cased, no duplicates or paths in the name, sorted");
        Eq(@"C:\d\Discord.exe", s.SkippedApps.First(a => a.Exe == "discord.exe").Path, "normalize: the first of a duplicate wins");
        Eq("", s.SkippedApps.First(a => a.Exe == "steam.exe").Path, "normalize: an absurd path is dropped");
        Eq("", s.SkippedApps.First(a => a.Exe == "alpha.exe").Path, "normalize: a path with control characters is dropped");
        s.SkippedApps.Add(null!);
        s.Normalize();
        Eq(3, s.SkippedApps.Count, "normalize: a null entry is dropped");
        s.SkippedApps = Enumerable.Range(0, 800).Select(i => new SkippedApp { Exe = $"app{i}.exe" }).ToList();
        s.Normalize();
        Eq(500, s.SkippedApps.Count, "normalize: at most 500");
        var copy = new AppSettings { SkippedApps = new() { new SkippedApp { Exe = "x.exe", Path = "p" } } }.Clone();
        Eq("x.exe", copy.SkippedApps.Single().Exe, "settings: survives a copy (what the store saves)");
        s.SkippedApps = null!;
        s.Normalize();
        Eq(0, s.SkippedApps.Count, "normalize: a list that is null becomes empty");

        return fail;
    }
}
