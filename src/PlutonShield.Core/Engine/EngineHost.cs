using System.Security.Cryptography;
using Neat.Adblock;
using PlutonShield.Core.Filters;
using PlutonShield.Core.Logging;

namespace PlutonShield.Core.Engine;

/// <summary>
/// Owns the engine in use. A new release is built on a background thread and swapped in; requests
/// that are being checked keep the old engine alive until they finish.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly AppPaths _paths;
    private readonly FilterStore _store;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly Func<string> _customRules;
    private readonly Func<IReadOnlyCollection<string>> _disabledLists;
    private AdblockEngine? _engine;

    /// <param name="customRules">Gives the user's own filter rules (may be empty). Read at every load.</param>
    /// <param name="disabledLists">Gives the file names of the lists the user turned off (may be empty). Read at every load.</param>
    public EngineHost(AppPaths paths, FilterStore store, Func<string>? customRules = null, Func<IReadOnlyCollection<string>>? disabledLists = null)
    {
        _paths = paths;
        _store = store;
        _customRules = customRules ?? (() => "");
        _disabledLists = disabledLists ?? (() => Array.Empty<string>());
    }

    /// <summary>Engine in use, or null before the first load.</summary>
    public AdblockEngine? Current => Volatile.Read(ref _engine);

    public string? LoadedVersion { get; private set; }

    /// <summary>The release folder that was loaded (without the user's rules).</summary>
    public string? LoadedDir { get; private set; }

    /// <summary>The user's rules that were part of the last load.</summary>
    public string LoadedCustomRules { get; private set; } = "";

    /// <summary>The lists that were left out of the last load, as <see cref="ComposedRelease.DisabledKey"/> writes them.</summary>
    public string LoadedDisabledKey { get; private set; } = "";

    public bool LoadedFromCache { get; private set; }

    public TimeSpan LastLoadTime { get; private set; }

    public event EventHandler? EngineChanged;

    /// <summary>
    /// Loads the current release again, e.g. after the user's rules or the choice of lists changed. Does nothing when the
    /// engine in use already is what the settings ask for (several quick changes then cost one load, not one each).
    /// </summary>
    public Task ReloadAsync(CancellationToken ct = default) =>
        LoadedDir is { } dir ? LoadAsync(dir, ct, onlyIfChanged: true) : Task.CompletedTask;

    public async Task LoadAsync(string releaseDir, CancellationToken ct = default, bool onlyIfChanged = false)
    {
        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var custom = _customRules() ?? "";
            var disabled = _disabledLists() ?? Array.Empty<string>();
            var disabledKey = ComposedRelease.DisabledKey(disabled);

            if (onlyIfChanged)
            {
                // A newer release may have been loaded while this call waited: that is the one to reload.
                if (LoadedDir is { } current) releaseDir = current;
                if (_engine is not null && LoadedDir == releaseDir && LoadedCustomRules == custom && LoadedDisabledKey == disabledKey) return;
            }

            var loadDir = releaseDir;
            if (!string.IsNullOrWhiteSpace(custom) || disabledKey.Length > 0)
            {
                try
                {
                    loadDir = await Task.Run(() => ComposedRelease.Build(releaseDir, custom, Path.Combine(_paths.CacheDir, "composed"), disabled), ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // The filters still work without the user's rules and choices.
                    Log.Error($"custom rules and list choices could not be applied: {ex.Message}");
                    custom = "";
                    disabledKey = "";
                }
            }

            var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(loadDir, "manifest.json"), ct).ConfigureAwait(false);
            var key = Convert.ToHexString(SHA256.HashData(manifestBytes));
            var cachePath = Path.Combine(_paths.CacheDir, "engine.cache");

            var started = DateTime.UtcNow;
            var fromCache = false;
            var engine = await Task.Run(() => AdblockEngine.Load(loadDir, cachePath, key, out fromCache), ct).ConfigureAwait(false);
            LastLoadTime = DateTime.UtcNow - started;

            var version = _store.ReadManifest(releaseDir)?.Version;
            var old = Interlocked.Exchange(ref _engine, engine);
            LoadedVersion = version;
            LoadedDir = releaseDir;
            LoadedCustomRules = custom;
            LoadedDisabledKey = disabledKey;
            LoadedFromCache = fromCache;
            old?.Dispose();

            Log.Info($"engine loaded from {releaseDir} (version {version}, {(string.IsNullOrWhiteSpace(custom) ? "no custom rules" : "with custom rules")}, {(disabledKey.Length == 0 ? "all lists on" : disabledKey.Split('|').Length + " list(s) off")}, {(fromCache ? "cache" : "lists")}, {LastLoadTime.TotalSeconds:F1}s)");
            EngineChanged?.Invoke(this, EventArgs.Empty);

            if (!fromCache)
            {
                // Next start can then skip parsing the lists. Failure only costs start-up time.
                _ = Task.Run(() =>
                {
                    try
                    {
                        engine.Save(cachePath, key);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"engine cache not written: {ex.Message}");
                    }
                });
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _engine, null)?.Dispose();
        _loadGate.Dispose();
    }
}
