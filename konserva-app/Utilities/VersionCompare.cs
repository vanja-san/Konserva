namespace Konserva.Utilities;

/// <summary>
/// Comparison of loader versions by numeric components.
/// </summary>
public static class VersionCompare
{
    /// <summary>
    /// Compares loader versions by numeric components
    /// ("0.19.4" &lt; "0.19.5"; Paper builds like "492 (ALPHA)" — by the leading number).
    /// Returns a negative, zero or positive value.
    /// </summary>
    public static int CompareLoaderVersions(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return 0;

        var aParts = a.Split('.');
        var bParts = b.Split('.');
        var len = Math.Max(aParts.Length, bParts.Length);

        for (var i = 0; i < len; i++)
        {
            var aPart = i < aParts.Length ? aParts[i] : "0";
            var bPart = i < bParts.Length ? bParts[i] : "0";

            // Numeric prefix of the part (up to the space) — e.g. "492 (ALPHA)"
            var aNumStr = aPart.Split(' ')[0];
            var bNumStr = bPart.Split(' ')[0];

            if (int.TryParse(aNumStr, out var aNum) && int.TryParse(bNumStr, out var bNum))
            {
                if (aNum != bNum)
                    return aNum.CompareTo(bNum);
            }
            else
            {
                var cmp = string.Compare(aPart, bPart, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0)
                    return cmp;
            }
        }

        return 0;
    }
}