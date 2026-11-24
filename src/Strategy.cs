using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PositionLab {
sealed class Rule {
    internal string Name, Buy, Sell;
    internal const string DefaultBuy = "CLOSE > MA20 AND CLOSE[1] <= MA20[1]";
    internal const string DefaultSell = "CLOSE < MA20 AND CLOSE[1] >= MA20[1]";
    internal static Rule Load(string path) {
        if (new FileInfo(path).Length > 16384) throw new FormatException("策略文件最多 16 KB。");
        var values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in File.ReadAllLines(path,new System.Text.UTF8Encoding(false,true))) {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
            int p = line.IndexOf('=');
            if (p <= 0) throw new FormatException("策略文件每行使用 NAME=、BUY= 或 SELL=。");
            string key=line.Substring(0,p).Trim();
            if (!new[]{"NAME","BUY","SELL"}.Contains(key.ToUpperInvariant()) || values.ContainsKey(key)) throw new FormatException("未知或重复策略键：" + key);
            values.Add(key,line.Substring(p+1).Trim());
        }
        if (!values.ContainsKey("BUY") || !values.ContainsKey("SELL")) throw new FormatException("策略须包含 BUY 和 SELL。");
        if (values.ContainsKey("NAME") && values["NAME"].Length > 80) throw new FormatException("策略名称最多 80 字符。");
        return new Rule { Name=values.ContainsKey("NAME") ? values["NAME"] : Path.GetFileNameWithoutExtension(path), Buy=values["BUY"], Sell=values["SELL"] };
    }
    internal static Func<Series,int,bool?> Compile(string input) { return new Parser(input).Parse(); }
    sealed class Parser {
        readonly string[] tokens;
        int cursor;
        string Peek { get { return cursor < tokens.Length ? tokens[cursor] : ""; } }
        internal Parser(string input) {
            if (string.IsNullOrWhiteSpace(input) || input.Length > 700) throw new FormatException("公式长度须为 1–700 字符。");
            tokens=Regex.Matches(input.ToUpperInvariant(),@"[A-Z_][A-Z0-9_]*|-?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)|>=|<=|!=|==|\S").Cast<Match>().Select(m=>m.Value).ToArray();
            if (tokens.Length > 180) throw new FormatException("公式最多 180 个记号。");
        }
        bool Take(string s) { if(Peek != s) return false; cursor++; return true; }
        internal Func<Series,int,bool?> Parse() {
            var f=Expression("OR",0);
            if(Peek.Length!=0) throw new FormatException("无法识别的规则内容：" + Peek);
            return f;
        }
        Func<Series,int,bool?> Expression(string op,int depth) {
            if(depth>12) throw new FormatException("括号嵌套最多 12 层。");
            var parts=new List<Func<Series,int,bool?>>();
            do { parts.Add(op=="OR" ? Expression("AND",depth) : Clause(depth)); } while(Take(op));
            if(parts.Count==1) return parts[0];
            return (s,i)=> {
                bool? value=op=="AND";
                foreach(var f in parts) value = op=="AND" ? value & f(s,i) : value | f(s,i);
                return value;
            };
        }
        Func<Series,int,bool?> Clause(int depth) {
            if(Take("(")) {
                var inner=Expression("OR",depth+1);
                if(!Take(")")) throw new FormatException("缺少右括号。");
                return inner;
            }
            var a=Value(); string op=Peek;
            if(!new[]{">","<",">=","<=","=","==","!="}.Contains(op)) throw new FormatException("缺少比较运算符。");
            cursor++; var b=Value();
            return (s,i)=>{
                double x=a(s,i), y=b(s,i);
                if(double.IsNaN(x)||double.IsNaN(y)) return null;
                switch(op) {
                    case ">": return x>y;
                    case "<": return x<y;
                    case ">=": return x>=y;
                    case "<=": return x<=y;
                    case "!=": return x!=y;
                    default: return x==y;
                }
            };
        }
        Func<Series,int,double> Value() {
            string t=Peek;
            if(t.Length==0) throw new FormatException("缺少指标或数值。");
            cursor++;
            if(Series.Fields.Contains(t)) {
                int lag=0;
                if(Take("[")) {
                    if(Peek.StartsWith("-")) throw new FormatException("检测到未来取值：" + t + "[" + Peek + "]。");
                    if(!int.TryParse(Peek,out lag)||lag<0||lag>252) throw new FormatException("回看索引须为 0–252。");
                    cursor++;
                    if(!Take("]")) throw new FormatException("缺少右方括号。");
                }
                return (s,i)=> i<lag ? double.NaN : s.V[t][i-lag];
            }
            if (char.IsDigit(t[0]) || t[0]=='-' || t[0]=='.') {
                double number=Data.Number(t,-1e20,1e20); return (s,i)=>number;
            }
            throw new FormatException("拒绝未知字段或函数：" + t + "。LEAD、未来引用、外部代码等均不可用。");
        }
    }
}
sealed class Fill {
    internal string Symbol;
    internal DateTime Date, Signal;
    internal string Side;
    internal double Price, Quantity, Fee, Pnl;
}
sealed class Backtest {
    internal DateTime[] Dates;
    internal double[] Equity, Benchmark, Invested;
    internal readonly List<Fill> Fills=new List<Fill>();
    internal int Start, End, Exposure, Skipped, Conflicts;
    internal double Initial;
    internal static Backtest Run(Series s, DateTime from, DateTime to, double initial, double fee, double slip, Rule rule) {
        if(from>to) throw new FormatException("起始日期必须早于或等于结束日期。");
        int start=Array.FindIndex(s.Bars,b=>b.Date>=from);
        int end=Array.FindLastIndex(s.Bars,b=>b.Date<=to);
        if(start<0 || end-start<1) throw new FormatException("区间至少需要两个交易日。");
        var buy=Rule.Compile(rule.Buy); var sell=Rule.Compile(rule.Sell);
        var result=new Backtest { Start=start,End=end,Initial=initial,Dates=new DateTime[end-start+1],Equity=new double[end-start+1],Benchmark=new double[end-start+1],Invested=new double[end-start+1] };
        double cash=initial, units=0, basis=0; int pending=0;
        for(int i=start;i<=end;i++) {
            Bar b=s.Bars[i]; int j=i-start;
            if(pending==1 && units==0) {
                double price=b.Open*(1+slip);
                double qty=Math.Floor(cash/(price*(1+fee)));
                double charge=price*qty*fee; double total=price*qty+charge;
                if(total>cash && qty>0) { qty--; charge=price*qty*fee; total=price*qty+charge; }
                if(qty>0) {
                    units=qty; basis=total; cash-=total;
                    result.Fills.Add(new Fill { Symbol=s.Symbol,Date=b.Date,Signal=s.Bars[i-1].Date,Side="买入",Price=price,Quantity=qty,Fee=charge,Pnl=double.NaN });
                } else result.Skipped++;
            } else if(pending==-1 && units>0) {
                double price=b.Open*(1-slip), charge=units*price*fee, net=units*price-charge;
                cash+=net;
                result.Fills.Add(new Fill { Symbol=s.Symbol,Date=b.Date,Signal=s.Bars[i-1].Date,Side="卖出",Price=price,Quantity=units,Fee=charge,Pnl=net-basis });
                units=0;
            }
            result.Dates[j]=b.Date; result.Equity[j]=cash+units*b.Close; result.Invested[j]=units*b.Close;
            result.Benchmark[j]=initial*b.Close/s.Bars[start].Close;
            if(units>0) result.Exposure++;
            pending=0;
            if(i==end) continue;
            bool? enter=buy(s,i), exit=sell(s,i);
            if(!enter.HasValue||!exit.HasValue) { result.Skipped++; continue; }
            if(enter.Value&&exit.Value) { result.Conflicts++; continue; }
            if(enter.Value&&units==0) pending=1;
            if(exit.Value&&units>0) pending=-1;
        }
        return result;
    }
    internal double Total { get { return Equity.Last()/Initial-1; } }
    internal double Annual { get { return Math.Pow(Equity.Last()/Initial,252.0/(Equity.Length-1))-1; } }
    internal double MaxDrawdown { get { return Stats.Drawdown(new[]{Initial}.Concat(Equity)); } }
    internal double Sharpe {
        get {
            var ret=new double[Equity.Length-1];
            for(int i=1;i<Equity.Length;i++) ret[i-1]=Equity[i]/Equity[i-1]-1;
            double volatility=Stats.Std(ret);
            return volatility>0 ? Math.Sqrt(252)*ret.Average()/volatility : double.NaN;
        }
    }
}
}
