namespace Liubai;

internal enum ToolKind { Action, Panel, Toggle }
[Flags] internal enum ToolCapabilities { None = 0, Drop = 1, Peek = 2, Menu = 4 }
internal sealed record ToolDefinition(string Id, string Name, ToolKind Kind, ToolCapabilities Capabilities = ToolCapabilities.None,
    string? PanelGroup = null, bool RequiredEntry = false, string? DropCaption = null)
{
    internal bool Has(ToolCapabilities value) => (Capabilities & value) == value;
}

// Compiled-in metadata only. Descriptions do not construct a service or a window.
internal sealed class ToolCatalog
{
    readonly Dictionary<string, ToolDefinition> byId;
    internal IReadOnlyList<ToolDefinition> Items { get; }
    internal ToolDefinition this[string id] => byId[id];
    internal bool Contains(string id) => byId.ContainsKey(id);
    internal ToolCatalog(IEnumerable<ToolDefinition> definitions)
    {
        var items = definitions.ToArray(); byId = new(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 40 || item.Id.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
                || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 80 || !Enum.IsDefined(item.Kind)
                || (item.Capabilities & ~(ToolCapabilities.Drop | ToolCapabilities.Peek | ToolCapabilities.Menu)) != 0
                || item.Kind == ToolKind.Panel && string.IsNullOrWhiteSpace(item.PanelGroup)
                || item.RequiredEntry && item.Kind != ToolKind.Panel
                || item.Has(ToolCapabilities.Drop) && string.IsNullOrWhiteSpace(item.DropCaption)
                || !byId.TryAdd(item.Id, item)) throw new InvalidDataException("工具定义无效或标识重复。");
        }
        if (items.Length == 0) throw new InvalidDataException("工具目录不能为空。");
        Items = Array.AsReadOnly(items);
    }
    internal static ToolCatalog BuiltIn { get; } = new([
        new("screenshot", "截图", ToolKind.Action, ToolCapabilities.Menu),
        new("qr", "扫码", ToolKind.Action),
        new("clock", "时钟", ToolKind.Panel, ToolCapabilities.Peek, "clock"),
        new("todo", "待办", ToolKind.Panel, ToolCapabilities.Drop | ToolCapabilities.Peek, "records", DropCaption: "文字添加到待办"),
        new("note", "随手记", ToolKind.Panel, ToolCapabilities.Drop | ToolCapabilities.Peek, "records", DropCaption: "保存为图文记录"),
        new("station", "中转站", ToolKind.Panel, ToolCapabilities.Drop, "station", DropCaption: "存入中转站"),
        new("guard", "屏幕守护", ToolKind.Toggle, ToolCapabilities.Menu),
        new("privacy", "一键隐私", ToolKind.Toggle, ToolCapabilities.Menu),
        new("ai", "AI 快捷窗", ToolKind.Panel, ToolCapabilities.Drop | ToolCapabilities.Menu, "ai", DropCaption: "暂存到 AI，确认后再交给网站"),
        new("settings", "设置", ToolKind.Panel, PanelGroup: "settings", RequiredEntry: true)
    ]);
}
