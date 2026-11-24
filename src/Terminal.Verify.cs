using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PositionLab {
partial class Terminal {
    internal void VerifyUi(string folder) {
        verifying=true;
        Exception failure=null;
        Shown+=async (o,e)=>{
            try { await ExerciseUi(folder); }
            catch(Exception error) { failure=error; }
            finally { Close(); }
        };
        Application.Run(this);
        if(failure!=null) throw failure;
    }

    async Task ExerciseUi(string folder) {
        Func<string,Task> click=async label=>{
            var button=Children(this).OfType<Button>().First(b=>b.Text==label);
            button.PerformClick();
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(busy&&watch.ElapsedMilliseconds<15000) await Task.Delay(12);
            if(busy) throw new Exception("UI action timed out: "+label);
        };
        Action<string> capture=file=>{
            Refresh();
            using(var img=new Bitmap(Width,Height)) {
                DrawToBitmap(img,new Rectangle(0,0,Width,Height));
                img.Save(Path.Combine(folder,file),System.Drawing.Imaging.ImageFormat.Png);
            }
        };
        capture("factors.png");
        foreach(string mode in FactorCharts.Tabs) {
            await click(mode);
            var plot=Children(this).OfType<Plot>().Single();
            if(plot.Width<200||plot.Height<110) throw new Exception("Invalid factor chart layout: "+mode);
            capture("chart-"+Array.IndexOf(FactorCharts.Tabs,mode)+".png");
        }
        var rangeButton=Children(this).OfType<Button>().Single(b=>(b.AccessibleName??"").StartsWith("图表范围"));
        ((ToolStripMenuItem)rangeButton.ContextMenuStrip.Items[0]).PerformClick();
        if(!rangeButton.Text.Contains("60")||chartRange!=60) throw new Exception("Chart range did not update.");
        await click("展开明细");
        if(Children(this).OfType<Plot>().Single().Visible) throw new Exception("Expanded factor table did not hide the chart.");
        await click("返回图表"); await click("日 K 线");
        await click("相关性与概率"); await click("拟合与验证");
        if(!status.Text.Contains("匹配")) throw new Exception("Model page did not render: "+status.Text);
        capture("probability.png");
        await click("滚动验证");
        var foldTable=Children(this).OfType<DataGridView>().FirstOrDefault(g=>string.Equals(Convert.ToString(g.Columns[0].HeaderText),"训练截至",StringComparison.Ordinal));
        if(foldTable==null||foldTable.Rows.Count<3) throw new Exception("Walk-forward table did not render: "+(foldTable==null?"missing":"rows="+foldTable.Rows.Count));
        capture("walkforward.png");
        await click("因子与系数");
        await click("策略审计与回测"); await click("未来函数审计"); await click("运行回测");
        if(!status.Text.Contains("基准")) throw new Exception("Backtest page did not render: "+status.Text);
        capture("strategy.png");
        await click("成交记录"); capture("fills.png");
        await click("持仓透视"); await click("加载演示持仓"); await click("分析组合");
        if(!status.Text.Contains("共 3 笔")) throw new Exception("Portfolio page did not render: "+status.Text);
        capture("positions.png");
        await click("配置与风格"); capture("risk.png");
        await click("数据与声明"); capture("help.png");
        Size=MinimumSize;
        await click("因子工作台"); capture("minimum.png");
        await click("展开明细"); capture("minimum-factors-table.png"); await click("返回图表");
        await click("相关性与概率"); await click("拟合与验证"); capture("minimum-probability.png");
        await click("策略审计与回测");
        foreach(var control in Children(this).Where(c=>c is DateTimePicker||c is TextBox))
            if(!control.Parent.ClientRectangle.Contains(control.Bounds)) throw new Exception("Backtest field is clipped at minimum size.");
        await click("运行回测"); await click("成交记录"); capture("minimum-fills.png");
        await click("持仓透视"); await click("分析组合"); capture("minimum-positions.png");
        await click("数据与声明"); capture("minimum-help.png");
    }

    static IEnumerable<Control> Children(Control root) {
        foreach(Control child in root.Controls) {
            yield return child;
            foreach(var grandchild in Children(child)) yield return grandchild;
        }
    }

}
}
