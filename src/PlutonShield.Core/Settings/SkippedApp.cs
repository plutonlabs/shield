namespace PlutonShield.Core.Settings;

/// <summary>A program that Pluton Shield leaves alone: no HTTPS opening, no blocking, no SafeSearch. Matched by file name.</summary>
public sealed class SkippedApp
{
    /// <summary>Lower-case file name, for example "discord.exe".</summary>
    public string Exe { get; set; } = "";

    /// <summary>Where it was last seen running from (only for showing it; it is not matched on).</summary>
    public string Path { get; set; } = "";
}
