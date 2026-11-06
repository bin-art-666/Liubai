using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Liubai;

internal sealed record UpdateRelease(string Tag, Version Version, string Notes, string Page, string Asset, string Download, long Size, string Sha256);
internal static partial class UpdatePolicy
{
    internal const string Repository = "https://github.com/bin-art-666/Liubai";
    internal const string Latest = "https://api.github.com/repos/bin-art-666/Liubai/releases/latest";
    internal const long MaximumDownload = 512L * 1024 * 1024, MaximumExpanded = 2L * 1024 * 1024 * 1024;
    internal static readonly string[] Required = ["Liubai.exe", "Liubai.dll", "Liubai.deps.json", "Liubai.runtimeconfig.json", "resources.pri", "App.xbf"];
    internal static Version VersionOf(string tag)
    {
        if (!Regex.IsMatch(tag, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("只支持正式版本号。");
        if (!Version.TryParse(tag.TrimStart('v'), out var version)) throw new InvalidDataException("版本号超出范围。");
        return version;
    }
    internal static UpdateRelease Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024) throw new InvalidDataException("更新信息过大。");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("该版本不是正式发布。");
        string tag = root.GetProperty("tag_name").GetString()!; var version = VersionOf(tag);
        string page = root.GetProperty("html_url").GetString()!;
        if (page != Repository + "/releases/tag/" + tag) throw new InvalidDataException("发布来源不匹配。");
        string name = $"Liubai-{tag}-win-x64.zip";
        var matches = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("没有唯一的 Windows x64 更新包，请打开发布页查看。");
        var asset = matches[0]; string url = asset.GetProperty("browser_download_url").GetString()!;
        if (url != Repository + "/releases/download/" + tag + "/" + name) throw new InvalidDataException("更新包来源不匹配。");
        long size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaximumDownload) throw new InvalidDataException("更新包大小无效。");
        string digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw new InvalidDataException("发布包缺少 SHA-256 校验，请打开发布页查看。");
        string notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new(tag, version, notes.Length > 30000 ? notes[..30000] : notes, page, name, url, size, digest[7..].ToLowerInvariant());
    }
    internal static bool DownloadHost(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    internal static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    internal static void VerifyPackage(string path, UpdateRelease release)
    {
        if (new FileInfo(path).Length != release.Size || !Hash(path).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新包校验失败，当前程序未更改。");
    }
    internal static string Extract(string zip, string destination, CancellationToken token)
    {
        if (Directory.Exists(destination)) throw new IOException("更新暂存目录已存在。");
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count is 0 or > 20000) throw new InvalidDataException("更新包文件数量无效。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (name.Length == 0 || name.StartsWith('/') || name.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.Any(c => c < 32 || ":*?\"<>|".Contains(c))
                || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
                || !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("更新包包含不安全的文件路径。");
            total = checked(total + entry.Length);
            if (entry.Length > MaximumDownload || total > MaximumExpanded) throw new InvalidDataException("解压内容过大。");
        }
        string[] roots = archive.Entries.Where(e => e.FullName.Replace('\\', '/').EndsWith("Liubai.exe", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName.Replace('\\', '/')).Where(n => Path.GetFileName(n).Equals("Liubai.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (roots.Length != 1 || roots[0].Count(c => c == '/') > 1) throw new InvalidDataException("更新包程序目录无效。");
        string prefix = roots[0][..^"Liubai.exe".Length];
        foreach (string required in Required) if (!names.Contains(prefix + required)) throw new InvalidDataException("更新包不完整：" + required);
        Directory.CreateDirectory(destination);
        try
        {
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested(); string name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.EndsWith('/')) continue;
                string path = Path.GetFullPath(Path.Combine(destination, name[prefix.Length..]));
                if (!path.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包路径越界。");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew);
                var buffer = new byte[65536]; int length;
                while ((length = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, length); }
            }
            ValidateRuntime(destination); return destination;
        }
        catch { Directory.Delete(destination, true); throw; }
    }
    internal static void ValidateRuntime(string path)
    {
        foreach (string file in Required) if (!File.Exists(Path.Combine(path, file))) throw new InvalidDataException("程序目录不完整：" + file);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)
            .Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)) throw new InvalidDataException("程序目录包含链接。");
    }
}

internal sealed record UpdatePlan(int Version, string Id, string Mode, string InstallRoot, string DataRoot, string Stage,
    int ParentPid, string ParentStart, string ExeSha256, string TargetVersion, bool Isolated, string SourceVersion, Dictionary<string, string> Hashes);
internal sealed class UpdateLayout
{
    internal string Install { get; }
    internal string Root { get; }
    internal bool Supported { get; }
    internal string Previous => Path.Combine(Root, "previous");
    internal string SettingsBackup => Path.Combine(Root, "previous-settings");
    internal UpdateLayout(string install)
    {
        Install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
        string? parent = Path.GetDirectoryName(Install); Supported = parent != null;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Install.ToUpperInvariant())))[..12].ToLowerInvariant();
        Root = Path.Combine(parent ?? Install, ".liubai-update-" + key);
    }
    internal void EnsureOwned()
    {
        if (!Supported) throw new IOException("请先将留白放入独立文件夹，再使用应用内更新。");
        UpdatePolicy.ValidateRuntime(Install);
        string owner = Path.Combine(Root, "owner.json");
        if (Directory.Exists(Root))
        {
            if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0 || !File.Exists(owner)
                || JsonSerializer.Deserialize<string>(File.ReadAllText(owner)) != Install) throw new IOException("更新目录不属于当前程序。");
        }
        else { Directory.CreateDirectory(Root); File.WriteAllText(owner, JsonSerializer.Serialize(Install)); }
    }
    internal static void Snapshot(string data, string destination)
    {
        if (Directory.Exists(destination)) throw new IOException("备份目录已存在。");
        Directory.CreateDirectory(destination);
        try
        {
            if (!Directory.Exists(data)) return;
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(data).Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase)))
            {
                var info = new FileInfo(file); total += info.Length;
                if (info.Length > 256L * 1024 * 1024 || total > 1024L * 1024 * 1024 || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("配置备份超出限制或包含链接。");
                File.Copy(file, Path.Combine(destination, info.Name), false);
            }
        }
        catch { Directory.Delete(destination, true); throw; }
    }
}
