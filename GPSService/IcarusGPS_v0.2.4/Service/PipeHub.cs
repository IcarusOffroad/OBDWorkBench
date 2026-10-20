using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
namespace Icarus.Gps.Service;
sealed class PipeHub : IDisposable {
 readonly Engine engine;
 readonly CancellationTokenSource stop=new();
 readonly List<Task> loops=new();
 public PipeHub(Engine e) {engine=e;}
 public void Start() {loops.Add(Listen(false));loops.Add(Listen(true));}
 static NamedPipeServerStream Create(bool admin) {
  var security=new PipeSecurity();
  security.SetAccessRuleProtection(true,false);
  security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
  foreach(var sid in new[]{WellKnownSidType.LocalServiceSid,WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})
   security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
  if(!admin) security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
  return NamedPipeServerStreamAcl.Create(admin?Wire.ConfigPipe:Wire.DataPipe,PipeDirection.InOut,16,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,65536,security);
 }
 async Task Listen(bool admin) {
  var clients=new List<Task>();
  while(!stop.IsCancellationRequested) {
   NamedPipeServerStream? pipe=null;
   try {
    pipe=Create(admin);await pipe.WaitForConnectionAsync(stop.Token);
    clients.RemoveAll(x=>x.IsCompleted);
    clients.Add(Serve(pipe,admin));pipe=null;
   } catch(OperationCanceledException) {break;}
   catch(Exception e) {Engine.Log("Pipe listener: "+e.Message); try {await Task.Delay(1000,stop.Token);}catch(OperationCanceledException){break;}}
   finally {pipe?.Dispose();}
  }
  await Task.WhenAll(clients);
 }
 async Task Serve(NamedPipeServerStream pipe,bool admin) {
  using(pipe) {
   try {
    using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true) {AutoFlush=true};
    if(admin) {
     using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(5000);
     var bytes=new List<byte>();var one=new byte[1];
     while(bytes.Count<4096) {if(await pipe.ReadAsync(one,deadline.Token)==0) throw new IOException("Request incomplete."); if(one[0]==10)break;bytes.Add(one[0]);}
     if(bytes.Count>=4096) throw new IOException("Request too large.");
     string response;
     try {
      string request=Encoding.UTF8.GetString(bytes.ToArray());
      using var doc=System.Text.Json.JsonDocument.Parse(request);
      if(doc.RootElement.TryGetProperty("Action",out var action)) {
       switch(action.GetString()) {
        case "Release": engine.ReleasePort();break;
        case "Resume": engine.ResumePort();break;
        default: throw new ArgumentException("Unknown GPS control action.");
       }
      } else engine.Configure(Wire.Decode<GpsConfig>(request));
      response=Wire.Encode(new {Ok=true,Error=""});
     }
     catch(Exception e) {response=Wire.Encode(new {Ok=false,Error=e.Message});}
     await writer.WriteLineAsync(response.AsMemory(),deadline.Token);
    } else {
     while(!stop.IsCancellationRequested) {
      using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(3000);
      await writer.WriteLineAsync(Wire.Encode(engine.Snapshot()).AsMemory(),deadline.Token);
      await Task.Delay(1000,stop.Token);
     }
    }
   } catch(Exception e) when(e is IOException or OperationCanceledException or ObjectDisposedException or UnauthorizedAccessException) { }
  }
 }
 public void Dispose() {stop.Cancel();try{Task.WhenAll(loops).Wait(TimeSpan.FromSeconds(5));}catch(AggregateException){} }
}
