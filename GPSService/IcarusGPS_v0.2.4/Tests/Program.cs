using Icarus.Gps;
var t=new DateTimeOffset(2026,10,17,3,0,0,TimeSpan.Zero);
int count=0;
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);count++;}
string Sentence(string s){byte cs=0;foreach(char c in s)cs^=(byte)c;return "$"+s+"*"+cs.ToString("X2");}
var c=new GpsConfig();var n=new Nmea(t);
Snapshot At(double seconds)=>n.Get(t.AddSeconds(seconds),c,true,"");
bool Feed(string s,double seconds=0)=>n.Feed(Sentence(s),t.AddSeconds(seconds));
Check(!At(0).FixValid,"No fix initially");
Check(!n.Feed("$GPRMC,broken*00",t),"Reject corrupt checksum");
Feed("GNRMC,030001.00,A,4000.0000,N,10500.0000,W,10.0,180.0,171026,,,A",1);
Check(At(1).FixValid && At(1).Latitude==40 && At(1).Longitude==-105,"GN talker and coordinates");
Check(Math.Abs(At(1).SpeedKph!.Value-18.52)<0.00001,"Knots converted to km/h");
Check(At(1).GpsUtc==t.AddSeconds(1),"RMC date and UTC time");
Check(At(1).TimeToFirstFixSeconds==1,"TTFF measured");
Feed("GNGGA,030002.00,4000.0000,N,10500.0000,W,1,11,1.01,1500.0,M,-20.0,M,,",2);
Check(At(2).AltitudeMeters==1500 && At(2).SatellitesUsed==11,"GGA altitude and satellites");
Feed("GPGSV,1,1,11",2);Feed("GLGSV,1,1,09",2);
Check(At(2).SatellitesInView["GP"]==11 && At(2).SatellitesInView["GL"]==9,"Separate satellite counts");
Check(!At(8).FixValid && At(8).Latitude==null && At(8).SpeedKph==null,"Stale fix clears current data");
Feed("GNRMC,030009,V,,,,,,,171026,,,N",9);
Check(!At(9).FixValid && At(9).SpeedKph==null,"Invalid RMC clears fix immediately");
Feed("GNGGA,030010,4000,N,10500,W,0,00,99.9,,M,,M,,",10);
Check(!At(10).FixValid,"GGA quality zero rejects fix");
Feed("GNGGA,030011,4000,N,10500,W,6,00,99.9,12,M,,M,,",11);
Check(!At(11).FixValid,"Dead reckoning is not a satellite fix");
Feed("GPRMC,030012,A,4065.0,N,10500,W,1,0,171026,,,A",12);
Check(!At(12).FixValid,"Reject invalid coordinate minutes");
Feed("GPRMC,030013,A,4000,N,10500,W,,0,171026,,,A",13);
Check(At(13).FixValid && At(13).SpeedKph==null,"Missing speed remains null");
Check(!n.Get(t.AddSeconds(13),c,false,"").FixValid,"Closed port cannot expose current fix");
for(int i=0;i<40;i++) Feed("GPGSV,1,1,08",14);
Check(At(14).RecentNmea.Length==20,"Raw NMEA buffer bounded");
Check(Wire.Decode<Snapshot>(Wire.Encode(At(14))).Latitude==40,"JSON protocol round trip");
try{new GpsConfig{Port="not-a-port"}.Validate();Check(false,"Invalid port");}catch(ArgumentException){Check(true,"Reject invalid settings");}
// Midnight GGA date rollover uses prior GPS date.
var midnight=new Nmea(t);
midnight.Feed(Sentence("GPRMC,235959,A,4000,N,10500,W,0,0,171026,,,A"),t);
midnight.Feed(Sentence("GPGGA,000001,4000,N,10500,W,1,08,1,10,M,,M,,"),t.AddSeconds(2));
Check(midnight.Get(t.AddSeconds(2),c,true,"").GpsUtc?.Day==18,"GGA midnight rollover");
// Desktop presentation logic: units, freshness, preference validation and satellite aliases.
var widget=new WidgetPreferences();
Check(Math.Abs(widget.Speed(160.9344)!.Value-100)<1e-8,"Widget mph conversion");
Check(Math.Abs(widget.Elevation(30.48)!.Value-100)<1e-8,"Widget feet conversion");
Check((widget with{SpeedUnit="km/h",ElevationUnit="m"}).Speed(12)==12 && (widget with{ElevationUnit="m"}).Elevation(123)==123,"Widget metric units");
Check(widget.Speed(null)==null && widget.Elevation(null)==null,"Widget missing measurements stay blank");
Check(WidgetPreferences.InView(new Dictionary<string,int>{{"GN",20},{"GP",11},{"GL",9}})==20,"Combined GN is not counted twice");
Check(WidgetPreferences.InView(new Dictionary<string,int>{{"GP",11},{"GL",9}})==20,"Separate constellations summed");
Check(WidgetPreferences.InView(new Dictionary<string,int>{{"GB",8},{"BD",8}})==8,"BeiDou aliases not counted twice");
Check(WidgetPreferences.InView(new Dictionary<string,int>())==null,"Unknown satellite view stays blank");
Check(!WidgetPreferences.IsCurrent(null,t) && !WidgetPreferences.IsCurrent(At(0),t.AddSeconds(6)),"Widget clears missing or stale snapshots");
Check(WidgetPreferences.IsCurrent(At(1),t.AddSeconds(1)),"Widget accepts current snapshot");
Check(!WidgetPreferences.IsCurrent(At(1) with{ProtocolVersion=2},t.AddSeconds(1)),"Widget rejects unsupported protocol");
var normalized=(widget with{Opacity=0.05,Scale=12,SpeedUnit="bad",ElevationUnit="bad"}).Normalize();
Check(normalized.Opacity==.4 && normalized.Scale==1.5 && normalized.SpeedUnit=="mph" && normalized.ElevationUnit=="ft","Widget invalid preferences normalized");
var saved=widget with{X=-1000,Y=120,Opacity=.75,Locked=true,Visible=false,ElevationUnit="m"};
Check(Wire.Decode<WidgetPreferences>(Wire.Encode(saved))==saved,"Widget settings round trip including negative monitor coordinates");
Console.WriteLine($"All {count} checks passed.");
