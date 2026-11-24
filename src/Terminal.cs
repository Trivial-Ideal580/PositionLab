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
sealed class Terminal : Form {
    readonly Panel body=new Panel(), side=new Panel(), head=new Panel();
    readonly Label title=LabelOf("",Theme.Head,Theme.Text), sub=LabelOf("",Theme.Small,Theme.Muted),
        status=LabelOf("",Theme.Small,Theme.Muted), source=LabelOf("",Theme.Small,Theme.Accent);
    readonly Button symbol;
    string selectedSymbol;
    readonly Button[] nav=new Button[5];
    readonly string[] names={"因子工作台","相关性与概率","策略审计与回测","持仓透视","数据与声明"};
    readonly string[] captions={"技术指标、历史分位与综合因子评分","基本面、消息情绪与价格因子的时序验证","策略导入、未来引用审计、样本区间与成交复核","持仓估值、集中度、周期与组合风险","导入格式、示例文件、回测假设与安全说明"};
    Dictionary<string,Series> market;
    Dictionary<DateTime,Context> demoContext;
    List<Holding> loaded;
    string marketName="合成演示行情", contextPath, rawPositions="", cashText="0", ruleName="均线突破", buy=Rule.DefaultBuy, sell=Rule.DefaultSell;
    DateTime? backFrom,backTo;
    string initial="100000", fee="8", slippage="5";
    bool demoMode=true,busy,verifying;
    int page,window=252,chartMode,chartRange=120;
    bool expandedFactors;

