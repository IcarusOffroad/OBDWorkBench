using System.Windows;
using System.Windows.Media;
namespace OBDWorkbench.Controls;

// Resolution-independent feather, intermeshing gears and circuit emblem.
public sealed class IcarusMark : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double scale = Math.Min(ActualWidth / 100, ActualHeight / 120);
        if (scale <= 0) return;
        dc.PushTransform(new ScaleTransform(scale, scale));
        var copper = new SolidColorBrush(Color.FromRgb(217, 154, 108));
        var ivory = new SolidColorBrush(Color.FromRgb(243, 238, 227));
        dc.DrawGeometry(ivory, null, Geometry.Parse("M20,91 C12,60 39,43 72,5 C70,40 52,65 20,91 Z"));
        dc.DrawGeometry(copper, null, Geometry.Parse("M23,86 C49,65 76,53 82,22 C84,59 67,80 23,86 Z"));
        var pen = new Pen(copper, 2);
        for (int i = 0; i < 4; i++)
        {
            double y = 43 + i * 12;
            dc.DrawLine(pen, new Point(52-i*4,y+20), new Point(84,y+5));
            dc.DrawLine(pen, new Point(84,y+5), new Point(93,y-7));
            dc.DrawEllipse(null, pen, new Point(93,y-10),3,3);
        }
        var cut = new Pen(new SolidColorBrush(Color.FromRgb(25,28,28)), 1.6);
        for (int i=0;i<5;i++) dc.DrawLine(cut,new Point(22+i*5,82-i*10),new Point(53+i*3,53-i*8));
        Gear(dc, new Point(26,99), 17, copper);
        Gear(dc, new Point(58,108), 12, copper);
        dc.Pop();
    }
    private static void Gear(DrawingContext dc, Point center, double r, Brush color)
    {
        var outline = new StreamGeometry();
        using (var g = outline.Open())
        {
            for(int i=0;i<48;i++)
            {
                double a=i*Math.PI/24, radius=i%4 is 1 or 2 ? r : r*0.79;
                var point=new Point(center.X+Math.Cos(a)*radius,center.Y+Math.Sin(a)*radius);
                if(i==0) g.BeginFigure(point,true,true); else g.LineTo(point,true,false);
            }
        }
        var ring = new CombinedGeometry(GeometryCombineMode.Exclude, outline, new EllipseGeometry(center,r*.42,r*.42));
        dc.DrawGeometry(color,null,ring);
    }
}
