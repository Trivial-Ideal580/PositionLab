using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PositionLab {
sealed class StatementMetric {
    internal readonly string Key, Label, Unit;
    internal readonly bool Flow, Percent, Amount;
    internal readonly string[] Aliases;
    internal StatementMetric(string key,string label,string unit,bool flow,bool percent,bool amount,params string[] aliases) {
        Key=key; Label=label; Unit=unit; Flow=flow; Percent=percent; Amount=amount; Aliases=aliases;
    }
}
sealed class StatementSubject {
    internal string Name = "";
    internal StatementMetric Metric;
    internal double[] Values = new double[0], Singles = new double[0], Ttms = new double[0], SingleYoy = new double[0], TtmYoy = new double[0];
    internal bool Mapped { get { return Metric != null; } }
    internal string Key { get { return Metric == null ? "" : Metric.Key; } }
}
sealed class StatementBook {
    internal const int MaxPeriods = 400, MaxSubjects = 400;
    internal string File = "", Symbol = "", Format = "", Sheet = "";
    internal DateTime[] Periods = new DateTime[0], Available = new DateTime[0];
    internal int HeaderRow, RawRows, ExtraCells, DuplicatePeriods, DuplicateMetrics;
    internal readonly List<StatementSubject> Subjects = new List<StatementSubject>();
    internal readonly List<StatementMetric> Metrics = new List<StatementMetric>();
    internal readonly List<string> Notes = new List<string>();
    internal static readonly StatementMetric[] Catalog = {
        new StatementMetric("net_profit","净利润","元",true,false,true,"净利润(元)","净利润","归属母公司所有者的净利润","归属于母公司所有者的净利润","归母净利润","归母净利润(元)"),
        new StatementMetric("net_profit_deducted","扣非净利润","元",true,false,true,"扣非净利润(元)","扣非净利润","扣除非经常性损益后的净利润","扣除非经常性损益的净利润"),
        new StatementMetric("revenue","营业总收入","元",true,false,true,"营业总收入(元)","营业总收入","营业收入(元)","营业收入"),
        new StatementMetric("eps","基本每股收益","元",true,false,false,"基本每股收益(元)","基本每股收益","每股收益(元)","每股收益"),
        new StatementMetric("bps","每股净资产","元",false,false,false,"每股净资产(元)","每股净资产"),
        new StatementMetric("ocf_ps","每股经营现金流","元",true,false,false,"每股经营现金流(元)","每股经营现金流","每股经营性现金流(元)"),
        new StatementMetric("capital_reserve_ps","每股资本公积金","元",false,false,false,"每股资本公积金(元)","每股资本公积金"),
        new StatementMetric("retained_ps","每股未分配利润","元",false,false,false,"每股未分配利润(元)","每股未分配利润"),
        new StatementMetric("gross_margin","销售毛利率","%",false,true,false,"销售毛利率","毛利率"),
        new StatementMetric("net_margin","销售净利率","%",false,true,false,"销售净利率","净利率"),
        new StatementMetric("roe","净资产收益率","%",false,true,false,"净资产收益率","加权净资产收益率"),
        new StatementMetric("roe_diluted","净资产收益率（摊薄）","%",false,true,false,"净资产收益率-摊薄","净资产收益率(摊薄)","净资产收益率—摊薄"),
        new StatementMetric("debt_ratio","资产负债率","%",false,true,false,"资产负债率"),
        new StatementMetric("current_ratio","流动比率","倍",false,false,false,"流动比率"),
        new StatementMetric("quick_ratio","速动比率","倍",false,false,false,"速动比率"),
        new StatementMetric("conservative_quick_ratio","保守速动比率","倍",false,false,false,"保守速动比率"),
        new StatementMetric("equity_ratio","产权比率","倍",false,false,false,"产权比率"),
        new StatementMetric("operating_cycle","营业周期","天",false,false,false,"营业周期(天)","营业周期"),
        new StatementMetric("receivable_days","应收账款周转天数","天",false,false,false,"应收账款周转天数(天)","应收账款周转天数"),
        new StatementMetric("inventory_days","存货周转天数","天",false,false,false,"存货周转天数(天)","存货周转天数"),
        new StatementMetric("net_profit_yoy","净利润同比增长率","%",false,true,false,"净利润同比增长率"),
        new StatementMetric("deducted_yoy","扣非净利润同比增长率","%",false,true,false,"扣非净利润同比增长率"),
        new StatementMetric("revenue_yoy","营业总收入同比增长率","%",false,true,false,"营业总收入同比增长率","营业收入同比增长率")
    };
    internal int Count { get { return Periods.Length; } }
    internal DateTime From { get { return Periods[0]; } }
    internal DateTime To { get { return Periods[Periods.Length - 1]; } }
    internal int MappedCount { get { return Subjects.Count(s => s.Mapped); } }

    internal static StatementBook Load(string path) {
        if (!System.IO.File.Exists(path)) throw new FormatException("文件不存在：" + path);
        var report = new ImportReport();
        var rows = MarketImport.ReadRows(path, report);
        if (rows.Count == 0) throw new FormatException("文件没有可读取的行。");
        var book = new StatementBook { File = System.IO.Path.GetFileName(path), Format = report.Format, Sheet = report.Sheet, RawRows = rows.Count };
        book.Symbol = SymbolOf(path);
        int header = FindHeader(rows);
        if (header < 0) throw new FormatException("找不到财报表头。需要一行同时包含「科目\\时间」与至少两个报告期，或首列科目、其余列为日期。");
        book.HeaderRow = header;
        var head = rows[header];
        var columns = new List<KeyValuePair<int,DateTime>>();
        for (int c = 1; c < head.Length; c++) {
            DateTime day;
            if (MarketImport.TryDate(head[c], out day)) columns.Add(new KeyValuePair<int,DateTime>(c, day));
        }
        if (columns.Count == 0) throw new FormatException("表头里没有可识别的报告期。请检查日期格式（如 2024-03-31）。");
        if (columns.Count > MaxPeriods) throw new FormatException("最多支持 " + MaxPeriods + " 个报告期，当前 " + columns.Count + " 个。");
        var unique = new List<KeyValuePair<int,DateTime>>();
        foreach (var column in columns) {
            if (unique.Any(u => u.Value == column.Value)) { book.DuplicatePeriods++; continue; }
            unique.Add(column);
        }
        if (book.DuplicatePeriods > 0) book.Notes.Add("表头有 " + book.DuplicatePeriods + " 个重复报告期，已保留第一列。");
        unique.Sort((a, b) => a.Value.CompareTo(b.Value));
        int n = unique.Count;
        book.Periods = new DateTime[n];
        book.Available = new DateTime[n];
        for (int i = 0; i < n; i++) { book.Periods[i] = unique[i].Value; book.Available[i] = Disclosure(unique[i].Value); }
        var aliases = new Dictionary<string,StatementMetric>(StringComparer.Ordinal);
        foreach (var metric in Catalog) foreach (string alias in metric.Aliases) {
            string key = MarketImport.Normalize(alias);
            if (key.Length > 0 && !aliases.ContainsKey(key)) aliases.Add(key, metric);
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int r = header + 1; r < rows.Count; r++) {
            var row = rows[r];
            if (row.Length == 0 || row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Length > head.Length && row.Skip(head.Length).Any(c => !string.IsNullOrWhiteSpace(c))) book.ExtraCells++;
            string name = row[0].Trim();
            if (name.Length == 0) continue;
            if (book.Subjects.Count >= MaxSubjects) throw new FormatException("最多支持 " + MaxSubjects + " 个科目。");
            StatementMetric metric;
            aliases.TryGetValue(MarketImport.Normalize(name), out metric);
            if (metric != null && seen.Contains(metric.Key)) {
                book.DuplicateMetrics++;
                book.Notes.Add("科目「" + name + "」与已识别条目重复，已忽略。");
                continue;
            }
            double scale = metric != null && metric.Amount ? ScaleOf(name) : 1;
            var subject = new StatementSubject { Name = name, Metric = metric };
            subject.Values = new double[n];
            int parsed = 0;
            for (int i = 0; i < n; i++) {
                int column = unique[i].Key;
                double value; bool percent;
                if (column >= row.Length || !TryCell(row[column], out value, out percent)) { subject.Values[i] = double.NaN; continue; }
                if (metric != null && metric.Amount && percent) { subject.Values[i] = double.NaN; continue; }
                subject.Values[i] = value * scale;
                parsed++;
            }
            if (parsed == 0) continue;
            if (metric != null) seen.Add(metric.Key);
            book.Subjects.Add(subject);
        }
        if (book.Subjects.Count == 0) throw new FormatException("表头之后没有可解析的科目行。请确认科目在行、报告期在列。");
        foreach (var metric in Catalog) if (book.Subjects.Any(s => s.Mapped && s.Key == metric.Key)) book.Metrics.Add(metric);
        book.Derive();
        if (book.ExtraCells > 0) book.Notes.Add("有 " + book.ExtraCells + " 行在表头宽度之外带多余单元格，已按表头丢弃。");
        if (book.DuplicateMetrics > 0) book.Notes.Add("忽略重复科目 " + book.DuplicateMetrics + " 行。");
        book.Notes.Add("披露日按法定披露期限推算（一季报 4/30、半年报 8/31、三季报 10/31、年报次年 4/30），文件未提供公告日期，界面上标注「按法定期限估计」。");
        return book;
    }
    static int FindHeader(List<string[]> rows) {
        for (int i = 0; i < Math.Min(rows.Count, 20); i++) {
            var row = rows[i];
            bool named = row.Any(c => (c ?? "").Contains("科目"));
            int dates = 0;
            for (int c = 1; c < row.Length; c++) { DateTime day; if (MarketImport.TryDate(row[c], out day)) dates++; }
            if (dates >= 2 && (named || string.IsNullOrWhiteSpace(row[0]) || !LooksNumeric(row[0]))) return i;
        }
        return -1;
    }
    static bool LooksNumeric(string text) {
        double value; bool percent;
        return TryCell(text, out value, out percent);
    }
    internal static DateTime Disclosure(DateTime period) {
        switch (period.Month) {
            case 3: return new DateTime(period.Year, 4, 30);
            case 6: return new DateTime(period.Year, 8, 31);
            case 9: return new DateTime(period.Year, 10, 31);
            case 12: return new DateTime(period.Year + 1, 4, 30);
            default: return period.AddMonths(4);
        }
    }
    static double ScaleOf(string header) {
        if (header.Contains("亿元")) return 1e8;
        if (header.Contains("万元")) return 1e4;
        if (header.Contains("千元")) return 1e3;
        return 1;
    }
    internal static bool TryCell(string text,out double value,out bool percent) {
        value = double.NaN; percent = false;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string trimmed = text.Trim().Replace(",", "").Replace(" ", "").Replace("　", "");
        if (trimmed.Length == 0 || trimmed == "-" || trimmed == "--" || trimmed == "—" || trimmed == "N/A" || trimmed == "NA" || trimmed == "不适用") return false;
        if (trimmed.EndsWith("%")) { percent = true; trimmed = trimmed.Substring(0, trimmed.Length - 1); }
        double scale = 1;
        if (trimmed.EndsWith("万")) { scale = 1e4; trimmed = trimmed.Substring(0, trimmed.Length - 1); }
        else if (trimmed.EndsWith("亿")) { scale = 1e8; trimmed = trimmed.Substring(0, trimmed.Length - 1); }
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        if (double.IsNaN(value) || double.IsInfinity(value)) return false;
        value *= scale;
        return true;
    }
    internal static string SymbolOf(string path) {
        string stem = System.IO.Path.GetFileNameWithoutExtension(path);
        var digits = new StringBuilder();
        foreach (char ch in stem) {
            if (char.IsDigit(ch)) digits.Append(ch);
            else if (digits.Length > 0 && digits.Length < 6) digits.Length = 0;
            if (digits.Length >= 6) break;
        }
        if (digits.Length >= 6) return digits.ToString(0, 6);
        string cleaned = MarketImport.CleanSymbol(stem);
        return cleaned.Length == 0 ? stem.ToUpperInvariant() : cleaned;
    }
    void Derive() {
        var index = new Dictionary<DateTime,int>();
        for (int i = 0; i < Periods.Length; i++) index[Periods[i]] = i;
        foreach (var subject in Subjects) {
            int n = Periods.Length;
            subject.Singles = Enumerable.Repeat(double.NaN, n).ToArray();
            subject.Ttms = Enumerable.Repeat(double.NaN, n).ToArray();
            subject.SingleYoy = Enumerable.Repeat(double.NaN, n).ToArray();
            subject.TtmYoy = Enumerable.Repeat(double.NaN, n).ToArray();
            if (!subject.Mapped) continue;
            if (subject.Metric.Flow) {
                for (int i = 0; i < n; i++) {
                    double cum = subject.Values[i];
                    if (double.IsNaN(cum)) continue;
                    int quarter = QuarterOf(Periods[i]);
                    if (quarter < 0) continue;
                    if (quarter == 1) { subject.Singles[i] = cum; continue; }
                    DateTime priorPeriod = QuarterEnd(Periods[i], -1);
                    int prior;
                    if (!index.TryGetValue(priorPeriod, out prior)) continue;
                    double previous = subject.Values[prior];
                    if (double.IsNaN(previous)) continue;
                    subject.Singles[i] = cum - previous;
                }
                for (int i = 0; i < n; i++) {
                    if (i < 3) continue;
                    if (Periods[i - 3] != QuarterEnd(Periods[i], -3)) continue;
                    double sum = 0; bool complete = true;
                    for (int k = 0; k < 4; k++) {
                        double value = subject.Singles[i - k];
                        if (double.IsNaN(value)) { complete = false; break; }
                        sum += value;
                    }
                    if (complete) subject.Ttms[i] = sum;
                }
            } else {
                for (int i = 0; i < n; i++) subject.Ttms[i] = subject.Values[i];
            }
            for (int i = 0; i < n; i++) {
                int prior;
                if (!index.TryGetValue(Periods[i].AddYears(-1), out prior)) continue;
                subject.SingleYoy[i] = Growth(subject.Singles[i], subject.Singles[prior]);
                subject.TtmYoy[i] = Growth(subject.Ttms[i], subject.Ttms[prior]);
            }
        }
        var unmatched = Subjects.Where(s => !s.Mapped).Select(s => s.Name).Take(4).ToArray();
        if (unmatched.Length > 0) Notes.Add("未识别科目（仅展示原始数值）：" + string.Join("、", unmatched) + (Subjects.Count(s => !s.Mapped) > unmatched.Length ? " 等" : "") + "。");
    }
    static double Growth(double current,double previous) {
        if (double.IsNaN(current) || double.IsNaN(previous) || previous <= 0) return double.NaN;
        return current / previous - 1;
    }
    internal static int QuarterOf(DateTime period) {
        switch (period.Month) {
            case 3: return 1;
            case 6: return 2;
            case 9: return 3;
            case 12: return 4;
            default: return -1;
        }
    }
    internal static DateTime QuarterEnd(DateTime period,int steps) {
        int quarter = QuarterOf(period);
        if (quarter < 0) return default(DateTime);
        int serial = period.Year * 4 + (quarter - 1) + steps;
        if (serial < 1) return default(DateTime);
        int year = serial / 4, index = serial % 4;
        int month = (index + 1) * 3;
        return new DateTime(year, month, DateTime.DaysInMonth(year, month));
    }
    internal StatementSubject Subject(string key) {
        foreach (var subject in Subjects) if (subject.Mapped && subject.Key == key) return subject;
        return null;
    }
    internal bool Has(string key) { return Subject(key) != null; }
    internal double Value(string key,int index) {
        if (index < 0 || index >= Periods.Length) return double.NaN;
        var subject = Subject(key);
        return subject == null ? double.NaN : subject.Values[index];
    }
    internal double Single(string key,int index) {
        if (index < 0 || index >= Periods.Length) return double.NaN;
        var subject = Subject(key);
        return subject == null ? double.NaN : subject.Singles[index];
    }
    internal double Ttm(string key,int index) {
        if (index < 0 || index >= Periods.Length) return double.NaN;
        var subject = Subject(key);
        return subject == null ? double.NaN : subject.Ttms[index];
    }
    internal int IndexAt(DateTime asOf) {
        int lo = 0, hi = Available.Length - 1, found = -1;
        while (lo <= hi) {
            int mid = lo + (hi - lo) / 2;
            if (Available[mid] <= asOf) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }
    internal int IndexOfPeriod(DateTime period) {
        for (int i = 0; i < Periods.Length; i++) if (Periods[i] == period) return i;
        return -1;
    }
    internal Dictionary<DateTime,Context> Contexts(Series series) {
        var result = new Dictionary<DateTime,Context>();
        var bps = Subject("bps"); var eps = Subject("eps");
        if (bps == null || eps == null) return result;
        for (int i = 0; i < series.Bars.Length; i++) {
            if (double.IsNaN(series.V["MOM20"][i])) continue;
            int index = IndexAt(series.Bars[i].Date);
            if (index < 0) continue;
            double bpsValue = bps.Values[index], epsTtm = eps.Ttms[index];
            if (double.IsNaN(bpsValue) || double.IsNaN(epsTtm) || bpsValue <= 0 || epsTtm <= 0) continue;
            double pe = series.Bars[i].Close / epsTtm;
            double roe = 100 * epsTtm / bpsValue;
            if (double.IsNaN(pe) || double.IsInfinity(pe) || pe <= 0 || pe > 1e5 || roe < -1e5 || roe > 1e5) continue;
            result[series.Bars[i].Date] = new Context { Roe = roe, Pe = pe, News = 0 };
        }
        return result;
    }
    internal List<string> Reconcile() {
        var issues = new List<string>();
        foreach (var subject in Subjects) {
            if (!subject.Mapped || !subject.Metric.Flow) continue;
            for (int i = 0; i < Periods.Length; i++) {
                if (QuarterOf(Periods[i]) != 4 || double.IsNaN(subject.Values[i])) continue;
                double sum = 0; bool complete = true;
                for (int k = 0; k < 4; k++) {
                    int at = IndexOfPeriod(new DateTime(Periods[i].Year, 3 + 3 * k, DateTime.DaysInMonth(Periods[i].Year, 3 + 3 * k)));
                    if (at < 0 || double.IsNaN(subject.Singles[at])) { complete = false; break; }
                    sum += subject.Singles[at];
                }
                if (!complete) continue;
                double tolerance = Math.Max(1, Math.Abs(subject.Values[i]) * 1e-9);
                if (Math.Abs(sum - subject.Values[i]) > tolerance)
                    issues.Add(subject.Name + " " + Periods[i].Year + " 年四个单季合计 " + Show(sum, 0) + " 与累计 " + Show(subject.Values[i], 0) + " 不符。");
            }
        }
        return issues;
    }
    internal void Write(string folder,out string csv,out string summary) {
        Directory.CreateDirectory(folder);
        csv = System.IO.Path.Combine(folder, "statement.csv");
        summary = System.IO.Path.Combine(folder, "statement-summary.txt");
        System.IO.File.WriteAllText(csv, Csv(), new UTF8Encoding(true));
        System.IO.File.WriteAllText(summary, Summary(), new UTF8Encoding(true));
    }
    internal string Csv() {
        var text = new StringBuilder();
        text.AppendLine("period,available_date,available_basis,cum_net_profit,quarter_net_profit,ttm_net_profit,cum_revenue,quarter_revenue,ttm_revenue," +
            "gross_margin,net_margin,roe,debt_ratio,eps,ttm_eps,bps,ocf_ps,net_profit_yoy,revenue_yoy,ttm_net_profit_yoy");
        for (int i = 0; i < Periods.Length; i++) {
            text.AppendLine(string.Join(",", new[]{
                Data.Day(Periods[i]), Data.Day(Available[i]), "法定期限估计",
                Num(Value("net_profit", i)), Num(Single("net_profit", i)), Num(Ttm("net_profit", i)),
                Num(Value("revenue", i)), Num(Single("revenue", i)), Num(Ttm("revenue", i)),
                Num(Value("gross_margin", i)), Num(Value("net_margin", i)), Num(Value("roe", i)), Num(Value("debt_ratio", i)),
                Num(Value("eps", i)), Num(Ttm("eps", i)), Num(Value("bps", i)), Num(Value("ocf_ps", i)),
                Pct(SubjectYoy("net_profit", i, false)), Pct(SubjectYoy("revenue", i, false)), Pct(SubjectYoy("net_profit", i, true))
            }));
        }
        return text.ToString();
    }
    double SubjectYoy(string key,int index,bool ttm) {
        var subject = Subject(key);
        if (subject == null) return double.NaN;
        return ttm ? subject.TtmYoy[index] : subject.SingleYoy[index];
    }
    internal double SingleYoy(string key,int index) {
        if (index < 0 || index >= Periods.Length) return double.NaN;
        return SubjectYoy(key, index, false);
    }
    internal double TtmYoy(string key,int index) {
        if (index < 0 || index >= Periods.Length) return double.NaN;
        return SubjectYoy(key, index, true);
    }
    static string Num(double value) {
        return double.IsNaN(value) || double.IsInfinity(value) ? "" : value.ToString("R", Data.Inv);
    }
    static string Pct(double value) {
        return double.IsNaN(value) || double.IsInfinity(value) ? "" : (100 * value).ToString("R", Data.Inv);
    }
    internal static string Show(double value,int digits) {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "—";
        if (digits <= 0) return value.ToString("N0", Data.Inv);
        return value.ToString("N" + digits, Data.Inv);
    }
    internal string Summary() {
        var text = new StringBuilder();
        text.AppendLine("财报：" + File + "  ·  标的 " + Symbol + "  ·  " + Format + (Sheet.Length > 0 ? " · 工作表 " + Sheet : ""));
        text.AppendLine("报告期：" + Periods.Length + " 个（" + Data.Day(From) + " ~ " + Data.Day(To) + "，升序）  ·  科目：" + Subjects.Count + " 个（识别 " + MappedCount + " 个）");
        text.AppendLine("表头行：" + (HeaderRow + 1) + "  ·  原始行：" + RawRows + "  ·  多余单元格丢弃：" + ExtraCells + " 行  ·  重复报告期：" + DuplicatePeriods);
        text.AppendLine();
        text.AppendLine("关键科目：" + string.Join("、", Metrics.Select(m => m.Label)));
        text.AppendLine();
        int latest = Periods.Length - 1;
        text.AppendLine("最新报告期：" + Data.Day(Periods[latest]) + "  ·  估计可见日：" + Data.Day(Available[latest]) + "（按法定期限估计）");
        AppendValue(text, "净利润（累计）", Value("net_profit", latest), 0, "元");
        AppendValue(text, "净利润（单季）", Single("net_profit", latest), 0, "元");
        AppendValue(text, "净利润（TTM）", Ttm("net_profit", latest), 0, "元");
        AppendValue(text, "营业总收入（累计）", Value("revenue", latest), 0, "元");
        AppendValue(text, "营业总收入（TTM）", Ttm("revenue", latest), 0, "元");
        AppendValue(text, "销售毛利率", Value("gross_margin", latest), 2, "%");
        AppendValue(text, "销售净利率", Value("net_margin", latest), 2, "%");
        AppendValue(text, "净资产收益率", Value("roe", latest), 2, "%");
        AppendValue(text, "资产负债率", Value("debt_ratio", latest), 2, "%");
        AppendValue(text, "基本每股收益（累计）", Value("eps", latest), 4, "元");
        AppendValue(text, "基本每股收益（TTM）", Ttm("eps", latest), 4, "元");
        AppendValue(text, "每股净资产", Value("bps", latest), 4, "元");
        double ocf = Value("ocf_ps", latest), epsValue = Value("eps", latest);
        if (!double.IsNaN(ocf) && !double.IsNaN(epsValue)) {
            text.AppendLine("现金流质量：每股经营现金流 / 每股收益 = " + (Math.Abs(epsValue) < 1e-9 ? "—" : (ocf / epsValue).ToString("0.00", Data.Inv)) +
                "（1 附近表示利润有现金支撑，持续偏低需警惕）。");
        }
        var issues = Reconcile();
        text.AppendLine("累计与单季对账：" + (issues.Count == 0 ? "通过（每年四个单季合计等于年报累计）" : issues.Count + " 处不一致"));
        foreach (string issue in issues.Take(6)) text.AppendLine("  - " + issue);
        text.AppendLine();
        text.AppendLine("最近八个报告期的单季净利润（元）：");
        int start = Math.Max(0, Periods.Length - 8);
        for (int i = start; i < Periods.Length; i++) {
            text.AppendLine("  " + Data.Day(Periods[i]) + "  " + Data.Day(Available[i]) + "  " + Show(Single("net_profit", i), 0) +
                "  （累计 " + Show(Value("net_profit", i), 0) + "，TTM " + Show(Ttm("net_profit", i), 0) + "）");
        }
        if (Notes.Count > 0) {
            text.AppendLine();
            text.AppendLine("说明：");
            foreach (string note in Notes) text.AppendLine("- " + note);
        }
        text.AppendLine();
        text.AppendLine("口径：金额按整数元展示；一季报直接取累计值，二/三/四季报减上一季累计得到单季；TTM 为最近四个单季之和；" +
            "同比只在基期为正时计算（单季对单季、TTM 对 TTM）。报表数据为累计口径，不做复权、不预测未来。");
        return text.ToString();
    }
    static void AppendValue(StringBuilder text,string label,double value,int digits,string unit) {
        text.AppendLine(label + "：" + Show(value, digits) + (double.IsNaN(value) || double.IsInfinity(value) ? "" : unit));
    }
}
}
