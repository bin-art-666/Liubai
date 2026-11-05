using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Liubai;

internal sealed record AiEndpoint(string Name, string Url)
{
    internal string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Url)))[..24];
}
internal sealed record AiBounds(int X, int Y, int Width, int Height);
internal sealed record AiOptions(int Version = 1, string Provider = "deepseek", string CustomName = "自定义 AI", string CustomUrl = "", bool Pinned = false,
    Dictionary<string, AiBounds>? Layouts = null, double DockWidth = 430, double DockHeight = 660, double DockTopOffset = 0);
internal static class AiPolicy
{
    internal static readonly string[] Providers = ["deepseek", "doubao", "xiaoyi", "chatgpt", "custom"];
    internal static readonly string[] Names = ["DeepSeek", "豆包", "小艺", "ChatGPT", "自定义 AI"];
    internal static IEnumerable<string> BuiltInProviders => Providers.Where(id => id != "custom");
    internal const double DefaultDockWidth = 430, MinimumDockWidth = 360, MaximumDockWidth = 1000;
    internal const double DefaultDockHeight = 660, MinimumDockHeight = 360, MaximumDockHeight = 1200;
    internal static int DockWidthPixels(double preferred, int workWidth, uint dpi)
        => Math.Max(1, Math.Min(workWidth, (int)Math.Round(Math.Clamp(preferred, MinimumDockWidth, MaximumDockWidth) * Math.Max(96, dpi) / 96d)));
    internal static AiBounds DockBounds(double width, double height, double offset, AiBounds work, int anchor, uint dpi)
    {
        double scale = Math.Max(96, dpi) / 96d;
        int minHeight = Math.Min(work.Height, (int)Math.Round(MinimumDockHeight * scale));
        int top = (int)Math.Clamp((long)Math.Max(anchor, work.Y) + (long)Math.Round(offset * scale), work.Y, (long)work.Y + work.Height - minHeight);
        int actualHeight = Math.Min((int)Math.Round(Math.Clamp(height, MinimumDockHeight, MaximumDockHeight) * scale), work.Y + work.Height - top);
        int actualWidth = DockWidthPixels(width, work.Width, dpi);
        return new(work.X + work.Width - actualWidth, top, actualWidth, actualHeight);
    }
    internal static AiEndpoint Resolve(AiOptions value) => value.Provider switch
    {
        "deepseek" => new("DeepSeek", "https://chat.deepseek.com/"),
        "doubao" => new("豆包", "https://www.doubao.com/chat/"),
        "xiaoyi" => new("小艺", "https://xiaoyi.huawei.com/"),
        "chatgpt" => new("ChatGPT", "https://chatgpt.com/"),
        "custom" => new(value.CustomName.Trim(), Https(value.CustomUrl)),
        _ => throw new InvalidDataException("请选择支持的 AI 服务。")
    };
    internal static string Https(string text)
    {
        if (text.Length > 2048 || text.Any(char.IsControl) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 || uri.UserInfo.Length > 0)
            throw new InvalidDataException("请输入完整的 HTTPS 网页地址，不支持带账号密码的地址。");
        return uri.AbsoluteUri;
    }
    internal static void Validate(AiOptions value)
    {
        if (value.Version != 1) throw new NotSupportedException("AI 设置来自更新版本，已停止写入。");
        if (!double.IsFinite(value.DockWidth) || value.DockWidth is < MinimumDockWidth or > MaximumDockWidth
            || !double.IsFinite(value.DockHeight) || value.DockHeight is < MinimumDockHeight or > MaximumDockHeight
            || !double.IsFinite(value.DockTopOffset) || value.DockTopOffset is < 0 or > 20000
            || value.CustomName is null || string.IsNullOrWhiteSpace(value.CustomName) || value.CustomName.Length > 40 || value.CustomName.Any(char.IsControl)
            || value.CustomUrl is null || value.CustomUrl.Length > 2048 || value.CustomUrl.Any(char.IsControl)
            || value.Layouts is { Count: > 32 } || value.Layouts?.Any(p => p.Key.Length != 24 || !p.Key.All(Uri.IsHexDigit) || p.Value is null || p.Value.Width is < 100 or > 20000 || p.Value.Height is < 100 or > 20000 || Math.Abs((long)p.Value.X) > 100000 || Math.Abs((long)p.Value.Y) > 100000) == true)
            throw new InvalidDataException("AI 设置无效，请检查名称、网址和窗口位置。");
        _ = Resolve(value);
        if (value.CustomUrl.Length > 0) _ = Https(value.CustomUrl);
    }
    internal static AiBounds Fit(AiBounds? saved, AiBounds work, uint dpi)
    {
        int width = Math.Min(work.Width, saved?.Width ?? (int)(900 * dpi / 96d));
        int height = Math.Min(work.Height, saved?.Height ?? (int)(760 * dpi / 96d));
        width = Math.Max(1, width); height = Math.Max(1, height);
        return new(Math.Clamp(saved?.X ?? work.X + (work.Width - width) / 2, work.X, work.X + work.Width - width),
            Math.Clamp(saved?.Y ?? work.Y + (work.Height - height) / 2, work.Y, work.Y + work.Height - height), width, height);
    }
    internal static bool AllowedNavigation(string address) => address.Equals("about:blank", StringComparison.OrdinalIgnoreCase)
        || Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
        || address.StartsWith("blob:https://", StringComparison.OrdinalIgnoreCase);
}
internal sealed class AiOptionsStore
{
    readonly string path;
    internal AiOptions Data { get; private set; } = new(Layouts: []);
    internal bool ReadOnly { get; }
    internal string? Warning { get; }
    bool preserveBackup;
    internal event Action? Changed;
    internal AiOptionsStore(string path)
    {
        this.path = path;
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
        try { Data = Read(path); }
        catch (NotSupportedException ex) { ReadOnly = true; Warning = ex.Message; }
        catch
        {
            try { Data = Read(path + ".bak"); preserveBackup = true; Warning = "已从备份恢复 AI 设置。"; }
            catch { ReadOnly = true; Warning = "AI 设置无法读取，原文件已保留，暂用默认服务。"; }
        }
    }
    static AiOptions Read(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException();
        var value = JsonSerializer.Deserialize<AiOptions>(File.ReadAllText(path)) ?? throw new InvalidDataException();
        AiPolicy.Validate(value); return value with { Layouts = value.Layouts ?? [] };
    }
    internal void Save(AiOptions value)
    {
        AiPolicy.Validate(value); if (ReadOnly) throw new IOException(Warning);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, preserveBackup ? null : path + ".bak"); else File.Move(temporary, path);
            Data = value with { Layouts = value.Layouts is null ? [] : new(value.Layouts) }; preserveBackup = false; Changed?.Invoke();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal void SaveBounds(string key, AiBounds bounds)
    {
        if (ReadOnly || Data.Layouts?.GetValueOrDefault(key) == bounds) return;
        var layouts = new Dictionary<string, AiBounds>(Data.Layouts ?? []);
        if (!layouts.ContainsKey(key) && layouts.Count >= 32) layouts.Remove(layouts.Keys.First());
        layouts[key] = bounds; Save(Data with { Layouts = layouts });
    }
}
