using System.Runtime.Versioning;
using PlutonShield.Core.Logging;

namespace PlutonShield.Core.Runtime;

/// <summary>Starts the app when the user signs in to Windows (HKCU Run key; no administrator rights needed).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Windows keeps the "turned off in Settings > Apps > Startup" choice here; a Run entry listed there as off never starts.
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "PlutonShield";
    // The name of the Run entry before the rename: removed whenever the entry is written or checked.
    private const string LegacyValueName = "NeatShield";

    /// <summary>The command Windows runs at sign-in. --background: the app may go straight to the tray.</summary>
    public static string CommandFor(string exePath) => $"\"{exePath}\" --background";

    /// <param name="exePath">Full path of the app.</param>
    /// <param name="clearWindowsOff">
    /// True when the user has just turned the option on in PlutonShield: an earlier "off" in Windows' own Startup list is then
    /// undone, because they asked for it now. False for a silent check, which respects what the user set in Windows.
    /// </param>
    public static void Set(bool enabled, string exePath, bool clearWindowsOff = false)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            SetWindows(enabled, exePath, clearWindowsOff);
        }
        catch (Exception ex)
        {
            Log.Warn($"start-with-Windows could not be changed: {ex.Message}");
        }
    }

    /// <summary>
    /// Makes the Windows entry match the setting, at every start of the app. The entry holds the path of the copy that
    /// was running when the option was switched on: after a reinstall, an update to another folder or a move of the zip
    /// that path is wrong and Windows silently starts nothing.
    /// </summary>
    public static void Sync(bool enabled, string exePath)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (SyncWindows(enabled, exePath)) Log.Info($"start-with-Windows entry updated (enabled={enabled})");
        }
        catch (Exception ex)
        {
            Log.Warn($"start-with-Windows could not be checked: {ex.Message}");
        }
    }

    public static bool IsSet() => OperatingSystem.IsWindows() && IsSetWindows();

    /// <summary>One line for the diagnostics: the command Windows runs, and whether that is usable.</summary>
    public static string Describe()
    {
        if (!OperatingSystem.IsWindows()) return "n/a";
        try
        {
            return DescribeWindows();
        }
        catch (Exception ex)
        {
            return "unreadable: " + ex.Message;
        }
    }

    internal static string? ExeOf(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return null;
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        var space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindows(bool enabled, string exePath, bool clearWindowsOff)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        if (enabled)
        {
            key.SetValue(ValueName, CommandFor(exePath));
            if (clearWindowsOff)
            {
                using var approved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool SyncWindows(bool enabled, string exePath)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

        var changed = false;
        if (key.GetValue(LegacyValueName) is not null)
        {
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            changed = true;
        }

        var current = key.GetValue(ValueName) as string;
        if (enabled)
        {
            var wanted = CommandFor(exePath);
            if (string.Equals(current, wanted, StringComparison.Ordinal)) return changed;
            key.SetValue(ValueName, wanted);
            return true;
        }

        if (current is null) return changed;
        key.DeleteValue(ValueName, throwOnMissingValue: false);
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsSetWindows()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    [SupportedOSPlatform("windows")]
    private static string DescribeWindows()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(ValueName) is not string command) return "no startup entry";

        var notes = new List<string>();
        if (ExeOf(command) is { } exe && !File.Exists(exe)) notes.Add("FILE NOT FOUND");
        using var approved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedKey);
        if (approved?.GetValue(ValueName) is byte[] { Length: > 0 } flag && (flag[0] & 1) == 1) notes.Add("TURNED OFF in Windows Settings > Apps > Startup");
        return command + (notes.Count == 0 ? "" : "  [" + string.Join("; ", notes) + "]");
    }
}
