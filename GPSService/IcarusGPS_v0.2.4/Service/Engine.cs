using Icarus.Gps;
using System.IO.Ports;
using System.Text;
namespace Icarus.Gps.Service;
sealed class Engine : IDisposable {
 readonly object gate=new();
 readonly CancellationTokenSource stop=new();
 GpsConfig config;
 Nmea parser=new(DateTimeOffset.UtcNow);
 bool open;
 bool released;
 string message="Starting";
 int generation;
 Task? worker;
 static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Icarus","GPS");
 static readonly string FileName=Path.Combine(Folder,"settings.json");
 public Engine() {
  try {config=File.Exists(FileName)?Wire.Decode<GpsConfig>(File.ReadAllText(FileName)):new(); config.Validate();}
  catch(Exception e) {config=new GpsConfig{Enabled=false};message="Settings error: "+e.Message; Log(message);}
 }
 public void Start() {worker=Task.Factory.StartNew(Run,stop.Token,TaskCreationOptions.LongRunning,TaskScheduler.Default);}
 public Snapshot Snapshot() {lock(gate) return parser.Get(DateTimeOffset.UtcNow,config,open,message);}
 public void Configure(GpsConfig next) {
  next.Validate();
  lock(gate) {
   Directory.CreateDirectory(Folder);
   string temp=FileName+".tmp";
   File.WriteAllText(temp,Wire.Encode(next),new UTF8Encoding(false));
   if(File.Exists(FileName)) File.Replace(temp,FileName,null); else File.Move(temp,FileName);
   config=next;released=false;open=false;generation++;message="Applying settings";
  }
 }
 public void ReleasePort() {lock(gate) {released=true;generation++;message="GPS port released for external application";}}
 public void ResumePort() {lock(gate) {released=false;generation++;message="Reacquiring GPS receiver";}}
 public void Reconnect() {lock(gate) {generation++;message="Reconnecting after resume";}}
 public static void Log(string text) {
  try {Directory.CreateDirectory(Folder); string path=Path.Combine(Folder,"service.log"); if(File.Exists(path)&&new FileInfo(path).Length>1024*1024) File.Move(path,path+".old",true); File.AppendAllText(path,DateTimeOffset.UtcNow.ToString("O")+" "+text+Environment.NewLine); } catch { }
 }
 void Run() {
  while(!stop.IsCancellationRequested) {
   GpsConfig c;int gen;bool isReleased;
   lock(gate) {c=config;gen=generation;isReleased=released;}
   if(isReleased) {lock(gate){open=false;message="GPS port released for external application";} stop.Token.WaitHandle.WaitOne(250);continue;}
   if(!c.Enabled) {lock(gate){open=false;message="GPS disabled — COM port released";} stop.Token.WaitHandle.WaitOne(500);continue;}
   try {
    using var port=new SerialPort(c.Port,c.Baud,Parity.None,8,StopBits.One) {Handshake=Handshake.None,DtrEnable=c.Dtr,RtsEnable=c.Rts,ReadTimeout=1000,WriteTimeout=1000,Encoding=Encoding.ASCII};
    lock(gate) {parser=new Nmea(DateTimeOffset.UtcNow);message="Opening "+c.Port;}
    port.Open();
    SerialPort? virtualOut=null;
    if(c.VirtualOutputEnabled) {
     try {
      virtualOut=new SerialPort(c.VirtualOutputPort,c.VirtualOutputBaud,Parity.None,8,StopBits.One) {Handshake=Handshake.None,ReadTimeout=1000,WriteTimeout=1000,Encoding=Encoding.ASCII};
      virtualOut.Open();Log("NMEA output opened on "+c.VirtualOutputPort);
     } catch(Exception ex) {virtualOut?.Dispose();virtualOut=null;Log("Virtual output unavailable: "+ex.Message);}
    }
    using(var virtualOutputLifetime=virtualOut) {
    lock(gate) {open=true;message="Receiving on "+c.Port+" at "+c.Baud+" baud";}
    Log("Opened "+c.Port);
    var pending=new StringBuilder(); var buffer=new char[512]; var lastBytes=DateTimeOffset.UtcNow;
    while(!stop.IsCancellationRequested) {
     lock(gate) {if(gen!=generation) break;}
     try {
      int count=port.Read(buffer,0,buffer.Length);lastBytes=DateTimeOffset.UtcNow;
      for(int i=0;i<count;i++) {
       char ch=buffer[i];
       if(ch=='\n' || ch=='\r') {
        if(pending.Length>0) {
         string sentence=pending.ToString();lock(gate) parser.Feed(sentence,DateTimeOffset.UtcNow);
         if(virtualOut!=null) {try {virtualOut.Write(sentence+"\r\n");} catch(Exception e) {Log("Virtual NMEA output stopped: "+e.Message);virtualOut.Dispose();virtualOut=null;}}
         pending.Clear();}
       } else if(ch=='$') {pending.Clear();pending.Append(ch);}
       else if(pending.Length<1024) pending.Append(ch);
       else pending.Clear();
      }
     } catch(TimeoutException) { if(DateTimeOffset.UtcNow-lastBytes>TimeSpan.FromSeconds(15)) throw new IOException("No serial data for 15 seconds; retrying connection."); }
    }
    }
   } catch(Exception e) {
    lock(gate) message=e.Message+" Retrying in 3 seconds.";
    Log(e.GetType().Name+": "+e.Message);
   } finally {lock(gate) open=false;}
   if(!stop.IsCancellationRequested) stop.Token.WaitHandle.WaitOne(300);
  }
 }
 public void Dispose() {stop.Cancel();worker?.Wait(TimeSpan.FromSeconds(5));}
}
