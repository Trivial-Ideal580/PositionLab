using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PositionLab {
sealed class ScriptProgram {
    internal string Name = "";
    internal int MaxOrders = 12;
    internal bool Pooled;
    internal List<Action<ScriptContext,Series,int>> Run = new List<Action<ScriptContext,Series,int>>();
}
sealed class ScriptContext {
    internal readonly Dictionary<string,Func<Series,int,double>> Parameters = new Dictionary<string,Func<Series,int,double>>(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string,double> Persistent = new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string,double> Bar = new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
    internal readonly List<KeyValuePair<string,double>> Orders = new List<KeyValuePair<string,double>>();
    internal Func<string,Func<Series,int,double>> Resolve {
        get {
            return name => {
                if (Bar.ContainsKey(name)) return (s, i) => Bar[name];
                if (Persistent.ContainsKey(name)) return (s, i) => Persistent[name];
                Func<Series,int,double> parameter;
                return Parameters.TryGetValue(name, out parameter) ? parameter : null;
            };
        }
    }
    internal bool IsDynamic(string name) { return Bar.ContainsKey(name) || Persistent.ContainsKey(name); }
    internal void ResetBar() { Bar.Clear(); }
}
static class Script {
    internal static bool Truthy(double value) { return !double.IsNaN(value) && value > 0.5; }
    internal static ScriptProgram Parse(string source) { return Parse(source, new ScriptContext()); }
    internal static ScriptProgram Parse(string source, ScriptContext context) {
        var program = new ScriptProgram { Name = "脚本策略" };
        var frames = new Stack<Frame>();
        var root = new List<Action<ScriptContext,Series,int>>();
        frames.Push(new Frame { Kind = "root" });
        foreach (string raw in source.Replace("\r\n", "\n").Split('\n')) {
            string line = raw.Trim();
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash).Trim();
            if (line.Length == 0) continue;
            string upper = line.ToUpperInvariant();
            if (upper.StartsWith("NAME=")) { program.Name = line.Substring(5).Trim(); continue; }
            if (upper.StartsWith("PARAM ")) { DefineParameter(context, line.Substring(6).Trim()); continue; }
            if (upper.StartsWith("VAR ")) { DefineState(context, line.Substring(4).Trim()); continue; }
            if (upper.StartsWith("INIT")) {
                int split = line.IndexOf('=');
                if (split > 0 && line.Substring(4, split - 4).Trim().Equals("pool", StringComparison.OrdinalIgnoreCase))
                    program.Pooled = line.Substring(split + 1).Trim() == "1";
                continue;
            }
            if (upper == "ON BAR" || upper.StartsWith("ON BAR ")) {
                frames.Push(new Frame { Kind = "onbar" });
                continue;
            }
            if (upper.StartsWith("IF ") || upper.StartsWith("IF(")) {
                string condition = line.Substring(2).Trim();
                if (!condition.ToUpperInvariant().EndsWith("THEN")) throw new FormatException("IF 语句须以 THEN 结尾：" + line);
                condition = condition.Substring(0, condition.Length - 4).Trim();
                frames.Push(new Frame { Kind = "if", Condition = Rule.Expression(condition, context.Resolve, context.IsDynamic) });
                continue;
            }
            if (upper == "ELSE") {
                if (frames.Peek().Kind != "if") throw new FormatException("ELSE 没有对应的 IF。");
                frames.Peek().InElse = true;
                continue;
            }
            if (upper.StartsWith("FOR ")) {
                frames.Push(ParseFor(line.Substring(4).Trim(), context));
                continue;
            }
            if (upper == "END") { Close(frames, root); continue; }
            if (upper.StartsWith("LET ")) {
                Assign(line.Substring(4).Trim(), frames.Peek().Target, context, false);
                continue;
            }
            if (upper.StartsWith("SET ")) {
                Assign(line.Substring(4).Trim(), frames.Peek().Target, context, true);
                continue;
            }
            if (upper.StartsWith("BUY ") || upper.StartsWith("SELL ")) {
                string side = upper.StartsWith("BUY ") ? "买入" : "卖出";
                var amount = Rule.Expression(line.Substring(side == "买入" ? 4 : 5).Trim(), context.Resolve, context.IsDynamic);
                var target = frames.Peek().Target;
                target.Add((ctx, s, i) => {
                    double fraction = amount(s, i);
                    if (double.IsNaN(fraction)) return;
                    fraction = Math.Max(0, Math.Min(1, fraction));
                    if (fraction > 0) ctx.Orders.Add(new KeyValuePair<string,double>(side, fraction));
                });
                continue;
            }
            throw new FormatException("无法识别的语句：" + line);
        }
        if (frames.Count != 1) throw new FormatException("有 " + (frames.Count - 1) + " 个语句块没有 END 结束。");
        program.Run = frames.Peek().Body;
        return program;
    }
    sealed class Frame {
        internal string Kind = "";
        internal Func<Series,int,double> Condition;
        internal string Variable = "";
        internal Func<Series,int,double> From, To, Step;
        internal readonly List<Action<ScriptContext,Series,int>> Body = new List<Action<ScriptContext,Series,int>>();
        internal readonly List<Action<ScriptContext,Series,int>> Else = new List<Action<ScriptContext,Series,int>>();
        internal bool InElse;
        internal List<Action<ScriptContext,Series,int>> Target { get { return InElse ? Else : Body; } }
    }
    static void Close(Stack<Frame> frames, List<Action<ScriptContext,Series,int>> root) {
        if (frames.Count <= 1) throw new FormatException("END 过多。");
        var frame = frames.Pop();
        var parent = frames.Peek().Target;
        if (frame.Kind == "onbar") {
            foreach (var statement in frame.Body) parent.Add(statement);
            return;
        }
        if (frame.Kind == "if") {
            var condition = frame.Condition; var body = frame.Body; var other = frame.Else;
            parent.Add((ctx, s, i) => {
                var branch = Truthy(condition(s, i)) ? body : other;
                foreach (var statement in branch) statement(ctx, s, i);
            });
            return;
        }
        if (frame.Kind == "for") {
            var variable = frame.Variable; var from = frame.From; var to = frame.To; var step = frame.Step; var body = frame.Body;
            parent.Add((ctx, s, i) => {
                double a = Math.Round(from(s, i)), b = Math.Round(to(s, i)), d = step == null ? 1 : Math.Round(step(s, i));
                if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(d) || d == 0) return;
                int iterations = 0;
                for (double k = a; d > 0 ? k <= b : k >= b; k += d) {
                    if (++iterations > 200) throw new FormatException("FOR 循环超过 200 次，请检查边界。");
                    ctx.Bar[variable] = k;
                    foreach (var statement in body) statement(ctx, s, i);
                }
                ctx.Bar.Remove(variable);
            });
        }
    }
    static Frame ParseFor(string text, ScriptContext context) {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+?)\s+TO\s+(.+?)(?:\s+STEP\s+(.+))?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) throw new FormatException("FOR 语句格式为 FOR i = 起始 TO 结束 [STEP 步长]。" + text);
        var frame = new Frame { Kind = "for", Variable = match.Groups[1].Value };
        frame.From = Rule.Expression(match.Groups[2].Value, context.Resolve, context.IsDynamic);
        frame.To = Rule.Expression(match.Groups[3].Value, context.Resolve, context.IsDynamic);
        if (match.Groups[4].Success) frame.Step = Rule.Expression(match.Groups[4].Value, context.Resolve, context.IsDynamic);
        return frame;
    }
    static void Assign(string text, List<Action<ScriptContext,Series,int>> target, ScriptContext context, bool persistent) {
        int split = text.IndexOf('=');
        if (split <= 0) throw new FormatException("赋值语句格式为 LET/SET 名称 = 表达式：" + text);
        string name = text.Substring(0, split).Trim();
        string expression = text.Substring(split + 1).Trim();
        var value = Rule.Expression(expression, context.Resolve, context.IsDynamic);
        if (persistent) {
            if (!context.Persistent.ContainsKey(name)) context.Persistent[name] = 0;
            target.Add((ctx, s, i) => { ctx.Persistent[name] = value(s, i); });
        } else {
            if (!context.Bar.ContainsKey(name)) context.Bar[name] = 0;
            target.Add((ctx, s, i) => { ctx.Bar[name] = value(s, i); });
        }
    }
    static void DefineParameter(ScriptContext context, string text) {
        int split = text.IndexOf('=');
        if (split <= 0) throw new FormatException("PARAM 格式为 PARAM 名称=数值。");
        string name = text.Substring(0, split).Trim();
        double value = Data.Number(text.Substring(split + 1).Trim(), -1e6, 1e6);
        context.Parameters[name] = (s, i) => value;
    }
    static void DefineState(ScriptContext context, string text) {
        int split = text.IndexOf('=');
        if (split <= 0) throw new FormatException("VAR 格式为 VAR 名称=初始值。");
        string name = text.Substring(0, split).Trim();
        double value = Data.Number(text.Substring(split + 1).Trim(), -1e12, 1e12);
        context.Persistent[name] = value;
    }
}
sealed class ScriptPortfolio {
    internal static PortfolioBacktest Run(Dictionary<string,Series> market, string[] symbols, DateTime from, DateTime to, double initial, double fee, double slip, string source, bool pooled) {
        if (pooled) return Pooled(market, symbols, from, to, initial, fee, slip, source);
        double slice = initial / symbols.Length;
        var runs = new List<KeyValuePair<string,Backtest>>();
        foreach (string symbol in symbols) {
            var run = ScriptBacktest.Run(market[symbol], from, to, slice, fee, slip, source);
            runs.Add(new KeyValuePair<string,Backtest>(symbol, run));
        }
        return Combine(runs, initial);
    }
    internal static PortfolioBacktest Combine(List<KeyValuePair<string,Backtest>> runs, double initial) {
        var result = new PortfolioBacktest { Initial = initial, Requested = runs.Count, Used = runs.Count };
        double slice = initial / runs.Count;
        foreach (var pair in runs) {
            var run = pair.Value;
            result.Symbols.Add(new SymbolResult { Symbol = pair.Key, Initial = slice, Final = run.Equity[run.Equity.Length - 1],
                Total = run.Total, Annual = run.Annual, MaxDrawdown = run.MaxDrawdown, Sharpe = run.Sharpe, Fills = run.Fills.Count,
                Exposure = (double)run.Exposure / run.Dates.Length, WinRate = PortfolioBacktest.WinRateOf(run.Fills) });
            result.Trades.AddRange(run.Fills);
        }
        var days = runs.SelectMany(r => r.Value.Dates).Distinct().OrderBy(d => d).ToArray();
        result.Dates = days;
        result.Equity = new double[days.Length];
        result.Benchmark = new double[days.Length];
        var cursors = new int[runs.Count];
        var equity = new double[runs.Count];
        var benchmark = new double[runs.Count];
        for (int k = 0; k < runs.Count; k++) { equity[k] = slice; benchmark[k] = slice; }
        for (int d = 0; d < days.Length; d++) {
            double sum = 0, bench = 0;
            for (int k = 0; k < runs.Count; k++) {
                var run = runs[k].Value;
                while (cursors[k] < run.Dates.Length && run.Dates[cursors[k]] == days[d]) { equity[k] = run.Equity[cursors[k]]; benchmark[k] = run.Benchmark[cursors[k]]; cursors[k]++; }
                sum += equity[k]; bench += benchmark[k];
            }
            result.Equity[d] = sum; result.Benchmark[d] = bench;
        }
        result.Fills = result.Trades.Count;
        result.Total = result.Equity[days.Length - 1] / initial - 1;
        result.Annual = Math.Pow(result.Equity[days.Length - 1] / initial, 252.0 / Math.Max(1, days.Length - 1)) - 1;
        result.MaxDrawdown = Stats.Drawdown(new[] { initial }.Concat(result.Equity));
        result.WinRate = PortfolioBacktest.WinRateOf(result.Trades);
        result.Trades.Sort((a, b) => a.Date != b.Date ? a.Date.CompareTo(b.Date) : string.CompareOrdinal(a.Symbol, b.Symbol));
        if (days.Length > 1) {
            var returns = new double[days.Length - 1];
            for (int i = 1; i < days.Length; i++) returns[i - 1] = result.Equity[i] / result.Equity[i - 1] - 1;
            double deviation = Stats.Std(returns);
            result.Sharpe = deviation > 0 ? Math.Sqrt(252) * returns.Average() / deviation : double.NaN;
        }
        int exposed = 0;
        for (int d = 0; d < days.Length; d++) { if (runs.Any(r => r.Value.Dates.Contains(days[d]))) exposed++; }
        result.Exposure = (double)exposed / days.Length;
        return result;
    }
    internal static PortfolioBacktest Pooled(Dictionary<string,Series> market, string[] symbols, DateTime from, DateTime to, double initial, double fee, double slip, string source) {
        var result = new PortfolioBacktest { Initial = initial, Requested = symbols.Length, Used = symbols.Length };
        var contexts = symbols.ToDictionary(s => s, s => new ScriptContext());
        var programs = symbols.ToDictionary(s => s, s => Script.Parse(source, contexts[s]));
        var bars = symbols.ToDictionary(s => s, s => market[s].Bars);
        var indices = new Dictionary<string,int>();
        var units = new Dictionary<string,double>();
        var basis = new Dictionary<string,double>();
        var lastClose = new Dictionary<string,double>();
        var pending = new Dictionary<string,List<KeyValuePair<string,double>>>();
        foreach (string symbol in symbols) {
            int start = Array.FindIndex(bars[symbol], b => b.Date >= from);
            indices[symbol] = start < 0 ? -1 : start;
            units[symbol] = 0; basis[symbol] = 0; pending[symbol] = new List<KeyValuePair<string,double>>();
            if (start >= 0) lastClose[symbol] = bars[symbol][start].Close;
        }
        var days = symbols.SelectMany(s => bars[s].Where(b => b.Date >= from && b.Date <= to).Select(b => b.Date)).Distinct().OrderBy(d => d).ToArray();
        if (days.Length < 2) throw new FormatException("区间至少需要两个交易日。");
        double cash = initial;
        int capacity = programs[symbols[0]].MaxOrders;
        int orders = 0;
        var firstClose = symbols.ToDictionary(s => s, s => lastClose[s]);
        for (int d = 0; d < days.Length; d++) {
            DateTime day = days[d];
            foreach (string symbol in symbols) {
                int index = indices[symbol];
                if (index < 0 || index >= bars[symbol].Length || bars[symbol][index].Date != day) continue;
                Bar bar = bars[symbol][index];
                foreach (var order in pending[symbol]) {
                    if (++orders > capacity) break;
                    double price = order.Key == "买入" ? bar.Open * (1 + slip) : bar.Open * (1 - slip);
                    if (order.Key == "买入") {
                        double amount = cash * order.Value;
                        double quantity = Math.Floor(amount / (price * (1 + fee)));
                        if (quantity <= 0) continue;
                        double charge = price * quantity * fee, total = price * quantity + charge;
                        if (total > cash) continue;
                        cash -= total; units[symbol] += quantity; basis[symbol] += total;
                        result.Trades.Add(new Fill { Symbol = symbol, Date = bar.Date, Signal = bars[symbol][index - 1].Date, Side = "买入", Price = price, Quantity = quantity, Fee = charge, Pnl = double.NaN });
                    } else {
                        double quantity = Math.Floor(units[symbol] * order.Value);
                        if (quantity <= 0) continue;
                        double charge = price * quantity * fee, net = price * quantity - charge;
                        double share = basis[symbol] * (quantity / units[symbol]);
                        cash += net; units[symbol] -= quantity; basis[symbol] -= share;
                        result.Trades.Add(new Fill { Symbol = symbol, Date = bar.Date, Signal = bars[symbol][index - 1].Date, Side = "卖出", Price = price, Quantity = quantity, Fee = charge, Pnl = net - share });
                        if (units[symbol] <= 1e-9) { units[symbol] = 0; basis[symbol] = 0; }
                    }
                }
                pending[symbol].Clear();
                lastClose[symbol] = bar.Close;
            }
            double equity = cash;
            double benchmark = 0;
            foreach (string symbol in symbols) {
                equity += units[symbol] * lastClose[symbol];
                benchmark += (initial / symbols.Length) * lastClose[symbol] / firstClose[symbol];
            }
            result.Equity = result.Equity.Concat(new[] { equity }).ToArray();
            result.Benchmark = result.Benchmark.Concat(new[] { benchmark }).ToArray();
            result.Dates = result.Dates.Concat(new[] { day }).ToArray();
            if (d == days.Length - 1) break;
            foreach (string symbol in symbols) {
                int index = indices[symbol];
                if (index < 0 || index >= bars[symbol].Length - 1 || bars[symbol][index].Date != day) continue;
                var context = contexts[symbol];
                context.ResetBar();
                foreach (var statement in programs[symbol].Run) statement(context, market[symbol], index);
                pending[symbol].AddRange(context.Orders);
                context.Orders.Clear();
                indices[symbol] = index + 1;
            }
        }
        foreach (string symbol in symbols) {
            result.Symbols.Add(new SymbolResult { Symbol = symbol, Initial = initial / symbols.Length, Final = units[symbol] * lastClose[symbol],
                Total = 0, Annual = double.NaN, MaxDrawdown = double.NaN, Sharpe = double.NaN, Fills = result.Trades.Count(f => f.Symbol == symbol),
                Exposure = 0, WinRate = PortfolioBacktest.WinRateOf(result.Trades.Where(f => f.Symbol == symbol).ToList()), Message = null });
        }
        result.Trades.Sort((a, b) => a.Date != b.Date ? a.Date.CompareTo(b.Date) : string.CompareOrdinal(a.Symbol, b.Symbol));
        result.Fills = result.Trades.Count;
        result.Total = result.Equity[result.Equity.Length - 1] / initial - 1;
        result.Annual = Math.Pow(result.Equity[result.Equity.Length - 1] / initial, 252.0 / Math.Max(1, result.Equity.Length - 1)) - 1;
        result.MaxDrawdown = Stats.Drawdown(new[] { initial }.Concat(result.Equity));
        result.WinRate = PortfolioBacktest.WinRateOf(result.Trades);
        var returns = new double[result.Equity.Length - 1];
        for (int i = 1; i < result.Equity.Length; i++) returns[i - 1] = result.Equity[i] / result.Equity[i - 1] - 1;
        double sd = Stats.Std(returns);
        result.Sharpe = sd > 0 ? Math.Sqrt(252) * returns.Average() / sd : double.NaN;
        result.Exposure = 0.5;
        return result;
    }
}
sealed class ScriptBacktest {
    internal static Backtest Run(Series series, DateTime from, DateTime to, double initial, double fee, double slip, string source) {
        int start = Array.FindIndex(series.Bars, b => b.Date >= from);
        int end = Array.FindLastIndex(series.Bars, b => b.Date <= to);
        if (start < 0 || end - start < 1) throw new FormatException("区间至少需要两个交易日。");
        var context = new ScriptContext();
        var program = Script.Parse(source, context);
        var result = new Backtest { Initial = initial, Dates = new DateTime[end - start + 1], Equity = new double[end - start + 1],
            Benchmark = new double[end - start + 1], Invested = new double[end - start + 1] };
        double cash = initial, units = 0, basis = 0;
        var pending = new List<KeyValuePair<string,double>>();
        int orders = 0;
        for (int i = start; i <= end; i++) {
            Bar bar = series.Bars[i]; int j = i - start;
            foreach (var order in pending) {
                if (++orders > program.MaxOrders) break;
                double price = order.Key == "买入" ? bar.Open * (1 + slip) : bar.Open * (1 - slip);
                if (order.Key == "买入") {
                    double amount = cash * order.Value;
                    double quantity = Math.Floor(amount / (price * (1 + fee)));
                    if (quantity <= 0) continue;
                    double charge = price * quantity * fee;
                    double total = price * quantity + charge;
                    if (total > cash) continue;
                    cash -= total; units += quantity; basis += total;
                    result.Fills.Add(new Fill { Symbol = series.Symbol, Date = bar.Date, Signal = series.Bars[i - 1].Date, Side = "买入",
                        Price = price, Quantity = quantity, Fee = charge, Pnl = double.NaN });
                } else {
                    double quantity = Math.Floor(units * order.Value);
                    if (quantity <= 0) quantity = order.Value >= 1 ? units : 0;
                    if (quantity <= 0) continue;
                    double charge = price * quantity * fee;
                    double net = price * quantity - charge;
                    double share = basis * (quantity / units);
                    cash += net; units -= quantity; basis -= share;
                    result.Fills.Add(new Fill { Symbol = series.Symbol, Date = bar.Date, Signal = series.Bars[i - 1].Date, Side = "卖出",
                        Price = price, Quantity = quantity, Fee = charge, Pnl = net - share });
                    if (units <= 1e-9) { units = 0; basis = 0; }
                }
            }
            pending.Clear();
            result.Dates[j] = bar.Date;
            result.Equity[j] = cash + units * bar.Close;
            result.Invested[j] = units * bar.Close;
            result.Benchmark[j] = initial * bar.Close / series.Bars[start].Close;
            if (units > 0) result.Exposure++;
            if (i == end) break;
            context.ResetBar();
            foreach (var statement in program.Run) statement(context, series, i);
            pending.AddRange(context.Orders);
            context.Orders.Clear();
        }
        return result;
    }
}
}
