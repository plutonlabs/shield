using PlutonShield.Core.Logging;
using PlutonShield.Core.Security;

namespace PlutonShield.Core.Runtime;

/// <summary>
/// What the uninstaller asks for (<c>PlutonShield.exe --cleanup</c>): give back everything PlutonShield changed outside its
/// own folders. The app must be closed first (<c>--quit</c>), which already restores the proxy; this is the second line of
/// defence for a copy that was killed, plus the part only an uninstall needs: the trusted root certificate.
/// </summary>
public static class UninstallCleanup
{
    /// <returns>What was done, for the log and for tests.</returns>
    public static IReadOnlyList<string> Run(string? dataRoot = null)
    {
        var done = new List<string>();
        try
        {
            using var runtime = new ShieldRuntime(dataRoot);

            if (runtime.SystemProxy.IsOurs())
            {
                runtime.SystemProxy.Restore();
                done.Add("Windows proxy setting restored");
            }

            var removed = CertificateService.RemoveAllTrusted();
            if (removed > 0) done.Add($"{removed} PlutonShield certificate(s) removed from the trusted list");
        }
        catch (Exception ex)
        {
            done.Add("cleanup failed: " + ex.Message);
        }

        foreach (var line in done) Log.Info("uninstall: " + line);
        return done;
    }
}
