namespace Konserva.Utilities;

/// <summary>
/// Сравнение версий загрузчиков по числовым компонентам.
/// </summary>
public static class VersionCompare
{
    /// <summary>
    /// Сравнивает версии загрузчика по числовым компонентам
    /// ("0.19.4" &lt; "0.19.5"; сборки Paper "492 (ALPHA)" — по ведущему числу).
    /// Возвращает отрицательное, ноль или положительное значение.
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

            // Числовой префикс части (до пробела) — напр. "492 (ALPHA)"
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