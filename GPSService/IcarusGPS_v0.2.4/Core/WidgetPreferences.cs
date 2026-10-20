namespace Icarus.Gps;
// Portable widget preferences / measurement presentation, independent of Windows UI.
public sealed record WidgetPreferences {
 public bool Visible {get;init;}=true;
 public string SpeedUnit {get;init;}="mph";
 public string ElevationUnit {get;init;}="ft";
 public double Opacity {get;init;}=.86;
 public double Scale {get;init;}=1;
 public bool Locked {get;init;}
 public int? X {get;init;}
 public int? Y {get;init;}
 public WidgetPreferences Normalize()=>this with {
  SpeedUnit=SpeedUnit is "mph" or "km/h"?SpeedUnit:"mph",
  ElevationUnit=ElevationUnit is "ft" or "m"?ElevationUnit:"ft",
  Opacity=double.IsFinite(Opacity)?Math.Clamp(Opacity,.4,1):.86,
  Scale=double.IsFinite(Scale)?Math.Clamp(Scale,.8,1.5):1
 };
 public double? Speed(double? kph)=>SpeedUnit=="mph"?kph/1.609344:kph;
 public double? Elevation(double? meters)=>ElevationUnit=="ft"?meters/0.3048:meters;
 public static bool IsCurrent(Snapshot? s,DateTimeOffset now)=>s is not null && s.ProtocolVersion==1 && (now-s.PublishedUtc).TotalSeconds is >=-2 and <5;
 public static int? InView(IReadOnlyDictionary<string,int> counts) {
  if(counts.TryGetValue("GN",out int combined)&&combined>=0)return combined;
  // BD/GB and QZ/GQ are aliases. Keep the larger reported count, not both.
  string[][] groups=[["GP"],["GL"],["GA"],["GB","BD"],["GQ","QZ"],["GI"]];
  var values=groups.Select(g=>g.Where(counts.ContainsKey).Select(k=>counts[k]).Where(v=>v>=0).DefaultIfEmpty(-1).Max()).Where(v=>v>=0).ToArray();
  return values.Length==0?null:values.Sum();
 }
}
