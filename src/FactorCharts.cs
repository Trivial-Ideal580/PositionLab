using System;
using System.Linq;
using System.Windows.Forms;

namespace PositionLab {
static class FactorCharts {
    internal static readonly string[] Tabs={
        "日 K 线","均线","布林带","MACD","RSI","成交量","动量","波动率"
    };
    internal static Control Create(Series s,int mode,int range) {
        int length=Math.Min(range,s.Bars.Length), start=s.Bars.Length-length;
        var bars=s.Bars.Skip(start).ToArray();
        var dates=bars.Select(b=>b.Date).ToArray();
        Func<string,double[]> cut=field=>{
            var result=new double[length];
            Array.Copy(s.V[field],start,result,0,length);
            return result;
        };
        Func<string,System.Drawing.Color,PlotLine> line=(field,color)=>new PlotLine(field,cut(field),color);
        switch(mode) {
            case 0:
                return new Plot(dates,"N2",new[]{line("MA5",Theme.Amber),line("MA20",Theme.Accent),line("MA60",Theme.Violet)},bars);
            case 1:
                return new Plot(dates,"N2",new[]{new PlotLine("收盘",cut("CLOSE"),Theme.Text),line("MA10",Theme.Accent),line("MA30",Theme.Amber),
                    line("MA120",Theme.Violet),line("EMA12",Theme.Teal),line("EMA26",Theme.Rise)});
            case 2:
                return new Plot(dates,"N2",new[]{new PlotLine("收盘",cut("CLOSE"),Theme.Text),new PlotLine("上轨",cut("BOLLUP"),Theme.Teal),
                    new PlotLine("中轨",cut("BOLLMID"),Theme.Accent),new PlotLine("下轨",cut("BOLLDOWN"),Theme.Violet)});
            case 3:
                return new Plot(dates,"0.000",new[]{new PlotLine("MACD 柱",cut("MACD"),Theme.Rise,true),line("DIF",Theme.Accent),line("DEA",Theme.Amber)},guides:new[]{0.0});
            case 4:
                return new Plot(dates,"0.0",new[]{line("RSI14",Theme.Accent)},guides:new[]{30.0,70.0},shade:true);
            case 5:
                return new Plot(dates,"volume",new[]{new PlotLine("成交量",cut("VOLUME"),Theme.Accent,true),line("VOLMA5",Theme.Amber)});
            case 6:
                return new Plot(dates,"P1",new[]{new PlotLine("单日涨跌",cut("RET1"),Theme.Rise,true),
                    new PlotLine("20 日动量",cut("MOM20"),Theme.Accent)},guides:new[]{0.0});
            default:
                var atr=cut("ATR14");
                for(int i=0;i<atr.Length;i++) atr[i]/=bars[i].Close;
                return new Plot(dates,"P1",new[]{new PlotLine("20 日年化波动",cut("VOL20"),Theme.Accent),
                    new PlotLine("ATR14 / 收盘价",atr,Theme.Violet)},shade:true);
        }
    }
}
}
