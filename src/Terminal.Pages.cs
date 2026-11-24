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
    void ExportBacktest(Backtest report,bool equity) {
        if(report==null) { SetStatus("请先运行回测。"); return; }
        using(var file=new SaveFileDialog{Filter="CSV|*.csv",FileName=equity?"backtest-equity.csv":"backtest-fills.csv",RestoreDirectory=true}) {
            if(file.ShowDialog(this)!=DialogResult.OK) return;
            string path=file.FileName;
            ExportAsync(path,report,equity);
        }
    }

    async void ExportAsync(string path,Backtest report,bool equity) {
        await Work(()=>{
            using(var writer=new StreamWriter(path,false,new UTF8Encoding(true))) {
                if(equity) {
                    writer.WriteLine("date,strategy_equity,benchmark_equity");
                    for(int i=0;i<report.Dates.Length;i++)
                        writer.WriteLine(string.Format(Data.Inv,"{0},{1:R},{2:R}",Data.Day(report.Dates[i]),report.Equity[i],report.Benchmark[i]));
                } else {
                    writer.WriteLine("fill_date,signal_date,side,price,quantity,fee,realized_pnl");
                    foreach(var t in report.Fills) writer.WriteLine(string.Format(Data.Inv,"{0},{1},{2},{3:R},{4:R},{5:R},{6}",
                        Data.Day(t.Date),Data.Day(t.Signal),t.Side,t.Price,t.Quantity,t.Fee,double.IsNaN(t.Pnl)?"":t.Pnl.ToString("R",Data.Inv)));
                }
            }
            return Path.GetFileName(path);
        },name=>SetStatus("已导出 "+name+"。"));
    }

    void Factors() {
        var s=Selected; int i=s.Bars.Length-1;
        var panel=Rows(body,44,91,-64,-36);
        var tools=Flow(); panel.Controls.Add(tools,0,0);
        var lookback=new NumericUpDown{Minimum=20,Maximum=2000,Value=window,Increment=20,Width=76,BackColor=Theme.Card,ForeColor=Theme.Text,Margin=new Padding(0,5,12,0)};
        Field(tools,"分位回看（日）",lookback);
        var grid=Grid("指标","当前值","历史分位","有效样本");
        AlignNumbers(grid,1,3);
        var rangeMenu=new ContextMenuStrip{Font=Theme.Small};
        Button rangeChoice=null;
        Action redraw=null;
        rangeChoice=ButtonOf("近 "+chartRange+" 日  ▾",()=>rangeMenu.Show(rangeChoice,0,rangeChoice.Height),false,120);
        rangeChoice.AccessibleName="图表范围，近 "+chartRange+" 个交易日";
        foreach(int range in new[]{60,120,250}) {
            int days=range;
            var option=new ToolStripMenuItem("近 "+days+" 日"){Checked=days==chartRange};
            option.Click+=(o,e)=>{
                chartRange=days; rangeChoice.Text="近 "+days+" 日  ▾";
                rangeChoice.AccessibleName="图表范围，近 "+days+" 个交易日";
                foreach(ToolStripMenuItem item in rangeMenu.Items) item.Checked=item==option;
                redraw();
            };
            rangeMenu.Items.Add(option);
        }
        rangeChoice.ContextMenuStrip=rangeMenu; rangeChoice.Disposed+=(o,e)=>rangeMenu.Dispose();
        Field(tools,"图表范围",rangeChoice);
        tools.Controls.Add(ButtonOf("导出指标",()=>ExportGrid(grid,"factors",this)));
        var kpis=PanelIn(panel,1);
        var chartShell=PanelIn(panel,2);
        chartShell.Padding=new Padding(0,3,0,9);
        var plotPanel=new Panel{Dock=DockStyle.Fill,BackColor=Theme.Card,Padding=new Padding(14,7,14,3)};
        chartShell.Controls.Add(plotPanel);
        var plotLayout=Rows(plotPanel,42,-100);
        var bar=Flow(); plotLayout.Controls.Add(bar,0,0);
        var graph=PanelIn(plotLayout,1);
        var buttons=new Button[FactorCharts.Tabs.Length];
        redraw=()=>{
            while(graph.Controls.Count>0) graph.Controls[0].Dispose();
            graph.Controls.Add(FactorCharts.Create(s,chartMode,chartRange));
            for(int k=0;k<buttons.Length;k++) TabStyle(buttons[k],k==chartMode);
        };
        for(int k=0;k<buttons.Length;k++) {
            int index=k;
            buttons[k]=ButtonOf(FactorCharts.Tabs[k],()=>{chartMode=index; redraw();},false,73);
            bar.Controls.Add(buttons[k]);
        }

        redraw();
        var factors=CardAt(panel,3,"历史分位 · 至少 20 个有效样本");
        factors.Controls.Add(grid,0,1);
        Button expand=null;
        Action showDetail=()=>{
            chartShell.Visible=!expandedFactors;
            panel.RowStyles[2].SizeType=expandedFactors?SizeType.Absolute:SizeType.Percent;
            panel.RowStyles[2].Height=expandedFactors?0:64;
            panel.RowStyles[3].Height=expandedFactors?100:36;
            expand.Text=expandedFactors?"返回图表":"展开明细";
        };
        expand=ButtonOf("展开明细",()=>{expandedFactors=!expandedFactors; showDetail();});
        tools.Controls.Add(expand);
        showDetail();
        grid.CellPainting+=(o,e)=>{
            double p;
            if(e.RowIndex<0||e.ColumnIndex!=2||!double.TryParse(Convert.ToString(e.Value).Replace("%",""),NumberStyles.Float,CultureInfo.InvariantCulture,out p)) return;
            e.PaintBackground(e.ClipBounds,true);
            var cell=e.CellBounds;
            using(var brush=new SolidBrush(Color.FromArgb(26,Theme.Accent)))
                e.Graphics.FillRectangle(brush,cell.X+5,cell.Y+5,(int)((cell.Width-10)*p/100),cell.Height-10);
            e.PaintContent(e.ClipBounds);
            e.Handled=true;
        };
        Action refresh=()=>{
            grid.Rows.Clear(); double mom=double.NaN,rsi=double.NaN,vol=double.NaN;
            foreach(string f in new[]{"MOM20","RSI14","VOL20","MACD","ATR14","VOLRATIO","RET1","CLOSE","MA5","MA10","MA20","MA30","MA60","MA120","EMA12","EMA26","DIF","DEA","BOLLUP","BOLLMID","BOLLDOWN","HHV20","LLV20","VOLMA5","VOLUME","OPEN","HIGH","LOW"}) {
                int count; double pct=Stats.Percentile(s.V[f],i,window,out count);
                grid.Rows.Add(f,Value(f,s.V[f][i]),Pct(pct),count);
                if(f=="MOM20") mom=pct; if(f=="RSI14") rsi=pct; if(f=="VOL20") vol=pct;
            }
            while(kpis.Controls.Count>0) kpis.Controls[0].Dispose();
            Cards(kpis,"综合分 · 满分 100",Num(100*(.5*mom+.3*rsi+.2*(1-vol)),"F1"),"最新收盘",Num(s.Bars[i].Close),"20 日动量",Pct(s.V["MOM20"][i]),"行情日期",Data.Day(s.Bars[i].Date));
        };
        lookback.ValueChanged+=(o,e)=>{window=(int)lookback.Value; refresh();}; refresh();
    }

    void Probability() {
        var p=Rows(body,42,54,88,-100);
        var tools=Flow(); p.Controls.Add(tools,0,0);
        var indicator=LabelOf(contextPath==null?(demoMode?"演示标的 DEMO-A 已附样例快照":"尚未导入快照"):Path.GetFileName(contextPath),Theme.Small,Theme.Muted);
        var cards=PanelOf(); p.Controls.Add(cards,0,2);
        var card=CardAt(p,3,"因子相关性、模型系数与滚动验证");
        var grid=Grid("输入因子","训练集 Pearson r · 对次日收益","标准化逻辑回归系数");
        var folds=Grid("训练截至","测试起","测试止","训练样本","测试样本","准确率","AUC","Brier");
        MinWidths(folds,96,92,92,82,82,78,70,70);
        AlignNumbers(folds,3,4,5,6,7);
        ViewTabs(PanelIn(card,1),new[]{"因子与系数","滚动验证"},grid,folds);
        tools.Controls.Add(ButtonOf("导入快照",()=>{
            string path=Open("基本面及信息面 CSV|*.csv");
            if(path!=null) {contextPath=path; indicator.Text=Path.GetFileName(path); ShowPage(1);}
        },false));
        tools.Controls.Add(ButtonOf("拟合与验证",async ()=>{
            var s=Selected; var path=contextPath;
            await Work(()=>Model.Fit(s,path==null?(demoMode&&s.Symbol=="DEMO-A"?demoContext:new Dictionary<DateTime,Context>()):Data.Contexts(path,s.Symbol)),m=>{
                while(cards.Controls.Count>0) cards.Controls[0].Dispose();
                Cards(cards,"次日上涨估计",Pct(m.Probability),"滚动验证准确率",m.WalkForward?Pct(m.WalkAccuracy):"—",
                    "滚动 AUC / Brier",m.WalkForward?Num(m.WalkAuc,"F2")+" / "+Num(m.WalkBrier,"F2"):"—",
                    "多数类基准",m.WalkForward?Pct(m.WalkBaseline):"—");
                grid.Rows.Clear();
                for(int i=0;i<Model.Features.Length;i++) grid.Rows.Add(Model.Features[i],Num(m.Correlations[i],"F4"),Num(m.Coefficients[i],"F4"));
                folds.Rows.Clear();
                foreach(var f in m.Folds) folds.Rows.Add(Data.Day(f.TrainEnd),Data.Day(f.TestStart),Data.Day(f.TestEnd),
                    f.TrainCount.ToString("N0"),f.TestCount.ToString("N0"),Pct(f.Accuracy),Num(f.Auc,"F3"),Num(f.Brier,"F4"));
                string walk=m.WalkForward
                    ? m.Folds.Count+" 折滚动 "+Pct(m.WalkAccuracy)+" ± "+Num(100*m.WalkAccuracyStd,"F1")+" 分"
                    : (m.Folds.Count==0?"滚动验证样本不足":m.Folds.Count+" 折（少于 3 折，仅供参考）");
                SetStatus("截至 "+Data.Day(m.Forecast)+"  ·  匹配 "+m.Matched+" 条  ·  单次切分 "+Pct(m.Accuracy)+"  ·  "+walk+
                    "  ·  训练至 "+Data.Day(m.TrainEnd)+"  ·  测试自 "+Data.Day(m.TestStart));
            });
        },true,112));
        tools.Controls.Add(ButtonOf("导出相关性",()=>ExportGrid(grid,"correlations",this),false,113));
        tools.Controls.Add(indicator);
        p.Controls.Add(Note(demoMode
            ? "演示行情为合成随机序列，模型准确率接近多数类基准属预期，不能用于判断模型能力。滚动验证按时间顺序扩窗重训，每折训练与测试之间留空一条。"
            : "按可用日精确对齐，按时间顺序扩窗滚动重训（至少 180 条训练、60 条测试，每折留空一条）。单次切分仅作参考，阈值 0.50。"),0,1);
        Cards(cards,"次日上涨估计","—","滚动验证准确率","—","滚动 AUC / Brier","—","多数类基准","—");
    }

    void Strategy() {
        var s=Selected;
        var p=Rows(body,43,34,34,44,43,88,-100);
        Backtest completed=null;
        var actions=Flow(); p.Controls.Add(actions,0,0);
        var buyBox=Input(buy,600); var sellBox=Input(sell,600); buyBox.MaxLength=sellBox.MaxLength=700;
        var buyLine=Rows(PanelIn(p,1),32); buyLine.ColumnCount=2; buyLine.ColumnStyles.Clear();
        buyLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,68)); buyLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        buyLine.Controls.Add(Note("买入"),0,0); buyBox.Dock=DockStyle.Fill; buyLine.Controls.Add(buyBox,1,0);
        var sellLine=Rows(PanelIn(p,2),32); sellLine.ColumnCount=2; sellLine.ColumnStyles.Clear();
        sellLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,68)); sellLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        sellLine.Controls.Add(Note("卖出"),0,0); sellBox.Dock=DockStyle.Fill; sellLine.Controls.Add(sellBox,1,0);
        buyBox.TextChanged+=(o,e)=>buy=buyBox.Text; sellBox.TextChanged+=(o,e)=>sell=sellBox.Text;
        Func<Rule> current=()=>new Rule {Name=ruleName,Buy=buy,Sell=sell};
        var dates=Flow(); dates.WrapContents=false; p.Controls.Add(dates,0,3);
        var from=DateInput(backFrom??PickerDate(s.Bars[Math.Max(0,s.Bars.Length-252)].Date));
        var to=DateInput(backTo??PickerDate(s.Bars.Last().Date));
        from.ValueChanged+=(o,e)=>backFrom=from.Value.Date; to.ValueChanged+=(o,e)=>backTo=to.Value.Date;
        var initBox=Input(initial,79); var feeBox=Input(fee,43); var slipBox=Input(slippage,43);
        initBox.Margin=feeBox.Margin=slipBox.Margin=new Padding(0,4,7,0);
        initBox.TextChanged+=(o,e)=>initial=initBox.Text; feeBox.TextChanged+=(o,e)=>fee=feeBox.Text; slipBox.TextChanged+=(o,e)=>slippage=slipBox.Text;
        Field(dates,"开始",from); Field(dates,"结束",to); Field(dates,"资金",initBox); Field(dates,"费用 bp",feeBox); Field(dates,"滑点 bp",slipBox);
        p.Controls.Add(Note("收盘生成信号，次日开盘成交。仅做多，满仓买入，期末按收盘估值。[1] 回看一日。"),0,4);
        var cards=PanelIn(p,5); Cards(cards,"策略收益","—","年化收益","—","最大回撤","—","夏普 · 无风险为 0","—");
        var results=PanelIn(p,6);
        var details=Grid("成交日","信号日","方向","成交价","数量","费用","已实现盈亏");
        MinWidths(details,110,110,62,85,78,77,110);
        var curve=PanelOf(); curve.Controls.Add(Note("运行策略后显示净值与持有基准。"));
        ViewTabs(results,new[]{"净值曲线","成交记录"},curve,details);
        var grid=details; AlignNumbers(grid,3,4,5,6);
        actions.Controls.Add(ButtonOf("导入策略",async ()=>{
            string path=Open("规则文件|*.rule;*.txt");
            if(path==null)return;
            await Work(()=>{
                var rule=Rule.Load(path); Rule.Compile(rule.Buy); Rule.Compile(rule.Sell); return rule;
            },r=>{ruleName=r.Name; buyBox.Text=r.Buy; sellBox.Text=r.Sell; SetStatus("已导入并通过静态审计："+r.Name);});
        }));
        actions.Controls.Add(ButtonOf("未来函数审计",async ()=>{
            Rule r=current(); await Work(()=>{Rule.Compile(r.Buy); Rule.Compile(r.Sell); return r;},
                x=>SetStatus("审计通过。规则只引用当前收盘已知指标或历史指标。请独立核验输入数据的时点和复权口径。"));
        },false,147));
        actions.Controls.Add(ButtonOf("运行回测",async ()=>{
            Rule rule=current(); DateTime a=from.Value.Date,b=to.Value.Date;
            string cap=initBox.Text,f=feeBox.Text,sl=slipBox.Text;
            await Work(()=>Backtest.Run(s,a,b,Data.Number(cap,1,1e14),Data.Number(f,0,1000)/10000,Data.Number(sl,0,1000)/10000,rule),r=>{
                completed=r;
                while(cards.Controls.Count>0) cards.Controls[0].Dispose();
                Cards(cards,"策略收益",Pct(r.Total),"年化收益",Pct(r.Annual),"最大回撤",Pct(r.MaxDrawdown),"夏普 · 无风险为 0",Num(r.Sharpe));
                curve.Controls[0].Dispose();
                curve.Controls.Add(new Plot(r.Dates,"P1",new[]{
                    new PlotLine("策略",r.Equity.Select(x=>x/r.Initial-1).ToArray(),Theme.Accent),
                    new PlotLine("买入持有",r.Benchmark.Select(x=>x/r.Initial-1).ToArray(),Theme.Violet)
                },shade:true));
                grid.Rows.Clear();
                foreach(var t in r.Fills.AsEnumerable().Reverse().Take(500)) grid.Rows.Add(Data.Day(t.Date),Data.Day(t.Signal),t.Side,Num(t.Price),Num(t.Quantity,"N0"),Num(t.Fee),Num(t.Pnl));
                int closed=r.Fills.Count(x=>x.Side=="卖出");
                string win=closed==0?"—":Pct((double)r.Fills.Count(x=>x.Side=="卖出"&&x.Pnl>0)/closed);
                SetStatus("基准 "+Pct(r.Benchmark.Last()/r.Initial-1)+"  ·  成交 "+r.Fills.Count+" 笔（显示最近 500）  ·  平仓胜率 "+win+
                    "  ·  持仓日占比 "+Pct((double)r.Exposure/r.Dates.Length)+"  ·  跳过 "+r.Skipped+"  ·  冲突 "+r.Conflicts);
            });
        },true,101));
        actions.Controls.Add(ButtonOf("导出全量成交",()=>ExportBacktest(completed,false),false,149));
        actions.Controls.Add(ButtonOf("导出净值",()=>ExportBacktest(completed,true)));
    }

    void Positions() {
        var p=Rows(body,43,36,105,88,-100);
        var tools=Flow(); p.Controls.Add(tools,0,0);
        var text=Input(rawPositions,500,true); text.MaxLength=200000; text.Dock=DockStyle.Fill;
        text.TextChanged+=(s,e)=>rawPositions=text.Text;
        p.Controls.Add(Note("每行一笔：标的,数量,单位成本,买入日期(YYYY-MM-DD),行业。留空时分析已导入的持仓。"),0,1);
        p.Controls.Add(text,0,2);
        var cards=PanelIn(p,3); Cards(cards,"组合市值（含现金）","—","浮动盈亏","—","最大标的权重","—","风格","—");
        var holdings=Grid("标的","行业","数量","收盘","市值","浮盈","权重","持有天数");
        MinWidths(holdings,96,70,76,74,93,92,76,88);
        AlignNumbers(holdings,2,3,4,5,6,7);
        var risk=Grid("指标","结果");
        ViewTabs(PanelIn(p,4),new[]{"持仓明细","配置与风格"},holdings,risk);
        var cash=Input(cashText,90); cash.TextChanged+=(o,e)=>cashText=cash.Text;
        Field(tools,"现金",cash);
        var used=LabelOf(loaded==null?"":("已导入 "+loaded.Count+" 笔"),Theme.Small,Theme.Muted);
        tools.Controls.Add(ButtonOf("导入持仓",async ()=>{
            string path=Open("持仓 CSV|*.csv"); if(path==null) return;
            await Work(()=>Data.Holdings(path),rows=>{loaded=rows; text.Clear(); used.Text="已导入 "+rows.Count+" 笔";});
        },false,103));
        tools.Controls.Add(ButtonOf("加载演示持仓",()=>{
            var bars=market["DEMO-A"].Bars;
            text.Text="DEMO-A,300,48,"+Data.Day(bars[bars.Length-86].Date)+",科技\r\nDEMO-B,100,72,"+Data.Day(bars[bars.Length-26].Date)+",消费\r\nDEMO-C,80,95,"+Data.Day(bars[bars.Length-7].Date)+",工业";
            cash.Text="6500";
        },false,145));
        tools.Controls[tools.Controls.Count-1].Visible=demoMode;
        tools.Controls.Add(ButtonOf("分析组合",async ()=>{
            string content=rawPositions, money=cashText; var imported=loaded;
            await Work(()=>Portfolio.Analyze(market,string.IsNullOrWhiteSpace(content)?imported??new List<Holding>():Manual(content),Data.Number(money,0,1e19)),a=>{
                while(cards.Controls.Count>0) cards.Controls[0].Dispose();
                Cards(cards,"组合市值（含现金）",Num(a.Total,"N0"),"浮动盈亏",Num(a.Profit,"N0"),"最大标的权重",Pct(a.Top),"风格",a.Style);
                holdings.Rows.Clear();
                foreach(var pos in a.Positions.Take(500)) holdings.Rows.Add(pos.Lot.Symbol,pos.Lot.Sector,Num(pos.Lot.Qty,"#,0.####"),Num(pos.Last),Num(pos.Value),Num(pos.Pnl),Pct(pos.Weight),Num(pos.Days,"N0"));
                risk.Rows.Clear();
                risk.Rows.Add("同步估值日",Data.Day(a.AsOf)); risk.Rows.Add("现金权重",Pct(a.Cash/a.Total)); risk.Rows.Add("HHI（含现金）",Num(a.Hhi,"F3"));
                risk.Rows.Add("市值加权持有天数",Num(a.Days,"F1")); risk.Rows.Add("正动量权重",Pct(a.Trend)); risk.Rows.Add("高于 MA20 权重",Pct(a.AboveMean)); risk.Rows.Add("趋势指标覆盖",Pct(a.TrendCoverage));
                risk.Rows.Add("均线上方入场权重",Pct(a.EntryTrend)); risk.Rows.Add("入场信号覆盖",Pct(a.EntryCoverage));
                risk.Rows.Add("近 60 日年化波动",Pct(a.Volatility)); risk.Rows.Add("近 60 日模拟回撤",Pct(a.Drawdown));
                risk.Rows.Add("同步收益样本",a.MissingRisk?"日期不一致":a.RiskSamples.ToString());
                foreach(var g in a.Positions.GroupBy(x=>x.Lot.Sector).OrderByDescending(g=>g.Sum(x=>x.Weight)).Take(20)) risk.Rows.Add("行业 · "+g.Key,Pct(g.Sum(x=>x.Weight)));
                SetStatus("共 "+a.Positions.Length+" 笔（显示最多 500）  ·  风格来自现有持仓周期  ·  风险按当前权重反推，现金收益取零。");
            });
        },true,103));
        tools.Controls.Add(ButtonOf("导出可见持仓",()=>ExportGrid(holdings,"positions",this),false,149));
        tools.Controls.Add(used);
    }

    static List<Holding> Manual(string content) {
        var result=new List<Holding>();
        foreach(var line in content.Split(new[]{"\r\n","\n","\r"},StringSplitOptions.RemoveEmptyEntries)) {
            if(string.IsNullOrWhiteSpace(line)) continue;
            var x=line.Split(',').Select(v=>v.Trim()).ToArray();
            if(x.Length!=5) throw new FormatException("手工持仓每行需要五列：" + line);
            result.Add(new Holding{Symbol=Data.Symbol(x[0]),Qty=Data.Number(x[1],1e-8,1e12),Cost=Data.Number(x[2],1e-8,1e10),
                Bought=Data.Date(x[3]),Sector=x[4].Length==0?"未分类":x[4]});
        }
        return result;
    }

    void Help() {
        var p=Rows(body,43,-100);
        var actions=Flow(); p.Controls.Add(actions,0,0);
        actions.Controls.Add(ButtonOf("恢复演示行情",async ()=>await Work(Demo.Create,d=>{
            market=d.Market; demoContext=d.Context; demoMode=true; marketName="合成演示行情"; contextPath=null; backFrom=backTo=null;
            SetSymbols("DEMO-A"); ShowPage(0); SetStatus("已恢复合成演示行情。");
        }),false,130));
        actions.Controls.Add(ButtonOf("保存示例文件",()=>{
            using(var d=new FolderBrowserDialog{Description="选择示例文件的父文件夹"}) {
                if(d.ShowDialog(this)!=DialogResult.OK) return;
                try { string dir=Program.WriteSamples(d.SelectedPath); SetStatus("已保存至 "+dir); }
                catch(Exception ex) { SetStatus(ex.Message); MessageBox.Show(this,ex.Message,"保存失败"); }
            }
        },true,129));
        var card=CardAt(p,1,"数据规范 · 计算口径 · 安全声明");
        var info=Input(Program.Help,500,true); info.ReadOnly=true; info.Dock=DockStyle.Fill; info.BackColor=Theme.Card; info.BorderStyle=BorderStyle.None;
        card.Controls.Add(info,0,1);
    }

}
}
