using System;
using System.Text;
using System.Runtime.InteropServices;
class DebugRunner
{
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct STARTUPINFO { public int cb; public string reserved, desktop, title; public int x,y,w,h,xchars,ychars,fill,flags; public short show,reserved2; public IntPtr reservedPtr,stdin,stdout,stderr; }
 [StructLayout(LayoutKind.Sequential)] struct PROCESSINFO { public IntPtr process,thread; public int pid,tid; }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,StringBuilder cmd,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string dir,ref STARTUPINFO startup,out PROCESSINFO info);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool WaitForDebugEvent(IntPtr ev,uint ms);
 [DllImport("kernel32.dll")] static extern bool ContinueDebugEvent(int pid,int tid,uint status);
 [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] bytes,int length,out IntPtr read);
 [DllImport("kernel32.dll")] static extern bool DebugActiveProcessStop(int pid);
 [DllImport("kernel32.dll")] static extern bool DebugSetProcessKillOnExit(bool kill);
 [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access,bool inherit,int tid);
 [DllImport("kernel32.dll")] static extern bool GetThreadContext(IntPtr thread,IntPtr context);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
 [DllImport("kernel32.dll",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module,string name);
 [DllImport("dbghelp.dll",CharSet=CharSet.Ansi)] static extern bool SymInitialize(IntPtr process,string path,bool invade);
 [DllImport("dbghelp.dll")] static extern uint SymSetOptions(uint options);
 [DllImport("dbghelp.dll")] static extern bool SymFromAddr(IntPtr process,ulong address,out ulong displacement,IntPtr symbol);
 [DllImport("dbghelp.dll")] static extern bool StackWalk64(uint machine,IntPtr process,IntPtr thread,IntPtr frame,IntPtr context,IntPtr read,IntPtr function,IntPtr module,IntPtr translate);
 [DllImport("dbghelp.dll")] static extern ulong SymGetModuleBase64(IntPtr process,ulong address);
 [DllImport("psapi.dll",CharSet=CharSet.Unicode)] static extern uint GetModuleFileNameEx(IntPtr process,IntPtr module,StringBuilder path,int size);
 static void Stack(IntPtr process,int tid)
 {
  SymSetOptions(0x10206);SymInitialize(process,Environment.SystemDirectory,true);
  var thread=OpenThread(0x48,false,tid);var raw=Marshal.AllocHGlobal(1500);var context=new IntPtr((raw.ToInt64()+15)&~15L);
  for(int i=0;i<1232;i++)Marshal.WriteByte(context,i,0);Marshal.WriteInt32(context,48,0x100003);GetThreadContext(thread,context);
  var frame=Marshal.AllocHGlobal(1024);for(int i=0;i<1024;i++)Marshal.WriteByte(frame,i,0);
  Marshal.WriteInt64(frame,0,Marshal.ReadInt64(context,248));Marshal.WriteInt32(frame,12,3);
  Marshal.WriteInt64(frame,32,Marshal.ReadInt64(context,160));Marshal.WriteInt32(frame,44,3);
  Marshal.WriteInt64(frame,48,Marshal.ReadInt64(context,152));Marshal.WriteInt32(frame,60,3);
  var dbghelp=LoadLibrary("dbghelp.dll");var function=GetProcAddress(dbghelp,"SymFunctionTableAccess64");var module=GetProcAddress(dbghelp,"SymGetModuleBase64");
  for(int i=0;i<22;i++)
  {
   ulong address=(ulong)Marshal.ReadInt64(frame,0);if(address==0)break;
   var symbol=Marshal.AllocHGlobal(1200);for(int j=0;j<1200;j++)Marshal.WriteByte(symbol,j,0);Marshal.WriteInt32(symbol,0,88);Marshal.WriteInt32(symbol,80,1024);ulong displacement;
   string name="";if(SymFromAddr(process,address,out displacement,symbol))name=Marshal.PtrToStringAnsi(new IntPtr(symbol.ToInt64()+84),Marshal.ReadInt32(symbol,76))+"+0x"+displacement.ToString("x");
   ulong baseAddress=SymGetModuleBase64(process,address);var filename=new StringBuilder(1024);GetModuleFileNameEx(process,new IntPtr((long)baseAddress),filename,1024);
   Console.WriteLine("STACK "+System.IO.Path.GetFileName(filename.ToString())+"!"+name+" [0x"+address.ToString("x")+" RVA 0x"+(address-baseAddress).ToString("x")+"]");
   Marshal.FreeHGlobal(symbol);if(!StackWalk64(0x8664,process,thread,frame,context,IntPtr.Zero,function,module,IntPtr.Zero))break;
  }
 }
 static void Main(string[] args)
 {
  var startup=new STARTUPINFO(); startup.cb=Marshal.SizeOf(startup); PROCESSINFO process;
  var command=new StringBuilder("\""+args[0]+"\" --inspect --show-settings --data-dir \""+args[1]+"\"");
  if(!CreateProcess(args[0],command,IntPtr.Zero,IntPtr.Zero,false,2,IntPtr.Zero,System.IO.Path.GetDirectoryName(args[0]),ref startup,out process)) throw new Exception("CreateProcess "+Marshal.GetLastWin32Error());
  DebugSetProcessKillOnExit(false);
  IntPtr ev=Marshal.AllocHGlobal(176); var until=DateTime.UtcNow.AddSeconds(15);
  bool heapBad=false,stackPrinted=false;int exceptions=0;
  while(DateTime.UtcNow<until)
  {
   if(!WaitForDebugEvent(ev,1000))continue;
   int code=Marshal.ReadInt32(ev,0),pid=Marshal.ReadInt32(ev,4),tid=Marshal.ReadInt32(ev,8);uint status=0x10002;
   if(code==1){uint exception=(uint)Marshal.ReadInt32(ev,16);if(exceptions++<5)Console.WriteLine("Exception 0x"+exception.ToString("X8")+" chance="+Marshal.ReadInt32(ev,168));if(exception==0x80000003&&heapBad&&!stackPrinted){stackPrinted=true;Stack(process.process,tid);} if(exception!=0x80000003)status=0x80010001;}
   if(code==8)
   {
    var address=Marshal.ReadIntPtr(ev,16); bool unicode=Marshal.ReadInt16(ev,24)!=0;int length=(ushort)Marshal.ReadInt16(ev,26);
    if(length>0&&length<30000){byte[] bytes=new byte[length*(unicode?2:1)];IntPtr read;if(ReadProcessMemory(process.process,address,bytes,bytes.Length,out read)){var message=(unicode?Encoding.Unicode:Encoding.Default).GetString(bytes).TrimEnd('\0');if(message.Contains("Free Heap"))heapBad=true;Console.WriteLine("Debug: "+message);}}
   }
   if(code==5){Console.WriteLine("Process exit: 0x"+((uint)Marshal.ReadInt32(ev,16)).ToString("X8"));ContinueDebugEvent(pid,tid,status);return;}
   ContinueDebugEvent(pid,tid,status);
  }
  DebugActiveProcessStop(process.pid);Console.WriteLine("Still running after 15s");
 }
}
