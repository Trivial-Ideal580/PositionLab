using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PositionLab {
static class Program {
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();
    internal const string Help=@"导入文件
行情支持 CSV、TXT、XLSX 与 XLS（Excel 97-2003）。程序会探测编码（UTF-8／GBK）、分隔符，并按列名别名识别同花顺、Wind、通达信等导出表头：日期可写 日期／交易日期／date／trade_dt，收盘可写 收盘／收盘价／close，成交量可写 成交量／volume；也接受标题行在表头之前、千分位数字与「万／亿」后缀。没有标的列时用文件名作为代码（如 600000.csv）。日期统一归一为 YYYY-MM-DD，价格必须为正，成交量可为零。同一标的日期重复时保留最后一条并计数。程序不会自动补停牌、缺失交易日和公司行动。

财报支持同花顺／Wind 导出的 XLS、XLSX 与 CSV。要求单公司多期、科目在行、报告期在列，数值为年初至今累计；一列一个报告期，列顺序不限（程序按日期升序重排）。界面「财报基本面」或命令行 --statements 财报.xls 输出目录 会生成 statement.csv 与 statement-summary.txt。单季：一季报直接取累计值，二／三／四季报减去上一季累计；TTM 为最近四个单季之和；同比按同口径（单季对单季、TTM 对 TTM），基期不为正时留空。披露日按法定披露期限推算：一季报 4 月 30 日、半年报 8 月 31 日、三季报 10 月 31 日、年报次年 4 月 30 日；文件通常没有公告日期，界面与导出均标注「按法定期限估计」。可见截至日之前的 K 线或模型样本看不到更晚的报告期。财报页的「接入概率模型」用最近可见报告期的 TTM 每股收益与每股净资产推导 ROE、用收盘价÷TTM 每股收益推导 PE，情绪取 0，与概率模型共享同一 available_date 口径。

基本面与信息面使用 UTF-8 CSV。字段为 symbol,available_date,roe,pe,sentiment。ROE 按百分点输入，例如 12.5；sentiment 范围为 -1 到 1。每个收盘日须录入当时已知的 ROE、PE 和情绪快照。盘后披露请从下一可用交易日起纳入。模型仅使用精确匹配的行，缺失日不填补。

持仓支持手工输入和 UTF-8 CSV。字段为 symbol,quantity,cost,bought_date,sector。支持多笔同标的长仓，按共同估值日收盘价估值。风险使用最多 60 个严格同步的日收益和当前市值权重，现金收益设为零。持仓风格按市值加权日历天划分，15 天及以下为短周期，16–90 天为中周期。仅有持仓快照时，无法重建真实交易频率、择时或历史净值。

技术指标与评分
使用 Wilder 平滑的 RSI14 和 ATR14，首个完整窗口以算术平均初始化；MACD 柱为 2 × (DIF−DEA)；布林带为 MA20 ± 2 倍总体标准差；年化波动使用 252 个交易日、样本标准差。综合分由动量分位 50%、RSI 分位 30%、低波动分位 20% 组成，并列按半权重计算。有效分位不足时留空。
与常见行情软件的口径差异：EMA 以首根收盘价为种子，而非以 SMA 为种子，序列前数十根的 DIF 与 MACD 会有小幅差异；布林带用总体标准差（除以 20）；年化统一按 252 个交易日。跨平台比对前请先统一这些口径。同一标的两根 K 线间隔超过 10 个自然日会计入缺口提示；程序不补停牌与缺失交易日，缺口处的 RET1 只是两端收盘价之差。

策略格式与执行
UTF-8 .rule 或 .txt 文件使用 NAME=、BUY=、SELL=。支持四则运算、括号与比较符 >、<、>=、<=、=、==、!=，逻辑 AND、OR、NOT。字段可带历史下标，如 CLOSE[1] 表示上一交易日，最多回看 252 日，负下标会被拒绝。函数：ABS(x)、MIN(a,b)、MAX(a,b)、IF(条件,a,b)、CROSS(a,b) 上穿、CROSSDOWN(a,b) 下穿、SUM(x,n)、AVG(x,n)、COUNT(条件,n)，窗口取 1–252。例 BUY=CROSS(MA5, MA20) AND COUNT(CLOSE > MA60, 5) >= 3。指标缺失处返回空值并跳过该日，不做填充；未识别字段、负向回看与未知函数在解析期即被拒绝。
回测从指定区间首日收盘开始判定信号，下一交易日开盘成交，冲突和所需指标缺失时跳过。支持单标的，或把全部标的按等权组合回测：组合模式下资金平均分配到每个标的，各自独立持仓、不做跨标的再平衡，组合净值为各标的净值之和，基准为等权买入持有；标的多于 30 个时按代码排序只取前 30 个，界面会显示实际数量。脚本策略可用 INIT pool=1 rebalance=monthly|weekly|<N 根> 与 TARGET 代码=权重 做资金池再平衡：再平衡日收盘按目标权重折算目标市值，差额在次根开盘成交，成交记录标记为 rebalance；卖出先于买入，现金不足时按可用现金缩量，maxposition 仍生效。仅做多、整单位、比例费率与滑点。未模拟涨跌停、停牌成交约束、分红税、资金利息和市场冲击。短区间的年化收益可很极端。
策略对比会在同一区间、同一费用参数下批量回测多条规则，列出收益、年化、最大回撤、夏普、成交、胜率与持仓占比，并叠加净值曲线。策略库默认四条预设，可导入规则或加入当前编辑中的规则；点「对比策略」时还会自动并入买入／卖出框里正在编辑的规则。

概率与相关性
采用训练集标准化、L2 正则逻辑回归，标签为下一条行情收盘相对当日收盘是否上涨。主口径为滚动验证：按时间顺序扩窗重训，每折至少 180 条训练、60 条测试，测试窗口互不重叠（步长等于测试长度，样本多时自动拉大，最多 40 折），训练与测试之间留空一条，汇总各折合并样本的准确率、AUC 与 Brier，并给出逐折明细。单次 70/30 切分仅作参考。相关性为训练集 Pearson r，准确率按 0.50 分类阈值计算。AUC 衡量排序，Brier 衡量概率误差。
演示行情是合成随机序列，不含可学习的前瞻信号，因此演示中的准确率接近多数类基准属预期结果，不能据此判断模型能力。关联无法证明因果，样本外表现会随市场条件改变。

从 Python 转换
策略页「导入策略 ▾ → 从 Python 代码（.py）」，或命令行 --convert 源文件.py [输出目录]，可把常见的 pandas／numpy／talib 表达式式策略翻译成本文的规则语言。可识别：列访问 df['close'] 与 df.close；rolling(n).mean() → AVG(x,n)；shift(k) → FIELD[k] 或 LAG(x,k)；pct_change() → RET1；ewm(span=12/26) → EMA12／EMA26；np.where → IF；np.maximum／np.minimum → MAX／MIN；abs → ABS；ta.SMA → AVG；ta.CROSS → CROSS；ta.RSI(close,14) → RSI14。变量在同一文件内内联，列名不区分大小写。
不支持循环、条件分支、类、多返回值函数、自定义函数与任意状态逻辑，遇到时逐条列出原因。转换结果会先通过规则解析器校验，校验不过即判失败。命令行会写出 名称.rule 与 convert-report.txt；界面只加载到编辑框，需要留档请手工另存为 .rule。
审计报告
命令行可对任意数据跑完整审计：PositionLab.exe --audit 策略.rule 行情.csv 输出目录 [费用bp] [滑点bp] [资金]。报告写入 audit.md、audit.html、audit-equity.csv 与 audit-significance.csv，包含数据识别结果（编码、分隔符、列映射）、规则静态审计、绩效与分标明细、截断不变性对照、成本敏感性、块自举显著性（零假设分布直方图与经验 p 值）和结论；退出码 0 通过、2 有警告、1 有失败。显著性检验把入场／离场信号沿时间轴环形平移 200 次，在同一价格路径上重跑，p>0.1 时警告「与随机择时无法区分」（固定随机种子，可复现）。界面在「策略审计与回测」页的「导出报告」做同样的事。
免责声明与安全
本程序用于研究、教学和策略验证。评分、样例和回测结果不构成投资建议、收益承诺或交易指令。历史表现和模型估计可能与结果显著偏离。请独立核验源数据的完整性、发布时间、交易与复权假设，并自行承担决策后果。
程序离线运行，无账号、行情网络请求、券商连接或自动下单。工作数据与编辑内容存放在本次运行的内存中。仅显式导出时写出文件。导出包含敏感持仓时请自行保护文件。演示行情为合成随机序列，不含可预测信号，演示中的模型准确率接近随机水平属预期。";
    [STAThread]
    static void Main(string[] args) {
        SetProcessDPIAware();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2&&args[0]=="--verify") {
            try { Verify.Run(args[1]); using(var form=new Terminal(false)) form.VerifyUi(args[1]); File.WriteAllText(Path.Combine(args[1],"verification.txt"),"PASS",Encoding.UTF8); }
            catch(Exception e) { File.WriteAllText(Path.Combine(args[1],"verification.txt"),e.ToString(),Encoding.UTF8); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==2&&args[0]=="--verify-nogui") {
            try { Verify.Run(args[1]); File.WriteAllText(Path.Combine(args[1],"verification.txt"),"PASS",Encoding.UTF8); }
            catch(Exception e) { File.WriteAllText(Path.Combine(args[1],"verification.txt"),e.ToString(),Encoding.UTF8); Environment.ExitCode=1; }
            return;
        }
        if(args.Length>=2&&args[0]=="--strategy") { RunStrategy(args); return; }
        if(args.Length>=2&&args[0]=="--statements") { RunStatements(args); return; }
        if(args.Length>=2&&args[0]=="--xls") { RunXls(args); return; }
        if(args.Length>=2&&args[0]=="--convert") { RunConvert(args); return; }
        if(args.Length>=4&&args[0]=="--audit") { RunAudit(args); return; }
        if(args.Length==2&&args[0]=="--sample-data") { WriteSamples(args[1]); return; }
        Application.Run(new Terminal());
    }
    static void RunStatements(string[] args) {
        int code=1;
        try {
            string path=args[1];
            string folder=args.Length>2?args[2]:Path.GetDirectoryName(Path.GetFullPath(path));
            var book=StatementBook.Load(path);
            string csv, summary;
            book.Write(folder,out csv,out summary);
            code=0;
        } catch(Exception error) {
            try {
                string folder=args.Length>2?args[2]:(args.Length>1?Path.GetDirectoryName(Path.GetFullPath(args[1])):".");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"statement-error.txt"),error.ToString(),Encoding.UTF8);
            } catch { }
            code=1;
        }
        Environment.ExitCode=code;
    }
    static void RunXls(string[] args) {
        try {
            var grid=Xls.Read(args[1]);
            string folder=args.Length>2?args[2]:Path.GetDirectoryName(Path.GetFullPath(args[1]));
            Directory.CreateDirectory(folder);
            var text=new StringBuilder();
            foreach(var row in grid) text.AppendLine(string.Join("\t",row));
            File.WriteAllText(Path.Combine(folder,"xls-dump.txt"),text.ToString(),new UTF8Encoding(true));
            Environment.ExitCode=0;
        } catch(Exception error) {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"xls-error.txt"),error.ToString(),Encoding.UTF8);
            Environment.ExitCode=1;
        }
    }
    static void RunStrategy(string[] args) {
        int code=1;
        try {
            string source=Data.Text(args[1]);
            string dataPath=args[2];
            string folder=args.Length>3?args[3]:Path.GetDirectoryName(Path.GetFullPath(args[1]));
            var import=new ImportReport();
            var market=MarketImport.Load(dataPath,import);
            var symbols=market.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Take(Library.MaxSymbols).ToArray();
            var from=market.Values.Max(s=>s.Bars[0].Date);
            var to=market.Values.Min(s=>s.Bars[s.Bars.Length-1].Date);
            double capital=1e6;
            var program=Script.Parse(source);
            var report=ScriptPortfolio.Run(market,symbols,from,to,capital,0.0008,0.0005,source,program.Pooled);
            var equity=report.Equity; var benchmark=report.Benchmark; var days=report.Dates;
            var text=new StringBuilder();
            text.AppendLine("策略：" + program.Name + (program.Pooled?"（资金池）":"（等分资金槽）") + "  ·  数据：" + import.Summary);
            text.AppendLine("区间：" + Data.Day(from) + " ~ " + Data.Day(to) + "  ·  " + symbols.Length + " 个标的");
            if(program.RebalanceMode.Length>0) {
                text.AppendLine("再平衡：" + program.RebalanceText + "  ·  执行 " + report.Rebalances + " 次  ·  目标 " +
                    string.Join("，",program.Targets.Select(t=>t.Key+"="+(100*t.Value).ToString("0.#",Data.Inv)+"%").ToArray()));
            }
            text.AppendLine();
            text.AppendLine("标的\t期末市值\t成交\t胜率");
            foreach(var row in report.Symbols) text.AppendLine(row.Symbol + "\t" + row.Final.ToString("N0",Data.Inv) + "\t" + row.Fills + "\t" + (100*row.WinRate).ToString("0.0",Data.Inv) + "%");
            text.AppendLine();
            text.AppendLine("组合收益\t" + (100*report.Total).ToString("0.0",Data.Inv) + "%");
            text.AppendLine("年化\t" + (100*report.Annual).ToString("0.0",Data.Inv) + "%");
            text.AppendLine("最大回撤\t" + (100*report.MaxDrawdown).ToString("0.0",Data.Inv) + "%");
            text.AppendLine("基准\t" + (100*(benchmark[benchmark.Length-1]/capital-1)).ToString("0.0",Data.Inv) + "%");
            var curve=new StringBuilder("date,equity,benchmark\n");
            for(int d=0;d<days.Length;d++) curve.AppendLine(string.Format(Data.Inv,"{0},{1:R},{2:R}",Data.Day(days[d]),equity[d],benchmark[d]));
            var fills=new StringBuilder("symbol,date,signal,side,price,quantity,fee,pnl,tag\n");
            foreach(var fill in report.Trades) fills.AppendLine(string.Format(Data.Inv,"{0},{1},{2},{3},{4:R},{5:R},{6:R},{7},{8}",fill.Symbol,Data.Day(fill.Date),Data.Day(fill.Signal),
                fill.Side,fill.Price,fill.Quantity,fill.Fee,double.IsNaN(fill.Pnl)?"":fill.Pnl.ToString("R",Data.Inv),fill.Tag??""));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"strategy-result.txt"),text.ToString(),new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(folder,"strategy-equity.csv"),curve.ToString(),new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(folder,"strategy-fills.csv"),fills.ToString(),new UTF8Encoding(true));
            code=0;
        } catch(Exception error) {
            try {
                string folder=args.Length>3?args[3]:(args.Length>1?Path.GetDirectoryName(Path.GetFullPath(args[1])):".");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"strategy-error.txt"),error.ToString(),Encoding.UTF8);
            } catch { }
            code=1;
        }
        Environment.ExitCode=code;
    }
    static void RunConvert(string[] args) {
        int code=1;
        try {
            string source=Data.Text(args[1]);
            string name=Path.GetFileNameWithoutExtension(args[1]);
            string folder=args.Length>2?args[2]:Path.GetDirectoryName(Path.GetFullPath(args[1]));
            var result=PythonConvert.Convert(source,name);
            string extension=".rule"; string content=result.Rule; string report=result.Report();
            if(!result.Ok) {
                try {
                    string script=PythonConvert.ConvertScript(source,name);
                    Script.Parse(script);
                    extension=".strategy"; content=script; result.Ok=true;
                    report+="\r\n改用脚本模式（包含控制流或状态逻辑）：\r\n"+script;
                } catch(Exception scriptError) {
                    report+="\r\n脚本模式也无法转换："+scriptError.Message;
                }
            }
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,name+extension),content,new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(folder,"convert-report.txt"),report,new UTF8Encoding(true));
            code=result.Ok?0:1;
        } catch(Exception error) {
            try {
                string folder=args.Length>2?args[2]:(args.Length>1?Path.GetDirectoryName(Path.GetFullPath(args[1])):".");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"convert-error.txt"),error.ToString(),Encoding.UTF8);
            } catch { }
            code=1;
        }
        Environment.ExitCode=code;
    }
    static void RunAudit(string[] args) {
        int code=1;
        try {
            string rulePath=args[1], dataPath=args[2], output=args[3];
            double fee=args.Length>4?Data.Number(args[4],0,1000)/10000:0.0008;
            double slip=args.Length>5?Data.Number(args[5],0,1000)/10000:0.0005;
            double capital=args.Length>6?Data.Number(args[6],1,1e14):1e6;
            var rule=Rule.Load(rulePath);
            var report=new ImportReport();
            var market=MarketImport.Load(dataPath,report);
            var symbols=market.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Take(Library.MaxSymbols).ToArray();
            var from=market.Values.Max(s=>s.Bars[0].Date);
            var to=market.Values.Min(s=>s.Bars[s.Bars.Length-1].Date);
            var result=Audit.Run(market,symbols,rule,from,to,capital,fee,slip,report);
            Audit.Write(result,output);
            code=result.Failures>0?1:result.Warnings>0?2:0;
        } catch(Exception error) {
            try {
                string folder=args.Length>3?args[3]:".";
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"audit-error.txt"),error.ToString(),Encoding.UTF8);
            } catch { }
            code=1;
        }
        Environment.ExitCode=code;
    }
    internal static string WriteSamples(string parent) {
        string dir=Path.Combine(parent,"PositionLab-Examples-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(dir);
        var d=Demo.Create();
        using(var w=new StreamWriter(Path.Combine(dir,"market.csv"),false,new UTF8Encoding(true))) {
            w.WriteLine("symbol,date,open,high,low,close,volume");
            foreach(var s in d.Market.Values) foreach(var b in s.Bars)
                w.WriteLine(string.Format(Data.Inv,"{0},{1},{2:R},{3:R},{4:R},{5:R},{6:R}",s.Symbol,Data.Day(b.Date),b.Open,b.High,b.Low,b.Close,b.Volume));
        }
        using(var w=new StreamWriter(Path.Combine(dir,"context.csv"),false,new UTF8Encoding(true))) {
            w.WriteLine("symbol,available_date,roe,pe,sentiment");
            foreach(var c in d.Context.OrderBy(p=>p.Key)) w.WriteLine(string.Format(Data.Inv,"DEMO-A,{0},{1:R},{2:R},{3:R}",Data.Day(c.Key),c.Value.Roe,c.Value.Pe,c.Value.News));
        }
        var day=d.Market["DEMO-A"].Bars.Last().Date;
        File.WriteAllText(Path.Combine(dir,"positions.csv"),"symbol,quantity,cost,bought_date,sector\r\nDEMO-A,300,48,"+Data.Day(day.AddDays(-120))+
            ",科技\r\nDEMO-B,100,72,"+Data.Day(day.AddDays(-35))+",消费\r\n",Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir,"ma20.rule"),"NAME=均线突破\r\nBUY="+Rule.DefaultBuy+"\r\nSELL="+Rule.DefaultSell+"\r\n",Encoding.UTF8);
        return dir;
    }
}
static class Verify {
    internal static readonly DateTime[] FixturePeriods = BuildFixturePeriods();
    static DateTime[] BuildFixturePeriods() {
        var list = new List<DateTime>();
        for (int year = 2020; year <= 2023; year++)
            for (int quarter = 4; quarter >= 1; quarter--) {
                int month = quarter * 3;
                list.Add(new DateTime(year, month, DateTime.DaysInMonth(year, month)));
            }
        list.Sort();
        list.Reverse();
        return list.ToArray();
    }
    static double FixtureNetProfit(DateTime period) {
        int year = period.Year, quarter = (period.Month + 2) / 3;
        double basis = 100 + 10 * (year - 2020);
        return basis + (quarter == 1 ? 0 : quarter == 2 ? 110 : quarter == 3 ? 240 : 400);
    }
    internal static string StatementFixture() {
        var text = new StringBuilder();
        text.Append("科目\\时间");
        foreach (var period in FixturePeriods) text.Append(",").Append(Data.Day(period));
        text.AppendLine();
        Action<string,Func<DateTime,string>> row = (name, value) => {
            text.Append(name);
            foreach (var period in FixturePeriods) text.Append(",").Append(value(period));
            text.AppendLine();
        };
        row("净利润(元)", p => FixtureNetProfit(p).ToString("0", Data.Inv));
        row("营业总收入(元)", p => (10 * FixtureNetProfit(p)).ToString("0", Data.Inv));
        row("基本每股收益(元)", p => (FixtureNetProfit(p) / 1000).ToString("0.###", Data.Inv));
        row("每股净资产(元)", p => "10.00");
        row("销售毛利率", p => "80.0%");
        row("销售净利率", p => "30.0%");
        row("净资产收益率", p => "12.0%");
        row("资产负债率", p => "40.0%");
        row("净利润同比增长率", p => "10.0%");
        row("营业总收入同比增长率", p => "10.0%");
        row("存货(元)", p => "500");
        row("流动比率", p => "2.0");
        row("产权比率", p => "0.5");
        return text.ToString();
    }
    internal static void WriteStatementFixture(string path) {
        File.WriteAllText(path, StatementFixture(), new UTF8Encoding(true));
    }
    static void WriteXlsx(string path) {
        using(var zip=System.IO.Compression.ZipFile.Open(path,System.IO.Compression.ZipArchiveMode.Create)) {
            Action<string,string> write=(name,content)=>{
                var entry=zip.CreateEntry(name);
                using(var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false))) writer.Write(content);
            };
            write("[Content_Types].xml","<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
            write("xl/workbook.xml","<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            write("xl/_rels/workbook.xml.rels","<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            string first=new DateTime(2024,1,2).ToOADate().ToString("0",Data.Inv);
            string second=new DateTime(2024,1,3).ToOADate().ToString("0",Data.Inv);
            write("xl/worksheets/sheet1.xml","<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"+
                "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>日期</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>收盘价</t></is></c><c r=\"C1\" t=\"inlineStr\"><is><t>成交量</t></is></c></row>"+
                "<row r=\"2\"><c r=\"A2\"><v>"+first+"</v></c><c r=\"B2\"><v>10.2</v></c><c r=\"C2\"><v>12000</v></c></row>"+
                "<row r=\"3\"><c r=\"A3\"><v>"+second+"</v></c><c r=\"B3\"><v>10.6</v></c><c r=\"C3\"><v>15000</v></c></row>"+
                "</sheetData></worksheet>");
        }
    }
    static void Check(bool ok,string name) { if(!ok) throw new InvalidOperationException(name); }
    static void Near(double a,double b,string name) {Check(Math.Abs(a-b)<1e-8*Math.Max(1,Math.Abs(b)),name+" "+a+" vs "+b);}
    static void Reject(Action action,string name) {
        try { action(); } catch(FormatException) { return; } throw new Exception(name+" should reject");
    }
    internal static void Run(string folder) {
        var rows=Enumerable.Range(0,140).Select(i=>new Bar{Date=new DateTime(2024,1,1).AddDays(i),Open=100+i,High=101+i,Low=99+i,Close=100+i,Volume=1000}).ToArray();
        var s=new Series("T",rows);
        int n;
        Near(s.V["MA20"][19],109.5,"SMA");
        Near(s.V["MOM20"][20],.2,"momentum");
        Near(s.V["RSI14"][14],100,"RSI");
        Near(s.V["ATR14"][14],2,"ATR");
        Near(s.V["VOLRATIO"][5],1,"volume ratio");
        Near(Stats.Percentile(s.V["CLOSE"],25,20,out n),1,"rising percentile"); Check(n==20,"percentile window");
        Near(Stats.Percentile(s.V["VOLUME"],30,20,out n),.5,"tie percentile");
        Check(double.IsNaN(Stats.Percentile(s.V["MA120"],122,20,out n)),"insufficient percentile");
        Reject(()=>Rule.Compile("CLOSE[-1] > 0"),"forward offset");
        Reject(()=>Rule.Compile("LEAD(CLOSE) > 0"),"unknown function");
        Reject(()=>Rule.Compile("CLOSE[253] > 0"),"lag bounds");
        Check(Rule.Compile("CLOSE[1] > 100 AND (MA20 > 0 OR VOLUME >= 1)")(s,0)==null,"missing data");
        var crossRule=Rule.Compile("CROSS(CLOSE, MA20)"); var explicitCross=Rule.Compile("CLOSE > MA20 AND CLOSE[1] <= MA20[1]");
        bool crossSame=true;
        for(int i=20;i<rows.Length;i++) if(crossRule(s,i)!=explicitCross(s,i)) crossSame=false;
        Check(crossSame,"cross equivalence");
        var arithmetic=Rule.Compile("CLOSE > MA20 * 1.02");
        for(int i=20;i<rows.Length;i+=7) Check(arithmetic(s,i)==(bool?)(s.V["CLOSE"][i]>s.V["MA20"][i]*1.02),"arithmetic precedence");
        Check(Rule.Compile("COUNT(CLOSE > MA20, 20) >= 15")(s,60)==(bool?)true,"count function");
        Check(Rule.Compile("IF(CLOSE > MA20, 1, 0) > 0.5")(s,30)==(bool?)true,"if function");
        Check(Rule.Compile("AVG(RET1, 5) > 0")(s,60)==(bool?)true,"avg function");
        Check(Rule.Compile("MAX(CLOSE, MA20) >= CLOSE")(s,50)==(bool?)true,"max function");
        Check(Rule.Compile("CLOSE / (VOLUME - VOLUME) > 0")(s,5)==null,"division by zero");
        Check(Rule.Compile("NOT (CLOSE < MA20) AND MIN(CLOSE, MA20) > 0")(s,40)==(bool?)true,"not and nested");
        Reject(()=>Rule.Compile("MAX(CLOSE) > 0"),"function arity");
        Reject(()=>Rule.Compile("CROSS(CLOSE)"),"cross arity");
        var info=Rule.Inspect("CROSS(CLOSE, MA20) AND COUNT(CLOSE > MA20, 20) >= 15");
        Check(info.Fields.Length==2&&info.Functions.Length==2&&info.MaxLag==0,"rule inspection");
        var python=@"import pandas as pd
def signal(df):
    close = df['close']
    ma20 = close.rolling(20).mean()
    buy = close > ma20
    sell = close < ma20
    return buy, sell";
        var converted=PythonConvert.Convert(python,"demo");
        Check(converted.Ok,"python convert");
        Check(converted.Buy=="CLOSE > AVG(CLOSE, 20)"&&converted.Sell=="CLOSE < AVG(CLOSE, 20)","python rolling mapping");
        Check(Rule.Compile(converted.Buy)!=null&&Rule.Compile(converted.Sell)!=null,"python output compiles");
        var shifted=PythonConvert.Convert("buy = close > close.rolling(20).mean()\nsell = close.shift(1) > close\n","s");
        Check(shifted.Ok&&shifted.Sell=="CLOSE[1] > CLOSE","python shift mapping");
        var where=PythonConvert.Convert("buy = np.where(close > close.rolling(20).mean(), 1, 0)\nsell = close < open\n","w");
        Check(where.Ok&&where.Buy.StartsWith("IF("),"python np.where mapping");
        var bad=PythonConvert.Convert("for i in range(10):\n    buy = close\nsell = close\n","bad");
        Check(!bad.Ok&&bad.Problems.Count>0,"python rejects control flow");

        Reject(()=>new Series("T",new[]{rows[0],rows[0]}),"duplicates");
        Reject(()=>Data.BarRow(new[]{"X","2024-01-01","10","8","7","9","1"}),"OHLC");
        Reject(()=>Data.Number("NaN",0,100),"non-finite");
        var rule=new Rule{Buy="CLOSE < 103",Sell="CLOSE >= 103 AND CLOSE < 104"};
        var r=Backtest.Run(s,rows[0].Date,rows[4].Date,1000,.01,.02,rule);
        Check(r.Fills.Count==2&&r.Fills[0].Date==rows[1].Date&&r.Fills[1].Date==rows[4].Date,"next open fills");
        Near(r.Fills[0].Price,101*1.02,"buy slippage");
        Near(r.Fills[1].Price,104*.98,"sell slippage");
        Near(r.Equity.Last(),1000+r.Fills[1].Pnl,"equity reconciliation");
        var d=Demo.Create(); var a=d.Market["DEMO-A"];
        var scriptSource="NAME=t\nVAR n = 0\nON BAR\n  IF CROSS(AVG(CLOSE,5), AVG(CLOSE,20)) THEN\n    BUY 0.5\n    SET n = n + 1\n  END\n  IF CROSSDOWN(AVG(CLOSE,5), AVG(CLOSE,20)) THEN\n    SELL 1\n  END\nEND";
        var scriptRun=ScriptBacktest.Run(a,a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,scriptSource);
        Check(scriptRun.Fills.Count>0&&scriptRun.Equity.Length>0&&scriptRun.Equity.All(x=>x>0),"script backtest");
        var loopSource="NAME=l\nVAR n = 0\nON BAR\n  FOR k = 1 TO 2\n    SET n = n + 1\n  END\n  IF n >= 6 THEN\n    BUY 1\n  END\nEND";
        var loopRun=ScriptBacktest.Run(a,a.Bars[0].Date,a.Bars[300].Date,1e5,0,0,loopSource);
        Check(loopRun.Fills.Count==1,"script loop and state");
        Reject(()=>ScriptBacktest.Run(a,a.Bars[0].Date,a.Bars[300].Date,1e5,0,0,"NAME=x\nON BAR\n  FOR k = 1 TO 1000\n    SET z = k\n  END\nEND"),"script loop guard");
        Reject(()=>Script.Parse("ON BAR\n  WHILE CLOSE > 0\n  END\nEND"),"script rejects unknown statement");
        Check(Script.Parse("NAME=样例\nPARAM fast=5\nON BAR\n  IF CLOSE > AVG(CLOSE, fast) THEN\n    BUY 1\n  END\nEND").Name=="样例","script parameter binding");        var scripted=PythonConvert.ConvertScript("ma20 = close.rolling(20).mean()\nif close > ma20:\n    buy = 1\nelse:\n    buy = 0\nsell = close < ma20\n","b");
        Check(scripted.Contains("ON BAR")&&scripted.Contains("ELSE"),"python script branch");
        Check(Script.Parse(scripted).Run.Count>0,"python script parses");
        for(int i=120;i<a.Bars.Length;i+=53) {
            double sum20=0, sum120=0, vol5=0;
            for(int j=i-19;j<=i;j++) sum20+=a.Bars[j].Close;
            for(int j=i-119;j<=i;j++) sum120+=a.Bars[j].Close;
            for(int j=i-4;j<=i;j++) vol5+=a.Bars[j].Volume;
            Near(a.V["MA20"][i],sum20/20,"rolling MA20");
            Near(a.V["MA120"][i],sum120/120,"rolling MA120");
            Near(a.V["VOLMA5"][i],vol5/5,"rolling volume MA");
        }
        var full=Backtest.Run(a,a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell});
        var trimmed=new Series(a.Symbol,a.Bars.Take(301));
        var prefix=Backtest.Run(trimmed,a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell});
        Check(full.Equity.SequenceEqual(prefix.Equity),"prefix invariance");
        var scope=new[]{"DEMO-A","DEMO-B","DEMO-C"};
        var poolSource="NAME=p\nINIT pool=1\nON BAR\n  IF CROSS(AVG(CLOSE,5), AVG(CLOSE,20)) THEN\n    BUY 1\n  END\n  IF CROSSDOWN(AVG(CLOSE,5), AVG(CLOSE,20)) THEN\n    SELL 1\n  END\nEND";
        var pooled=ScriptPortfolio.Run(d.Market,scope,a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,poolSource,true);
        Check(pooled.Fills>0&&pooled.Equity.Length==pooled.Dates.Length&&pooled.Equity.All(x=>x>0),"pooled capital backtest");
        var sleeves=ScriptPortfolio.Run(d.Market,scope,a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,poolSource,false);
        Check(sleeves.Equity.Length==pooled.Equity.Length&&sleeves.Symbols.Count==3,"script sleeve aggregation");
        var shadow=ScriptBacktest.Run(a,a.Bars[0].Date,a.Bars[20].Date,1e5,0,0,"NAME=s\nON BAR\n  LET ma20 = 1\n  IF CLOSE > ma20 THEN\n    BUY 1\n  END\nEND");
        Check(shadow.Fills.Count>0,"script variable shadows field name");
        var portfolio=PortfolioBacktest.Run(d.Market,scope,a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell});
        Check(portfolio.Symbols.Count==3&&portfolio.Symbols.All(x=>!x.Failed),"portfolio symbols");
        Check(portfolio.Trades.Count==portfolio.Fills&&portfolio.Trades.All(t=>!string.IsNullOrEmpty(t.Symbol)),"portfolio fills carry symbol");
        Near(portfolio.Equity.Last(),portfolio.Symbols.Sum(x=>x.Final),"portfolio equity equals slice sum");
        Check(portfolio.Benchmark.Length==portfolio.Dates.Length&&portfolio.Equity.All(x=>x>0),"portfolio alignment");
        var single=PortfolioBacktest.Run(d.Market,new[]{"DEMO-A"},a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell});
        Near(single.Total,Backtest.Run(a,a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell}).Total,"single symbol parity");
        var comparison=Comparison.Run(d.Market,scope,a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,Library.Initial());
        Check(comparison.Rows.Count==Library.Presets.Length&&comparison.Dates.Length==portfolio.Dates.Length,"comparison rows");
        Check(comparison.Rows.All(row=>row.Curve.Length==comparison.Dates.Length&&Math.Abs(row.Curve[0])<1e-9),"comparison curves");
        Reject(()=>Comparison.Run(d.Market,scope,a.Bars[0].Date,a.Bars[300].Date,1e5,0,0,new List<Rule>()),"empty library");
        Reject(()=>PortfolioBacktest.Run(d.Market,new[]{"MISSING"},a.Bars[0].Date,a.Bars[300].Date,1e5,0,0,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell}),"missing symbol");
        var audit=Audit.Run(d.Market,scope,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell},a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,null);
        Check(audit.Checks.Count>=7&&audit.Verdict!="失败","audit verdict");
        Check(audit.Checks.Any(c=>c.Name=="截断不变性"&&c.Level=="通过"),"audit truncation check");
        Check(audit.Checks.Any(c=>c.Name=="显著性"),"audit significance check");
        Check(audit.Significance!=null&&audit.Significance.Available&&audit.Significance.PValue>0&&audit.Significance.PValue<=1,"audit significance result");
        var auditFolder=Path.Combine(folder,"audit");
        Audit.Write(audit,auditFolder);
        Check(File.Exists(Path.Combine(auditFolder,"audit.md"))&&File.Exists(Path.Combine(auditFolder,"audit.html"))&&
            File.Exists(Path.Combine(auditFolder,"audit-equity.csv"))&&File.Exists(Path.Combine(auditFolder,"audit-significance.csv"))&&
            File.ReadAllText(Path.Combine(auditFolder,"audit.md")).Contains("截断不变性"),"audit report files");
        Check(File.ReadAllText(Path.Combine(auditFolder,"audit.html")).Contains("<svg"),"audit chart");
        Check(File.ReadAllText(Path.Combine(auditFolder,"audit.html")).Contains("显著性直方图"),"audit significance histogram");
        var significance=Significance.Run(d.Market,scope,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell},a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,40);
        Check(significance.Available&&significance.Iterations==40&&significance.Null.Length==40,"significance iterations");
        Check(significance.PValue>0&&significance.PValue<=1&&!double.IsNaN(significance.Actual)&&!double.IsNaN(significance.NullMedian),"significance statistics");
        Check(significance.Null.SequenceEqual(significance.Null.OrderBy(x=>x)),"significance null sorted");
        var repeated=Significance.Run(d.Market,scope,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell},a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,40);
        Check(repeated.PValue==significance.PValue&&repeated.Actual==significance.Actual,"significance reproducible");
        var m=Model.Fit(a,d.Context);
        Check(m.Probability>0&&m.Probability<1&&m.TestStart>m.TrainEnd&&m.Brier>=0&&m.Brier<=1,"model split and range");
        Check(m.WalkForward&&m.Folds.Count>=3&&m.Folds.Count<=Model.MaxFolds,"walk-forward folds");
        Check(m.WalkAccuracy>=0&&m.WalkAccuracy<=1&&m.WalkBrier>=0&&m.WalkBrier<=1&&m.WalkAccuracyStd>=0,"walk aggregates");
        Check(!double.IsNaN(m.WalkAuc)&&m.WalkCorrect>0&&m.WalkTotal==m.Folds.Count*Model.TestSize,"walk pool");
        for(int k=0;k<m.Folds.Count;k++) {
            var f=m.Folds[k];
            Check(f.TrainEnd<f.TestStart&&f.TestStart<=f.TestEnd,"fold ordering");
            Check(f.TrainCount>=Model.MinTrain-1&&f.TestCount==Model.TestSize,"fold sizes");
            Check(f.Accuracy>=0&&f.Accuracy<=1&&f.Brier>=0&&f.Brier<=1,"fold metrics");
            if(k>0) Check(f.TrainEnd>m.Folds[k-1].TrainEnd,"folds advance");
        }
        var prefixModel=Model.Fit(new Series(a.Symbol,a.Bars.Take(400)),d.Context);
        Check(prefixModel.Folds.Count>0,"prefix folds");
        foreach(var f in prefixModel.Folds) {
            var match=m.Folds.FirstOrDefault(g=>g.TrainEnd==f.TrainEnd&&g.TestStart==f.TestStart);
            Check(match!=null&&match.Accuracy==f.Accuracy&&match.Auc==f.Auc&&match.Brier==f.Brier,"walk-forward uses no future data");
        }
        var gapBars=new[]{new Bar{Date=new DateTime(2024,1,2),Open=1,High=1,Low=1,Close=1,Volume=1},
            new Bar{Date=new DateTime(2024,2,20),Open=1,High=1,Low=1,Close=1,Volume=1}};
        Check(new Series("GAP",gapBars).Gaps==1&&s.Gaps==0,"gap detection");
        var settingsFile=Path.Combine(folder,"settings.ini");
        new Settings{Window=77,ChartMode=3,ChartRange=250,Initial="5000",Fee="12",Slippage="3",Cash="100",
            Width=1111,Height=777,Left=40,Top=30,Maximized=true}.Save(settingsFile);
        var stored=Settings.Load(settingsFile);
        Check(stored.Window==77&&stored.ChartMode==3&&stored.ChartRange==250&&stored.Initial=="5000"&&stored.Fee=="12"&&
            stored.Slippage=="3"&&stored.Cash=="100"&&stored.Width==1111&&stored.Height==777&&stored.Left==40&&
            stored.Top==30&&stored.Maximized,"settings roundtrip");
        File.WriteAllText(settingsFile,"window=x\nchartMode=99\nfee=abc\n",Encoding.UTF8);
        var broken=Settings.Load(settingsFile);
        Check(broken.Window==252&&broken.ChartMode==0&&broken.Fee=="8","settings fallback");
        Check(Settings.Load(Path.Combine(folder,"absent.ini")).Window==252,"settings defaults");
        Reject(()=>Model.Fit(a,new Dictionary<DateTime,Context>()),"missing recent snapshot");
        var sample=Program.WriteSamples(folder);
        var roundtrip=Data.Market(Path.Combine(sample,"market.csv"));
        var snapshot=Data.Contexts(Path.Combine(sample,"context.csv"),"DEMO-A");
        Check(roundtrip.Count==3&&snapshot.Count==516,"CSV sample import");
        Near(roundtrip["DEMO-A"].Bars.Last().Close,a.Bars.Last().Close,"CSV price roundtrip");
        Check(Data.Holdings(Path.Combine(sample,"positions.csv")).Count==2,"holdings CSV");
        var ths=Path.Combine(folder,"600000.csv");
        File.WriteAllText(ths,"日期,开盘,最高,最低,收盘,成交量\n2024-01-02,10,10.5,9.9,10.2,12000\n2024-01-03,10.2,10.8,10.1,10.6,15000\n2024-01-04,10.6,11,10.5,10.9,18000\n",Encoding.GetEncoding(936));
        var thsReport=new ImportReport();
        var thsMarket=MarketImport.Load(ths,thsReport);
        Check(thsMarket.ContainsKey("600000")&&thsMarket["600000"].Bars.Length==3,"THS csv import");
        Check(thsReport.Encoding=="GBK","GBK detection");
        Near(thsMarket["600000"].Bars[2].Close,10.9,"THS close");
        var wind=Path.Combine(folder,"wind.csv");
        File.WriteAllText(wind,"Wind 导出：日频\nwind_code,trade_dt,open,high,low,close,volume\n600000.SH,2024/1/2,10,10.5,9.9,10.2,\"1,200,000\"\n600000.SH,2024/1/3,10.2,10.8,10.1,10.6,\"1,500,000\"\n",Encoding.UTF8);
        var windReport=new ImportReport();
        var windMarket=MarketImport.Load(wind,windReport);
        Check(windMarket.ContainsKey("600000")&&windReport.HeaderRows==1,"Wind header detection");
        Near(windMarket["600000"].Bars[0].Volume,1200000,"thousand separators");
        var book=Path.Combine(folder,"600000.xlsx");
        WriteXlsx(book);
        var bookReport=new ImportReport();
        var bookMarket=MarketImport.Load(book,bookReport);
        Check(bookMarket.ContainsKey("600000")&&bookMarket["600000"].Bars.Length==2,"xlsx import");
        Near(bookMarket["600000"].Bars[0].Close,10.2,"xlsx numeric cell");
        Check(bookMarket["600000"].Bars[0].Date==new DateTime(2024,1,2)&&bookReport.Format=="XLSX","xlsx date serial");
        var statementFile=Path.Combine(folder,"statements-demo.csv");
        WriteStatementFixture(statementFile);
        var statementBook=StatementBook.Load(statementFile);
        Check(statementBook.Count==16&&statementBook.Subjects.Count>=10,"statement periods and subjects");
        Check(statementBook.Periods[0]==new DateTime(2020,3,31)&&statementBook.Periods[15]==new DateTime(2023,12,31),"statement ascending periods");
        Near(statementBook.Single("net_profit",12),130,"statement Q1 single");
        Near(statementBook.Single("net_profit",13),110,"statement Q2 single");
        Near(statementBook.Single("net_profit",14),130,"statement Q3 single");
        Near(statementBook.Single("net_profit",15),160,"statement Q4 single");
        Near(statementBook.Ttm("net_profit",15),530,"statement TTM net profit");
        Near(statementBook.Ttm("revenue",15),5300,"statement TTM revenue");
        Check(statementBook.Available[0]==new DateTime(2020,4,30)&&statementBook.Available[4]==new DateTime(2021,4,30)&&
            statementBook.Available[15]==new DateTime(2024,4,30),"statement disclosure dates");
        bool leak=false;
        for(int i=0;i<statementBook.Count;i++) if(statementBook.IndexAt(statementBook.Available[i].AddDays(-1))>=i) leak=true;
        Check(!leak,"statement hides period before disclosure");
        Check(statementBook.IndexAt(new DateTime(2024,4,29))==14&&statementBook.IndexAt(new DateTime(2024,4,30))==15,"statement visible cutoff");
        Check(statementBook.Reconcile().Count==0,"statement cumulative reconciliation");
        string statementCsv,statementSummary;
        statementBook.Write(Path.Combine(folder,"statements"),out statementCsv,out statementSummary);
        Check(File.ReadAllLines(statementCsv).Length==17&&File.ReadAllText(statementCsv).Contains("法定期限估计"),"statement csv output");
        Check(File.ReadAllText(statementSummary).Contains("按法定期限估计")&&File.ReadAllText(statementSummary).Contains("累计与单季对账：通过"),"statement summary output");
        var statementBars=new List<Bar>(); var statementDay=new DateTime(2024,1,2);
        while(statementDay<=new DateTime(2025,12,31)) {
            if(statementDay.DayOfWeek!=DayOfWeek.Saturday&&statementDay.DayOfWeek!=DayOfWeek.Sunday) {
                double close=50+Math.Sin(statementBars.Count/9.0)*4;
                statementBars.Add(new Bar{Date=statementDay,Open=close,High=close*1.01,Low=close*0.99,Close=close,Volume=1e6});
            }
            statementDay=statementDay.AddDays(1);
        }
        var statementSeries=new Series("DEMO-A",statementBars);
        var statementContexts=statementBook.Contexts(statementSeries);
        Check(statementContexts.Count>300&&statementContexts.ContainsKey(new DateTime(2024,4,29)),"statement contexts generated");
        var cutoffBar=statementBars.First(x=>x.Date==new DateTime(2024,4,29));
        Near(statementContexts[new DateTime(2024,4,29)].Pe,cutoffBar.Close/statementBook.Ttm("eps",14),"statement context uses visible period");
        var statementModel=Model.Fit(statementSeries,statementContexts);
        Check(statementModel.Matched>100&&statementModel.Probability>0&&statementModel.Probability<1,"statement model fit");

        var flatDays=new List<DateTime>(); var flatDay=new DateTime(2024,1,2);
        while(flatDays.Count<60) {
            if(flatDay.DayOfWeek!=DayOfWeek.Saturday&&flatDay.DayOfWeek!=DayOfWeek.Sunday) flatDays.Add(flatDay);
            flatDay=flatDay.AddDays(1);
        }
        var flatMarket=new Dictionary<string,Series>();
        foreach(string code in new[]{"AAA","BBB"}) {
            var list=new List<Bar>();
            foreach(var day in flatDays) list.Add(new Bar{Date=day,Open=100,High=100,Low=100,Close=100,Volume=1e6});
            flatMarket[code]=new Series(code,list);
        }
        var rebalanceSource="NAME=rb\nINIT pool=1 rebalance=monthly\nTARGET AAA = 0.5\nTARGET BBB = 0.5\nVAR done = 0\nON BAR\n  IF done = 0 THEN\n    BUY 1\n    SET done = 1\n  END\nEND";
        var rebalanced=ScriptPortfolio.Run(flatMarket,new[]{"AAA","BBB"},flatDays[0],flatDays[flatDays.Count-1],1e5,0,0,rebalanceSource,true);
        Check(rebalanced.Rebalances>=2,"rebalance events");
        Check(rebalanced.Trades.Count(t=>t.Tag=="rebalance")>=2,"rebalance fills tagged");
        double unitsA=0,unitsB=0,cashLeft=1e5;
        foreach(var trade in rebalanced.Trades) {
            double gross=trade.Price*trade.Quantity;
            if(trade.Side=="买入") { cashLeft-=gross+trade.Fee; if(trade.Symbol=="AAA") unitsA+=trade.Quantity; else unitsB+=trade.Quantity; }
            else { cashLeft+=gross-trade.Fee; if(trade.Symbol=="AAA") unitsA-=trade.Quantity; else unitsB-=trade.Quantity; }
        }
        double finalEquity=cashLeft+(unitsA+unitsB)*100;
        Near(finalEquity,1e5,"rebalance keeps flat equity");
        Check(Math.Abs(unitsA*100/finalEquity-0.5)*finalEquity<100.001&&Math.Abs(unitsB*100/finalEquity-0.5)*finalEquity<100.001,"rebalance target weights within one lot");
        Reject(()=>Script.Parse("NAME=x\nINIT pool=0\nTARGET AAA = 0.5\nON BAR\nEND"),"target requires pooled");
        Reject(()=>Script.Parse("NAME=x\nINIT pool=1 rebalance=monthly\nON BAR\nEND"),"rebalance requires target");
        Reject(()=>Script.Parse("NAME=x\nINIT pool=1 rebalance=monthly\nTARGET AAA = 0.7\nTARGET BBB = 0.7\nON BAR\nEND"),"target weight sum");
        Reject(()=>Script.Parse("NAME=x\nINIT bogus=1\nON BAR\nEND"),"unknown init key");
        var realStatement=@"C:\Users\LiuCL\Downloads\300033_main_report.xls";
        if(File.Exists(realStatement)) {
            var realBook=StatementBook.Load(realStatement);
            Check(realBook.Count>=15&&realBook.Subjects.Count>=20,"sample statement periods and subjects");
            Check(realBook.Subjects.All(x=>x.Name.Length>0),"sample statement labels");
            Check(realBook.Reconcile().Count==0,"sample statement reconciliation");
            Check(double.IsNaN(realBook.Single("net_profit",0)),"sample first period has no single quarter");
        }
        Check(Rule.Load(Path.Combine(sample,"ma20.rule")).Buy==Rule.DefaultBuy,"rule import");
        var invalid=Path.Combine(folder,"bad.csv");
        File.WriteAllText(invalid,"symbol,date,open,high,low,close,volume\nT,2024-01-01,1,2,1,1\n");
        Reject(()=>Data.Market(invalid),"ragged CSV");
        File.WriteAllText(invalid,"symbol,date,open,high,low,close,volume\nT,2024-01-02,1,2,1,1,100\nT,2024-01-01,1,2,1,1,200\n");
        Check(Data.Market(invalid)["T"].Bars[0].Volume==200,"unsorted CSV");
        var stress=System.Diagnostics.Stopwatch.StartNew();
        var build=System.Diagnostics.Stopwatch.StartNew();
        var large=new Series("S",Enumerable.Range(0,Data.Limit).Select(i=>new Bar {
            Date=new DateTime(1900,1,1).AddDays(i),Open=100,Close=100,High=100,Low=100,Volume=0
        }));
        build.Stop();
        Check(double.IsNaN(large.V["VOLRATIO"].Last()),"zero denominator");
        Near(large.V["RSI14"].Last(),50,"flat RSI");
        var largeRun=Backtest.Run(large,large.Bars[0].Date,large.Bars.Last().Date,1000,0,0,rule);
        stress.Stop();
        Check(largeRun.Fills.Count==1,"long series execution");
        File.WriteAllText(Path.Combine(folder,"stress-ms.txt"),
            "series="+build.ElapsedMilliseconds.ToString(Data.Inv)+" total="+stress.ElapsedMilliseconds.ToString(Data.Inv)+" backtest="+(stress.ElapsedMilliseconds-build.ElapsedMilliseconds).ToString(Data.Inv));
        var holding=new Holding{Symbol="DEMO-A",Sector="Tech",Qty=10,Cost=35,Bought=a.Bars[100].Date};
        var p=Portfolio.Analyze(d.Market,new List<Holding>{holding},1000);
        Near(p.Total,a.Bars.Last().Close*10+1000,"portfolio NAV"); Check(p.RiskSamples==60&&p.Volatility>0,"portfolio risk");
        Reject(()=>Portfolio.Analyze(d.Market,new List<Holding>{new Holding{Symbol="NONE"}},0),"missing symbol");
    }
}
}


