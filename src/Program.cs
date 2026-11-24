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
行情使用 UTF-8 CSV。字段为 symbol,date,open,high,low,close,volume。日期为 YYYY-MM-DD，价格必须为正，成交量可为零。同一标的日期不可重复，导入后按日期排序。价格和持仓成本请用同一币种及一致的复权口径。程序不会自动补停牌、缺失交易日和公司行动。

基本面与信息面使用 UTF-8 CSV。字段为 symbol,available_date,roe,pe,sentiment。ROE 按百分点输入，例如 12.5；sentiment 范围为 -1 到 1。每个收盘日须录入当时已知的 ROE、PE 和情绪快照。盘后披露请从下一可用交易日起纳入。模型仅使用精确匹配的行，缺失日不填补。

持仓支持手工输入和 UTF-8 CSV。字段为 symbol,quantity,cost,bought_date,sector。支持多笔同标的长仓，按共同估值日收盘价估值。风险使用最多 60 个严格同步的日收益和当前市值权重，现金收益设为零。持仓风格按市值加权日历天划分，15 天及以下为短周期，16–90 天为中周期。仅有持仓快照时，无法重建真实交易频率、择时或历史净值。

技术指标与评分
使用价差平滑版 RSI14 和 ATR14，首个完整窗口以算术平均初始化；MACD 柱为 2 × (DIF−DEA)；布林带为 MA20 ± 2 倍总体标准差；年化波动使用 252 个交易日、样本标准差。综合分由动量分位 50%、RSI 分位 30%、低波动分位 20% 组成，并列按半权重计算。有效分位不足时留空。

策略格式与执行
UTF-8 .rule 或 .txt 文件使用 NAME=、BUY=、SELL=。比较符支持 >、<、>=、<=、=、==、!=，可用 AND、OR 和小括号。字段参考因子页，PRICE 是 CLOSE 的别名。CLOSE[1] 表示上一交易日，最多回看 252 日。RET1、MOM20 和 VOL20 在规则中用小数，例如 0.03 表示 3%。外部代码、负回看和未知函数会被拒绝。
回测从指定区间首日收盘开始判定信号，下一交易日开盘成交，冲突和所需指标缺失时跳过。仅支持单标的、长仓、整单位和比例费率／滑点。买入持有基准按区间首日收盘归一化，无费用。未模拟涨跌停、停牌成交约束、分红税、资金利息和市场冲击。短区间的年化收益可很极端。

概率与相关性
采用训练集标准化、L2 正则逻辑回归。样本依时间切分，边界留空一条，预测标签为下一条行情收盘相对当日收盘上涨。相关性为训练集 Pearson r，准确率按 0.50 分类阈值计算。AUC 衡量排序，Brier 衡量概率误差。关联无法证明因果，样本外表现会随市场条件改变。

免责声明与安全
本程序用于研究、教学和策略验证。评分、样例和回测结果不构成投资建议、收益承诺或交易指令。历史表现和模型估计可能与结果显著偏离。请独立核验源数据的完整性、发布时间、交易与复权假设，并自行承担决策后果。
程序离线运行，无账号、行情网络请求、券商连接或自动下单。工作数据与编辑内容存放在本次运行的内存中。仅显式导出时写出文件。导出包含敏感持仓时请自行保护文件。";
    [STAThread]
    static void Main(string[] args) {
        SetProcessDPIAware();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2&&args[0]=="--verify") {
            try { Verify.Run(args[1]); using(var form=new Terminal()) form.VerifyUi(args[1]); File.WriteAllText(Path.Combine(args[1],"verification.txt"),"PASS",Encoding.UTF8); }
            catch(Exception e) { File.WriteAllText(Path.Combine(args[1],"verification.txt"),e.ToString(),Encoding.UTF8); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==2&&args[0]=="--sample-data") { WriteSamples(args[1]); return; }
        Application.Run(new Terminal());
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
        var full=Backtest.Run(a,a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell});
        var trimmed=new Series(a.Symbol,a.Bars.Take(301));
        var prefix=Backtest.Run(trimmed,a.Bars[0].Date,a.Bars[300].Date,1e5,.0008,.0005,new Rule{Buy=Rule.DefaultBuy,Sell=Rule.DefaultSell});
        Check(full.Equity.SequenceEqual(prefix.Equity),"prefix invariance");
        var m=Model.Fit(a,d.Context);
        Check(m.Probability>0&&m.Probability<1&&m.TestStart>m.TrainEnd&&m.Brier>=0&&m.Brier<=1,"model split and range");
        Reject(()=>Model.Fit(a,new Dictionary<DateTime,Context>()),"missing recent snapshot");
        var sample=Program.WriteSamples(folder);
        var roundtrip=Data.Market(Path.Combine(sample,"market.csv"));
        var snapshot=Data.Contexts(Path.Combine(sample,"context.csv"),"DEMO-A");
        Check(roundtrip.Count==3&&snapshot.Count==516,"CSV sample import");
        Near(roundtrip["DEMO-A"].Bars.Last().Close,a.Bars.Last().Close,"CSV price roundtrip");
        Check(Data.Holdings(Path.Combine(sample,"positions.csv")).Count==2,"holdings CSV");
        Check(Rule.Load(Path.Combine(sample,"ma20.rule")).Buy==Rule.DefaultBuy,"rule import");
        var invalid=Path.Combine(folder,"bad.csv");
        File.WriteAllText(invalid,"symbol,date,open,high,low,close,volume\nT,2024-01-01,1,2,1,1\n");
        Reject(()=>Data.Market(invalid),"ragged CSV");
        File.WriteAllText(invalid,"symbol,date,open,high,low,close,volume\nT,2024-01-02,1,2,1,1,100\nT,2024-01-01,1,2,1,1,200\n");
        Check(Data.Market(invalid)["T"].Bars[0].Volume==200,"unsorted CSV");
        var stress=System.Diagnostics.Stopwatch.StartNew();
        var large=new Series("S",Enumerable.Range(0,Data.Limit).Select(i=>new Bar {
            Date=new DateTime(1900,1,1).AddDays(i),Open=100,Close=100,High=100,Low=100,Volume=0
        }));
        Check(double.IsNaN(large.V["VOLRATIO"].Last()),"zero denominator");
        Near(large.V["RSI14"].Last(),50,"flat RSI");
        var largeRun=Backtest.Run(large,large.Bars[0].Date,large.Bars.Last().Date,1000,0,0,rule);
        Check(largeRun.Fills.Count==1,"long series execution");
        File.WriteAllText(Path.Combine(folder,"stress-ms.txt"),stress.ElapsedMilliseconds.ToString(Data.Inv));
        var holding=new Holding{Symbol="DEMO-A",Sector="Tech",Qty=10,Cost=35,Bought=a.Bars[100].Date};
        var p=Portfolio.Analyze(d.Market,new List<Holding>{holding},1000);
        Near(p.Total,a.Bars.Last().Close*10+1000,"portfolio NAV"); Check(p.RiskSamples==60&&p.Volatility>0,"portfolio risk");
        Reject(()=>Portfolio.Analyze(d.Market,new List<Holding>{new Holding{Symbol="NONE"}},0),"missing symbol");
    }
}
}


