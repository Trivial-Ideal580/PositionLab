using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PositionLab {
sealed class PlotLine {
    internal readonly string Name;
    internal readonly double[] Values;
    internal readonly Color Color;
    internal readonly bool Bars;
    internal PlotLine(string name,double[] values,Color color,bool bars=false) {
        Name=name; Values=values; Color=color; Bars=bars;
    }
}
sealed class Plot : Control {
    readonly PlotLine[] lines;
    readonly DateTime[] dates;
    readonly Bar[] candles;
    readonly double[] guides;
    readonly string units;
    readonly bool shade;
    readonly double min,max;
    readonly bool hasValues;
    int cursor=-1;

    internal Plot(DateTime[] days,string unit,PlotLine[] series,Bar[] candles=null,double[] guides=null,bool shade=false) {
        dates=days; units=unit; lines=series; this.candles=candles; this.guides=guides??new double[0]; this.shade=shade;
        var values=lines.SelectMany(line=>line.Values).Concat(this.guides);
        if(candles!=null) values=values.Concat(candles.Select(b=>b.Low)).Concat(candles.Select(b=>b.High));
        if(lines.Any(line=>line.Bars)) values=values.Concat(new[]{0.0});
        double lo=double.PositiveInfinity, hi=double.NegativeInfinity;
        foreach(double v in values.Where(Finite)) {lo=Math.Min(lo,v); hi=Math.Max(hi,v);}
        hasValues=Finite(lo);
        double pad=hi==lo ? Math.Max(.01,Math.Abs(hi)*.035) : (hi-lo)*.065;
        min=lo-pad; max=hi+pad;
        Dock=DockStyle.Fill; DoubleBuffered=true; BackColor=Theme.Card; ResizeRedraw=true;
        AccessibleName=candles!=null?"日 K 线价格图":string.Join("、",lines.Select(s=>s.Name))+"图";
    }
    static bool Finite(double value) { return !double.IsNaN(value)&&!double.IsInfinity(value); }
    Rectangle BoundsForPlot {get {return new Rectangle(76,55,Math.Max(1,Width-100),Math.Max(1,Height-89));}}
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        var r=BoundsForPlot;
        int next=r.Contains(e.Location)&&dates.Length>1?(int)Math.Round((double)(e.X-r.Left)*(dates.Length-1)/r.Width):-1;
        if(next!=cursor) {cursor=next; Invalidate();}
    }
    protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e); cursor=-1; Invalidate();}
    string Format(double value,bool axis=false) {
        if(!Finite(value)) return "—";
        if(units=="volume"||axis&&units!="P1"&&Math.Abs(value)>=1e5) {
            double magnitude=Math.Abs(value);
            if(magnitude>=1e8) return (value/1e8).ToString("0.##")+"亿";
            if(magnitude>=1e4) return (value/1e4).ToString("0.##")+"万";
            return value.ToString("0");
        }
        if(units!="P1"&&value!=0&&Math.Abs(value)<.01) return value.ToString("0.##E+0",Data.Inv);
        if(units=="P1"&&Math.Abs(value)>=1e4) return (100*value).ToString("0.##E+0",Data.Inv)+"%";
        return value.ToString(units,Data.Inv);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if(Width<180||Height<110) return;
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        if(dates.Length<2||!hasValues) {
            TextRenderer.DrawText(g,"当前区间有效数据不足",Theme.Body,ClientRectangle,Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            return;
        }
        var box=BoundsForPlot;
        Func<int,float> xAt=i=>box.Left+(float)i*box.Width/(dates.Length-1);
        Func<double,float> yAt=v=>box.Bottom-(float)((v-min)/(max-min))*box.Height;
        Action<string,Color,Rectangle,TextFormatFlags> label=(text,color,bounds,align)=>
            TextRenderer.DrawText(g,text,Theme.Chart,bounds,color,align|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter);
        using(var grid=new Pen(Theme.Grid))
        using(var guide=new Pen(Theme.SoftInk) {DashStyle=DashStyle.Dash})
        using(var cross=new Pen(Theme.SoftInk) {DashStyle=DashStyle.Dot}) {
            for(int j=0;j<=4;j++) {
                int y=box.Top+box.Height*j/4;
                g.DrawLine(grid,box.Left,y,box.Right,y);
                label(Format(max-(max-min)*j/4,true),Theme.Muted,new Rectangle(1,y-10,box.Left-12,20),TextFormatFlags.Right);
            }
            int ticks=Math.Min(dates.Length-1,Math.Max(1,box.Width/125));
            for(int j=0;j<=ticks;j++) {
                int i=(dates.Length-1)*j/ticks;
                int x=(int)xAt(i);
                if(j>0&&j<ticks) g.DrawLine(grid,x,box.Top,x,box.Bottom);
                label(Data.Day(dates[i]),Theme.Muted,new Rectangle(Math.Max(0,Math.Min(Width-102,x-51)),box.Bottom+8,102,22),TextFormatFlags.HorizontalCenter);
            }
            foreach(double value in guides) g.DrawLine(guide,box.Left,yAt(value),box.Right,yAt(value));
            var state=g.Save();
            g.SetClip(new Rectangle(box.Left-2,box.Top,box.Width+5,box.Height+1));
            if(candles!=null) DrawCandles(g,xAt,yAt,box);
            foreach(var line in lines.Where(x=>x.Bars)) DrawBars(g,line,xAt,yAt,box);
            foreach(var line in lines.Where(x=>!x.Bars)) DrawLine(g,line,xAt,yAt,box);
            if(cursor>=0) {
                float x=xAt(cursor);
                g.DrawLine(cross,x,box.Top,x,box.Bottom);
                foreach(var line in lines.Where(l=>!l.Bars&&Finite(l.Values[cursor]))) {
                    using(var marker=new SolidBrush(line.Color)) g.FillEllipse(marker,x-3,yAt(line.Values[cursor])-3,6,6);
                }
            }
            g.Restore(state);
        }
        DrawLegend(g,cursor<0?dates.Length-1:cursor);
    }
    void DrawLegend(Graphics g,int index) {
        int x=8,y=4;
        Action<string,Color> item=(value,color)=>{
            int size=TextRenderer.MeasureText(g,value,Theme.Chart,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix).Width+17;
            if(x>8&&x+size>Width-5) {x=8; y+=20;}
            TextRenderer.DrawText(g,value,Theme.Chart,new Point(x,y),color,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
            x+=size;
        };
        item(Data.Day(dates[index]),Theme.Text);
        if(candles!=null) {
            var b=candles[index];
            item("开 "+Format(b.Open)+"  高 "+Format(b.High)+"  低 "+Format(b.Low)+"  收 "+Format(b.Close),b.Close>=b.Open?Theme.Rise:Theme.Fall);
        }
        foreach(var line in lines) item(line.Name+"  "+Format(line.Values[index]),line.Color);
        if(guides.Length>0&&guides.Any(v=>v!=0)) item("参考线 "+string.Join(" / ",guides.Select(v=>Format(v))),Theme.Muted);
    }
    void DrawCandles(Graphics g,Func<int,float> xAt,Func<double,float> yAt,Rectangle box) {
        float width=Math.Max(1,Math.Min(9,(float)box.Width/dates.Length*.64f));
        using(var up=new SolidBrush(Theme.Rise))
        using(var down=new SolidBrush(Theme.Fall))
        using(var upPen=new Pen(Theme.Rise))
        using(var downPen=new Pen(Theme.Fall)) {
            for(int i=0;i<candles.Length;i++) {
                Bar b=candles[i]; bool above=b.Close>=b.Open; float x=xAt(i);
                g.DrawLine(above?upPen:downPen,x,yAt(b.High),x,yAt(b.Low));
                float top=yAt(Math.Max(b.Open,b.Close));
                g.FillRectangle(above?up:down,x-width/2,top,width,Math.Max(1,yAt(Math.Min(b.Open,b.Close))-top));
            }
        }
    }
    void DrawBars(Graphics g,PlotLine line,Func<int,float> xAt,Func<double,float> yAt,Rectangle box) {
        float width=Math.Max(1,Math.Min(11,(float)box.Width/dates.Length*.69f));
        float zero=yAt(0);
        using(var positive=new SolidBrush(Color.FromArgb(155,line.Color)))
        using(var negative=new SolidBrush(Color.FromArgb(180,Theme.Fall))) {
            for(int i=0;i<dates.Length;i++) {
                double v=line.Values[i];
                if(!Finite(v)||v==0) continue;
                float y=yAt(v);
                g.FillRectangle(v>0?positive:negative,xAt(i)-width/2,Math.Min(y,zero),width,Math.Max(1,Math.Abs(y-zero)));
            }
        }
    }
    void DrawLine(Graphics g,PlotLine line,Func<int,float> xAt,Func<double,float> yAt,Rectangle box) {
        var points=new System.Collections.Generic.List<PointF>();
        int step=Math.Max(1,dates.Length/Math.Max(1,box.Width*2));
        using(var ink=new Pen(line.Color,1.8f)) {
            Action flush=()=>{
                if(points.Count>1) {
                    if(shade&&line==lines[0]) {
                        using(var fill=new LinearGradientBrush(box,Color.FromArgb(42,line.Color),Color.FromArgb(3,line.Color),90))
                            g.FillPolygon(fill,new[]{new PointF(points[0].X,box.Bottom)}.Concat(points).Concat(new[]{new PointF(points.Last().X,box.Bottom)}).ToArray());
                    }
                    g.DrawLines(ink,points.ToArray());
                }
                points.Clear();
            };
            for(int i=0;i<dates.Length;i=Math.Min(i+step,dates.Length-1)) {
                if(Finite(line.Values[i])) points.Add(new PointF(xAt(i),yAt(line.Values[i]))); else flush();
                if(i==dates.Length-1) break;
            }
            flush();
        }
    }
}
static class Theme {
    internal static readonly Color Background=C("#F1F4F9"), Sidebar=C("#17253D"), SidebarActive=C("#293D60"), SidebarText=C("#F5F8FF"), SidebarMuted=C("#B7C6E1"),
        Card=C("#FFFFFF"), Surface=C("#EAF0FA"), Border=C("#DCE4EF"), Grid=C("#E6EDF5"), SoftInk=C("#A3B1C8"),
        Text=C("#172844"), Muted=C("#506584"), Accent=C("#2856D9"), AccentHover=C("#1C43BA"),
        Violet=C("#764BB5"), Amber=C("#956019"), Teal=C("#087B78"), Rise=C("#BB4D51"), Fall=C("#008273");
    static Color C(string value) {return ColorTranslator.FromHtml(value);}
    internal static readonly Font Body=new Font("Microsoft YaHei UI",17,FontStyle.Regular,GraphicsUnit.Pixel),
        Small=new Font("Microsoft YaHei UI",16,FontStyle.Regular,GraphicsUnit.Pixel),
        Chart=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel),
        Head=new Font("Microsoft YaHei UI",31,FontStyle.Bold,GraphicsUnit.Pixel),
        Section=new Font("Microsoft YaHei UI",18,FontStyle.Bold,GraphicsUnit.Pixel),
        Metric=new Font("Microsoft YaHei UI",29,FontStyle.Bold,GraphicsUnit.Pixel),
        MetricSmall=new Font("Microsoft YaHei UI",21,FontStyle.Bold,GraphicsUnit.Pixel);
}
}


