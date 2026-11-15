using System.Reflection;

namespace PlutonShield.Core.Update;

/// <summary>The version of the running app. tools\build.ps1 stamps it from the release tag (v0.4.0 gives 0.4.0).</summary>
public static class AppVersion
{
    /// <summary>
    /// Environment variable for trying the update feature without a real release: <c>PLUTONSHIELD_FAKE_VERSION=0.0.1</c>
    /// makes the app believe it is that old, so the newest release counts as an update.
    /// </summary>
    public const string FakeVersionVariable = "PLUTONSHIELD_FAKE_VERSION";

    public static SemVer Current
    {
        get
        {
            if (SemVer.TryParse(Environment.GetEnvironmentVariable(FakeVersionVariable), out var fake)) return fake;

            var assembly = typeof(AppVersion).Assembly;
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (SemVer.TryParse(info, out var v)) return v;

            var av = assembly.GetName().Version;
            return new SemVer(av?.Major ?? 0, av?.Minor ?? 0, Math.Max(av?.Build ?? 0, 0), "");
        }
    }
}
