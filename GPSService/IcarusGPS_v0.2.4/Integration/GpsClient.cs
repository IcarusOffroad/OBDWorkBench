using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
namespace Icarus.Gps;
// Include this file and reference Core in Workbench / Maps. Windows only.
public static class GpsClient {
 public static async IAsyncEnumerable<Snapshot> Subscribe([EnumeratorCancellation] CancellationToken cancellation=default) {
  using var pipe=new NamedPipeClientStream(".",Wire.DataPipe,PipeDirection.InOut,PipeOptions.Asynchronous);
  await pipe.ConnectAsync(3000,cancellation);
  using var reader=new StreamReader(pipe,Encoding.UTF8);
  while(!cancellation.IsCancellationRequested) {
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(5000);
   string? line=await reader.ReadLineAsync(timeout.Token);
   if(line is null) throw new IOException("GPS service disconnected.");
   yield return Wire.Decode<Snapshot>(line);
  }
 }
}
