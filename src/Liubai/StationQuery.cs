namespace Liubai;

internal enum StationCategory { All, Images, Files, Folders, Text, Links }
internal enum StationPeriod { All, Today, Last7Days, Last30Days }
internal sealed record StationQuery(StationCategory Category = StationCategory.All, StationPeriod Period = StationPeriod.All, string Search = "")
{
    internal bool Filtered => Category != StationCategory.All || Period != StationPeriod.All || !string.IsNullOrWhiteSpace(Search);
    internal static readonly string[] CategoryNames = ["全部", "图片", "文件", "文件夹", "文字", "链接"];
    internal static readonly string[] PeriodNames = ["全部时间", "今天", "最近 7 天", "最近 30 天"];
    static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".avif", ".heic", ".heif", ".svg" };
    internal static StationCategory Classify(StationItem item) => item.Kind switch
    {
        StationKind.File => Images.Contains(Path.GetExtension(item.Value)) ? StationCategory.Images : StationCategory.Files,
        StationKind.Folder => StationCategory.Folders, StationKind.Text => StationCategory.Text, _ => StationCategory.Links
    };
    internal static void Validate(StationQuery query)
    {
        if (!Enum.IsDefined(query.Category) || !Enum.IsDefined(query.Period) || query.Search is null || query.Search.Length > 200 || query.Search.Any(char.IsControl))
            throw new InvalidDataException("中转站筛选设置无效。");
    }
    internal bool Matches(StationItem item, DateTimeOffset now, TimeZoneInfo zone, bool category = true)
    {
        if (category && Category != StationCategory.All && Classify(item) != Category) return false;
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var day = TimeZoneInfo.ConvertTime(item.Created, zone).Date;
        int days = Period switch { StationPeriod.Today => 1, StationPeriod.Last7Days => 7, StationPeriod.Last30Days => 30, _ => 0 };
        if (days > 0 && (day > today || day < today.AddDays(1 - days))) return false;
        string needle = Search.Trim();
        if (needle.Length == 0) return true;
        string text = item.Kind is StationKind.File or StationKind.Folder ? Path.GetFileName(item.Value) is { Length: > 0 } name ? name : item.Value : item.Value;
        return text.Contains(needle, StringComparison.OrdinalIgnoreCase) || item.Title.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
    internal StationItem[] Apply(IEnumerable<StationItem> items, DateTimeOffset now, TimeZoneInfo zone)
        => items.Where(i => Matches(i, now, zone)).OrderByDescending(i => i.Created).ThenByDescending(i => i.Id).ToArray();
    internal static string Group(DateTimeOffset created, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = TimeZoneInfo.ConvertTime(now, zone).Date; var day = TimeZoneInfo.ConvertTime(created, zone).Date;
        return day == today ? "今天" : day == today.AddDays(-1) ? "昨天" : day < today ? "更早" : "其他日期";
    }
    internal static string TimeLabel(DateTimeOffset created, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(created, zone); string group = Group(created, now, zone);
        return group is "今天" or "昨天" ? $"{group} {local:HH:mm}" : $"{local:yyyy-MM-dd HH:mm}";
    }
    internal string Description => CategoryNames[(int)Category] + " · " + PeriodNames[(int)Period] + (Search.Trim().Length > 0 ? " · 搜索“" + Search.Trim() + "”" : "");
}
