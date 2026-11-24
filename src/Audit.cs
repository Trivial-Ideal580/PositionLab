using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PositionLab {
sealed class AuditCheck {
    internal string Name, Detail;
    internal string Level = "通过";
}
sealed class AuditResult {
    internal string RuleName = "", Buy = "", Sell = "", DataSource = "";
    internal DateTime From, To, Generated = DateTime.Now;
    internal string[] Symbols = new string[0];
    internal double Capital, Fee, Slip;
    internal PortfolioBacktest Backtest;
    internal RuleInfo Rule = new RuleInfo();
    internal ImportReport Import;
    internal readonly List<AuditCheck> Checks = new List<AuditCheck>();
    internal readonly List<string> Notes = new List<string>();
    internal int Failures { get { return Checks.Count(c => c.Level == "失败"); } }
    internal int Warnings { get { return Checks.Count(c => c.Level == "警告"); } }
    internal string Verdict { get { return Failures > 0 ? "失败" : Warnings > 0 ? "警告" : "通过"; } }
}
static class Audit {
    internal static AuditResult Run(Dictionary<string,Series> market, string[] symbols, Rule rule, DateTime from, DateTime to, double capital, double fee, double slip, ImportReport import) {
        if (symbols.Length == 0) throw new FormatException("至少需要一个标的。");
        if (from >= to) throw new FormatException("审计区间至少需要两个交易日。");
        Rule.Compile(rule.Buy); Rule.Compile(rule.Sell);
        var result = new AuditResult {
            RuleName = rule.Name, Buy = rule.Buy, Sell = rule.Sell, From = from, To = to, Symbols = symbols,
            Capital = capital, Fee = fee, Slip = slip, Import = import,
            Rule = Rule.Inspect(rule.Buy + " " + rule.Sell),
            DataSource = import == null ? "" : import.Summary
        };
        var backtest = PortfolioBacktest.Run(market, symbols, from, to, capital, fee, slip, rule);
        result.Backtest = backtest;

        result.Checks.Add(new AuditCheck { Name = "规则可解析", Detail = "买入与卖出表达式均通过解析与静态审计。" });
        result.Checks.Add(new AuditCheck {
            Name = "无未来引用",
            Level = result.Rule.MaxLag > 252 ? "失败" : "通过",
            Detail = "最大回看 " + result.Rule.MaxLag + " 个交易日；负向回看与未知函数在解析期即被拒绝。"
        });

        int bars = symbols.Sum(s => market[s].Bars.Length);
        result.Checks.Add(new AuditCheck {
            Name = "样本长度",
            Level = bars >= 120 ? "通过" : "警告",
            Detail = "区间内合计 " + bars + " 根 K 线（" + symbols.Length + " 个标的）。少于 120 根时统计量不稳定。"
        });

        int gaps = symbols.Sum(s => market[s].Gaps);
        result.Checks.Add(new AuditCheck {
            Name = "数据连续性",
            Level = gaps == 0 ? "通过" : "警告",
            Detail = gaps == 0 ? "未发现超过 10 个自然日的缺口。" : ("发现 " + gaps + " 处超过 10 个自然日的缺口，缺口处收益会被放大。")
        });

        if (backtest.Fills == 0) result.Checks.Add(new AuditCheck { Name = "策略触发", Level = "失败", Detail = "区间内没有任何成交，规则可能过严或指标缺失。" });
        else if (backtest.Fills < 5) result.Checks.Add(new AuditCheck { Name = "策略触发", Level = "警告", Detail = "仅 " + backtest.Fills + " 笔成交，样本过少，结论不可靠。" });
        else result.Checks.Add(new AuditCheck { Name = "策略触发", Level = "通过", Detail = "共 " + backtest.Fills + " 笔成交，可计算胜率与换手。" });

        // 截断不变性：未来数据不应改变历史成交与净值
        var span = (to - from).TotalDays;
        var cut = from.AddDays(span * 0.7);
        if (cut > from && market[symbols[0]].Bars.Count(b => b.Date >= from && b.Date <= cut) >= 3) {
            var prefix = PortfolioBacktest.Run(market, symbols, from, cut, capital, fee, slip, rule);
            var lookup = new Dictionary<DateTime, double>();
            for (int i = 0; i < backtest.Dates.Length; i++) lookup[backtest.Dates[i]] = backtest.Equity[i];
            double worst = 0;
            int compared = 0;
            for (int i = 0; i < prefix.Dates.Length; i++) {
                double other;
                if (!lookup.TryGetValue(prefix.Dates[i], out other)) continue;
                compared++;
                worst = Math.Max(worst, Math.Abs(prefix.Equity[i] - other) / Math.Max(1, Math.Abs(other)));
            }
            result.Checks.Add(new AuditCheck {
                Name = "截断不变性",
                Level = compared > 0 && worst < 1e-9 ? "通过" : "失败",
                Detail = compared == 0 ? "无法比较（区间过短）。" : ("截断到 " + Data.Day(cut) + " 后重跑，重叠区 " + compared + " 天净值最大偏差 " + worst.ToString("0.###E+0", CultureInfo.InvariantCulture) + "。")
            });
        } else {
            result.Checks.Add(new AuditCheck { Name = "截断不变性", Level = "警告", Detail = "区间过短，无法做截断对照。" });
        }

        var zero = PortfolioBacktest.Run(market, symbols, from, to, capital, 0, 0, rule);
        var heavy = PortfolioBacktest.Run(market, symbols, from, to, capital, fee * 2, slip * 2, rule);
        bool fragile = backtest.Total > 0 && heavy.Total <= 0 && zero.Total > 0;
        result.Checks.Add(new AuditCheck {
            Name = "成本敏感性",
            Level = fragile ? "警告" : "通过",
            Detail = "零成本收益 " + Pct(zero.Total) + "，当前参数 " + Pct(backtest.Total) + "，双倍成本 " + Pct(heavy.Total) + "。"
        });

        if (backtest.Total < backtest.Benchmark[backtest.Benchmark.Length - 1] / backtest.Initial - 1 - 1e-12)
            result.Notes.Add("区间收益低于等权买入持有基准，策略未跑赢被动持有。");
        if (backtest.WinRate < 0.4 && backtest.Fills >= 10)
            result.Notes.Add("平仓胜率 " + Pct(backtest.WinRate) + " 偏低，收益可能依赖少数大赚交易。");
        if (result.Rule.Functions.Length > 0)
            result.Notes.Add("规则使用了函数：" + string.Join("、", result.Rule.Functions) + "。");
        return result;
    }
    internal static string Write(AuditResult result, string folder) {
        Directory.CreateDirectory(folder);
        string markdown = Markdown(result);
        string mdPath = Path.Combine(folder, "audit.md");
        File.WriteAllText(mdPath, markdown, new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(folder, "audit.html"), Html(result, markdown), new UTF8Encoding(true));
        var summary = new StringBuilder();
        summary.AppendLine("结论=" + result.Verdict);
        summary.AppendLine("失败=" + result.Failures);
        summary.AppendLine("警告=" + result.Warnings);
        foreach (var check in result.Checks) summary.AppendLine(check.Level + "\t" + check.Name + "\t" + check.Detail);
        File.WriteAllText(Path.Combine(folder, "audit-summary.txt"), summary.ToString(), new UTF8Encoding(true));
        var curve = new StringBuilder();
        curve.AppendLine("date,strategy_equity,benchmark_equity");
        for (int i = 0; i < result.Backtest.Dates.Length; i++)
            curve.AppendLine(string.Format(Data.Inv, "{0},{1:R},{2:R}", Data.Day(result.Backtest.Dates[i]), result.Backtest.Equity[i], result.Backtest.Benchmark[i]));
        File.WriteAllText(Path.Combine(folder, "audit-equity.csv"), curve.ToString(), new UTF8Encoding(true));
        return mdPath;
    }
    internal static string Pct(double value) {
        return double.IsNaN(value) || double.IsInfinity(value) ? "—" : (100 * value).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }
    static string Number(double value, string format) {
        return double.IsNaN(value) || double.IsInfinity(value) ? "—" : value.ToString(format, CultureInfo.InvariantCulture);
    }
    static string Markdown(AuditResult result) {
        var text = new StringBuilder();
        var backtest = result.Backtest;
        text.AppendLine("# 策略审计报告 · " + result.RuleName);
        text.AppendLine();
        text.AppendLine("- 结论：**" + result.Verdict + "**（失败 " + result.Failures + " 项，警告 " + result.Warnings + " 项）");
        text.AppendLine("- 生成时间：" + result.Generated.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        text.AppendLine("- 区间：" + Data.Day(result.From) + " ~ " + Data.Day(result.To) + "（" + backtest.Dates.Length + " 个交易日）");
        text.AppendLine("- 标的：" + result.Symbols.Length + " 个（等权）· " + string.Join("、", result.Symbols.Take(12)) + (result.Symbols.Length > 12 ? " …" : ""));
        text.AppendLine("- 资金：" + Number(result.Capital, "N0") + " · 费用：" + Number(result.Fee * 10000, "0.####") + " bp · 滑点：" + Number(result.Slip * 10000, "0.####") + " bp");
        if (result.DataSource.Length > 0) text.AppendLine("- 数据：" + result.DataSource);
        text.AppendLine();
        text.AppendLine("## 规则");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine("BUY=" + result.Buy);
        text.AppendLine("SELL=" + result.Sell);
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("引用字段：" + (result.Rule.Fields.Length == 0 ? "—" : string.Join("、", result.Rule.Fields)));
        text.AppendLine();
        text.AppendLine("使用函数：" + (result.Rule.Functions.Length == 0 ? "—" : string.Join("、", result.Rule.Functions)) + " · 最大回看：" + result.Rule.MaxLag + " 日");
        text.AppendLine();
        text.AppendLine("## 绩效");
        text.AppendLine();
        text.AppendLine("| 指标 | 数值 |");
        text.AppendLine("| --- | --- |");
        text.AppendLine("| 策略收益 | " + Pct(backtest.Total) + " |");
        text.AppendLine("| 年化收益 | " + Pct(backtest.Annual) + " |");
        text.AppendLine("| 最大回撤 | " + Pct(backtest.MaxDrawdown) + " |");
        text.AppendLine("| 夏普（无风险 0） | " + Number(backtest.Sharpe, "F2") + " |");
        text.AppendLine("| 基准收益 | " + Pct(backtest.Benchmark[backtest.Benchmark.Length - 1] / backtest.Initial - 1) + " |");
        text.AppendLine("| 成交笔数 | " + backtest.Fills + " |");
        text.AppendLine("| 平仓胜率 | " + Pct(backtest.WinRate) + " |");
        text.AppendLine("| 持仓日占比 | " + Pct(backtest.Exposure) + " |");
        text.AppendLine();
        text.AppendLine("## 检查项");
        text.AppendLine();
        text.AppendLine("| 检查 | 结论 | 说明 |");
        text.AppendLine("| --- | --- | --- |");
        foreach (var check in result.Checks) text.AppendLine("| " + check.Name + " | " + check.Level + " | " + check.Detail.Replace("|", "/") + " |");
        text.AppendLine();
        text.AppendLine("## 分标的明细");
        text.AppendLine();
        text.AppendLine("| 标的 | 初始 | 期末 | 收益 | 最大回撤 | 夏普 | 成交 | 胜率 |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in backtest.Symbols) {
            if (row.Failed) { text.AppendLine("| " + row.Symbol + " | — | — | — | — | — | — | " + row.Message + " |"); continue; }
            text.AppendLine("| " + row.Symbol + " | " + Number(row.Initial, "N0") + " | " + Number(row.Final, "N0") + " | " + Pct(row.Total) + " | " +
                Pct(row.MaxDrawdown) + " | " + Number(row.Sharpe, "F2") + " | " + row.Fills + " | " + Pct(row.WinRate) + " |");
        }
        text.AppendLine();
        if (result.Notes.Count > 0) {
            text.AppendLine("## 观察");
            text.AppendLine();
            foreach (string note in result.Notes) text.AppendLine("- " + note);
            text.AppendLine();
        }
        text.AppendLine("## 说明");
        text.AppendLine();
        text.AppendLine("本报告只检查规则与数据的工程一致性：信号在收盘判定、次日开盘成交、不使用未来数据、成本与断点可复现。它不验证策略是否有经济逻辑，也不构成投资建议。历史表现不代表未来收益。");
        return text.ToString();
    }
    static string Html(AuditResult result, string markdown) {
        var text = new StringBuilder();
        text.AppendLine("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
        text.AppendLine("<title>策略审计报告 · " + Escape(result.RuleName) + "</title>");
        text.AppendLine("<style>body{font-family:'Microsoft YaHei UI',system-ui,sans-serif;margin:0;background:#f1f4f9;color:#172844}" +
            "main{max-width:960px;margin:0 auto;padding:28px 32px 48px;background:#fff}" +
            "h1{font-size:24px;margin:0 0 6px}h2{font-size:17px;margin:26px 0 10px;border-bottom:1px solid #dce4ef;padding-bottom:6px}" +
            ".tag{display:inline-block;padding:3px 10px;border-radius:11px;font-size:13px;color:#fff;background:#087b78}" +
            ".tag.warn{background:#956019}.tag.fail{background:#bb4d51}" +
            "table{border-collapse:collapse;width:100%;font-size:13px}th,td{border-bottom:1px solid #e6edf5;padding:7px 9px;text-align:left}" +
            "th{color:#506584;font-weight:600}pre{background:#f5f7fb;padding:10px 12px;border-radius:6px;overflow:auto;font-size:13px}" +
            ".muted{color:#506584;font-size:13px}li{margin:4px 0}</style></head><body><main>");
        var level = result.Verdict == "通过" ? "" : result.Verdict == "警告" ? " warn" : " fail";
        text.AppendLine("<h1>策略审计报告 · " + Escape(result.RuleName) + "</h1>");
        text.AppendLine("<p class=\"muted\">生成于 " + result.Generated.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " · 区间 " + Data.Day(result.From) + " ~ " + Data.Day(result.To) + " · " + result.Symbols.Length + " 个标的等权</p>");
        text.AppendLine("<p><span class=\"tag" + level + "\">" + result.Verdict + "</span> <span class=\"muted\">失败 " + result.Failures + " 项，警告 " + result.Warnings + " 项</span></p>");
        text.AppendLine(Chart(result));
        text.AppendLine("<h2>规则</h2><pre>BUY=" + Escape(result.Buy) + "\nSELL=" + Escape(result.Sell) + "</pre>");
        text.AppendLine("<p class=\"muted\">字段：" + Escape(result.Rule.Fields.Length == 0 ? "—" : string.Join("、", result.Rule.Fields)) +
            " · 函数：" + Escape(result.Rule.Functions.Length == 0 ? "—" : string.Join("、", result.Rule.Functions)) + " · 最大回看 " + result.Rule.MaxLag + " 日</p>");
        text.AppendLine("<h2>绩效</h2><table><tr><th>指标</th><th>数值</th></tr>");
        var backtest = result.Backtest;
        text.AppendLine(Row("策略收益", Pct(backtest.Total)) + Row("年化收益", Pct(backtest.Annual)) + Row("最大回撤", Pct(backtest.MaxDrawdown)) +
            Row("夏普（无风险 0）", Number(backtest.Sharpe, "F2")) + Row("基准收益", Pct(backtest.Benchmark[backtest.Benchmark.Length - 1] / backtest.Initial - 1)) +
            Row("成交笔数", backtest.Fills.ToString(CultureInfo.InvariantCulture)) + Row("平仓胜率", Pct(backtest.WinRate)) + Row("持仓日占比", Pct(backtest.Exposure)));
        text.AppendLine("</table>");
        text.AppendLine("<h2>检查项</h2><table><tr><th>检查</th><th>结论</th><th>说明</th></tr>");
        foreach (var check in result.Checks) text.AppendLine("<tr><td>" + Escape(check.Name) + "</td><td>" + Escape(check.Level) + "</td><td>" + Escape(check.Detail) + "</td></tr>");
        text.AppendLine("</table>");
        text.AppendLine("<h2>分标的明细</h2><table><tr><th>标的</th><th>初始</th><th>期末</th><th>收益</th><th>最大回撤</th><th>夏普</th><th>成交</th><th>胜率</th></tr>");
        foreach (var row in backtest.Symbols) {
            if (row.Failed) { text.AppendLine("<tr><td>" + Escape(row.Symbol) + "</td><td colspan=\"7\">" + Escape(row.Message) + "</td></tr>"); continue; }
            text.AppendLine("<tr><td>" + Escape(row.Symbol) + "</td><td>" + Number(row.Initial, "N0") + "</td><td>" + Number(row.Final, "N0") + "</td><td>" +
                Pct(row.Total) + "</td><td>" + Pct(row.MaxDrawdown) + "</td><td>" + Number(row.Sharpe, "F2") + "</td><td>" + row.Fills + "</td><td>" + Pct(row.WinRate) + "</td></tr>");
        }
        text.AppendLine("</table>");
        if (result.Notes.Count > 0) {
            text.AppendLine("<h2>观察</h2><ul>");
            foreach (string note in result.Notes) text.AppendLine("<li>" + Escape(note) + "</li>");
            text.AppendLine("</ul>");
        }
        if (result.DataSource.Length > 0) text.AppendLine("<h2>数据</h2><p class=\"muted\">" + Escape(result.DataSource) + "</p>");
        text.AppendLine("<h2>说明</h2><p class=\"muted\">本报告只检查规则与数据的工程一致性：信号在收盘判定、次日开盘成交、不使用未来数据、成本与断点可复现。它不验证策略是否有经济逻辑，也不构成投资建议。</p>");
        text.AppendLine("</main></body></html>");
        return text.ToString();
    }
    static string Row(string name, string value) { return "<tr><td>" + Escape(name) + "</td><td>" + Escape(value) + "</td></tr>"; }
    static string Chart(AuditResult result) {
        var equity = result.Backtest.Equity;
        var benchmark = result.Backtest.Benchmark;
        if (equity.Length < 2) return "";
        double low = Math.Min(equity.Min(), benchmark.Min()), high = Math.Max(equity.Max(), benchmark.Max());
        if (high - low < 1e-9) high = low + 1;
        const double width = 900, height = 220, pad = 34;
        Func<double[], int, string> path = (values, i) => {
            var points = new StringBuilder();
            for (int k = 0; k < values.Length; k++) {
                double x = pad + (width - 2 * pad) * k / (values.Length - 1);
                double y = height - pad - (height - 2 * pad) * (values[k] - low) / (high - low);
                points.Append(k == 0 ? "M" : " L").Append(x.ToString("0.#", CultureInfo.InvariantCulture)).Append(",").Append(y.ToString("0.#", CultureInfo.InvariantCulture));
            }
            return points.ToString();
        };
        var svg = new StringBuilder();
        svg.Append("<svg viewBox=\"0 0 ").Append(width).Append(" ").Append(height).Append("\" width=\"100%\" role=\"img\">");
        svg.Append("<rect width=\"").Append(width).Append("\" height=\"").Append(height).Append("\" fill=\"#ffffff\"/>");
        svg.Append("<path d=\"").Append(path(benchmark, 1)).Append("\" fill=\"none\" stroke=\"#764bb5\" stroke-width=\"1.6\"/>");
        svg.Append("<path d=\"").Append(path(equity, 0)).Append("\" fill=\"none\" stroke=\"#2856d9\" stroke-width=\"2\"/>");
        svg.Append("<text x=\"").Append(pad).Append("\" y=\"").Append(height - 8).Append("\" font-size=\"12\" fill=\"#506584\">").Append(Data.Day(result.From)).Append(" → ").Append(Data.Day(result.To)).Append("</text>");
        svg.Append("<text x=\"").Append(width - 220).Append("\" y=\"20\" font-size=\"12\" fill=\"#2856d9\">策略</text>");
        svg.Append("<text x=\"").Append(width - 160).Append("\" y=\"20\" font-size=\"12\" fill=\"#764bb5\">买入持有</text>");
        svg.Append("</svg>");
        return svg.ToString();
    }
    static string Escape(string text) {
        return (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
}
