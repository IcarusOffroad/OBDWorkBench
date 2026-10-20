using System.Globalization;
using System.Text.Json;
namespace Icarus.Gps;
public sealed record GpsConfig {
 public bool Enabled {get;init;} = true;
 public string Port {get;init;} = "COM6";
 public int Baud {get;init;} = 9600;
 public bool Dtr {get;init;}
 public bool Rts {get;init;}
 public bool VirtualOutputEnabled {get;init;}
 public string VirtualOutputPort {get;init;} = "COM15";
 public int VirtualOutputBaud {get;init;} = 9600;
 public void Validate() {
  if (!System.Text.RegularExpressions.Regex.IsMatch(Port, @"^COM[1-9][0-9]{0,3}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new ArgumentException("Enter a COM port such as COM6.");
  if (!new[]{1200,2400,4800,9600,19200,38400,57600,115200,230400}.Contains(Baud)) throw new ArgumentException("Unsupported baud rate.");
  if (VirtualOutputEnabled) {
   if (!System.Text.RegularExpressions.Regex.IsMatch(VirtualOutputPort, @"^COM[1-9][0-9]{0,3}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new ArgumentException("Enter virtual output port such as COM15.");
   if (string.Equals(Port,VirtualOutputPort,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Physical and virtual output COM ports must be different.");
   if (!new[]{1200,2400,4800,9600,19200,38400,57600,115200,230400}.Contains(VirtualOutputBaud)) throw new ArgumentException("Unsupported virtual output baud rate.");
  }
 }
}
public sealed record Snapshot {
 public int ProtocolVersion {get;init;} = 1;
 public DateTimeOffset PublishedUtc {get;init;}
 public GpsConfig Config {get;init;} = new();
 public string State {get;init;} = "Disconnected";
 public string Message {get;init;} = "";
 public bool PortOpen {get;init;}
 public bool DataFresh {get;init;}
 public bool FixValid {get;init;}
 public double? Latitude {get;init;}
 public double? Longitude {get;init;}
 public double? SpeedKph {get;init;}
 public double? CourseDegrees {get;init;}
 public double? AltitudeMeters {get;init;}
 public double? Hdop {get;init;}
 public int? SatellitesUsed {get;init;}
 public Dictionary<string,int> SatellitesInView {get;init;} = new();
 public int? FixQuality {get;init;}
 public DateTimeOffset? GpsUtc {get;init;}
 public DateTimeOffset? LastNmeaUtc {get;init;}
 public DateTimeOffset? LastFixUtc {get;init;}
 public double? LastFixAgeSeconds {get;init;}
 public double AcquisitionSeconds {get;init;}
 public double? TimeToFirstFixSeconds {get;init;}
 public long ValidSentences {get;init;}
 public long RejectedSentences {get;init;}
 public string[] RecentNmea {get;init;} = [];
}
public static class Wire {
 public const string DataPipe = "Icarus.GPS.Data.v1";
 public const string ConfigPipe = "Icarus.GPS.Config.v1";
 public const string ServiceName = "IcarusGPS";
 public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
 public static string Encode<T>(T value) => JsonSerializer.Serialize(value,Json);
 public static T Decode<T>(string line) => JsonSerializer.Deserialize<T>(line,Json) ?? throw new FormatException("Empty JSON object.");
}
