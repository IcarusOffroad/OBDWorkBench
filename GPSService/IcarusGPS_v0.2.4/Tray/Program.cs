using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
namespace Icarus.Gps.Tray;
static class Program {
 [STAThread] static int Main(string[] args) {
  Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
  Application.EnableVisualStyles();
  Application.SetCompatibleTextRenderingDefault(false);
  if(args.Length==2 && (args[0]=="--apply-config" || args[0]=="--control")) {
   try {
    string request;
    if(args[0]=="--control") {
     if(args[1]!="Release" && args[1]!="Resume") throw new ArgumentException("Unknown action.");
     request=Wire.Encode(new {Action=args[1]});
    } else {
     var c=Wire.Decode<GpsConfig>(Encoding.UTF8.GetString(Convert.FromBase64String(args[1])));c.Validate();request=Wire.Encode(c);
    }
    using var pipe=new NamedPipeClientStream(".",Wire.ConfigPipe,PipeDirection.InOut,PipeOptions.Asynchronous);
    pipe.Connect(5000);
    using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};writer.WriteLine(request);
    using var reader=new StreamReader(pipe);
    var read=reader.ReadLineAsync();if(!read.Wait(7000))throw new IOException("Service did not acknowledge settings.");
    using var result=JsonDocument.Parse(read.Result??"{}");
    if(!result.RootElement.GetProperty("Ok").GetBoolean()) throw new IOException(result.RootElement.GetProperty("Error").GetString());
    return 0;
   } catch(Exception e) {MessageBox.Show(e.Message,"GPS settings were not saved",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
  using var mutex=new Mutex(true,@"Local\Icarus.GPS.Tray.v1",out bool first);
  if(!first) {MessageBox.Show("Icarus GPS is already running. Open it from the system tray (including the hidden icons arrow).","Icarus GPS");return 0;}
  Application.Run(new MonitorForm(args.Contains("--tray")));return 0;
 }
}
sealed class MonitorForm : Form {
 readonly NotifyIcon tray=new();
 readonly Icon appIcon=LoadBrandIcon("IcarusGPS.Icon");
 readonly WidgetController widget;
 readonly CancellationTokenSource stop=new();
 readonly Label status=new(), values=new(), serviceInfo=new();
 readonly TextBox raw=new();
 readonly ComboBox port=new(),baud=new(),virtualPort=new(),virtualBaud=new();
 readonly CheckBox virtualEnabled=new(){Text="Broadcast NMEA to virtual COM pair",AutoSize=true};
 ToolStripMenuItem? releaseItem,resumeItem;
 readonly CheckBox enabled=new(){Text="Enable GPS connection",Checked=true,AutoSize=true},dtr=new(){Text="DTR",AutoSize=true},rts=new(){Text="RTS",AutoSize=true};
 readonly Button save=new(){Text="Save settings (administrator)",AutoSize=true},copy=new(){Text="Copy latitude,longitude",AutoSize=true};
 readonly Dictionary<string,Icon> icons=new();
 Snapshot? latest;
 bool closing,loaded;
 string lastRaw="",iconState="";
 readonly bool startHidden;
 public MonitorForm(bool hidden) {
  Icon=appIcon;
  widget=new WidgetController(ShowMonitor);
  startHidden=hidden;Text="Icarus Offroad — GPS Monitor v0.2.4";Size=new Size(820,680);MinimumSize=new Size(650,580);
  BackColor=Color.FromArgb(28,32,37);ForeColor=Color.Gainsboro;Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterScreen;
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=4};
  root.RowStyles.Add(new RowStyle(SizeType.Absolute,44));root.RowStyles.Add(new RowStyle(SizeType.Absolute,26));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,30));Controls.Add(root);
  status.Dock=DockStyle.Fill;status.Font=new Font(Font.FontFamily,15,FontStyle.Bold);status.Text="● Connecting to GPS service…";root.Controls.Add(status,0,0);
  serviceInfo.Dock=DockStyle.Fill;serviceInfo.Text="The service continues running when this window is closed.";root.Controls.Add(serviceInfo,0,1);
  var tabs=new TabControl{Dock=DockStyle.Fill};root.Controls.Add(tabs,0,2);
  var overview=new TabPage("GPS status"){BackColor=BackColor,ForeColor=ForeColor};tabs.TabPages.Add(overview);
  var view=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=1,RowCount=2};view.RowStyles.Add(new RowStyle(SizeType.Percent,100));view.RowStyles.Add(new RowStyle(SizeType.Absolute,46));overview.Controls.Add(view);
  values.Dock=DockStyle.Fill;values.Font=new Font("Consolas",11);values.Text="Waiting for service data…";view.Controls.Add(values,0,0);view.Controls.Add(copy,0,1);copy.Enabled=false;copy.Click+=(_,_)=>{if(latest?.FixValid==true && DateTimeOffset.UtcNow-latest.PublishedUtc<TimeSpan.FromSeconds(5)) Clipboard.SetText(FormattableString.Invariant($"{latest.Latitude:F7},{latest.Longitude:F7}"));};
  var settings=new TabPage("Connection settings"){BackColor=BackColor,ForeColor=ForeColor};tabs.TabPages.Add(settings);
  var config=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(14),AutoScroll=true};settings.Controls.Add(config);
  config.Controls.Add(enabled);config.Controls.Add(new Label{Text="GPS receiver COM port",AutoSize=true});
  var portRow=new FlowLayoutPanel{AutoSize=true};port.Width=160;portRow.Controls.Add(port);var refresh=new Button{Text="Refresh ports",AutoSize=true};portRow.Controls.Add(refresh);refresh.Click+=(_,_)=>RefreshPorts();config.Controls.Add(portRow);
  config.Controls.Add(new Label{Text="Baud rate (8 data bits, no parity, 1 stop bit)",AutoSize=true});baud.DropDownStyle=ComboBoxStyle.DropDownList;baud.Items.AddRange(new object[]{1200,2400,4800,9600,19200,38400,57600,115200,230400});baud.SelectedItem=9600;config.Controls.Add(baud);
  var signals=new FlowLayoutPanel{AutoSize=true};signals.Controls.Add(dtr);signals.Controls.Add(rts);config.Controls.Add(signals);
  config.Controls.Add(new Label{Text="DTR / RTS default off. Enable only if your receiver requires them.\nSaving reconnects the receiver and automatically persists these settings.\nDisable GPS here to release the COM port for another application.",AutoSize=true,Margin=new Padding(0,12,0,12)});
  config.Controls.Add(new Label{Text="Optional virtual COM output (requires a third-party virtual COM pair driver)",AutoSize=true,Margin=new Padding(0,12,0,3)});
  config.Controls.Add(virtualEnabled);
  var virtualRow=new FlowLayoutPanel{AutoSize=true};
  virtualRow.Controls.Add(new Label{Text="Icarus output",AutoSize=true});virtualPort.Width=90;virtualPort.Text="COM15";virtualRow.Controls.Add(virtualPort);
  virtualRow.Controls.Add(new Label{Text="Baud",AutoSize=true});virtualBaud.DropDownStyle=ComboBoxStyle.DropDownList;virtualBaud.Items.AddRange(new object[]{1200,2400,4800,9600,19200,38400,57600,115200,230400});virtualBaud.SelectedItem=9600;virtualRow.Controls.Add(virtualBaud);config.Controls.Add(virtualRow);
  config.Controls.Add(new Label{Text="Example: Icarus writes COM15; Google Earth reads its paired COM16. Never choose the receiver port here.",AutoSize=true});
  config.Controls.Add(save);save.Click+=async(_,_)=>await Save();
  var debug=new TabPage("NMEA diagnostics"){BackColor=BackColor};tabs.TabPages.Add(debug);raw.Multiline=true;raw.ReadOnly=true;raw.Dock=DockStyle.Fill;raw.ScrollBars=ScrollBars.Both;raw.WordWrap=false;raw.BackColor=Color.FromArgb(18,21,25);raw.ForeColor=Color.LightGreen;raw.Font=new Font("Consolas",10);debug.Controls.Add(raw);
  var footer=new Label{Dock=DockStyle.Fill,Text="Close to hide in the tray • Right-click the tray icon to exit the monitor",AutoSize=false};root.Controls.Add(footer,0,3);
  icons["gray"]=LoadBrandIcon("IcarusGPS.Icon.offline");
  icons["amber"]=LoadBrandIcon("IcarusGPS.Icon.acquiring");
  icons["green"]=LoadBrandIcon("IcarusGPS.Icon.fix");
  tray.Icon=icons["gray"];tray.Text="Icarus GPS — service unavailable";tray.Visible=true;tray.DoubleClick+=(_,_)=>ShowMonitor();
  var menu=new ContextMenuStrip();menu.Items.Add("Open GPS monitor",null,(_,_)=>ShowMonitor());var showWidget=new ToolStripMenuItem("Show desktop widget");showWidget.Click+=(_,_)=>widget.Show();menu.Items.Add(showWidget);
  releaseItem=new ToolStripMenuItem("Release physical GPS port for Google Earth");releaseItem.Click+=async(_,_)=>await ControlPort("Release");menu.Items.Add(releaseItem);
  resumeItem=new ToolStripMenuItem("Resume GPS receiver");resumeItem.Click+=async(_,_)=>await ControlPort("Resume");menu.Items.Add(resumeItem);
  menu.Items.Add(new ToolStripSeparator());
  menu.Items.Add("Hide desktop widget",null,(_,_)=>widget.Hide());
  menu.Items.Add("Recover widget / bring forward for 10 seconds",null,(_,_)=>widget.Recover());
  menu.Items.Add("Widget details…",null,(_,_)=>MessageBox.Show(widget.Diagnostics,"Desktop widget details"));
  menu.Items.Add("Reset widget position",null,(_,_)=>widget.ResetPosition());menu.Opening+=(_,_)=>{showWidget.Checked=widget.Preferences.Visible;};
  menu.Items.Add("Exit monitor and widget (service keeps running)",null,(_,_)=>{closing=true;Close();});tray.ContextMenuStrip=menu;
  RefreshPorts();port.Text="COM6";
  FormClosing+=(_,e)=>{if(!closing && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
  Shown+=(_,_)=>{if(startHidden)Hide();widget.Start();_ = Listen();};
 }
 static Icon LoadBrandIcon(string resource) {
  using var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
   ?? throw new InvalidOperationException("Missing embedded GPS icon: "+resource);
  using var original=new Icon(stream);
  return (Icon)original.Clone();
 }
 void RefreshPorts() {
  string previous=port.Text;port.Items.Clear();
  try {using var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");if(key!=null)foreach(string s in key.GetValueNames().Select(x=>key.GetValue(x)?.ToString()).OfType<string>().Distinct().OrderBy(x=>x))port.Items.Add(s);}catch{}
  port.Text=previous;
 }
 void ShowMonitor(){Show();WindowState=FormWindowState.Normal;Activate();}
 async Task Save() {
  try {
   var c=new GpsConfig{Enabled=enabled.Checked,Port=port.Text.Trim().ToUpperInvariant(),Baud=Convert.ToInt32(baud.SelectedItem),Dtr=dtr.Checked,Rts=rts.Checked,VirtualOutputEnabled=virtualEnabled.Checked,VirtualOutputPort=virtualPort.Text.Trim().ToUpperInvariant(),VirtualOutputBaud=Convert.ToInt32(virtualBaud.SelectedItem)};c.Validate();save.Enabled=false;
   string encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(Wire.Encode(c)));
   using var process=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--apply-config "+encoded){UseShellExecute=true,Verb="runas"});
   if(process==null)throw new IOException("Could not start settings helper.");await process.WaitForExitAsync();
   if(process.ExitCode==0) MessageBox.Show("Settings saved. The service will reconnect automatically.","Icarus GPS");
  } catch(Exception e) {MessageBox.Show(e.Message,"Unable to save settings",MessageBoxButtons.OK,MessageBoxIcon.Information);}finally{save.Enabled=true;}
 }
 async Task ControlPort(string action) {
  try {
   using var process=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--control "+action){UseShellExecute=true,Verb="runas"});
   if(process==null) throw new IOException("Unable to launch control helper.");
   await process.WaitForExitAsync();
   if(process.ExitCode!=0) MessageBox.Show("GPS control command failed. See service status.","Icarus GPS");
  } catch(Exception e) {MessageBox.Show(e.Message,"GPS control",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
 }
 async Task Listen() {
  while(!stop.IsCancellationRequested) {
   try {
    using var pipe=new NamedPipeClientStream(".",Wire.DataPipe,PipeDirection.InOut,PipeOptions.Asynchronous);
    await pipe.ConnectAsync(3000,stop.Token);using var reader=new StreamReader(pipe);
    while(!stop.IsCancellationRequested) {
     using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(5000);
     var line=await reader.ReadLineAsync(deadline.Token);if(line==null)throw new IOException("Service disconnected.");
     Render(Wire.Decode<Snapshot>(line));
    }
   } catch(Exception e) when(e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or JsonException) {
    if(stop.IsCancellationRequested)break;
    latest=null;widget.Update(null);copy.Enabled=false;SetStatus("gray","Service unavailable");serviceInfo.Text="Install/start IcarusGPS service. "+e.Message;values.Text="No current GPS data. Reconnecting…";raw.Clear();lastRaw="";
   }
   try{await Task.Delay(2000,stop.Token);}catch(OperationCanceledException){break;}
  }
 }
 void SetStatus(string color,string text) {
  status.Text="● "+text;status.ForeColor=color=="green"?Color.LightGreen:color=="amber"?Color.Goldenrod:Color.Silver;
  if(iconState!=color){tray.Icon=icons[color];iconState=color;}string tooltip="Icarus GPS — "+text;tray.Text=tooltip[..Math.Min(63,tooltip.Length)];
 }
 static string F(double? value,string format="F2")=>value?.ToString(format,System.Globalization.CultureInfo.InvariantCulture)??"—";
 void Render(Snapshot s) {
  latest=s;widget.Update(s);SetStatus(s.FixValid?"green":s.PortOpen?"amber":"gray",s.State switch{"Fix"=>"Valid GPS fix","Acquiring"=>"Finding satellites","WaitingForData"=>"COM open — waiting for NMEA","Disabled"=>"GPS disabled",_=>"Receiver disconnected"});
  serviceInfo.Text=s.Message;copy.Enabled=s.FixValid;
  if(!loaded){enabled.Checked=s.Config.Enabled;port.Text=s.Config.Port;baud.SelectedItem=s.Config.Baud;dtr.Checked=s.Config.Dtr;rts.Checked=s.Config.Rts;virtualEnabled.Checked=s.Config.VirtualOutputEnabled;virtualPort.Text=s.Config.VirtualOutputPort;virtualBaud.SelectedItem=s.Config.VirtualOutputBaud;loaded=true;}
  string inView=string.Join(", ",s.SatellitesInView.Select(x=>x.Key+": "+x.Value));
  values.Text=$"Latitude           {F(s.Latitude,"F7")}\nLongitude          {F(s.Longitude,"F7")}\nSpeed              {F(s.SpeedKph)} km/h  /  {F(s.SpeedKph/1.609344)} mph\nCourse             {F(s.CourseDegrees)}°\nAltitude (MSL)     {F(s.AltitudeMeters)} m\nSatellites used    {s.SatellitesUsed?.ToString()??"—"}\nIn view by talker  {(inView.Length>0?inView:"—")}\nHDOP / quality     {F(s.Hdop)} / {s.FixQuality?.ToString()??"—"}\nGPS UTC            {s.GpsUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")??"—"}\nLast fix age       {F(s.LastFixAgeSeconds,"F0")} seconds\n{(s.TimeToFirstFixSeconds.HasValue?"First fix in":"Acquisition time"),-19}{F(s.AcquisitionSeconds,"F0")} seconds\nNMEA accepted      {s.ValidSentences}   rejected: {s.RejectedSentences}";
  string text=string.Join(Environment.NewLine,s.RecentNmea);if(text!=lastRaw){lastRaw=text;raw.Text=text;raw.SelectionStart=raw.TextLength;raw.ScrollToCaret();}
 }
 protected override void Dispose(bool disposing) {if(disposing){stop.Cancel();widget.Dispose();tray.Visible=false;tray.Dispose();foreach(var icon in icons.Values)icon.Dispose();appIcon.Dispose();}base.Dispose(disposing);}
}
