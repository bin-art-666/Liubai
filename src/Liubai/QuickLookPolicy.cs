namespace Liubai;

internal static class QuickLookPolicy
{
    internal const int MaximumCharacters=20000;
    internal static bool TextFile(string path)=>new[]{".txt",".md",".csv",".json",".xml",".log",".cs",".cpp",".c",".h",".py",".yaml",".yml",".ini",".html",".css",".js",".ts",".sql"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    internal static async Task<(string Text,bool Truncated)> ReadText(string path,CancellationToken token)
    {
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,4096,true);
        using var reader=new StreamReader(stream,System.Text.Encoding.UTF8,true);
        var buffer=new char[MaximumCharacters+1];int used=0;
        while(used<buffer.Length){int read=await reader.ReadAsync(buffer.AsMemory(used),token);if(read==0)break;used+=read;}
        return(new string(buffer,0,Math.Min(used,MaximumCharacters)),used>MaximumCharacters);
    }
}
