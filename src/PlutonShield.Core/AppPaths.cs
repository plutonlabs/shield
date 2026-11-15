namespace PlutonShield.Core;

/// <summary>Where PlutonShield keeps its files.</summary>
public sealed class AppPaths
{
    /// <param name="root">Data folder. Default: %LOCALAPPDATA%\PlutonShield (data of the old NeatShield name is moved there once).</param>
    /// <param name="bundledFilters">Folder with a neat-filters release shipped next to the app (first-run / offline fallback).</param>
    public AppPaths(string? root = null, string? bundledFilters = null)
    {
        if (root is null)
        {
            Root = DefaultRoot;
            MigrateLegacyData(Root);
        }
        else
        {
            Root = root;
        }

        BundledFiltersDir = bundledFilters ?? Path.Combine(AppContext.BaseDirectory, "filters");
        foreach (var dir in new[] { Root, FiltersDir, CacheDir, CertDir, LogDir })
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>The data folder of the app: %LOCALAPPDATA%\PlutonShield.</summary>
    public static string DefaultRoot => Path.Combine(LocalAppData, "PlutonShield");

    /// <summary>The data folder the app used under its old name, NeatShield.</summary>
    public static string LegacyRoot => Path.Combine(LocalAppData, "NeatShield");

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string Root { get; }

    /// <summary>Downloaded releases: filters\releases\&lt;version&gt;\ and filters\current.txt.</summary>
    public string FiltersDir => Path.Combine(Root, "filters");

    public string CacheDir => Path.Combine(Root, "cache");

    public string CertDir => Path.Combine(Root, "cert");

    public string LogDir => Path.Combine(Root, "logs");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string StatsFile => Path.Combine(Root, "stats.json");

    /// <summary>The user's own proxy settings, saved while PlutonShield has the system proxy.</summary>
    public string ProxyBackupFile => Path.Combine(Root, "proxy-backup.json");

    /// <summary>Public part of the root CA, for browsers that keep their own certificate list (Firefox).</summary>
    public string CaCertFile => Path.Combine(CertDir, "PlutonShield-Root-CA.cer");

    public string CaKeyFile => Path.Combine(CertDir, "plutonshield-ca.pfx");

    public string BundledFiltersDir { get; }

    /// <summary>
    /// First start after the rename: moves the old NeatShield folder (settings, filters, statistics) to the new one.
    /// The root certificate is not kept, because it has a new name: a new one is made on the first start.
    /// </summary>
    private static void MigrateLegacyData(string newRoot)
    {
        try
        {
            if (Directory.Exists(newRoot) || !Directory.Exists(LegacyRoot)) return;

            Directory.Move(LegacyRoot, newRoot);

            var certDir = Path.Combine(newRoot, "cert");
            if (Directory.Exists(certDir)) Directory.Delete(certDir, recursive: true);
        }
        catch (Exception)
        {
            // The old copy may still be running and hold its files: the app then starts with a new, empty folder.
        }
    }
}
