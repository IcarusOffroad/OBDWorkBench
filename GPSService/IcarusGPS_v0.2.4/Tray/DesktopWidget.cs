using System.Drawing.Drawing2D;
namespace Icarus.Gps.Tray;
sealed class DesktopWidget : Form {
 readonly WidgetController controller;
 Snapshot? snapshot;
 WidgetPreferences prefs=new();
 DateTimeOffset frontUntil;
 bool dragging;
 Point dragStart,windowStart;
 ContextMenuStrip? activeMenu;
 double dpiScale=1;
 public DesktopWidget(WidgetController owner) {
  controller=owner;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=false;
  StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;
  BackColor=Color.FromArgb(22,28,35);ForeColor=Color.White;DoubleBuffered=true;
  Text="Icarus GPS Desktop Widget";ClientSize=new Size(300,158);
  SetStyle(ControlStyles.ResizeRedraw,true);
 }
 protected override bool ShowWithoutActivation=>true;
 protected override CreateParams CreateParams {
  get{var cp=base.CreateParams;cp.ExStyle|=0x08000000|0x00000080;return cp;}// NOACTIVATE + TOOLWINDOW
 }
 protected override void WndProc(ref Message m) {
  if(m.Msg==0x7B){m.Result=IntPtr.Zero;return;}// Widget owns its menu; do not forward to desktop.
  if(m.Msg==0x21){m.Result=new IntPtr(3);return;}// MA_NOACTIVATE
  base.WndProc(ref m);
 }
 public void Apply(WidgetPreferences next,bool reset) {
  prefs=next;
  dpiScale=Math.Max(.75,DeviceDpi/96.0)*prefs.Scale;
  Size wanted=new((int)Math.Round(300*dpiScale),(int)Math.Round(158*dpiScale));
  bool resize=Size!=wanted;
  if(Math.Abs(Opacity-prefs.Opacity)>.001)Opacity=prefs.Opacity;
  if(!Visible)Show();
  if(reset||resize) {
   var current=IsHandleCreated?DesktopNative.ScreenBounds(Handle):Bounds;
   var area=Screen.PrimaryScreen?.WorkingArea??new Rectangle(0,0,1024,768);
   int x=prefs.X??area.Right-wanted.Width-24,y=prefs.Y??area.Top+32;
   if(!reset){x=current.X;y=current.Y;}
   var target=new Rectangle(x,y,wanted.Width,wanted.Height);
   var screen=Screen.AllScreens.FirstOrDefault(s=>s.WorkingArea.IntersectsWith(target))??Screen.PrimaryScreen;
   area=screen?.WorkingArea??area;
   x=Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-wanted.Width));
   y=Math.Clamp(y,area.Top,Math.Max(area.Top,area.Bottom-wanted.Height));
   DesktopNative.MoveScreen(Handle,x,y,wanted.Width,wanted.Height);
  }
  Invalidate();
 }
 public void Update(Snapshot? value){snapshot=value;Invalidate();}
 public void MaintainDesktop() {
  if(IsDisposed||!IsHandleCreated||!Visible)return;
  if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;
  if(!dragging && activeMenu is not {Visible:true}) {
   if(DateTimeOffset.UtcNow<frontUntil)
    DesktopNative.SetWindowPos(Handle,DesktopNative.TopMost,0,0,0,0,DesktopNative.NoSize|DesktopNative.NoMove|DesktopNative.NoActivate);
   else {
    if(TopMost)TopMost=false;
    // Native temporary topmost does not update Form.TopMost; clear it explicitly once.
    if(frontUntil!=default){DesktopNative.SetWindowPos(Handle,DesktopNative.NotTopMost,0,0,0,0,DesktopNative.NoSize|DesktopNative.NoMove|DesktopNative.NoActivate);frontUntil=default;}
    DesktopNative.PlaceAboveDesktop(Handle);
   }
  }
  // Reposition after rotation / monitor removal, using actual screen coordinates.
  var bounds=DesktopNative.ScreenBounds(Handle);
  if(!dragging && !Screen.AllScreens.Any(s=>s.WorkingArea.Contains(bounds))) {
   var area=Screen.FromRectangle(bounds).WorkingArea;
   DesktopNative.MoveScreen(Handle,Math.Clamp(bounds.X,area.Left,Math.Max(area.Left,area.Right-bounds.Width)),Math.Clamp(bounds.Y,area.Top,Math.Max(area.Top,area.Bottom-bounds.Height)),bounds.Width,bounds.Height);
  }
 }
 public void BringForwardTemporarily(){frontUntil=DateTimeOffset.UtcNow.AddSeconds(10);Show();MaintainDesktop();}
 protected override void OnMouseDown(MouseEventArgs e) {
  base.OnMouseDown(e);
  if(e.Button!=MouseButtons.Left || prefs.Locked)return;
  dragging=true;dragStart=Cursor.Position;var bounds=DesktopNative.ScreenBounds(Handle);windowStart=bounds.Location;Capture=true;
 }
 protected override void OnMouseMove(MouseEventArgs e) {
  base.OnMouseMove(e);if(!dragging)return;
  Point p=Cursor.Position;var bounds=DesktopNative.ScreenBounds(Handle);
  DesktopNative.MoveScreen(Handle,windowStart.X+p.X-dragStart.X,windowStart.Y+p.Y-dragStart.Y,bounds.Width,bounds.Height);
 }
 protected override void OnMouseUp(MouseEventArgs e) {
  base.OnMouseUp(e);
  if(e.Button==MouseButtons.Left && dragging){dragging=false;Capture=false;MaintainDesktop();controller.RememberPosition(DesktopNative.ScreenBounds(Handle).Location);}
  if(e.Button==MouseButtons.Right)OpenMenu(new Point((int)(e.X/dpiScale),(int)(e.Y/dpiScale)),e.Location);
 }
 protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(dragging&&!Capture){dragging=false;controller.RememberPosition(DesktopNative.ScreenBounds(Handle).Location);}}
 void OpenMenu(Point logical,Point at) {
  activeMenu?.Dispose();var menu=new ContextMenuStrip();activeMenu=menu;
  void Units(string title,string[] options,string current,Action<string> set) {
   var item=new ToolStripMenuItem(title);
   foreach(string option in options){var entry=new ToolStripMenuItem(option=="m"?"Meters (m)":option=="ft"?"Feet (ft)":option){Checked=current==option};entry.Click+=(_,_)=>set(option);item.DropDownItems.Add(entry);}
   menu.Items.Add(item);
  }
  void Speed()=>Units("Speed units",["mph","km/h"],prefs.SpeedUnit,u=>controller.Set(prefs with{SpeedUnit=u}));
  void Elevation()=>Units("Elevation units",["m","ft"],prefs.ElevationUnit,u=>controller.Set(prefs with{ElevationUnit=u}));
  if(logical.Y is >=34 and <109 && logical.X>=150) {
   menu.Items.Add(new ToolStripMenuItem("Elevation units"){Enabled=false});
   foreach(string unit in new[]{"m","ft"}) {var item=new ToolStripMenuItem(unit=="m"?"Meters (m)":"Feet (ft)"){Checked=prefs.ElevationUnit==unit};item.Click+=(_,_)=>controller.Set(prefs with{ElevationUnit=unit});menu.Items.Add(item);}
   menu.Items.Add(new ToolStripSeparator());Speed();
  }else if(logical.Y is >=34 and <109 && logical.X<150) {
   menu.Items.Add(new ToolStripMenuItem("Speed units"){Enabled=false});
   foreach(string unit in new[]{"mph","km/h"}) {var item=new ToolStripMenuItem(unit){Checked=prefs.SpeedUnit==unit};item.Click+=(_,_)=>controller.Set(prefs with{SpeedUnit=unit});menu.Items.Add(item);}
   menu.Items.Add(new ToolStripSeparator());Elevation();
  }else{Speed();Elevation();}
  var opacity=new ToolStripMenuItem("Panel opacity");
  foreach(int percent in new[]{40,60,75,86,100}){var item=new ToolStripMenuItem(percent+"%"){Checked=Math.Abs(prefs.Opacity-percent/100.0)<.01};item.Click+=(_,_)=>controller.Set(prefs with{Opacity=percent/100.0});opacity.DropDownItems.Add(item);}menu.Items.Add(opacity);
  var size=new ToolStripMenuItem("Widget size");
  foreach(var value in new[]{.8,1,1.25,1.5}){var item=new ToolStripMenuItem($"{value*100:F0}%"){Checked=Math.Abs(prefs.Scale-value)<.01};item.Click+=(_,_)=>controller.Set(prefs with{Scale=value});size.DropDownItems.Add(item);}menu.Items.Add(size);
  var locked=new ToolStripMenuItem("Lock position"){Checked=prefs.Locked};locked.Click+=(_,_)=>controller.Set(prefs with{Locked=!prefs.Locked});menu.Items.Add(locked);
  menu.Items.Add("Reset to desktop corner",null,(_,_)=>{controller.Set(prefs with{X=null,Y=null});controller.ResetPosition();});
  menu.Items.Add(new ToolStripSeparator());
  menu.Items.Add("Open GPS monitor / connection settings",null,(_,_)=>controller.OpenMonitor());
  menu.Items.Add("Hide widget (restore from tray)",null,(_,_)=>controller.Set(prefs with{Visible=false}));
  if(controller.SettingsError is string error){menu.Items.Add(new ToolStripSeparator());menu.Items.Add(new ToolStripMenuItem(error){Enabled=false});}
  menu.Show(this,at);
 }
 protected override void OnPaint(PaintEventArgs e) {
  base.OnPaint(e);var g=e.Graphics;g.ScaleTransform((float)dpiScale,(float)dpiScale);g.SmoothingMode=SmoothingMode.AntiAlias;
  using var border=new Pen(Color.FromArgb(74,91,108));g.DrawRectangle(border,0,0,299,157);
  using var line=new Pen(Color.FromArgb(55,66,79));g.DrawLine(line,12,31,288,31);g.DrawLine(line,12,109,288,109);g.DrawLine(line,150,41,150,99);
  bool current=WidgetPreferences.IsCurrent(snapshot,DateTimeOffset.UtcNow);
  var s=current?snapshot:null;bool fix=s?.FixValid==true;
  Color led=fix?Color.LimeGreen:s?.PortOpen==true?Color.Goldenrod:Color.Gray;
  using var dot=new SolidBrush(led);g.FillEllipse(dot,114,13,8,8);
  string state=s is null?"Service unavailable":s.State switch{"Fix"=>"Valid GPS fix","Acquiring"=>"Acquiring satellites","WaitingForData"=>"Waiting for NMEA","Disabled"=>"GPS disabled",_=>"Receiver disconnected"};
  Draw("ICARUS GPS",12,9,9,Color.White,true);Draw(state,127,10,8,led);
  Draw("SPEED · "+prefs.SpeedUnit,12,38,8,Color.Silver);
  Draw("ELEV · MSL · "+prefs.ElevationUnit,160,38,8,Color.Silver);
  Value(Number(fix?prefs.Speed(s!.SpeedKph):null,"F1"),10);
  Value(Number(fix?prefs.Elevation(s!.AltitudeMeters):null,"F0"),158);
  Draw("Sats used",12,118,8,Color.Silver);Draw(s?.SatellitesUsed?.ToString()??"—",75,115,10,Color.White,true);
  Draw("In view",163,118,8,Color.Silver);Draw(s is null?"—":WidgetPreferences.InView(s.SatellitesInView)?.ToString()??"—",216,115,10,Color.White,true);
  string foot=s is null?"Waiting for GPS service":!fix&&s.LastFixAgeSeconds is double age?$"Last fix {age:F0}s ago":fix?"Right-click a reading for units":$"Acquiring: {s.AcquisitionSeconds:F0}s";
    Draw(foot,12,140,7,Color.FromArgb(160,177,194));
  void Value(string text,float x) {
   float points=30;
   // Keep normal readings equally large; accommodate unusually long values within their column.
   while(points>18){using var measure=new Font("Segoe UI",points*96/72,FontStyle.Bold,GraphicsUnit.Pixel);if(g.MeasureString(text,measure).Width<=132)break;points--;}
   Draw(text,x,55,points,Color.White,true);
  }
  void Draw(string text,float x,float y,float points,Color color,bool bold=false){using var font=new Font("Segoe UI",points*96/72,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);using var brush=new SolidBrush(color);g.DrawString(text,font,brush,x,y);}
 }
 static string Number(double? n,string format)=>n?.ToString(format,System.Globalization.CultureInfo.InvariantCulture)??"—";
 protected override void Dispose(bool disposing){if(disposing)activeMenu?.Dispose();base.Dispose(disposing);}
}
