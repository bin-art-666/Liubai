using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Liubai.Modern;

[DataContract]
public class Todo
{
    [DataMember] public string Id = Guid.NewGuid().ToString();
    [DataMember] public string Text = "";
    [DataMember] public bool Done;
}
[DataContract]
public class TodoState
{
    [DataMember] public List<Todo> Items = [];
}

public class TodoStore
{
    public string FilePath { get; }
    public TodoState Data { get; private set; } = new();
    public string? Warning { get; }
    public TodoStore(string path)
    {
        FilePath = path;
        if (!File.Exists(path)) return;
        try { Data = Read(path); }
        catch
        {
            try { File.Copy(path, path + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmssfff")); } catch { }
            try { Data = Read(path + ".bak"); Warning = "已从备份恢复待办。"; }
            catch { Warning = "待办文件无法读取，原文件已保留，请先检查数据文件夹。"; }
        }
    }
    static TodoState Read(string path)
    {
        using var stream = File.OpenRead(path);
        var data = (TodoState?)new DataContractJsonSerializer(typeof(TodoState)).ReadObject(stream);
        if (data?.Items == null || data.Items.Any(x => x == null || x.Text == null)) throw new InvalidDataException();
        return data;
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            new DataContractJsonSerializer(typeof(TodoState)).WriteObject(stream, Data);
            stream.Flush(true);
        }
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
        else File.Move(temporary, FilePath);
    }
}

public class AutoHideState
{
    public bool Visible { get; private set; }
    bool wasAtEdge;
    public void Reveal() { Visible = true; }
    public bool Update(bool atEdge, bool withinSurface, bool interacting = false, bool autoHide = true)
    {
        if (!autoHide) { wasAtEdge = atEdge; Visible = true; return true; }
        if (atEdge && !wasAtEdge && !Visible) Reveal();
        wasAtEdge = atEdge;
        if (Visible && !atEdge && !withinSurface && !interacting) Visible = false;
        return Visible;
    }
}
