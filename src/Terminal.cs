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
sealed partial class Terminal : Form {
    readonly Panel body=new Panel(), side=new Panel(), head=new Panel();
    readonly Label title=LabelOf("",Theme.Head,Theme.Text), sub=LabelOf("",Theme.Small,Theme.Muted),
        status=LabelOf("",Theme.Small,Theme.Muted), source=LabelOf("",Theme.Small,Theme.Accent);
    readonly Button symbol;
    string selectedSymbol;
    readonly Button[] nav=new Button[6];
    readonly string[] names={"因子工作台","相关性与概率","策略审计与回测","持仓透视","财报基本面","数据与声明"};
    readonly string[] captions={"技术指标、历史分位与综合因子评分","基本面、情绪与价格因子的时序验证与滚动评估","策略导入、未来引用审计、样本区间与成交复核","持仓估值、集中度、周期与组合风险","财报科目、单季与 TTM、披露期限与可见性","导入格式、示例文件、回测假设与安全说明"};
    Dictionary<string,Series> market;
    Dictionary<DateTime,Context> demoContext;
    List<Holding> loaded;
    StatementBook statements;
    DateTime? statementAsOf;
    FundamentalAnalysis fundamentals;
    int fundamentalWindow=250;
    string marketName="合成演示行情", contextPath, rawPositions="", cashText="0", ruleName="均线突破", buy=Rule.DefaultBuy, sell=Rule.DefaultSell;
    DateTime? backFrom,backTo;
    string initial="100000", fee="8", slippage="5";
    bool demoMode=true,busy,verifying;
    int page,window=252,chartMode,chartRange=120;
    bool expandedFactors;
    readonly Settings settings;
    readonly bool persist;

    internal Terminal() : this(true) { }

    internal Terminal(bool persist) {
        this.persist=persist;
        settings=persist?Settings.Load():new Settings();
        Text="持仓分析与因子实验室";
        Font=Theme.Body; ForeColor=Theme.Text; BackColor=Theme.Background;
        AutoScaleMode=AutoScaleMode.Dpi;
        StartPosition=FormStartPosition.CenterScreen; MinimumSize=new Size(1050,700); Size=new Size(1390,900);
        var screen=Screen.FromPoint(Cursor.Position).WorkingArea;
        Size=new Size(Math.Max(MinimumSize.Width,Math.Min(Width,screen.Width)),Math.Max(MinimumSize.Height,Math.Min(Height,screen.Height)));
        window=settings.Window; chartMode=settings.ChartMode; chartRange=settings.ChartRange;
        initial=settings.Initial; fee=settings.Fee; slippage=settings.Slippage; cashText=settings.Cash;
        if(settings.HasBounds) {
            var bounds=new Rectangle(settings.Left,settings.Top,Math.Max(MinimumSize.Width,settings.Width),Math.Max(MinimumSize.Height,settings.Height));
            if(Screen.AllScreens.Any(s=>s.WorkingArea.IntersectsWith(bounds))) { StartPosition=FormStartPosition.Manual; Bounds=bounds; }
        }
        if(settings.Maximized) WindowState=FormWindowState.Maximized;
        FormClosing+=(o,e)=>SaveSettings();
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
                SetSymbols((string)list.SelectedItem); backFrom=backTo=null; fundamentals=null; ShowPage(page);
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
                case 4: Fundamentals(); break;
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
        string file=Open("行情文件|*.csv;*.txt;*.xlsx;*.xlsm|所有文件|*.*");
        if(file==null) return;
        await Work(()=>{
            var report=new ImportReport();
            var data=MarketImport.Load(file,report);
            return new KeyValuePair<Dictionary<string,Series>,ImportReport>(data,report);
        },pair=>{
            var data=pair.Key; var report=pair.Value;
            market=data; marketName=Path.GetFileName(file); demoMode=false; contextPath=null; backFrom=backTo=null; fundamentals=null;
            SetSymbols(""); ShowPage(page);
            int gaps=data.Values.Sum(s=>s.Gaps);
            string detail=report.Mapping.Count>0?"  ·  "+string.Join("，",report.Mapping):"";
            SetStatus("已载入 "+report.Summary+detail+
                (gaps>0?"  ·  检测到 "+gaps+" 处超过 10 个自然日的缺口":""));
        });
    }

    string Open(string filter) {
        using(var dialog=new OpenFileDialog{Filter=filter,CheckFileExists=true,RestoreDirectory=true})
            return dialog.ShowDialog(this)==DialogResult.OK ? dialog.FileName : null;
    }
    void SaveSettings() {
        if(!persist) return;
        var bounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;
        settings.Window=window; settings.ChartMode=chartMode; settings.ChartRange=chartRange;
        settings.Initial=initial; settings.Fee=fee; settings.Slippage=slippage; settings.Cash=cashText;
        settings.Left=bounds.X; settings.Top=bounds.Y; settings.Width=bounds.Width; settings.Height=bounds.Height;
        settings.Maximized=WindowState==FormWindowState.Maximized;
        settings.Save();
    }

}
}
