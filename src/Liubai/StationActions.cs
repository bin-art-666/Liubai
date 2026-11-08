namespace Liubai;

internal enum StationAction { Open, Copy, Folder, Path, Edit, Group, Remove }
internal sealed record StationActions(bool Grid = false, StationAction[]? Files = null, StationAction[]? Links = null, StationAction[]? Text = null)
{
    internal static StationAction[] Available(StationKind kind) => kind is StationKind.File or StationKind.Folder
        ? [StationAction.Open, StationAction.Copy, StationAction.Folder, StationAction.Path, StationAction.Group, StationAction.Remove]
        : [StationAction.Open, StationAction.Copy, StationAction.Edit, StationAction.Group, StationAction.Remove];
    internal StationAction[] Visible(StationKind kind)
    {
        var allowed = Available(kind);
        var chosen = kind is StationKind.File or StationKind.Folder ? Files : kind == StationKind.Link ? Links : Text;
        return Grid ? allowed : (chosen ?? [StationAction.Open, StationAction.Copy]).Where(allowed.Contains).Distinct().Take(3).ToArray();
    }
    internal void Validate()
    {
        foreach (var values in new[] { Files, Links, Text })
            if (values != null && (values.Length > 3 || values.Any(a => !Enum.IsDefined(a)) || values.Distinct().Count() != values.Length))
                throw new InvalidDataException("项目常用操作应为最多三个不同操作。");
        if (Files?.Except(Available(StationKind.File)).Any() == true || Links?.Except(Available(StationKind.Link)).Any() == true || Text?.Except(Available(StationKind.Text)).Any() == true)
            throw new InvalidDataException("操作不适用于该内容类型。");
    }
    internal static string Label(StationAction action) => action switch {
        StationAction.Open => "打开", StationAction.Copy => "复制", StationAction.Folder => "所在文件夹",
        StationAction.Path => "查看路径", StationAction.Edit => "编辑", StationAction.Group => "加入分组", _ => "移除记录" };
}
