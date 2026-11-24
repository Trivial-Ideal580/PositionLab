using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PositionLab {
sealed class RuleInfo {
    internal string[] Fields = new string[0];
    internal string[] Functions = new string[0];
    internal int MaxLag;
}
sealed class Rule {
    internal string Name, Buy, Sell;
    internal const string DefaultBuy = "CLOSE > MA20 AND CLOSE[1] <= MA20[1]";
    internal const string DefaultSell = "CLOSE < MA20 AND CLOSE[1] >= MA20[1]";
    internal static readonly string[] Functions={"ABS","MIN","MAX","IF","CROSS","CROSSDOWN","SUM","AVG","COUNT","LAG"};
    internal static Rule Load(string path) {
        if (new FileInfo(path).Length > 16384) throw new FormatException("策略文件最多 16 KB。");
        var values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in Data.Text(path).Split(new[]{"\r\n","\n","\r"},StringSplitOptions.None)) {
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
    internal static RuleInfo Inspect(string input) {
        var info=new RuleInfo();
        var fields=new List<string>(); var functions=new List<string>();
        var tokens=Tokenizer(input);
        for(int i=0;i<tokens.Length;i++) {
            string token=tokens[i];
            if(Series.Fields.Contains(token) && !fields.Contains(token)) fields.Add(token);
            if(Functions.Contains(token) && i+1<tokens.Length && tokens[i+1]=="(" && !functions.Contains(token)) functions.Add(token);
            if(i>0 && tokens[i]=="[" && i+2<tokens.Length) {
                int lag;
                if(int.TryParse(tokens[i+1],out lag) && lag>info.MaxLag) info.MaxLag=lag;
            }
        }
        info.Fields=fields.ToArray(); info.Functions=functions.ToArray();
        return info;
    }
    internal static string[] Tokenizer(string input) {
        return Regex.Matches(input.ToUpperInvariant(),@"[A-Z_][A-Z0-9_]*|(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)|>=|<=|!=|==|\S")
            .Cast<Match>().Select(m=>m.Value).ToArray();
    }
    internal static Func<Series,int,bool?> Compile(string input) { return new Parser(input,null,null).Parse(); }
    internal static Func<Series,int,double> Expression(string input,Func<string,Func<Series,int,double>> resolve,Func<string,bool> dynamicName) {
        return new Parser(input,resolve,dynamicName).Numeric();
    }
    sealed class Parser {
        static readonly string[] Comparators={">","<",">=","<=","=","==","!="};
        readonly string[] tokens;
        int cursor;
        string Peek { get { return cursor<tokens.Length ? tokens[cursor] : ""; } }
        readonly Func<string,Func<Series,int,double>> resolve;
        readonly Func<string,bool> dynamicName;
        int lookback;
        internal Parser(string input,Func<string,Func<Series,int,double>> resolve,Func<string,bool> dynamicName) {
            this.resolve=resolve; this.dynamicName=dynamicName;
            if (string.IsNullOrWhiteSpace(input) || input.Length > 1200) throw new FormatException("公式长度须为 1–1200 字符。");
            tokens=Tokenizer(input);
            if (tokens.Length > 400) throw new FormatException("公式最多 400 个记号。");
        }
        bool Take(string token) { if(Peek!=token) return false; cursor++; return true; }
        internal Func<Series,int,double> Numeric() {
            var value=Or(0);
            if(Peek.Length!=0) throw new FormatException("无法识别的规则内容：" + Peek);
            return value;
        }
        internal Func<Series,int,bool?> Parse() {
            var value=Or(0);
            if(Peek.Length!=0) throw new FormatException("无法识别的规则内容：" + Peek);
            return (s,i)=>{
                double v=value(s,i);
                return double.IsNaN(v) ? (bool?)null : v>0.5;
            };
        }
        Func<Series,int,double> Or(int depth) {
            var parts=new List<Func<Series,int,double>> { And(depth) };
            while(Take("OR")) parts.Add(And(depth));
            if(parts.Count==1) return parts[0];
            return (s,i)=>{
                bool unknown=false;
                foreach(var part in parts) {
                    double v=part(s,i);
                    if(v>0.5) return 1;
                    if(double.IsNaN(v)) unknown=true;
                }
                return unknown ? double.NaN : 0;
            };
        }
        Func<Series,int,double> And(int depth) {
            var parts=new List<Func<Series,int,double>> { Not(depth) };
            while(Take("AND")) parts.Add(Not(depth));
            if(parts.Count==1) return parts[0];
            return (s,i)=>{
                bool unknown=false;
                foreach(var part in parts) {
                    double v=part(s,i);
                    if(double.IsNaN(v)) unknown=true;
                    else if(v<=0.5) return 0;
                }
                return unknown ? double.NaN : 1;
            };
        }
        Func<Series,int,double> Not(int depth) {
            if(Take("NOT")) {
                var inner=Not(depth);
                return (s,i)=>{
                    double v=inner(s,i);
                    return double.IsNaN(v) ? double.NaN : (v>0.5 ? 0 : 1);
                };
            }
            return Relational(depth);
        }
        Func<Series,int,double> Relational(int depth) {
            var left=Additive(depth);
            string op=Peek;
            if(!Comparators.Contains(op)) return left;
            cursor++;
            var right=Additive(depth);
            return (s,i)=>{
                double a=left(s,i), b=right(s,i);
                if(double.IsNaN(a)||double.IsNaN(b)) return double.NaN;
                switch(op) {
                    case ">": return a>b ? 1 : 0;
                    case "<": return a<b ? 1 : 0;
                    case ">=": return a>=b ? 1 : 0;
                    case "<=": return a<=b ? 1 : 0;
                    case "!=": return a!=b ? 1 : 0;
                    default: return a==b ? 1 : 0;
                }
            };
        }
        Func<Series,int,double> Additive(int depth) {
            var left=Multiplicative(depth);
            while(true) {
                if(Take("+")) { var right=Multiplicative(depth); var previous=left; left=(s,i)=>previous(s,i)+right(s,i); }
                else if(Take("-")) { var right=Multiplicative(depth); var previous=left; left=(s,i)=>previous(s,i)-right(s,i); }
                else return left;
            }
        }
        Func<Series,int,double> Multiplicative(int depth) {
            var left=Unary(depth);
            while(true) {
                if(Take("*")) { var right=Unary(depth); var previous=left; left=(s,i)=>previous(s,i)*right(s,i); }
                else if(Take("/")) {
                    var right=Unary(depth); var previous=left;
                    left=(s,i)=>{ double a=previous(s,i), b=right(s,i); return b==0 ? double.NaN : a/b; };
                } else return left;
            }
        }
        Func<Series,int,double> Unary(int depth) {
            if(Take("-")) { var inner=Unary(depth); return (s,i)=>-inner(s,i); }
            if(Take("+")) return Unary(depth);
            return Primary(depth);
        }
        Func<Series,int,double> Primary(int depth) {
            if(depth>12) throw new FormatException("括号嵌套最多 12 层。");
            if(Take("(")) {
                var inner=Or(depth+1);
                if(!Take(")")) throw new FormatException("缺少右括号。");
                return inner;
            }
            string token=Peek;
            if(token.Length==0) throw new FormatException("缺少指标或数值。");
            if(resolve!=null) {
                var shadow=resolve(token);
                if(shadow!=null) {
                    if(lookback>0&&dynamicName!=null&&dynamicName(token)) throw new FormatException("变量 " + token + " 会随每根 K 线变化，不能用在 LAG／SUM／AVG／COUNT／CROSS 内部。");
                    cursor++;
                    return shadow;
                }
            }
            if(Series.Fields.Contains(token)) { cursor++; return Field(token); }
            if(char.IsDigit(token[0])||token[0]=='.') {
                cursor++;
                double number=Data.Number(token,-1e20,1e20);
                return (s,i)=>number;
            }
            if(char.IsLetter(token[0])||token[0]=='_') {
                if(resolve!=null) {
                    var variable=resolve(token);
                    if(variable!=null) {
                        if(lookback>0&&dynamicName!=null&&dynamicName(token)) throw new FormatException("变量 " + token + " 会随每根 K 线变化，不能用在 LAG／SUM／AVG／COUNT／CROSS 内部。");
                        cursor++;
                        return variable;
                    }
                }
                cursor++;
                return Function(token,depth);
            }
            throw new FormatException("拒绝未知字段或函数：" + token + "。LEAD、未来引用、外部代码等均不可用。");
        }
        Func<Series,int,double> Field(string token) {
            int lag=0;
            if(Take("[")) {
                if(Peek.StartsWith("-")) throw new FormatException("检测到未来取值：" + token + "[" + Peek + "]。");
                if(!int.TryParse(Peek,out lag)||lag<0||lag>252) throw new FormatException("回看索引须为 0–252。");
                cursor++;
                if(!Take("]")) throw new FormatException("缺少右方括号。");
            }
            string field=token;
            return (s,i)=> i<lag ? double.NaN : s.V[field][i-lag];
        }
        Func<Series,int,double> Function(string name,int depth) {
            name=name.ToUpperInvariant();
            if(!Take("(")) throw new FormatException("函数 " + name + " 需要括号。");
            bool window=name=="LAG"||name=="SUM"||name=="AVG"||name=="COUNT"||name=="CROSS"||name=="CROSSDOWN";
            if(window) lookback++;
            var args=Arguments(depth);
            if(window) lookback--;
            switch(name) {
                case "ABS": Arity(name,args,1); return (s,i)=>Math.Abs(args[0](s,i));
                case "MIN": Arity(name,args,2); return (s,i)=>Math.Min(args[0](s,i),args[1](s,i));
                case "MAX": Arity(name,args,2); return (s,i)=>Math.Max(args[0](s,i),args[1](s,i));
                case "IF": Arity(name,args,3); return (s,i)=>{
                    double condition=args[0](s,i);
                    return double.IsNaN(condition) ? double.NaN : (condition>0.5 ? args[1](s,i) : args[2](s,i));
                };
                case "CROSS": Arity(name,args,2); return Crossing(args[0],args[1],true);
                case "CROSSDOWN": Arity(name,args,2); return Crossing(args[0],args[1],false);
                case "SUM": Arity(name,args,2); return Window(args[0],args[1],false);
                case "AVG": Arity(name,args,2); return Window(args[0],args[1],true);
                case "COUNT": Arity(name,args,2); return Window(args[0],args[1],false,true);
                case "LAG": Arity(name,args,2); return Shift(args[0],args[1]);
            }
            throw new FormatException("拒绝未知字段或函数：" + name + "。LEAD、未来引用、外部代码等均不可用。");
        }
        List<Func<Series,int,double>> Arguments(int depth) {
            var list=new List<Func<Series,int,double>>();
            if(Take(")")) return list;
            do { list.Add(Or(depth+1)); } while(Take(","));
            if(!Take(")")) throw new FormatException("缺少右括号。");
            return list;
        }
        static void Arity(string name,List<Func<Series,int,double>> args,int expected) {
            if(args.Count!=expected) throw new FormatException("函数 " + name + " 需要 " + expected + " 个参数，实际 " + args.Count + " 个。");
        }
        static Func<Series,int,double> Crossing(Func<Series,int,double> a,Func<Series,int,double> b,bool up) {
            return (s,i)=>{
                if(i<1) return double.NaN;
                double a0=a(s,i), b0=b(s,i), a1=a(s,i-1), b1=b(s,i-1);
                if(double.IsNaN(a0)||double.IsNaN(b0)||double.IsNaN(a1)||double.IsNaN(b1)) return double.NaN;
                bool crossed=up ? (a0>b0&&a1<=b1) : (a0<b0&&a1>=b1);
                return crossed ? 1 : 0;
            };
        }
        static Func<Series,int,double> Shift(Func<Series,int,double> inner,Func<Series,int,double> size) {
            return (s,i)=>{
                double width=size(s,i);
                if(double.IsNaN(width)) return double.NaN;
                int lag=(int)Math.Round(width);
                if(lag<0||lag>252) throw new FormatException("LAG 的偏移须为 0–252 个交易日。");
                return i<lag ? double.NaN : inner(s,i-lag);
            };
        }
        static Func<Series,int,double> Window(Func<Series,int,double> inner,Func<Series,int,double> size,bool average,bool count=false) {
            return (s,i)=>{
                double width=size(s,i);
                if(double.IsNaN(width)) return double.NaN;
                int window=(int)Math.Round(width);
                if(window<1||window>252||i-window+1<0) return double.NaN;
                double total=0;
                for(int k=0;k<window;k++) {
                    double v=inner(s,i-k);
                    if(double.IsNaN(v)) return double.NaN;
                    total+=count ? (v>0.5?1:0) : v;
                }
                return average ? total/window : total;
            };
        }
    }
}
sealed class Fill {
    internal string Symbol;
    internal DateTime Date, Signal;
    internal string Side;
    internal double Price, Quantity, Fee, Pnl;
    internal string Tag;
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
