using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PositionLab {
sealed class SignificanceResult {
    internal int Iterations, Bars;
    internal double Actual, NullMean, NullStd, NullMedian, Best, Worst, PValue;
    internal bool Available;
    internal double[] Null = new double[0];
}
static class Significance {
    internal const int DefaultIterations = 200;
    const int Seed = 20260815;
    // 块自举：把入场/离场信号沿时间轴环形平移，保留信号聚集结构，再在同一价格路径上重跑。
    // 零假设是「同样的持仓规则，随机选择入场时点」，超额收益不应显著高于随机平移。
    internal static SignificanceResult Run(Dictionary<string,Series> market,string[] symbols,Rule rule,DateTime from,DateTime to,double capital,double fee,double slip,int iterations) {
        if (iterations < 20) iterations = 20;
        if (iterations > 2000) iterations = 2000;
        var plans = new List<Plan>();
        double slice = capital / symbols.Length;
        var buy = Rule.Compile(rule.Buy); var sell = Rule.Compile(rule.Sell);
        foreach (string symbol in symbols) {
            Series series;
            if (!market.TryGetValue(symbol, out series)) continue;
            int start = Array.FindIndex(series.Bars, b => b.Date >= from);
            int end = Array.FindLastIndex(series.Bars, b => b.Date <= to);
            if (start < 0 || end - start < 3) continue;
            int n = end - start;
            var enter = new bool?[n]; var exit = new bool?[n];
            for (int k = 0; k < n; k++) { enter[k] = buy(series, start + k); exit[k] = sell(series, start + k); }
            plans.Add(new Plan { Series = series, Start = start, End = end, Slice = slice, Fee = fee, Slip = slip, Enter = enter, Exit = exit });
        }
        var result = new SignificanceResult { Iterations = iterations };
        if (plans.Count == 0) return result;
        result.Bars = plans.Sum(p => p.Enter.Length);
        result.Actual = plans.Sum(p => Equity(p, 0)) / capital - 1;
        var random = new Random(Seed);
        var nulls = new double[iterations];
        for (int it = 0; it < iterations; it++) {
            double sum = 0;
            foreach (var plan in plans) {
                int shift = plan.Enter.Length <= 1 ? 0 : random.Next(1, plan.Enter.Length);
                sum += Equity(plan, shift);
            }
            nulls[it] = sum / capital - 1;
        }
        result.Null = nulls;
        Array.Sort(nulls);
        result.NullMean = nulls.Average();
        result.NullStd = Stats.Std(nulls);
        result.NullMedian = nulls[nulls.Length / 2];
        result.Best = nulls[nulls.Length - 1];
        result.Worst = nulls[0];
        int beaten = nulls.Count(value => value >= result.Actual);
        result.PValue = (double)(beaten + 1) / (iterations + 1);
        result.Available = true;
        return result;
    }
    sealed class Plan {
        internal Series Series;
        internal int Start, End;
        internal double Slice;
        internal double Fee, Slip;
        internal bool?[] Enter, Exit;
    }
    static double Equity(Plan plan,int shift) {
        var bars = plan.Series.Bars;
        int n = plan.Enter.Length;
        double cash = plan.Slice, units = 0;
        int pending = 0;
        double last = cash;
        for (int i = plan.Start; i <= plan.End; i++) {
            Bar bar = bars[i];
            if (pending == 1 && units == 0) {
                double price = bar.Open * (1 + plan.Slip);
                double quantity = Math.Floor(cash / (price * (1 + plan.Fee)));
                if (quantity > 0) { cash -= price * quantity * (1 + plan.Fee); units = quantity; }
            } else if (pending == -1 && units > 0) {
                cash += bar.Open * (1 - plan.Slip) * units * (1 - plan.Fee); units = 0;
            }
            last = cash + units * bar.Close;
            pending = 0;
            if (i == plan.End) break;
            int k = i - plan.Start;
            int source = k - shift;
            while (source < 0) source += n;
            bool? enter = plan.Enter[source], exit = plan.Exit[source];
            if (!enter.HasValue || !exit.HasValue) continue;
            if (enter.Value && exit.Value) continue;
            if (enter.Value && units == 0) pending = 1;
            if (exit.Value && units > 0) pending = -1;
        }
        return last;
    }
}
}
