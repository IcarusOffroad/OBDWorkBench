using System.Globalization;
namespace Icarus.Gps;
// One parser instance per open connection. Call only under the engine lock.
public sealed class Nmea {
 static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
 readonly DateTimeOffset started;
 readonly Queue<string> raw = new();
 readonly Dictionary<string,(int Count,DateTimeOffset At)> views = new();
 DateTimeOffset? lastNmea,lastFix,positionAt,rmcAt,ggaAt;
 DateTimeOffset? gpsUtc;
 double? lat,lon,speed,course,alt,hdop,ttff;
 int? used,quality;
 bool fix;
 long valid,rejected;
 public Nmea(DateTimeOffset start) { started=start; }
 static double? Num(string s) => double.TryParse(s,NumberStyles.Float,Inv,out var x) && double.IsFinite(x) ? x : null;
 static int? Int(string s) => int.TryParse(s,out var x) ? x : null;
 static double? Coord(string v,string h,bool latitude) {
  var n=Num(v); if(n is null || n<0) return null;
  double d=Math.Floor(n.Value/100), m=n.Value-d*100, max=latitude?90:180;
  if(m>=60 || d>max || (d==max && m>0) || !(latitude ? h is "N" or "S" : h is "E" or "W")) return null;
  return (d+m/60)*(h is "S" or "W"?-1:1);
 }
 public bool Feed(string line,DateTimeOffset now) {
  line=line.Trim(); if(line.Length>1024) {rejected++;return false;}
  raw.Enqueue(line); while(raw.Count>20) raw.Dequeue();
  var star=line.IndexOf('*');
  if(!line.StartsWith('$') || star<6 || star+3!=line.Length || !byte.TryParse(line.AsSpan(star+1),NumberStyles.HexNumber,Inv,out var cs)) {rejected++;return false;}
  byte sum=0; for(int i=1;i<star;i++) sum^=(byte)line[i];
  if(sum!=cs) {rejected++;return false;}
  string[] a=line[1..star].Split(',');
  if(a[0].Length!=5) {rejected++;return false;}
  string type=a[0][2..], talker=a[0][..2];
  if(type=="RMC" && a.Length>=10) {
   var la=Coord(a[3],a[4],true); var lo=Coord(a[5],a[6],false);
   // RMC status V and mode N/E (estimated) cannot provide a satellite fix.
   bool ok=a[2]=="A" && la!=null && lo!=null && !(a.Length>12 && a[12] is "N" or "E");
   Position(ok,la,lo,now); rmcAt=now;
   speed=ok && Num(a[7]) is double s && s>=0?s*1.852:null;
   course=ok && Num(a[8]) is double c && c>=0 && c<360?c:null;
   gpsUtc=ok?Time(a[1],a[9],now):null;
  } else if(type=="GGA" && a.Length>=11) {
   var la=Coord(a[2],a[3],true); var lo=Coord(a[4],a[5],false);
   quality=Int(a[6]); // 6=estimated/dead reckoning, 7=manual, 8=simulation.
   bool ok=quality is >=1 and <=5 && la!=null && lo!=null;
   Position(ok,la,lo,now); ggaAt=now;
   used=Int(a[7]); hdop=Num(a[8]); alt=ok && a[10]=="M"?Num(a[9]):null;
   gpsUtc=ok?Time(a[1],"",gpsUtc??now):null;
  } else if(type=="GSV" && a.Length>=4 && Int(a[3]) is int count && count>=0) views[talker]=(count,now);
  else if(type=="RMC" || type=="GGA" || type=="GSV") {rejected++;return false;}
  lastNmea=now;valid++;return true;
 }
 void Position(bool ok,double? la,double? lo,DateTimeOffset now) {
  fix=ok; positionAt=now;
  if(ok) {lat=la;lon=lo;lastFix=now;ttff??=Math.Max(0,(now-started).TotalSeconds);}
  else {lat=null;lon=null;speed=null;course=null;gpsUtc=null;}
 }
 static DateTimeOffset? Time(string t,string date,DateTimeOffset reference) {
  if(t.Length<6 || !int.TryParse(t[..2],out var h) || !int.TryParse(t.Substring(2,2),out var m) || Num(t[4..]) is not double s || h>23 || m>59 || s<0 || s>=60) return null;
  try {
   DateTime day=reference.UtcDateTime.Date;
   if(date.Length==6 && int.TryParse(date[..2],out var dd) && int.TryParse(date.Substring(2,2),out var mm) && int.TryParse(date[4..],out var yy)) day=new DateTime(yy>=80?1900+yy:2000+yy,mm,dd,0,0,0,DateTimeKind.Utc);
   var result=new DateTimeOffset(day.AddHours(h).AddMinutes(m).AddSeconds(s));
   if(date.Length==0) {if(result-reference>TimeSpan.FromHours(12)) result=result.AddDays(-1); if(reference-result>TimeSpan.FromHours(12)) result=result.AddDays(1);}
   return result;
  } catch(ArgumentOutOfRangeException) {return null;}
 }
 static bool Fresh(DateTimeOffset? t,DateTimeOffset now,double seconds=5) => t.HasValue && (now-t.Value).TotalSeconds is >=0 && (now-t.Value).TotalSeconds<=seconds;
 public Snapshot Get(DateTimeOffset now,GpsConfig config,bool open,string message) {
  bool data=config.Enabled&&open&&Fresh(lastNmea,now), good=data&&fix&&Fresh(positionAt,now), gga=good&&Fresh(ggaAt,now);
  return new Snapshot {
   PublishedUtc=now,Config=config,PortOpen=open,DataFresh=data,FixValid=good,
   State=!config.Enabled?"Disabled":!open?"Disconnected":!data?"WaitingForData":good?"Fix":"Acquiring",
   Message=message,Latitude=good?lat:null,Longitude=good?lon:null,SpeedKph=good&&Fresh(rmcAt,now)?speed:null,
   CourseDegrees=good&&Fresh(rmcAt,now)?course:null,AltitudeMeters=gga?alt:null,Hdop=gga?hdop:null,
   SatellitesUsed=Fresh(ggaAt,now)?used:null,FixQuality=Fresh(ggaAt,now)?quality:null,GpsUtc=good?gpsUtc:null,
   SatellitesInView=views.Where(x=>Fresh(x.Value.At,now,15)).ToDictionary(x=>x.Key,x=>x.Value.Count),
   LastNmeaUtc=lastNmea,LastFixUtc=lastFix,LastFixAgeSeconds=lastFix.HasValue?Math.Max(0,(now-lastFix.Value).TotalSeconds):null,
   AcquisitionSeconds=ttff??Math.Max(0,(now-started).TotalSeconds),TimeToFirstFixSeconds=ttff,
   ValidSentences=valid,RejectedSentences=rejected,RecentNmea=raw.ToArray()
  };
 }
}
