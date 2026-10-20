using System.Runtime.InteropServices;
namespace Icarus.Gps.Service;
static class Program {
 static Engine? engine;
 static PipeHub? hub;
 static readonly ManualResetEvent stopped=new(false);
 static Native.ServiceMain main=MainService;
 static Native.Handler handler=Control;
 static IntPtr handle;
 static Native.Status currentStatus;
 static readonly object statusGate=new();
 public static int Main(string[] args) {
  if(args.Contains("--console")) {
   Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;stopped.Set();};
   Console.WriteLine("Icarus GPS diagnostic console. Ctrl+C to stop. Do not run alongside the installed service.");
   Run();return 0;
  }
  var table=new[]{new Native.Entry{Name=Wire.ServiceName,Main=main},new Native.Entry()};
  if(!Native.StartServiceCtrlDispatcher(table)) {Console.Error.WriteLine("Start with Services, or use --console. Windows error "+Marshal.GetLastWin32Error());return 1;}
  return 0;
 }
 static void MainService(int argc,IntPtr argv) {
  handle=Native.RegisterServiceCtrlHandlerEx(Wire.ServiceName,handler,IntPtr.Zero);
  if(handle==IntPtr.Zero) return;
  Status(2,0,10000);
  try {Run();Status(1,0,0);} catch(Exception e) {Engine.Log(e.ToString());Status(1,0,0,1);}
 }
 static void Run() {
  using var e=new Engine();engine=e;
  using var h=new PipeHub(e);hub=h;
  e.Start();h.Start();if(handle!=IntPtr.Zero)Status(4,1|4|64,0);
  stopped.WaitOne(); // Disposal cancels pipes and serial reads.
 }
 static uint Control(uint code,uint eventType,IntPtr eventData,IntPtr context) {
  if(code is 1 or 5) {Status(3,0,10000);stopped.Set();}
  else if(code==13 && eventType is 7 or 18) engine?.Reconnect();
  else if(code==4) {lock(statusGate) Native.SetServiceStatus(handle,ref currentStatus);}
  return 0;
 }
 static void Status(uint state,uint accepted,uint hint,uint error=0) {
  var s=new Native.Status {Type=0x10,State=state,Accepted=accepted,Win32Exit=error,WaitHint=hint,Checkpoint=state is 2 or 3?1u:0u};
  lock(statusGate) {currentStatus=s;Native.SetServiceStatus(handle,ref currentStatus);}
 }
}
static class Native {
 [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void ServiceMain(int argc,IntPtr argv);
 [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate uint Handler(uint control,uint eventType,IntPtr eventData,IntPtr context);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct Entry {
  [MarshalAs(UnmanagedType.LPWStr)] public string? Name;
  [MarshalAs(UnmanagedType.FunctionPtr)] public ServiceMain? Main;
 }
 [StructLayout(LayoutKind.Sequential)] public struct Status { public uint Type,State,Accepted,Win32Exit,ServiceExit,Checkpoint,WaitHint; }
 [DllImport("advapi32.dll",EntryPoint="StartServiceCtrlDispatcherW",CharSet=CharSet.Unicode,SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)] public static extern bool StartServiceCtrlDispatcher([In] Entry[] table);
 [DllImport("advapi32.dll",EntryPoint="RegisterServiceCtrlHandlerExW",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr RegisterServiceCtrlHandlerEx(string name,Handler handler,IntPtr context);
 [DllImport("advapi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool SetServiceStatus(IntPtr handle,ref Status status);
}
