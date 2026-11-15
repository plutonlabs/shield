using System.Globalization;
using System.Text.RegularExpressions;

namespace PlutonShield.Core.Update;

/// <summary>A version such as 0.4.0 or 0.4.0-rc.1 (semantic versioning). A pre-release is older than the release it leads to.</summary>
public readonly partial record struct SemVer(int Major, int Minor, int Patch, string Pre) : IComparable<SemVer>
{
    public bool IsPrerelease => Pre.Length > 0;

    /// <summary>Reads "0.4.0", "v0.4.0", "0.4.0-rc.1" and "0.4.0+build" (the build part is ignored).</summary>
    public static bool TryParse(string? text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Pattern().Match(text.Trim());
        if (!m.Success) return false;
        version = new SemVer(
            int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            m.Groups[4].Success ? m.Groups[4].Value : "");
        return true;
    }

    public int CompareTo(SemVer other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // 0.4.0 is newer than 0.4.0-rc.1.
        if (Pre.Length == 0 || other.Pre.Length == 0) return (Pre.Length == 0 ? 1 : 0) - (other.Pre.Length == 0 ? 1 : 0);

        var a = Pre.Split('.');
        var b = other.Pre.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNum = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var an);
            var bNum = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bn);
            c = aNum && bNum ? an.CompareTo(bn)
                : aNum ? -1 // a number comes before a word
                : bNum ? 1
                : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c < 0 ? -1 : 1;
        }

        return a.Length.CompareTo(b.Length);
    }

    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;

    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;

    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;

    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre.Length > 0 ? "-" + Pre : "");

    [GeneratedRegex(@"^v?(\d{1,9})\.(\d{1,9})\.(\d{1,9})(?:-([0-9A-Za-z][0-9A-Za-z.-]*))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex Pattern();
}
