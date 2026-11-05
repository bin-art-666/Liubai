namespace Liubai;

internal static class ToolbarOrder
{
    internal static readonly string[] Defaults = ["screenshot", "qr", "clock", "todo", "note", "station", "settings"];
    internal static string[] Normalize(IEnumerable<string>? saved)
    {
        var known = Defaults.ToHashSet(StringComparer.Ordinal);
        return (saved ?? []).Where(id => id != null && known.Contains(id)).Distinct(StringComparer.Ordinal)
            .Concat(Defaults).Distinct(StringComparer.Ordinal).ToArray();
    }
    internal static string[] Move(IEnumerable<string> order, string source, int insertionIndex)
    {
        var current = Normalize(order);
        if (!current.Contains(source, StringComparer.Ordinal)) return current;
        var others = current.Where(id => id != source).ToList();
        others.Insert(Math.Clamp(insertionIndex, 0, others.Count), source); return others.ToArray();
    }
}