    internal Terminal() {
        Text="持仓分析与因子实验室";
        Font=Theme.Body; ForeColor=Theme.Text; BackColor=Theme.Background;
        AutoScaleMode=AutoScaleMode.Dpi;
        StartPosition=FormStartPosition.CenterScreen; MinimumSize=new Size(1050,700); Size=new Size(1390,900);
        var screen=Screen.FromPoint(Cursor.Position).WorkingArea;
        Size=new Size(Math.Max(MinimumSize.Width,Math.Min(Width,screen.Width)),Math.Max(MinimumSize.Height,Math.Min(Height,screen.Height)));
        var demo=Demo.Create(); market=demo.Market; demoContext=demo.Context;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1, Margin=Padding.Empty};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,202)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        Controls.Add(root);
        side.Dock=DockStyle.Fill; side.BackColor=Theme.Sidebar; side.Margin=Padding.Empty; root.Controls.Add(side,0,0);
        var workspace=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty,Padding=new Padding(24,16,24,8)};
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,77)); workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100)); workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
        root.Controls.Add(workspace,1,0);
        head.Dock=body.Dock=status.Dock=DockStyle.Fill;
        head.Margin=body.Margin=status.Margin=Padding.Empty;
        workspace.Controls.Add(head,0,0); workspace.Controls.Add(body,0,1); workspace.Controls.Add(status,0,2);
        status.TextAlign=ContentAlignment.MiddleLeft; status.AutoEllipsis=true;
        BuildSidebar();
        title.AutoSize=false; title.SetBounds(0,0,500,43); sub.Location=new Point(2,46);
        var top=Flow(); top.Dock=DockStyle.Right; top.Width=295; top.FlowDirection=FlowDirection.RightToLeft; top.Padding=new Padding(0,13,0,0);
        top.Controls.Add(ButtonOf("导入行情",async ()=>await ImportMarket(),true,100));
        symbol=ButtonOf("选择标的",ChooseSymbol,false,174);
        symbol.AutoEllipsis=true; top.Width=298; top.Controls.Add(symbol);
        head.Controls.Add(top); head.Controls.Add(title); head.Controls.Add(sub);
        SetSymbols("DEMO-A"); ShowPage(0);
        SetStatus("已加载合成演示行情。导入自己的 CSV 可开始正式研究。");
    }
    void BuildSidebar() {
        var mark=new Panel {BackColor=Theme.Accent}; mark.SetBounds(18,28,34,34);
        mark.Paint+=(o,e)=>{
            using(var pen=new Pen(Theme.Card,2)) e.Graphics.DrawLines(pen,new[]{new Point(7,23),new Point(13,17),new Point(18,20),new Point(26,10)});
        };
        side.Controls.Add(mark);
        var brand=LabelOf("持仓实验室",Theme.Section,Theme.SidebarText); brand.Location=new Point(62,32); side.Controls.Add(brand);
        for(int i=0;i<nav.Length;i++) {
            int target=i;
            nav[i]=ButtonOf(names[i],()=>{ShowPage(target); SetStatus("当前标的 "+selectedSymbol+"  ·  "+marketName);},false,173);
            nav[i].Location=new Point(14,95+i*51); nav[i].Height=43; nav[i].TextAlign=ContentAlignment.MiddleLeft; nav[i].Padding=new Padding(10,0,0,0);
            side.Controls.Add(nav[i]);
        }
        var bottom=new Panel {Dock=DockStyle.Bottom, Height=62};
        side.Controls.Add(bottom);
        source.ForeColor=Theme.SidebarText; source.AutoSize=false; source.AutoEllipsis=true; source.SetBounds(20,4,170,25);
        var badge=LabelOf("离线运行 · 仅供研究",Theme.Small,Theme.SidebarMuted);
        badge.Location=new Point(20,31); bottom.Controls.Add(badge); bottom.Controls.Add(source);
    }
    Series Selected { get { return market[selectedSymbol]; } }
    void SetSymbols(string prefer) {
        selectedSymbol=market.ContainsKey(prefer)?prefer:market.Keys.OrderBy(x=>x).First();
        symbol.Text=selectedSymbol+"  ▾";
        symbol.AccessibleName="选择标的，当前 "+selectedSymbol;
    }
    void ChooseSymbol() {
        using(var picker=new Form {Text="选择标的",StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(410,420),FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,
            BackColor=Theme.Background,ForeColor=Theme.Text,Font=Theme.Body}) {
            var layout=Rows(picker,37,-100,40);
            layout.Padding=new Padding(10);
            var search=Input("",200); search.Dock=DockStyle.Fill; search.AccessibleName="搜索标的代码";
            var list=new ListBox{Dock=DockStyle.Fill,BackColor=Theme.Card,ForeColor=Theme.Text,IntegralHeight=false};
            var actions=Flow();
            var pick=ButtonOf("切换标的",()=>{
                if(list.SelectedItem==null) return;
                picker.DialogResult=DialogResult.OK; picker.Close();
            },true);
            actions.Controls.Add(pick);
            layout.Controls.Add(search,0,0); layout.Controls.Add(list,0,1); layout.Controls.Add(actions,0,2);
            picker.AcceptButton=pick;
            var sorted=market.Keys.OrderBy(x=>x).ToArray();
            Action filter=()=>{
                string term=search.Text.Trim();
                list.BeginUpdate(); list.Items.Clear();
                list.Items.AddRange(sorted.Where(x=>x.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0).Take(500).Cast<object>().ToArray());
                list.SelectedItem=selectedSymbol; if(list.SelectedIndex<0&&list.Items.Count>0) list.SelectedIndex=0;
                list.EndUpdate();
            };
            search.TextChanged+=(o,e)=>filter();
            list.DoubleClick+=(o,e)=>pick.PerformClick();
            filter();
            if(picker.ShowDialog(this)==DialogResult.OK&&(string)list.SelectedItem!=selectedSymbol) {
                SetSymbols((string)list.SelectedItem); backFrom=backTo=null; ShowPage(page);
                SetStatus("当前标的 "+selectedSymbol);
            }
        }
    }
    void ShowPage(int index) {
        page=index; title.Text=names[index]; sub.Text=captions[index]; source.Text=marketName;
        for(int i=0;i<nav.Length;i++) {nav[i].BackColor=i==page?Theme.SidebarActive:Theme.Sidebar; nav[i].ForeColor=i==page?Theme.SidebarText:Theme.SidebarMuted; nav[i].FlatAppearance.MouseOverBackColor=Theme.SidebarActive;}
        body.SuspendLayout();
        try {
            while(body.Controls.Count>0) body.Controls[0].Dispose();
            switch(page) {
                case 0: Factors(); break;
                case 1: Probability(); break;
                case 2: Strategy(); break;
                case 3: Positions(); break;
                default: Help(); break;
            }
        } finally { body.ResumeLayout(true); }
    }
    void SetStatus(string message) {status.Text=message; }
    async Task Work<T>(Func<T> calculate,Action<T> render) {
        if(busy) return;
        busy=true; Enabled=false; UseWaitCursor=true; SetStatus("正在处理，请稍候…");
        try { var result=await Task.Run(calculate); SetStatus("处理完成"); render(result); }
        catch(Exception e) { SetStatus(e.Message); if(!verifying) MessageBox.Show(this,e.Message,"无法完成操作",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
        finally { UseWaitCursor=false; Enabled=true; busy=false; }
    }
    async Task ImportMarket() {
        string file=Open("行情 CSV|*.csv");
        if(file==null) return;
        await Work(()=>Data.Market(file),data=>{
            market=data; marketName=Path.GetFileName(file); demoMode=false; contextPath=null; backFrom=backTo=null;
            SetSymbols(""); ShowPage(page); SetStatus("已载入 "+data.Count+" 个标的，"+data.Values.Sum(s=>s.Bars.Length).ToString("N0")+" 条行情。");
        });
    }
    string Open(string filter) {
        using(var dialog=new OpenFileDialog{Filter=filter,CheckFileExists=true,RestoreDirectory=true})
            return dialog.ShowDialog(this)==DialogResult.OK ? dialog.FileName : null;
    }
    static Label LabelOf(string text,Font font,Color color) {
        return new Label {Text=text,Font=font,ForeColor=color,AutoSize=true,Margin=new Padding(0,4,0,4),UseMnemonic=false};
    }
    static Button ButtonOf(string label,Action action,bool primary=false,int width=105) {
        var button=new Button {Text=label,Width=Math.Max(width,TextRenderer.MeasureText(label,Theme.Small).Width+22),Height=34,FlatStyle=FlatStyle.Flat,BackColor=primary?Theme.Accent:Theme.Surface,
            ForeColor=primary?Theme.Card:Theme.Text,Font=Theme.Small,Margin=new Padding(0,3,8,3),Cursor=Cursors.Hand,UseMnemonic=false};
        button.FlatAppearance.BorderSize=0;
        button.FlatAppearance.MouseOverBackColor=primary?Theme.AccentHover:Theme.Border;
        button.Click+=(s,e)=>action(); return button;
    }
    static FlowLayoutPanel Flow() { return new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty,Padding=Padding.Empty}; }
    static TableLayoutPanel Rows(Control parent,params int[] sizes) {
        var panel=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=sizes.Length,Margin=Padding.Empty,Padding=Padding.Empty};
        foreach(int n in sizes) panel.RowStyles.Add(new RowStyle(n<0?SizeType.Percent:SizeType.Absolute,Math.Abs(n)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); parent.Controls.Add(panel); return panel;
    }
    static void ViewTabs(Control container,string[] names,params Control[] pages) {
        var holder=new Panel{Dock=DockStyle.Fill,BackColor=Theme.Card,Padding=new Padding(14,8,14,9)};
        container.Controls.Add(holder);
        var r=Rows(holder,44,-100);
        var bar=Flow(); r.Controls.Add(bar,0,0);
        var content=PanelIn(r,1);
        var buttons=new Button[names.Length];
        Action<int> select=index=>{
            for(int j=0;j<pages.Length;j++) {
                pages[j].Visible=j==index;
                TabStyle(buttons[j],j==index);
            }
        };
        for(int i=0;i<pages.Length;i++) {
            int index=i;
            buttons[i]=ButtonOf(names[i],()=>select(index),false,91);
            bar.Controls.Add(buttons[i]); content.Controls.Add(pages[i]);
        }
        select(0);
    }
    static void TabStyle(Button button,bool active) {
        button.BackColor=active?Theme.Accent:Theme.Card;
        button.ForeColor=active?Theme.Card:Theme.Muted;
        button.FlatAppearance.MouseOverBackColor=active?Theme.AccentHover:Theme.Surface;
    }
    static Panel PanelOf() { return new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty}; }
    static TableLayoutPanel Card(Control parent,string name) {
        var panel=new Panel {Dock=DockStyle.Fill, BackColor=Theme.Card,Margin=Padding.Empty,Padding=new Padding(14,9,14,9)};
        parent.Controls.Add(panel);
        var r=Rows(panel,36,-100);
        r.Controls.Add(LabelOf(name,Theme.Section,Theme.Text),0,0); return r;
    }
    static TableLayoutPanel CardAt(TableLayoutPanel parent,int row,string name) { var holder=PanelOf(); parent.Controls.Add(holder,0,row); return Card(holder,name); }
    static void Cards(Control parent,params string[] items) {
        var t=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=items.Length/2,RowCount=1,Margin=Padding.Empty};
        parent.Controls.Add(t);
        for(int i=0;i<items.Length;i+=2) {
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,200f/items.Length));
            var p=new Panel{BackColor=Theme.Card,Dock=DockStyle.Fill,Margin=new Padding(0,0,i==items.Length-2?0:10,10)};
            var title=LabelOf(items[i],Theme.Small,Theme.Muted); title.Location=new Point(16,9); p.Controls.Add(title);
            var value=LabelOf(items[i+1],Theme.Metric, i==0?Theme.Accent:Theme.Text); value.AutoSize=false; value.AutoEllipsis=true; value.TextAlign=ContentAlignment.MiddleLeft;
            Action fit=()=>{
                int available=Math.Max(1,p.ClientSize.Width-28);
                value.Font=TextRenderer.MeasureText(value.Text,Theme.Metric).Width<=available?Theme.Metric:Theme.MetricSmall;
                value.SetBounds(14,28,available,47);
            };
            p.Controls.Add(value); p.Resize+=(o,e)=>fit(); fit();
            t.Controls.Add(p,i/2,0);
        }
    }
    static DataGridView Grid(params string[] columns) {
        var g=new DataGridView {Dock=DockStyle.Fill,ReadOnly=true,RowHeadersVisible=false,BackgroundColor=Theme.Card, BorderStyle=BorderStyle.None,
            AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,EnableHeadersVisualStyles=false,ColumnHeadersHeight=37,GridColor=Theme.Grid,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None};
        g.DefaultCellStyle.BackColor=Theme.Card; g.DefaultCellStyle.ForeColor=Theme.Text; g.DefaultCellStyle.SelectionBackColor=Theme.Border;
        g.DefaultCellStyle.SelectionForeColor=Theme.Text; g.DefaultCellStyle.Font=Theme.Small;
        g.AlternatingRowsDefaultCellStyle.BackColor=Theme.Background; g.DefaultCellStyle.Padding=new Padding(7,1,7,1);
        g.ColumnHeadersDefaultCellStyle.BackColor=Theme.Surface; g.ColumnHeadersDefaultCellStyle.ForeColor=Theme.Muted;
        g.ColumnHeadersDefaultCellStyle.Font=Theme.Small; g.ColumnHeadersDefaultCellStyle.Padding=new Padding(7,0,7,0); g.RowTemplate.Height=34;
        foreach(string s in columns) {int i=g.Columns.Add(s,s); g.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable; g.Columns[i].MinimumWidth=TextRenderer.MeasureText(s,Theme.Small).Width+21;}
        return g;
    }
    static void AlignNumbers(DataGridView grid,params int[] indexes) {
        foreach(int i in indexes) {
            grid.Columns[i].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;
            grid.Columns[i].HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleRight;
        }
    }
    static void MinWidths(DataGridView grid,params int[] widths) {
        for(int i=0;i<widths.Length;i++) grid.Columns[i].MinimumWidth=Math.Max(grid.Columns[i].MinimumWidth,widths[i]);
    }
    static TextBox Input(string s,int width=90,bool multiline=false) {
        return new TextBox {Text=s, BackColor=Theme.Surface,ForeColor=Theme.Text,BorderStyle=BorderStyle.FixedSingle,Width=width,
            Multiline=multiline,ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None,Margin=new Padding(0,3,12,3)};
    }
    static Label Note(string text) {
        return new Label {Text=text,Font=Theme.Small,ForeColor=Theme.Muted,Dock=DockStyle.Fill,UseMnemonic=false,TextAlign=ContentAlignment.MiddleLeft};
    }
    static void Field(FlowLayoutPanel f,string label,Control control) {
        var l=LabelOf(label,Theme.Small,Theme.Muted); l.Margin=new Padding(0,9,6,0); f.Controls.Add(l); f.Controls.Add(control);
    }
    static string Num(double v,string pattern="N2") {
        return double.IsNaN(v)||double.IsInfinity(v) ? "—" : v.ToString(pattern,CultureInfo.InvariantCulture);
    }
    static string Pct(double v) {return Num(v,"P1");}
    static string Value(string name,double n) {
        return new[]{"RET1","MOM20","VOL20"}.Contains(name) ? Pct(n) : Num(n,"#,0.######");
    }
    static void ExportGrid(DataGridView grid,string name,Terminal owner) {
        using(var file=new SaveFileDialog { Filter="CSV|*.csv",FileName=name+".csv",RestoreDirectory=true }) {
            if(file.ShowDialog(owner)!=DialogResult.OK) return;
            try {
                using(var writer=new StreamWriter(file.FileName,false,new UTF8Encoding(true))) {
                    writer.WriteLine(string.Join(",",grid.Columns.Cast<DataGridViewColumn>().Select(c=>Data.Escape(c.HeaderText))));
                    foreach(DataGridViewRow row in grid.Rows) writer.WriteLine(string.Join(",",row.Cells.Cast<DataGridViewCell>().Select(c=>Data.Escape(Convert.ToString(c.Value)))));
                }
                owner.SetStatus("已导出 "+Path.GetFileName(file.FileName)+"。");
            } catch(Exception error) { MessageBox.Show(owner,error.Message,"导出失败"); }
        }
    }
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
        var card=CardAt(p,3,"因子相关性与模型系数");
        var grid=Grid("输入因子","训练集 Pearson r · 对次日收益","标准化逻辑回归系数");
        card.Controls.Add(grid,0,1);
        tools.Controls.Add(ButtonOf("导入快照",()=>{
            string path=Open("基本面及信息面 CSV|*.csv");
            if(path!=null) {contextPath=path; indicator.Text=Path.GetFileName(path); ShowPage(1);}
        },false));
        tools.Controls.Add(ButtonOf("拟合与验证",async ()=>{
            var s=Selected; var path=contextPath;
            await Work(()=>Model.Fit(s,path==null?(demoMode&&s.Symbol=="DEMO-A"?demoContext:new Dictionary<DateTime,Context>()):Data.Contexts(path,s.Symbol)),m=>{
                while(cards.Controls.Count>0) cards.Controls[0].Dispose();
                Cards(cards,"次日上涨估计",Pct(m.Probability),"样本外准确率",Pct(m.Accuracy),"多数类基准",Pct(m.Baseline),"AUC / Brier",Num(m.Auc,"F2")+" / "+Num(m.Brier,"F2"));
                grid.Rows.Clear();
                for(int i=0;i<Model.Features.Length;i++) grid.Rows.Add(Model.Features[i],Num(m.Correlations[i],"F4"),Num(m.Coefficients[i],"F4"));
                SetStatus("截至 "+Data.Day(m.Forecast)+"  ·  训练 "+m.Train+" 条至 "+Data.Day(m.TrainEnd)+"  ·  测试 "+m.Test+" 条自 "+Data.Day(m.TestStart)+"  ·  匹配 "+m.Matched+" 条");
            });
        },true,112));
        tools.Controls.Add(ButtonOf("导出相关性",()=>ExportGrid(grid,"correlations",this),false,113));
        tools.Controls.Add(indicator);
        p.Controls.Add(Note("按可用日精确对齐，并按时间留出约 30% 测试集，边界留空一条。预测截至最新行情日，阈值为 0.50。模型估计随样本变化。"),0,1);
        Cards(cards,"次日上涨估计","—","样本外准确率","—","多数类基准","—","AUC / Brier","—");
    }
    static DateTimePicker DateInput(DateTime date) {
        return new DateTimePicker {Value=date,Width=135,Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd",Margin=new Padding(0,4,6,0)};
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
    static DateTime PickerDate(DateTime date) {
        return date<DateTimePicker.MinimumDateTime?DateTimePicker.MinimumDateTime:date>DateTimePicker.MaximumDateTime?DateTimePicker.MaximumDateTime:date;
    }
    static Panel PanelIn(TableLayoutPanel p,int row) {var q=PanelOf(); p.Controls.Add(q,0,row); return q;}
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












