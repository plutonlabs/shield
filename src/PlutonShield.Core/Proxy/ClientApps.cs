using System.Collections.Concurrent;
using System.Diagnostics;

namespace PlutonShield.Core.Proxy;

/// <summary>
/// A program on this computer that talks through the proxy. Rules match on <see cref="Exe"/> (the file name, lower case),
/// not on the folder, because programs that update themselves (Discord, Teams...) move to a new folder with each version.
/// <see cref="Path"/> is where it was last seen running, for showing it to the person.
/// </summary>
public sealed record ClientApp(string Exe, string Path)
{
    /// <summary>The file name without ".exe", for showing (with the capitals it has on disk, when the path is known).</summary>
    public string Name
    {
        get
        {
            var file = Exe;
            var cut = Path.LastIndexOfAny(new[] { '\\', '/' });
            if (cut >= 0 && Path[(cut + 1)..].Trim().Equals(Exe, StringComparison.OrdinalIgnoreCase)) file = Path[(cut + 1)..].Trim();
            return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
        }
    }

    /// <summary>The app that belongs to a file path (either kind of slash), or null when it names no file.</summary>
    public static ClientApp? FromPath(string? path)
    {
        var exe = ExeNameOf(path);
        return exe is null ? null : new ClientApp(exe, path!.Trim());
    }

    /// <summary>The lower-case file name of a path, or null when there is none or it is not a usable name.</summary>
    public static string? ExeNameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim();
        var cut = path.LastIndexOfAny(new[] { '\\', '/' });
        var name = (cut >= 0 ? path[(cut + 1)..] : path).Trim().ToLowerInvariant();
        return IsValidExe(name) ? name : null;
    }

    /// <summary>A file name only: no folder, no drive, no control characters, not too long.</summary>
    public static bool IsValidExe(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 128 && name != "." && name != ".."
        && name.All(c => c >= ' ' && c != '\\' && c != '/' && c != ':' && c != '*' && c != '?' && c != '"' && c != '<' && c != '>' && c != '|');
}

/// <summary>Finds out which program a process id belongs to. The answers are kept for a minute (process ids are reused).</summary>
public sealed class ClientAppResolver
{
    private static readonly long TtlMs = 60_000;
    private const int MaxCached = 512;

    private readonly Func<int, ClientApp?> _lookup;
    private readonly ConcurrentDictionary<int, (ClientApp? App, long Expires)> _cache = new();

    /// <param name="lookup">How a process id becomes a program; the default asks the operating system. For tests.</param>
    public ClientAppResolver(Func<int, ClientApp?>? lookup = null) => _lookup = lookup ?? SystemLookup;

    public ClientApp? Resolve(int pid)
    {
        if (pid <= 4) return null; // unknown (-1), idle (0) and the Windows "System" process (4): not a program to choose

        var now = Environment.TickCount64;
        if (_cache.TryGetValue(pid, out var hit) && hit.Expires > now) return hit.App;

        ClientApp? app;
        try
        {
            app = _lookup(pid);
        }
        catch (Exception)
        {
            app = null;
        }

        if (_cache.Count >= MaxCached)
        {
            foreach (var old in _cache.Where(kv => kv.Value.Expires <= now).Select(kv => kv.Key).ToList()) _cache.TryRemove(old, out _);
            if (_cache.Count >= MaxCached) _cache.Clear();
        }

        _cache[pid] = (app, now + TtlMs);
        return app;
    }

    private static ClientApp? SystemLookup(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                if (File.ResolveLinkTarget($"/proc/{pid}/exe", false)?.FullName is { } target &&
                    ClientApp.FromPath(target.Replace(" (deleted)", "")) is { } linux)
                {
                    return linux;
                }
            }
            catch (Exception)
            {
                // fall back to the process name
            }
        }

        using var process = Process.GetProcessById(pid);
        try
        {
            if (ClientApp.FromPath(process.MainModule?.FileName) is { } app) return app; // refused for protected processes
        }
        catch (Exception)
        {
            // only the name is known then
        }

        var name = process.ProcessName.Trim().ToLowerInvariant();
        if (OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.Ordinal)) name += ".exe";
        return ClientApp.IsValidExe(name) ? new ClientApp(name, "") : null;
    }
}

/// <summary>The programs that went through the proxy during this run, for the "Apps" page.</summary>
public sealed class ObservedApps
{
    public sealed record Entry(ClientApp App, DateTime LastSeen, long Requests);

    private const int MaxApps = 300;

    private sealed class Counter
    {
        public volatile ClientApp App = null!;
        public long Requests;
        public long LastSeenTicks;
    }

    private readonly ConcurrentDictionary<string, Counter> _apps = new(StringComparer.Ordinal);

    /// <summary>Raised when a program is seen for the first time (not on every request). May come from any thread.</summary>
    public event EventHandler? Changed;

    public void Record(ClientApp app)
    {
        var added = false;
        var counter = _apps.GetOrAdd(app.Exe, _ =>
        {
            added = true;
            return new Counter { App = app };
        });

        if (!ReferenceEquals(counter.App, app) && app.Path.Length > 0) counter.App = app; // it moved (an update)
        Interlocked.Increment(ref counter.Requests);
        Interlocked.Exchange(ref counter.LastSeenTicks, DateTime.Now.Ticks);

        if (!added) return;
        if (_apps.Count > MaxApps)
        {
            var oldest = _apps.OrderBy(kv => Interlocked.Read(ref kv.Value.LastSeenTicks)).First().Key;
            _apps.TryRemove(oldest, out _);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The programs seen so far, the most recent first.</summary>
    public IReadOnlyList<Entry> Snapshot() => _apps.Values
        .Select(c => new Entry(c.App, new DateTime(Interlocked.Read(ref c.LastSeenTicks)), Interlocked.Read(ref c.Requests)))
        .OrderByDescending(e => e.LastSeen)
        .ToList();
}
