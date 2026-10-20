using System.Runtime.InteropServices;
using System.Text;
namespace Icarus.Gps.Tray;
// Keep a normal, non-activating tool window above Explorer's desktop, below ordinary apps.
// No SetParent / WS_CHILD: cross-process desktop parenting can hide a layered window.
static class DesktopNative {
 public const uint NoSize=1,NoMove=2,NoZOrder=4,NoActivate=0x10,ShowWindow=0x40;
 public static readonly IntPtr TopMost=new(-1),NotTopMost=new(-2);
 [StructLayout(LayoutKind.Sequential)]public struct Rect {public int Left,Top,Right,Bottom;}
 delegate bool EnumProc(IntPtr hwnd,IntPtr data);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr data);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder text,int count);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr h,uint command);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
 public static void PlaceAboveDesktop(IntPtr h) {
  IntPtr desktop=IntPtr.Zero;
  // Enumerated in top-to-bottom Z order. Anchor above the first visible shell surface.
  EnumWindows((w,_)=>{
   if(w==h || !IsWindowVisible(w))return true;
   var name=new StringBuilder(128);GetClassName(w,name,name.Capacity);
   if(name.ToString() is "Progman" or "WorkerW"){desktop=w;return false;}
   return true;
  },IntPtr.Zero);
  if(desktop==IntPtr.Zero)return; // Preserve current position/order while Explorer is unavailable.
  IntPtr previous=GetWindow(desktop,3); // GW_HWNDPREV, immediately above shell surface.
  if(previous==h)return; // Already above desktop: no need to disturb other windows.
  SetWindowPos(h,previous,0,0,0,0,NoSize|NoMove|NoActivate);
 }
 public static void MoveScreen(IntPtr h,int x,int y,int width,int height) {
  SetWindowPos(h,IntPtr.Zero,x,y,width,height,NoZOrder|NoActivate|ShowWindow);
 }
 public static Rectangle ScreenBounds(IntPtr h) {
  GetWindowRect(h,out var r);return Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
 }
}
