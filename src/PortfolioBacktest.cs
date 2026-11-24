using System;
using System.Collections.Generic;
using System.Linq;

namespace PositionLab {
sealed class SymbolResult {
    internal string Symbol;
    internal double Initial, Final, Total, Annual, MaxDrawdown, Sharpe, WinRate, Exposure;
    internal int Fills;
    internal string Message;
    internal bool Failed { get { return Message != null; } }
}
sealed class PortfolioBacktest {
    internal DateTime[] Dates = new DateTime[0];
    internal double[] Equity = new double[0], Benchmark = new double[0];
    internal double Initial, Total, Annual, MaxDrawdown, Sharpe, WinRate, Exposure;
    internal int Fills, Skipped, Conflicts, Requested, Used, Rebalances;
    internal readonly List<Fill> Trades = new List<Fill>();
    internal readonly List<SymbolResult> Symbols = new List<SymbolResult>();

    internal static PortfolioBacktest Run(Dictionary<string,Series> market, string[] symbols, DateTime from, DateTime to, double initial, double fee, double slip, Rule rule) {
        var requested=symbols.Distinct().ToArray();
        if(requested.Length==0) throw new FormatException("至少需要一个标的。");
        var usable=new List<string>();
        var skipped=new List<SymbolResult>();
        foreach(string symbol in requested) {
            Series series;
            if(!market.TryGetValue(symbol,out series)) { skipped.Add(new SymbolResult{Symbol=symbol,Message="缺少行情"}); continue; }
            int start=Array.FindIndex(series.Bars,b=>b.Date>=from);
            int end=Array.FindLastIndex(series.Bars,b=>b.Date<=to);
            if(start<0||end-start<1) { skipped.Add(new SymbolResult{Symbol=symbol,Message="区间内行情不足"}); continue; }
            usable.Add(symbol);
        }
        if(usable.Count==0) throw new FormatException("所选标的在区间内都没有足够行情（至少两个交易日）。");
        double slice=initial/usable.Count;
        var result=new PortfolioBacktest {Initial=initial,Requested=requested.Length,Used=usable.Count};
        var runs=new List<Backtest>();
        foreach(string symbol in usable) {
            var run=Backtest.Run(market[symbol],from,to,slice,fee,slip,rule);
            runs.Add(run);
            result.Symbols.Add(new SymbolResult {Symbol=symbol,Initial=slice,Final=run.Equity[run.Equity.Length-1],Total=run.Total,
                Annual=run.Annual,MaxDrawdown=run.MaxDrawdown,Sharpe=run.Sharpe,Fills=run.Fills.Count,
                Exposure=(double)run.Exposure/run.Dates.Length,WinRate=WinRateOf(run.Fills)});
            result.Trades.AddRange(run.Fills);
            result.Skipped+=run.Skipped; result.Conflicts+=run.Conflicts;
        }
        result.Symbols.AddRange(skipped);
        result.Trades.Sort((a,b)=>a.Date!=b.Date?a.Date.CompareTo(b.Date):string.CompareOrdinal(a.Symbol,b.Symbol));
        result.Fills=result.Trades.Count;

        var days=runs.SelectMany(r=>r.Dates).Distinct().OrderBy(d=>d).ToArray();
        result.Dates=days;
        result.Equity=new double[days.Length];
        result.Benchmark=new double[days.Length];
        var cursors=new int[runs.Count];
        var equity=new double[runs.Count];
        var benchmark=new double[runs.Count];
        var invested=new double[runs.Count];
        for(int k=0;k<runs.Count;k++) { equity[k]=slice; benchmark[k]=slice; }
        int exposed=0;
        for(int d=0;d<days.Length;d++) {
            double sum=0, bench=0, held=0;
            for(int k=0;k<runs.Count;k++) {
                var run=runs[k];
                while(cursors[k]<run.Dates.Length&&run.Dates[cursors[k]]==days[d]) {
                    equity[k]=run.Equity[cursors[k]];
                    benchmark[k]=run.Benchmark[cursors[k]];
                    invested[k]=run.Invested[cursors[k]];
                    cursors[k]++;
                }
                sum+=equity[k]; bench+=benchmark[k]; held+=invested[k];
            }
            result.Equity[d]=sum; result.Benchmark[d]=bench;
            if(held>0) exposed++;
        }
        result.Exposure=(double)exposed/days.Length;
        result.Total=result.Equity[days.Length-1]/initial-1;
        result.Annual=days.Length>1 ? Math.Pow(result.Equity[days.Length-1]/initial,252.0/(days.Length-1))-1 : 0;
        result.MaxDrawdown=Stats.Drawdown(new[]{initial}.Concat(result.Equity));
        result.WinRate=WinRateOf(result.Trades);
        if(days.Length>1) {
            var returns=new double[days.Length-1];
            for(int i=1;i<days.Length;i++) returns[i-1]=result.Equity[i]/result.Equity[i-1]-1;
            double deviation=Stats.Std(returns);
            result.Sharpe=deviation>0 ? Math.Sqrt(252)*returns.Average()/deviation : double.NaN;
        }
        return result;
    }
    internal static double WinRateOf(List<Fill> fills) {
        int closed=0, wins=0;
        foreach(var fill in fills) if(fill.Side=="卖出") { closed++; if(fill.Pnl>0) wins++; }
        return closed==0 ? double.NaN : (double)wins/closed;
    }
}
sealed class ComparisonRow {
    internal string Name;
    internal PortfolioBacktest Result;
    internal double[] Curve;
}
sealed class Comparison {
    internal DateTime[] Dates;
    internal readonly List<ComparisonRow> Rows=new List<ComparisonRow>();
    internal readonly List<string> Warnings=new List<string>();
    internal static Comparison Run(Dictionary<string,Series> market, string[] symbols, DateTime from, DateTime to, double initial, double fee, double slip, List<Rule> rules) {
        if(rules.Count==0) throw new FormatException("策略库为空，至少需要一条策略。");
        var result=new Comparison();
        foreach(var rule in rules) {
            try {
                var run=PortfolioBacktest.Run(market,symbols,from,to,initial,fee,slip,rule);
                result.Rows.Add(new ComparisonRow{Name=rule.Name,Result=run,Curve=run.Equity.Select(x=>x/run.Initial-1).ToArray()});
                if(result.Dates==null) result.Dates=run.Dates;
            } catch(Exception error) {
                result.Warnings.Add(rule.Name+"："+error.Message);
            }
        }
        if(result.Rows.Count==0) throw new FormatException("所有策略都未能完成回测。"+(result.Warnings.Count>0?" "+result.Warnings[0]:""));
        return result;
    }
}
static class Library {
    internal const int MaxRules=8, MaxSymbols=30;
    internal static readonly Rule[] Presets = {
        new Rule{Name="均线突破",Buy="CLOSE > MA20 AND CLOSE[1] <= MA20[1]",Sell="CLOSE < MA20 AND CLOSE[1] >= MA20[1]"},
        new Rule{Name="动量趋势",Buy="MOM20 > 0.05 AND CLOSE > MA60",Sell="MOM20 < 0"},
        new Rule{Name="快慢均线",Buy="MA5 > MA20 AND MA5[1] <= MA20[1]",Sell="MA5 < MA20 AND MA5[1] >= MA20[1]"},
        new Rule{Name="布林反转",Buy="CLOSE < BOLLDOWN",Sell="CLOSE > BOLLMID"}
    };
    internal static List<Rule> Initial() { return new List<Rule>(Presets); }
    internal static bool Add(List<Rule> rules, Rule rule) {
        if(rules.Any(r=>r.Buy==rule.Buy&&r.Sell==rule.Sell)) return false;
        if(rules.Count>=MaxRules) throw new FormatException("策略库最多 "+MaxRules+" 条，请先恢复预设或移除。");
        rules.Add(new Rule{Name=rule.Name,Buy=rule.Buy,Sell=rule.Sell});
        return true;
    }
}
}
