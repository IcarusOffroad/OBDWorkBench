namespace Icarus.Gps.Tray;
sealed class WidgetStore {
 static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Icarus","GPS");
 static readonly string FileName=Path.Combine(Folder,"widget.json");
 public WidgetPreferences Value {get;private set;}=new();
 public string? Error {get;private set;}
 public WidgetStore() {
  try {if(File.Exists(FileName))Value=Wire.Decode<WidgetPreferences>(File.ReadAllText(FileName)).Normalize();}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException){Error="Could not load widget settings: "+e.Message;}
 }
 public void Save(WidgetPreferences next) {
  Value=next.Normalize();
  try {
   Directory.CreateDirectory(Folder);string temp=FileName+".tmp";
   File.WriteAllText(temp,Wire.Encode(Value));File.Move(temp,FileName,true);Error=null;
  }catch(Exception e) when(e is IOException or UnauthorizedAccessException){Error="Could not save widget settings: "+e.Message;}
 }
}
