using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Liubai;

internal sealed record ArchivedFile(string Path, long Size, string Sha256);
internal sealed record ArchivedItem(StationItem Item, string Path, ArchivedFile[] Files);
internal sealed record ArchiveFailure(Guid Id, string Title, string Error);
internal sealed record StationArchiveResult(string Directory, bool Canceled, ArchivedItem[] Saved, ArchiveFailure[] Failed);
internal sealed record ArchiveProgress(int Completed, int Total, string Name);

internal static class StationArchive
{
    internal static async Task<StationArchiveResult> Save(IReadOnlyList<StationItem> items, string destination, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellation = default)
    {
        if (items.Count == 0) throw new InvalidDataException("中转站没有可归档的项目。");
        if (!Path.IsPathFullyQualified(destination)) throw new ArgumentException("请选择归档目录。");
        string name = $"中转站归档_{DateTime.Now:yyyy-MM-dd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
        string root = Path.Combine(Path.GetFullPath(destination), "未完成_" + name);
        Directory.CreateDirectory(Path.Combine(root, "项目"));
        var saved = new List<ArchivedItem>(); var failed = new List<ArchiveFailure>(); bool canceled = false;
        await Manifest(root, "未完成归档", items, saved, failed);
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            try
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report(new(index, items.Count, Title(item)));
                var entry = await SaveItem(item, index, root, cancellation);
                saved.Add(entry);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { canceled = true; break; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { failed.Add(new(item.Id, Title(item), ex.Message)); }
            progress?.Report(new(index + 1, items.Count, Title(item)));
        }
        canceled |= cancellation.IsCancellationRequested;
        await Manifest(root, canceled ? "未完成归档（已取消，原记录保留）" : failed.Count > 0 ? "部分完成" : "归档完成", items, saved, failed);
        if (!canceled)
        {
            string complete = Path.Combine(Path.GetFullPath(destination), name);
            Directory.Move(root, complete); root = complete;
        }
        return new(root, canceled, saved.ToArray(), failed.ToArray());
    }
    static string Title(StationItem item) => item.Kind is StationKind.File or StationKind.Folder
        ? Path.GetFileName(Path.TrimEndingDirectorySeparator(item.Value)) : item.Title.Length > 0 ? item.Title : item.Kind == StationKind.Link ? new Uri(item.Value).Host : "文字";
    static string SafeName(string name)
    {
        string clean = string.Concat(name.Select(c => char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (clean.Length == 0) return "项目";
        if (clean.Length <= 120) return clean;
        string extension = Path.GetExtension(clean); if (extension.Length > 20) extension = "";
        return clean[..(120 - extension.Length)] + extension;
    }
    static async Task<ArchivedItem> SaveItem(StationItem item, int index, string root, CancellationToken token)
    {
        string name = $"{index + 1:D4}_" + SafeName(Title(item));
        if (item.Kind == StationKind.Text) name += ".txt";
        if (item.Kind == StationKind.Link) name += ".url";
        string relative = Path.Combine("项目", name), target = Path.Combine(root, relative);
        string pending = Path.Combine(root, "项目", "未完成_" + name);
        var files = new List<ArchivedFile>();
        if (item.Kind == StationKind.Folder)
        {
            string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.Value));
            string canonicalSource = CanonicalDirectory(source), canonicalRoot = CanonicalDirectory(root);
            string sourcePrefix = Path.EndsInDirectorySeparator(canonicalSource) ? canonicalSource : canonicalSource + Path.DirectorySeparatorChar;
            if (canonicalRoot.Equals(canonicalSource, StringComparison.OrdinalIgnoreCase) || canonicalRoot.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("归档位置位于源文件夹内，请选择其他目录。");
            await CopyDirectory(source, pending, "", files, token);
            token.ThrowIfCancellationRequested(); Directory.Move(pending, target);
        }
        else
        {
            ArchivedFile copied;
            if (item.Kind == StationKind.File) copied = await CopyFile(item.Value, pending, "", token);
            else
            {
                byte[] bytes = Encoding.UTF8.GetBytes(item.Kind == StationKind.Link ? "[InternetShortcut]\r\nURL=" + item.Value + "\r\n" : item.Value);
                await File.WriteAllBytesAsync(pending, bytes, token);
                await using var verify = File.OpenRead(pending);
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(verify, token)).ToLowerInvariant();
                if (hash != Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()) throw new IOException("归档内容校验失败。");
                copied = new("", bytes.Length, hash);
            }
            token.ThrowIfCancellationRequested(); File.Move(pending, target); files.Add(copied);
        }
        return new(item, relative, files.ToArray());
    }
    static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("该项包含链接或重解析目录，未自动跟随；原记录保留。");
    }
    static string CanonicalDirectory(string path)
    {
        string full = Path.GetFullPath(path), root = Path.GetPathRoot(full)!;
        string current = root;
        foreach (string component in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (Directory.Exists(current) && new DirectoryInfo(current).ResolveLinkTarget(true) is { } target) current = target.FullName;
        }
        if (current.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) current = @"\\" + current[8..];
        else if (current.StartsWith(@"\\?\", StringComparison.Ordinal)) current = current[4..];
        return Path.TrimEndingDirectorySeparator(current);
    }
    static async Task CopyDirectory(string source, string target, string relative, List<ArchivedFile> files, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); RejectLink(source); Directory.CreateDirectory(target);
        foreach (string child in Directory.EnumerateFileSystemEntries(source))
        {
            token.ThrowIfCancellationRequested(); RejectLink(child);
            string name = Path.GetFileName(child), next = Path.Combine(relative, name), destination = Path.Combine(target, name);
            if (Directory.Exists(child)) await CopyDirectory(child, destination, next, files, token);
            else files.Add(await CopyFile(child, destination, next, token));
        }
    }
    static async Task<ArchivedFile> CopyFile(string source, string target, string relative, CancellationToken token)
    {
        RejectLink(source);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            byte[] buffer = new byte[81920]; int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            { hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), token); size += read; }
            await output.FlushAsync(token); output.Flush(true);
        }
        string expected = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        await using var verify = File.OpenRead(target);
        if (verify.Length != size || Convert.ToHexString(await SHA256.HashDataAsync(verify, token)).ToLowerInvariant() != expected) throw new IOException("文件复制后的校验失败，原记录保留。");
        return new(relative, size, expected);
    }
    static async Task Manifest(string root, string status, IReadOnlyList<StationItem> source, List<ArchivedItem> saved, List<ArchiveFailure> failed)
    {
        var document = new { Version = 1, Status = status, Created = DateTimeOffset.Now, Total = source.Count,
            Items = saved.Select(s => new { s.Item.Id, s.Item.Kind, s.Item.Title, s.Item.Created, Source = s.Item.Kind == StationKind.Text ? null : s.Item.Value, s.Path, s.Files }), Failed = failed };
        string path = Path.Combine(root, "index.json");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.Move(path + ".tmp", path, true);
        var text = new StringBuilder().AppendLine(status).AppendLine($"共 {source.Count} 项，成功 {saved.Count} 项，失败 {failed.Count} 项").AppendLine();
        foreach (var entry in saved) text.AppendLine(entry.Path + " · " + entry.Item.Created.ToString("yyyy-MM-dd HH:mm:ss"));
        foreach (var failure in failed) text.AppendLine("未完成：" + failure.Title + " — " + failure.Error);
        await File.WriteAllTextAsync(Path.Combine(root, "归档目录.txt"), text.ToString(), Encoding.UTF8);
    }
}
