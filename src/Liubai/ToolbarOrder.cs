namespace Liubai;

internal static class ToolbarOrder
{
    internal static readonly string[] Defaults = ["screenshot", "qr", "clock", "todo", "note", "station", "guard", "privacy", "ai", "settings"];
    internal static string[] Normalize(IEnumerable<string>? saved)
    {
        var known = Defaults.ToHashSet(StringComparer.Ordinal);
        var order = (saved ?? []).Where(id => id != null && known.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
        bool hadGuard = order.Contains("guard"), hadPrivacy = order.Contains("privacy"), hadAi = order.Contains("ai");
        foreach (string id in Defaults.Where(id => id is not ("guard" or "privacy" or "ai"))) if (!order.Contains(id)) order.Add(id);
        if (!hadGuard) order.Insert(order.IndexOf("settings"), "guard");
        if (!hadPrivacy) order.Insert(order.IndexOf("settings"), "privacy");
        if (!hadAi) order.Insert(order.IndexOf("settings"), "ai");
        return order.ToArray();
    }
    internal static string[] Move(IEnumerable<string> order, string source, int insertionIndex)
    {
        var current = Normalize(order);
        if (!current.Contains(source, StringComparer.Ordinal)) return current;
        var others = current.Where(id => id != source).ToList();
        others.Insert(Math.Clamp(insertionIndex, 0, others.Count), source); return others.ToArray();
    }
}
