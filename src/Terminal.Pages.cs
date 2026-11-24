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
    void ExportBacktest(PortfolioBacktest report,bool equity) {
        if(report==null) { SetStatus("请先运行回测。"); return; }
        using(var file=new SaveFileDialog{Filter="CSV|*.csv",FileName=equity?"backtest-equity.csv":"backtest-fills.csv",RestoreDirectory=true}) {
            if(file.ShowDialog(this)!=DialogResult.OK) return;
            string path=file.FileName;
            ExportAsync(path,report,equity);
        }
    }

    async void ExportAsync(string path,PortfolioBacktest report,bool equity) {
        await Work(()=>{
            using(var writer=new StreamWriter(path,false,new UTF8Encoding(true))) {
                if(equity) {
                    writer.WriteLine("date,strategy_equity,benchmark_equity");
                    for(int i=0;i<report.Dates.Length;i++)
                        writer.WriteLine(string.Format(Data.Inv,"{0},{1:R},{2:R}",Data.Day(report.Dates[i]),report.Equity[i],report.Benchmark[i]));
                } else {
                    writer.WriteLine("symbol,fill_date,signal_date,side,price,quantity,fee,realized_pnl,tag");
                    foreach(var t in report.Trades) writer.WriteLine(string.Format(Data.Inv,"{0},{1},{2},{3},{4:R},{5:R},{6:R},{7},{8}",
                        t.Symbol,Data.Day(t.Date),Data.Day(t.Signal),t.Side,t.Price,t.Quantity,t.Fee,double.IsNaN(t.Pnl)?"":t.Pnl.ToString("R",Data.Inv),t.Tag??""));
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
        var p=Rows(body,84,54,88,-100);
        var tools=Flow(); p.Controls.Add(tools,0,0);
        var indicator=LabelOf(contextPath!=null?Path.GetFileName(contextPath):(statements!=null?"财报 "+statements.File+"（"+statements.Symbol+"）":(demoMode?"演示标的 DEMO-A 已附样例快照":"尚未导入快照")),Theme.Small,Theme.Muted);
        var cards=PanelOf(); p.Controls.Add(cards,0,2);
        var card=CardAt(p,3,"因子相关性、模型系数、滚动验证与基本面相关");
        var grid=Grid("输入因子","训练集 Pearson r · 对次日收益","标准化逻辑回归系数");
        var folds=Grid("训练截至","测试起","测试止","训练样本","测试样本","准确率","AUC","Brier");
        MinWidths(folds,96,92,92,82,82,78,70,70);
        AlignNumbers(folds,3,4,5,6,7);
        var fundPage=PanelOf();
        var fundLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};
        fundLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,52));
        fundLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,48));
        fundLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        fundLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var fundNote=Note("导入财报后点「分析基本面」，查看综合分、各指标与股价的相关程度，以及滚动相关的时序变化。");
        fundNote.AutoEllipsis=true;
        fundLayout.Controls.Add(fundNote,0,0); fundLayout.SetColumnSpan(fundNote,2);
        var fundGrid=Grid("指标","最新值","收盘 r","20 日 r","近 1 年","近 3 年");
        MinWidths(fundGrid,130,60,66,76,68,68);
        AlignNumbers(fundGrid,1,2,3,4,5);
        var fundChart=PanelOf();
        fundLayout.Controls.Add(fundGrid,0,1);
        fundLayout.Controls.Add(fundChart,1,1);
        fundPage.Controls.Add(fundLayout);
        var showTab=ViewTabs(PanelIn(card,1),new[]{"因子与系数","滚动验证","基本面相关性"},grid,folds,fundPage);
        Action<FundamentalAnalysis> renderFundamentals=a=>{
            fundGrid.Rows.Clear();
            foreach(var item in a.Series)
                fundGrid.Rows.Add(item.Label,FundamentalValue(item),Num(item.PriceCorr,"F3"),Num(item.ReturnCorr,"F3"),
                    Num(item.RecentCorr,"F3"),Num(item.LongCorr,"F3"));
            while(fundChart.Controls.Count>0) fundChart.Controls[0].Dispose();
            var want=new[]{"score","roe_ttm","net_margin","revenue_growth","profit_growth"};
            var lines=new List<PlotLine>();
            foreach(string key in want) {
                var item=a.Series.First(x=>x.Key==key);
                string name=key=="score"?"综合":key=="roe_ttm"?"ROE":key=="net_margin"?"净利率":key=="revenue_growth"?"营收增":"净利增";
                lines.Add(new PlotLine(name,item.Rolling,Theme.Series[lines.Count%Theme.Series.Length]));
            }
            fundChart.Controls.Add(new Plot(a.Dates,"R2",lines.ToArray(),null,new[]{0.0}));
            var score=a.Series.First(x=>x.Key=="score");
            fundNote.Text="综合分 0–100（可见 "+Data.Day(a.FirstVisible)+" ~ "+Data.Day(a.LastPeriod)+" / "+a.Periods+" 期）  ·  窗口 "+
                a.Window+" 日  ·  综合分与 20 日收益 r="+Num(score.ReturnCorr,"F3");
        };
        var cached=fundamentals;
        if(cached!=null&&(statements==null||cached.StatementFile!=statements.File||cached.Symbol!=Selected.Symbol)) { fundamentals=null; cached=null; }
        if(cached!=null) renderFundamentals(cached);
        tools.Controls.Add(ButtonOf("导入快照",()=>{
            string path=Open("基本面及信息面 CSV|*.csv");
            if(path!=null) {contextPath=path; indicator.Text=Path.GetFileName(path); fundamentals=null; ShowPage(1);}
        },false));
        var windowInput=new NumericUpDown{Minimum=60,Maximum=1000,Value=Math.Max(60,Math.Min(1000,fundamentalWindow)),Increment=20,Width=76,BackColor=Theme.Card,ForeColor=Theme.Text,Margin=new Padding(0,5,12,0)};
        windowInput.ValueChanged+=(o,e)=>{fundamentalWindow=(int)windowInput.Value; fundamentals=null;};
        Field(tools,"滚动窗口（日）",windowInput);
        tools.Controls.Add(ButtonOf("分析基本面",async ()=>{
            var book=statements; var s=Selected; int window=(int)windowInput.Value;
            if(book==null) {SetStatus("请先在「财报基本面」导入财报文件。"); return;}
            await Work(()=>{
                RequireStatementMatch(book,s);
                return FundamentalAnalysis.Run(book,s,window);
            },a=>{
                fundamentals=a; renderFundamentals(a); showTab(2);
                var score=a.Series.First(x=>x.Key=="score");
                SetStatus("基本面相关  ·  综合分 "+Num(score.Latest,"F1")+" / 100  ·  与 20 日收益 r="+Num(score.ReturnCorr,"F3")+
                    "  ·  与收盘价 r="+Num(score.PriceCorr,"F3")+"  ·  样本 "+score.Samples+"  ·  窗口 "+a.Window+" 日  ·  报告期 "+a.Periods+" 个");
            });
        },false,120));
        tools.Controls.Add(ButtonOf("拟合与验证",async ()=>{
            var s=Selected; var path=contextPath; var book=statements;
            await Work(()=>{
                Dictionary<DateTime,Context> context; string source;
                if(path!=null) {context=Data.Contexts(path,s.Symbol); source="CSV 快照";}
                else if(book!=null) {
                    RequireStatementMatch(book,s);
                    context=book.Contexts(s); source="财报推导（"+book.Symbol+"，ROE/PE，情绪取 0）";
                    if(context.Count<100) throw new FormatException("财报推导的快照只有 "+context.Count+" 条，至少需要 100 条；请换用更长的行情，或补齐每股收益与每股净资产，也可导入 CSV 快照。");
                } else {context=demoMode&&s.Symbol=="DEMO-A"?demoContext:new Dictionary<DateTime,Context>(); source="演示快照";}
                return new KeyValuePair<Model,string>(Model.Fit(s,context),source);
            },pair=>{
                var m=pair.Key;
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
                SetStatus("截至 "+Data.Day(m.Forecast)+"  ·  快照 "+pair.Value+"  ·  匹配 "+m.Matched+" 条  ·  单次切分 "+Pct(m.Accuracy)+"  ·  "+walk+
                    "  ·  训练至 "+Data.Day(m.TrainEnd)+"  ·  测试自 "+Data.Day(m.TestStart));
            });
        },true,112));
        tools.Controls.Add(ButtonOf("导出相关性",()=>ExportGrid(grid,"correlations",this),false,113));
        tools.Controls.Add(ButtonOf("导出基本面",()=>ExportGrid(fundGrid,"fundamental-correlations",this),false,113));
        tools.Controls.Add(indicator);
        p.Controls.Add(Note(statements!=null
            ? "模型优先使用财报推导的 ROE（TTM 每股收益÷每股净资产）与 PE（收盘÷TTM 每股收益），情绪取 0；导入 CSV 快照可覆盖。基本面相关性只用披露日当时可见的报告期，综合分为质量指标的滚动分位合成，r 是 Pearson 相关，不构成预测。"
            : "按可用日精确对齐，按时间顺序扩窗滚动重训（至少 180 条训练、60 条测试，每折留空一条）。单次切分仅作参考，阈值 0.50；导入财报后可用「分析基本面」。"),0,1);
        Cards(cards,"次日上涨估计","—","滚动验证准确率","—","滚动 AUC / Brier","—","多数类基准","—");
    }

    static string FundamentalValue(FundamentalSeries item) {
        if(double.IsNaN(item.Latest)) return "—";
        if(item.Key=="score") return Num(item.Latest,"F1");
        if(item.Unit=="%") return Num(item.Latest,"F2")+"%";
        return Num(item.Latest,"F2");
    }

    static bool CodeLike(string text) {
        return text.Length>=5&&text.Length<=6&&text.All(char.IsDigit);
    }

    static void RequireStatementMatch(StatementBook book,Series series) {
        if(CodeLike(book.Symbol)&&CodeLike(series.Symbol)&&!string.Equals(book.Symbol,series.Symbol,StringComparison.OrdinalIgnoreCase))
            throw new FormatException("财报标的 "+book.Symbol+" 与当前标的 "+series.Symbol+" 不一致。请把行情切换到 "+book.Symbol+"，或重新导入与当前标的对应的财报。");
    }

    void Strategy() {
        var s=Selected;
        var p=Rows(body,43,43,34,34,44,43,88,-100);
        PortfolioBacktest last=null;
        string scriptSource=null, scriptName="";
        var library=Library.Initial();
        var allSymbols=false;
        var actions=Flow(); p.Controls.Add(actions,0,0);
        var buyBox=Input(buy,600); var sellBox=Input(sell,600); buyBox.MaxLength=sellBox.MaxLength=700;
        var buyLine=Rows(PanelIn(p,2),32); buyLine.ColumnCount=2; buyLine.ColumnStyles.Clear();
        buyLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,68)); buyLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        buyLine.Controls.Add(Note("买入"),0,0); buyBox.Dock=DockStyle.Fill; buyLine.Controls.Add(buyBox,1,0);
        var sellLine=Rows(PanelIn(p,3),32); sellLine.ColumnCount=2; sellLine.ColumnStyles.Clear();
        sellLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,68)); sellLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        sellLine.Controls.Add(Note("卖出"),0,0); sellBox.Dock=DockStyle.Fill; sellLine.Controls.Add(sellBox,1,0);
        buyBox.TextChanged+=(o,e)=>buy=buyBox.Text; sellBox.TextChanged+=(o,e)=>sell=sellBox.Text;
        Func<Rule> current=()=>new Rule {Name=ruleName,Buy=buy,Sell=sell};
        Func<string[]> scope=()=>{
            if(!allSymbols) return new[]{selectedSymbol};
            return market.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Take(Library.MaxSymbols).ToArray();
        };
        Func<string> scopeName=()=>{
            if(!allSymbols) return "当前标的 "+selectedSymbol;
            return market.Count>Library.MaxSymbols
                ? ("全部标的（前 "+Library.MaxSymbols+" / 共 "+market.Count+"）")
                : ("全部标的（"+market.Count+"）");
        };

        var tools=Flow(); p.Controls.Add(tools,1,0);
        DateTimePicker from=null, to=null;
        TextBox initBox=null, feeBox=null, slipBox=null;
        DataGridView compareGrid=null;
        Panel compareChart=null;
        Action<int> showTab=null;
        var scopeMenu=new ContextMenuStrip{Font=Theme.Small};
        Button scopeButton=null;
        scopeButton=ButtonOf("",()=>scopeMenu.Show(scopeButton,0,scopeButton.Height),false,152);
        Action scopeText=()=>{ scopeButton.Text=scopeName()+"  ▾"; scopeButton.AccessibleName="回测范围，"+scopeName(); };
        foreach(bool all in new[]{false,true}) {
            bool target=all;
            var item=new ToolStripMenuItem(all?"全部标的（等权）":"仅当前标的"){Checked=all==allSymbols};
            item.Click+=(o,e)=>{ allSymbols=target; foreach(ToolStripMenuItem x in scopeMenu.Items) x.Checked=x==item; scopeText(); };
            scopeMenu.Items.Add(item);
        }
        scopeButton.Disposed+=(o,e)=>scopeMenu.Dispose();
        scopeButton.ContextMenuStrip=scopeMenu;
        scopeText();
        Field(tools,"回测范围",scopeButton);
        var libraryLabel=LabelOf("",Theme.Small,Theme.Muted);
        libraryLabel.Margin=new Padding(4,9,10,0);
        Action libraryText=()=>{ libraryLabel.Text="策略库 "+library.Count+" 条  "; };
        libraryText();
        tools.Controls.Add(ButtonOf("加入对比",()=>{
            try {
                if(Library.Add(library,current())) { libraryText(); SetStatus("已加入策略库："+ruleName); }
                else SetStatus("策略库里已有同一条规则。");
            } catch(FormatException error) { SetStatus(error.Message); }
        },false,105));
        tools.Controls.Add(ButtonOf("恢复预设",()=>{
            library=Library.Initial(); libraryText(); SetStatus("策略库已恢复为 "+library.Count+" 条预设策略。");
        },false,103));
        tools.Controls.Add(ButtonOf("对比策略",async ()=>{
            var rules=new List<Rule>(library);
            try { Library.Add(rules,current()); } catch(FormatException) { }
            DateTime a=from.Value.Date,b=to.Value.Date;
            string cap=initBox.Text,f=feeBox.Text,sl=slipBox.Text;
            string[] targets=scope();
            string script=scriptSource; string scriptLabel=scriptName;
            await Work(()=>{
                double capital=Data.Number(cap,1,1e14), feeRate=Data.Number(f,0,1000)/10000, slipRate=Data.Number(sl,0,1000)/10000;
                if(script!=null) {
                    var combined=new Comparison();
                    var run=ScriptPortfolio.Run(market,targets,a,b,capital,feeRate,slipRate,script,Script.Parse(script).Pooled);
                    combined.Dates=run.Dates;
                    combined.Rows.Add(new ComparisonRow{Name=(scriptLabel.Length==0?"脚本":scriptLabel)+"（脚本）",Result=run,Curve=run.Equity.Select(x=>x/run.Initial-1).ToArray()});
                    foreach(var preset in library) {
                        try {
                            var other=PortfolioBacktest.Run(market,targets,a,b,capital,feeRate,slipRate,preset);
                            combined.Rows.Add(new ComparisonRow{Name=preset.Name,Result=other,Curve=other.Equity.Select(x=>x/other.Initial-1).ToArray()});
                        } catch(Exception error) { combined.Warnings.Add(preset.Name+"："+error.Message); }
                    }
                    return combined;
                }
                return Comparison.Run(market,targets,a,b,capital,feeRate,slipRate,rules);
            },c=>{
                compareGrid.Rows.Clear();
                foreach(var row in c.Rows) compareGrid.Rows.Add(row.Name,Pct(row.Result.Total),Pct(row.Result.Annual),
                    Pct(row.Result.MaxDrawdown),Num(row.Result.Sharpe),row.Result.Fills.ToString(),Pct(row.Result.WinRate),Pct(row.Result.Exposure));
                while(compareChart.Controls.Count>0) compareChart.Controls[0].Dispose();
                var lines=c.Rows.Select((row,i)=>new PlotLine(row.Name,row.Curve,Theme.Series[i%Theme.Series.Length])).ToArray();
                compareChart.Controls.Add(new Plot(c.Dates,"P1",lines,shade:true));
                showTab(3);
                SetStatus("策略对比 "+c.Rows.Count+" 条  ·  范围 "+scopeName()+"  ·  "+Data.Day(a)+" ~ "+Data.Day(b)+
                    (c.Warnings.Count>0?"  ·  "+c.Warnings.Count+" 条未完成":""));
            });
        },true,101));
        tools.Controls.Add(ButtonOf("导出报告",()=>{
            using(var dialog=new FolderBrowserDialog{Description="选择审计报告的输出文件夹"}) {
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                if(scriptSource!=null) ExportScriptReport(scriptSource,scriptName,scope(),from.Value.Date,to.Value.Date,initBox.Text,feeBox.Text,slipBox.Text,dialog.SelectedPath);
                else ExportReport(current(),scope(),from.Value.Date,to.Value.Date,initBox.Text,feeBox.Text,slipBox.Text,dialog.SelectedPath);
            }
        },false,103));
        tools.Controls.Add(libraryLabel);

        var dates=Flow(); dates.WrapContents=false; p.Controls.Add(dates,0,4);
        from=DateInput(backFrom??PickerDate(s.Bars[Math.Max(0,s.Bars.Length-252)].Date));
        to=DateInput(backFrom==null&&backTo==null?PickerDate(s.Bars.Last().Date):(backTo??PickerDate(s.Bars.Last().Date)));
        from.ValueChanged+=(o,e)=>backFrom=from.Value.Date; to.ValueChanged+=(o,e)=>backTo=to.Value.Date;
        initBox=Input(initial,79); feeBox=Input(fee,43); slipBox=Input(slippage,43);
        initBox.Margin=feeBox.Margin=slipBox.Margin=new Padding(0,4,7,0);
        initBox.TextChanged+=(o,e)=>initial=initBox.Text; feeBox.TextChanged+=(o,e)=>fee=feeBox.Text; slipBox.TextChanged+=(o,e)=>slippage=slipBox.Text;
        Field(dates,"开始",from); Field(dates,"结束",to); Field(dates,"资金",initBox); Field(dates,"费用 bp",feeBox); Field(dates,"滑点 bp",slipBox);
        p.Controls.Add(Note("收盘生成信号，次日开盘成交。仅做多，整单位，各标的等权分配资金、独立持仓；规则回测不做跨标的再平衡，脚本可用 INIT rebalance 与 TARGET 做资金池再平衡。[1] 回看一日。"),0,5);
        var cards=PanelIn(p,6); Cards(cards,"策略收益","—","年化收益","—","最大回撤","—","夏普 · 无风险为 0","—");
        var results=PanelIn(p,7);
        var details=Grid("标的","成交日","信号日","方向","成交价","数量","费用","已实现盈亏","标记");
        MinWidths(details,88,100,100,58,82,74,72,104,72);
        AlignNumbers(details,4,5,6,7);
        var symbolGrid=Grid("标的","初始资金","期末估值","收益","最大回撤","夏普","成交","胜率");
        MinWidths(symbolGrid,88,100,100,80,90,70,64,70);
        AlignNumbers(symbolGrid,1,2,3,4,5,6,7);
        var comparePage=PanelOf();
        var compareInner=Rows(comparePage,-58,-42);
        compareGrid=Grid("策略","收益","年化","最大回撤","夏普","成交","胜率","持仓占比");
        MinWidths(compareGrid,150,80,80,90,70,64,70,84);
        AlignNumbers(compareGrid,1,2,3,4,5,6,7);
        compareChart=PanelOf();
        compareInner.Controls.Add(compareGrid,0,0); compareInner.Controls.Add(compareChart,0,1);
        var curve=PanelOf(); curve.Controls.Add(Note("运行策略后显示净值与持有基准。"));
        showTab=ViewTabs(results,new[]{"净值曲线","成交记录","标的明细","策略对比"},curve,details,symbolGrid,comparePage);

        Action<Rule> loadRule=r=>{
            ruleName=r.Name; buyBox.Text=r.Buy; sellBox.Text=r.Sell;
            try { Library.Add(library,new Rule{Name=r.Name,Buy=r.Buy,Sell=r.Sell}); libraryText(); } catch(FormatException) { }
            SetStatus("已导入并通过静态审计："+r.Name);
        };
        var importMenu=new ContextMenuStrip{Font=Theme.Small};
        Button importButton=null;
        importButton=ButtonOf("导入策略 ▾",()=>importMenu.Show(importButton,0,importButton.Height),false,112);
        importButton.AccessibleName="导入策略，可选规则文件或 Python 代码";
        importButton.ContextMenuStrip=importMenu;
        importButton.Disposed+=(o,e)=>importMenu.Dispose();
        importMenu.Items.Add(new ToolStripMenuItem("从规则文件（.rule / .txt）",null,(EventHandler)(async (o,e)=>{
            string path=Open("规则文件|*.rule;*.txt");
            if(path==null) return;
            await Work(()=>{
                var rule=Rule.Load(path); Rule.Compile(rule.Buy); Rule.Compile(rule.Sell); return rule;
            },loadRule);
        })));
        importMenu.Items.Add(new ToolStripMenuItem("从策略脚本（.strategy）",null,(EventHandler)(async (o,e)=>{
            string path=Open("策略脚本|*.strategy;*.txt");
            if(path==null) return;
            await Work(()=>{
                string text=Data.Text(path);
                var program=Script.Parse(text);
                return new KeyValuePair<string,ScriptProgram>(text,program);
            },pair=>{
                scriptSource=pair.Key; scriptName=pair.Value.Name;
                SetStatus("已加载脚本 "+(scriptName.Length==0?"(未命名)":scriptName)+(pair.Value.Pooled?"（资金池模式）":"（单标的等分资金）")+
                    (pair.Value.RebalanceText.Length>0?"  ·  "+pair.Value.RebalanceText:"")+"，运行回测将执行脚本");
            });
        })));
        importMenu.Items.Add(new ToolStripMenuItem("从 Python 代码（.py）",null,(EventHandler)(async (o,e)=>{
            string path=Open("Python 源文件|*.py;*.txt");
            if(path==null) return;
            await Work(()=>{
                var converted=PythonConvert.Convert(Data.Text(path),Path.GetFileNameWithoutExtension(path));
                if(!converted.Ok) throw new FormatException(converted.Report());
                Rule.Compile(converted.Buy); Rule.Compile(converted.Sell);
                return new Rule{Name=converted.Name,Buy=converted.Buy,Sell=converted.Sell};
            },r=>{
                loadRule(r);
                SetStatus("已从 Python 转换并加载："+r.Name+"（转换结果未写入磁盘，需要留档请手工另存为 .rule）");
            });
        })));
        actions.Controls.Add(importButton);
        actions.Controls.Add(ButtonOf("未来函数审计",async ()=>{
            Rule r=current(); await Work(()=>{Rule.Compile(r.Buy); Rule.Compile(r.Sell); return r;},
                x=>SetStatus("审计通过。规则只引用当前收盘已知指标或历史指标。请独立核验输入数据的时点和复权口径。"));
        },false,147));
        actions.Controls.Add(ButtonOf("运行回测",async ()=>{
            Rule rule=current(); DateTime a=from.Value.Date,b=to.Value.Date;
            string cap=initBox.Text,f=feeBox.Text,sl=slipBox.Text;
            string[] targets=scope();
            await Work(()=>{
                double capital=Data.Number(cap,1,1e14), feeRate=Data.Number(f,0,1000)/10000, slipRate=Data.Number(sl,0,1000)/10000;
                if(scriptSource!=null) return ScriptPortfolio.Run(market,targets,a,b,capital,feeRate,slipRate,scriptSource,Script.Parse(scriptSource).Pooled);
                return PortfolioBacktest.Run(market,targets,a,b,capital,feeRate,slipRate,rule);
            },r=>{
                last=r;
                while(cards.Controls.Count>0) cards.Controls[0].Dispose();
                Cards(cards,"策略收益",Pct(r.Total),"年化收益",Pct(r.Annual),"最大回撤",Pct(r.MaxDrawdown),"夏普 · 无风险为 0",Num(r.Sharpe));
                curve.Controls[0].Dispose();
                curve.Controls.Add(new Plot(r.Dates,"P1",new[]{
                    new PlotLine("策略",r.Equity.Select(x=>x/r.Initial-1).ToArray(),Theme.Accent),
                    new PlotLine("买入持有",r.Benchmark.Select(x=>x/r.Initial-1).ToArray(),Theme.Violet)
                },shade:true));
                details.Rows.Clear();
                foreach(var t in r.Trades.AsEnumerable().Reverse().Take(500))
                    details.Rows.Add(t.Symbol,Data.Day(t.Date),Data.Day(t.Signal),t.Side,Num(t.Price),Num(t.Quantity,"N0"),Num(t.Fee),Num(t.Pnl),t.Tag??"");
                symbolGrid.Rows.Clear();
                foreach(var row in r.Symbols) symbolGrid.Rows.Add(row.Symbol,
                    row.Failed?"—":Num(row.Initial,"N0"),row.Failed?"—":Num(row.Final,"N0"),row.Failed?"—":Pct(row.Total),
                    row.Failed?"—":Pct(row.MaxDrawdown),row.Failed?"—":Num(row.Sharpe),row.Failed?"—":row.Fills.ToString(),
                    row.Failed?"—":Pct(row.WinRate));
                SetStatus("基准 "+Pct(r.Benchmark[r.Benchmark.Length-1]/r.Initial-1)+"  ·  "+r.Used+" 个标的等权  ·  成交 "+r.Fills+
                    " 笔（显示最近 500）  ·  平仓胜率 "+Pct(r.WinRate)+"  ·  持仓日占比 "+Pct(r.Exposure)+
                    (r.Rebalances>0?"  ·  再平衡 "+r.Rebalances+" 次":"")+
                    "  ·  跳过 "+r.Skipped+"  ·  冲突 "+r.Conflicts+(r.Requested>r.Used?"  ·  "+(r.Requested-r.Used)+" 个标的被跳过":""));
            });
        },true,101));
        actions.Controls.Add(ButtonOf("导出全量成交",()=>ExportBacktest(last,false),false,149));
        actions.Controls.Add(ButtonOf("导出净值",()=>ExportBacktest(last,true)));
    }

    async void ExportReport(Rule rule,string[] targets,DateTime from,DateTime to,string capital,string fee,string slip,string folder) {
        await Work(()=>{
            var result=Audit.Run(market,targets,rule,from,to,Data.Number(capital,1,1e14),
                Data.Number(fee,0,1000)/10000,Data.Number(slip,0,1000)/10000,null);
            result.DataSource=demoMode?"合成演示行情（界面）":marketName+"（界面导入）";
            Audit.Write(result,folder);
            return result.Verdict+" · 失败 "+result.Failures+" / 警告 "+result.Warnings;
        },text=>SetStatus("审计报告已写入 "+folder+"：" + text));
    }

    async void ExportScriptReport(string script,string label,string[] targets,DateTime from,DateTime to,string capital,string fee,string slip,string folder) {
        await Work(()=>{
            double cap=Data.Number(capital,1,1e14), feeRate=Data.Number(fee,0,1000)/10000, slipRate=Data.Number(slip,0,1000)/10000;
            var program=Script.Parse(script);
            var run=ScriptPortfolio.Run(market,targets,from,to,cap,feeRate,slipRate,script,program.Pooled);
            var result=new AuditResult{RuleName=label.Length==0?"脚本策略":label,Buy="（脚本策略，无规则表达式）",Sell="",
                From=from,To=to,Symbols=targets,Capital=cap,Fee=feeRate,Slip=slipRate,Backtest=run,Rule=new RuleInfo()};
            result.DataSource=demoMode?"合成演示行情（界面）":marketName+"（界面导入）";
            result.Checks.Add(new AuditCheck{Name="脚本可解析",Detail="已通过解析，共 "+program.Run.Count+" 条语句"+(program.Pooled?"，资金池模式":"，等分资金槽")+"。"});
            result.Checks.Add(new AuditCheck{Name="无未来数据",Detail="信号在收盘判定、下一根开盘成交；状态变量禁止进入回看函数。"});
            result.Checks.Add(new AuditCheck{Name="策略触发",Level=run.Fills==0?"失败":run.Fills<5?"警告":"通过",Detail="共 "+run.Fills+" 笔成交。"});
            var cut=from.AddDays((to-from).TotalDays*0.7);
            if(cut>from) {
                var prefix=ScriptPortfolio.Run(market,targets,from,cut,cap,feeRate,slipRate,script,program.Pooled);
                var lookup=new Dictionary<DateTime,double>();
                for(int i=0;i<run.Dates.Length;i++) lookup[run.Dates[i]]=run.Equity[i];
                double worst=0; int compared=0;
                for(int i=0;i<prefix.Dates.Length;i++) { double other; if(!lookup.TryGetValue(prefix.Dates[i],out other)) continue; compared++; worst=Math.Max(worst,Math.Abs(prefix.Equity[i]-other)/Math.Max(1,Math.Abs(other))); }
                result.Checks.Add(new AuditCheck{Name="截断不变性",Level=compared>0&&worst<1e-9?"通过":"失败",
                    Detail=compared==0?"无法比较。":"截断到 "+Data.Day(cut)+" 重跑，重叠 "+compared+" 天最大偏差 "+worst.ToString("0.###E+0",CultureInfo.InvariantCulture)+"。"});
            }
            Audit.Write(result,folder);
            return result.Verdict;
        },v=>SetStatus("审计报告已写入 "+folder+"：脚本"+v));
    }

    void Fundamentals() {
        var book=statements;
        var p=Rows(body,43,40,88,88,-100);
        var tools=Flow(); p.Controls.Add(tools,0,0);
        var state=LabelOf(book==null?"尚未导入财报":book.File+"  ·  标的 "+book.Symbol+"  ·  "+book.Count+" 个报告期  ·  "+book.MappedCount+" 个关键科目",Theme.Small,Theme.Muted);
        state.Margin=new Padding(6,9,10,0); state.AutoEllipsis=true;
        tools.Controls.Add(ButtonOf("导入财报",async ()=>{
            string path=Open("财报文件|*.xls;*.xlsx;*.xlsm;*.csv|所有文件|*.*");
            if(path==null) return;
            await Work(()=>StatementBook.Load(path),b=>{
                statements=b;
                fundamentals=null;
                bool matched=market.ContainsKey(b.Symbol);
                if(matched) { SetSymbols(b.Symbol); backFrom=backTo=null; }
                statementAsOf=Selected.Bars.Last().Date;
                ShowPage(page);
                string note="已载入 "+b.File+"  ·  "+b.Count+" 个报告期  ·  "+b.Subjects.Count+" 个科目（识别 "+b.MappedCount+" 个）  ·  披露日按法定期限估计";
                if(!matched&&CodeLike(b.Symbol)&&CodeLike(selectedSymbol)&&b.Symbol!=selectedSymbol)
                    note+="  ·  注意：财报标的 "+b.Symbol+" 不在行情中，当前标的 "+selectedSymbol+"，请在相关性分析前确认对应关系";
                SetStatus(note);
            });
        },true,103));
        var asOfDate=PickerDate(statementAsOf??Selected.Bars.Last().Date);
        var asOf=DateInput(asOfDate);
        asOf.ValueChanged+=(o,e)=>{ statementAsOf=asOf.Value.Date; ShowPage(page); };
        Field(tools,"可见截至",asOf);
        tools.Controls.Add(ButtonOf("接入概率模型",async ()=>{
            var s=Selected; var current=statements;
            if(current==null) { SetStatus("请先导入财报，再接入概率模型。"); return; }
            await Work(()=>{
                var contexts=current.Contexts(s);
                if(contexts.Count<100) throw new FormatException("财报推导的快照只有 "+contexts.Count+" 条，至少需要 100 条。请换用更长的行情或补齐每股收益与每股净资产。");
                return new KeyValuePair<Dictionary<DateTime,Context>,Model>(contexts,Model.Fit(s,contexts));
            },pair=>{
                var m=pair.Value;
                SetStatus("财报模型（ROE=TTM 每股收益÷每股净资产，PE=收盘÷TTM 每股收益，情绪取 0）  ·  匹配 "+m.Matched+" 条  ·  "+
                    (m.WalkForward?"滚动验证 "+Pct(m.WalkAccuracy)+" · AUC "+Num(m.WalkAuc,"F2"):"滚动折数不足")+"  ·  训练至 "+Data.Day(m.TrainEnd));
            });
        },false,131));
        tools.Controls.Add(ButtonOf("导出报表",()=>{
            var current=statements;
            if(current==null) { SetStatus("请先导入财报。"); return; }
            using(var dialog=new FolderBrowserDialog{Description="选择 statement.csv 与摘要的输出文件夹"}) {
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                try {
                    string csv,summary; current.Write(dialog.SelectedPath,out csv,out summary);
                    SetStatus("已导出 "+Path.GetFileName(csv)+" 与 "+Path.GetFileName(summary)+"。");
                } catch(Exception error) { SetStatus(error.Message); }
            }
        },false,103));
        tools.Controls.Add(state);

        var bar=Flow(); p.Controls.Add(bar,0,1);
        bar.Controls.Add(Note(book==null
            ? "可读取同花顺／Wind 导出的 .xls、.xlsx 或 CSV：科目在行、报告期在列、数值为年初至今累计。导入后按「可见截至」日期决定哪些报告期可用。"
            : "累计口径：一季报直接取，二/三/四季报减上一季累计得单季；TTM 为最近四个单季之和。披露日按法定期限推算并标注「按法定期限估计」，可见截至日之前的 K 线取不到更晚的报告期。"));

        int visible=book==null?-1:book.IndexAt(asOfDate);
        double net=visible<0?double.NaN:book.Ttm("net_profit",visible);
        double revenue=visible<0?double.NaN:book.Ttm("revenue",visible);
        double roe=visible<0?double.NaN:book.Value("roe",visible);
        double single=visible<0?double.NaN:book.Single("net_profit",visible);
        double ocf=visible<0?double.NaN:book.Value("ocf_ps",visible);
        double eps=visible<0?double.NaN:book.Ttm("eps",visible);
        var cards=PanelIn(p,2);
        Cards(cards,"可见报告期",visible<0?"—":Data.Day(book.Periods[visible]),
            "单季净利润（元）",visible<0?"—":StatementBook.Show(single,0),
            "TTM 净利润（元）",visible<0?"—":StatementBook.Show(net,0));
        var cards2=PanelIn(p,3);
        Cards(cards2,"TTM 营业总收入（元）",visible<0?"—":StatementBook.Show(revenue,0),
            "净资产收益率",visible<0?"—":StatementBook.Show(roe,2)+"%",
            "现金流质量",visible<0||double.IsNaN(ocf)||double.IsNaN(eps)||Math.Abs(eps)<1e-12?"—":(ocf/eps).ToString("0.00",Data.Inv));

        var inner=CardAt(p,4,"财报视图 · 数值为累计口径，TTM 为最近四个单季之和");
        var season=Grid("报告期","估计可见日","单季净利润","单季营收","TTM 净利润","TTM 营收","单季净利同比","TTM 净利同比");
        MinWidths(season,88,98,110,110,110,110,96,96); AlignNumbers(season,2,3,4,5,6,7);
        var cumulative=Grid("报告期","累计净利润","累计营收","毛利率","净利率","ROE","资产负债率","每股收益");
        MinWidths(cumulative,88,110,112,80,80,76,84,88); AlignNumbers(cumulative,1,2,3,4,5,6,7);
        var subjects=Grid("科目","可见期数值","可见期单季","可见期 TTM","识别");
        MinWidths(subjects,150,110,110,110,90); AlignNumbers(subjects,1,2,3);
        var chart=PanelOf();
        var viewTabs=ViewTabs(inner,new[]{"单季与 TTM","累计与同比","科目明细","趋势图"},season,cumulative,subjects,chart);
        if(book!=null) {
            for(int i=book.Count-1;i>=0;i--) {
                season.Rows.Add(Data.Day(book.Periods[i]),Data.Day(book.Available[i]),StatementBook.Show(book.Single("net_profit",i),0),
                    StatementBook.Show(book.Single("revenue",i),0),StatementBook.Show(book.Ttm("net_profit",i),0),StatementBook.Show(book.Ttm("revenue",i),0),
                    Pct(book.SingleYoy("net_profit",i)),Pct(book.TtmYoy("net_profit",i)));
                cumulative.Rows.Add(Data.Day(book.Periods[i]),StatementBook.Show(book.Value("net_profit",i),0),StatementBook.Show(book.Value("revenue",i),0),
                    StatementBook.Show(book.Value("gross_margin",i),2),StatementBook.Show(book.Value("net_margin",i),2),
                    StatementBook.Show(book.Value("roe",i),2),StatementBook.Show(book.Value("debt_ratio",i),2),StatementBook.Show(book.Value("eps",i),4));
            }
            for(int i=0;i<book.Subjects.Count;i++) {
                var subject=book.Subjects[i];
                subjects.Rows.Add(subject.Name,visible<0?"—":StatementBook.Show(subject.Values[visible],Math.Abs(subject.Values[visible])>=100?0:4),
                    subject.Mapped&&subject.Metric.Flow&&visible>=0?StatementBook.Show(subject.Singles[visible],Math.Abs(subject.Singles[visible])>=100?0:4):"—",
                    subject.Mapped&&visible>=0?StatementBook.Show(subject.Ttms[visible],Math.Abs(subject.Ttms[visible])>=100?0:4):"—",
                    subject.Mapped?subject.Metric.Label:"原始科目");
            }
            var days=book.Periods;
            var singles=book.Subject("net_profit");
            var singleValues=singles==null?new double[days.Length]:singles.Singles;
            var ttmValues=singles==null?new double[days.Length]:singles.Ttms;
            chart.Controls.Add(new Plot(days,"#,0",new[]{
                new PlotLine("单季净利润",singleValues,Theme.Accent,true),
                new PlotLine("TTM 净利润",ttmValues,Theme.Violet)
            },shade:false));
        } else {
            var blank=Note("尚未导入财报。点击「导入财报」选择同花顺／Wind 导出的 .xls、.xlsx 或 CSV。");
            chart.Controls.Add(blank);
            viewTabs(2);
        }
        if(book!=null) SetStatus("财报 "+book.File+"  ·  可见截至 "+Data.Day(asOfDate)+"  ·  "+
            (visible<0?"此时还没有可用报告期":("可见 "+Data.Day(book.Periods[visible])+"（估计披露日 "+Data.Day(book.Available[visible])+"）"))+
            "  ·  披露日为法定期限估计");
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
