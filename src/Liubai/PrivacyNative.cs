using System.Runtime.InteropServices;
using System.Text;

namespace Liubai;

internal sealed class PrivacyNative : IPrivacyDevices
{
    const uint Present = 0x100, Started = 8, HasProblem = 0x400;
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_Device_ID_ListW(string? filter, [Out] char[] buffer, uint length, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_DevNode_Registry_PropertyW(uint node, uint property, out uint type, byte[] buffer, ref uint length, uint flags);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Disable_DevNode(uint node, uint flags);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Enable_DevNode(uint node, uint flags);
    static string Property(uint node, uint property)
    {
        byte[] buffer = new byte[8192]; uint size = (uint)buffer.Length;
        return CM_Get_DevNode_Registry_PropertyW(node, property, out _, buffer, ref size, 0) == 0
            ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0') : "";
    }
    internal static string? Kind(string id, string deviceClass, string service)
    {
        if (deviceClass.Equals("Camera", StringComparison.OrdinalIgnoreCase)
            || deviceClass.Equals("Image", StringComparison.OrdinalIgnoreCase) && service.Equals("usbvideo", StringComparison.OrdinalIgnoreCase)) return "摄像头";
        // Capture endpoints only: never disable MEDIA adapters, USB parents or rendering endpoints.
        if (deviceClass.Equals("AudioEndpoint", StringComparison.OrdinalIgnoreCase)
            && id.StartsWith("SWD\\MMDEVAPI\\{0.0.1.", StringComparison.OrdinalIgnoreCase)) return "麦克风";
        return null;
    }
    public PrivacyDevice? Find(string id)
    {
        if (!PrivacyFiles.ValidId(id) || CM_Locate_DevNodeW(out uint node, id, 0) != 0) return null;
        string? kind = Kind(id, Property(node, 8), Property(node, 5));
        if (kind is null || CM_Get_DevNode_Status(out uint status, out uint problem, node, 0) != 0) return null;
        string name = Property(node, 13); if (name.Length == 0) name = Property(node, 1);
        return new(id, name.Length == 0 ? kind : name, kind, (status & Started) != 0 && (status & HasProblem) == 0, (status & HasProblem) != 0 && problem == 22);
    }
    internal PrivacyDevice[] List()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out uint size, null, Present) != 0 || size > 4_000_000) throw new IOException("设备列表读取失败。");
            var buffer = new char[size];
            if (CM_Get_Device_ID_ListW(null, buffer, size, Present) != 0) continue;
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(Find).OfType<PrivacyDevice>().OrderBy(d => d.Kind).ThenBy(d => d.Name).ToArray();
        }
        throw new IOException("设备列表正在变化，请重试。");
    }
    public void SetEnabled(string id, bool enabled)
    {
        if (Find(id) is null || CM_Locate_DevNodeW(out uint node, id, 0) != 0) throw new IOException("设备暂不可用。");
        uint result = enabled ? CM_Enable_DevNode(node, 0) : CM_Disable_DevNode(node, 0x4 | 0x8);
        if (result != 0) throw new IOException($"设备操作未成功（0x{result:X}），可能被驱动拒绝或需要重启。");
        for (int i = 0; i < 20; i++)
        {
            var current = Find(id); if (enabled ? current?.Enabled == true : current?.Disabled == true) return;
            Thread.Sleep(100);
        }
        throw new IOException("设备状态尚未确认，已保留恢复记录。");
    }
}
