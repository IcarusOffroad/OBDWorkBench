using Microsoft.Win32;
namespace Icarus.Gps.Tray;
// The widget is independent of Explorer; preserve UI if widget creation fails.
sealed class WidgetController : IDisposable {
 readonly WidgetStore store=new();
 readonly Action openMonitor;
 readonly System.Windows.Forms.Timer timer=new(){Interval=1000};
 DesktopWidget? widget;
 Snapshot? snapshot;
 bool disposed,resetPosition;
 string? failure;
 public WidgetPreferences Preferences=>store.Value;
 public string? SettingsError=>failure??store.Error;
 public string Diagnostics=>"Icarus GPS v0.2.2\nWidget enabled: "+Preferences.Visible+"\nWindow created: "+(widget?.IsHandleCreated==true)+"\nWindow visible: "+(widget?.Visible==true)+"\nBounds: "+(widget?.IsHandleCreated==true?DesktopNative.ScreenBounds(widget.Handle).ToString():"unavailable")+"\nMode: desktop window (no Explorer parenting)\n"+(SettingsError??"No widget errors recorded.");
 public WidgetController(Action open){openMonitor=open;timer.Tick+=(_,_)=>Tick();}
 public void Start(){timer.Start();Tick();}
 public void Update(Snapshot? next){snapshot=next;widget?.Update(next);}
 public void Set(WidgetPreferences next){store.Save(next);Tick();}
 public void Show()=>Set(Preferences with{Visible=true});
 public void Hide()=>Set(Preferences with{Visible=false});
 public void Recover(){store.Save(Preferences with{Visible=true,X=null,Y=null});resetPosition=true;Tick();if(widget?.IsHandleCreated==true)widget.BringForwardTemporarily();}
 public void ResetPosition(){store.Save(Preferences with{X=null,Y=null,Visible=true});resetPosition=true;Tick();}
 public void OpenMonitor()=>openMonitor();
 public void RememberPosition(Point point)=>store.Save(Preferences with{X=point.X,Y=point.Y});
 void Tick() {
  if(disposed)return;
  try {TickCore();failure=null;}
  catch(Exception e) {
   if(failure!=e.Message){failure=e.Message;try {string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Icarus","GPS");Directory.CreateDirectory(folder);string path=Path.Combine(folder,"widget-errors.log");if(File.Exists(path)&&new FileInfo(path).Length>100_000)File.Delete(path);File.AppendAllText(path,DateTimeOffset.Now.ToString("O")+" "+e+Environment.NewLine);}catch{}}
  }
 }
 void TickCore() {
  if(!Preferences.Visible){widget?.Hide();return;}
  if(widget is null || widget.IsDisposed || !widget.IsHandleCreated){widget?.Dispose();widget=new DesktopWidget(this);widget.Show();resetPosition=true;}
  widget.Apply(Preferences,resetPosition);resetPosition=false;
  widget.Update(snapshot);widget.MaintainDesktop();
 }
 public void Dispose(){disposed=true;timer.Stop();timer.Dispose();widget?.Dispose();}
}
