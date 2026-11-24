using System;
using System.Collections.Generic;
using System.Linq;

namespace PositionLab {
sealed class FundamentalSeries {
    internal string Key = "", Label = "", Unit = "";
    internal double[] Values = new double[0];
    internal double[] Rolling = new double[0];
    internal double Latest = double.NaN, PriceCorr = double.NaN, ReturnCorr = double.NaN, RecentCorr = double.NaN, LongCorr = double.NaN;
    internal int Samples, RollingSamples;
}
sealed class FundamentalAnalysis {
    internal string Symbol = "", StatementFile = "";
    internal DateTime[] Dates = new DateTime[0];
    internal int[] PeriodAt = new int[0];
    internal double[] PeriodScores = new double[0];
    internal readonly List<FundamentalSeries> Series = new List<FundamentalSeries>();
    internal int Window, Periods, ScoreSamples;
    internal double LatestScore = double.NaN;
    internal DateTime FirstVisible, LastPeriod;
    internal static readonly string[] Composite = { "roe_ttm", "gross_margin", "net_margin", "debt_ratio", "revenue_growth", "profit_growth", "ocf_quality" };
    internal static readonly string[] Labels = { "综合基本面", "ROE（TTM）", "销售毛利率", "销售净利率", "资产负债率", "营收同比", "净利同比", "现金流质量", "PE（TTM）" };
    internal static readonly string[] Keys = { "score", "roe_ttm", "gross_margin", "net_margin", "debt_ratio", "revenue_growth", "profit_growth", "ocf_quality", "pe" };
    internal static readonly string[] Units = { "分", "%", "%", "%", "%", "%", "%", "倍", "倍" };

    internal static FundamentalAnalysis Run(StatementBook book, Series series, int window) {
        int n = series.Bars.Length;
        var result = new FundamentalAnalysis {
            Symbol = series.Symbol, StatementFile = book.File, Dates = series.Bars.Select(b => b.Date).ToArray(),
            PeriodAt = new int[n], Periods = book.Count, FirstVisible = book.Available[0], LastPeriod = book.Periods[book.Count - 1]
        };
        result.PeriodScores = CompositeScores(book);
        int periods = book.Count;
        var periodValues = new Dictionary<string,double[]>(StringComparer.Ordinal);
        foreach (string key in Keys) {
            if (key == "score") continue;
            periodValues[key] = new double[periods];
            for (int p = 0; p < periods; p++) periodValues[key][p] = key == "pe" ? double.NaN : PeriodMetric(book, p, key);
        }
        var values = new Dictionary<string,double[]>(StringComparer.Ordinal);
        foreach (string key in Keys) values[key] = Enumerable.Repeat(double.NaN, n).ToArray();
        for (int i = 0; i < n; i++) {
            int p = book.IndexAt(series.Bars[i].Date);
            result.PeriodAt[i] = p;
            if (p < 0) continue;
            values["score"][i] = result.PeriodScores[p];
            foreach (string key in Keys) {
                if (key == "score" || key == "pe") continue;
                values[key][i] = periodValues[key][p];
            }
            double eps = book.Ttm("eps", p);
            if (!double.IsNaN(eps) && eps > 0) values["pe"][i] = series.Bars[i].Close / eps;
        }
        var close = series.Bars.Select(b => b.Close).ToArray();
        var ret = new double[n];
        for (int i = 20; i < n; i++) if (close[i - 20] > 0 && close[i] > 0) ret[i] = close[i] / close[i - 20] - 1;
        int w = Math.Max(60, Math.Min(1000, window));
        if (w * 2 > n) w = Math.Max(20, n / 2);
        result.Window = w;
        for (int k = 0; k < Keys.Length; k++) {
            var item = new FundamentalSeries { Key = Keys[k], Label = Labels[k], Unit = Units[k], Values = values[Keys[k]] };
            int count;
            item.PriceCorr = Correlation(item.Values, close, 0, n - 1, out count);
            item.ReturnCorr = Correlation(item.Values, ret, 0, n - 1, out count);
            item.Samples = count;
            int recentFrom = Math.Max(0, n - 250), longFrom = Math.Max(0, n - 750);
            int ignored;
            item.RecentCorr = Correlation(item.Values, ret, recentFrom, n - 1, out ignored);
            item.LongCorr = Correlation(item.Values, ret, longFrom, n - 1, out ignored);
            item.Rolling = RollingCorrelation(item.Values, ret, w);
            item.RollingSamples = item.Rolling.Count(v => !double.IsNaN(v));
            if (Keys[k] == "pe") {
                // PE 的分子就是收盘价，与股价相关是恒等式，展示最新值即可，不算相关。
                item.PriceCorr = item.ReturnCorr = item.RecentCorr = item.LongCorr = double.NaN;
                item.Rolling = Enumerable.Repeat(double.NaN, n).ToArray();
                item.RollingSamples = 0;
            }
            for (int i = n - 1; i >= 0; i--) if (!double.IsNaN(item.Values[i])) { item.Latest = item.Values[i]; break; }
            result.Series.Add(item);
        }
        result.ScoreSamples = result.PeriodScores.Count(v => !double.IsNaN(v));
        for (int i = n - 1; i >= 0; i--) if (!double.IsNaN(values["score"][i])) { result.LatestScore = values["score"][i]; break; }
        return result;
    }
    static double PeriodMetric(StatementBook book, int period, string key) {
        switch (key) {
            case "roe_ttm": {
                double eps = book.Ttm("eps", period), bps = book.Value("bps", period);
                return double.IsNaN(eps) || double.IsNaN(bps) || bps <= 0 ? double.NaN : 100 * eps / bps;
            }
            case "ocf_quality": {
                double ocf = book.Value("ocf_ps", period), eps = book.Ttm("eps", period);
                return double.IsNaN(ocf) || double.IsNaN(eps) || eps <= 0 ? double.NaN : ocf / eps;
            }
            case "revenue_growth": {
                double growth = book.TtmYoy("revenue", period);
                return double.IsNaN(growth) ? double.NaN : 100 * growth;
            }
            case "profit_growth": {
                double growth = book.TtmYoy("net_profit", period);
                return double.IsNaN(growth) ? double.NaN : 100 * growth;
            }
            default:
                return book.Value(key, period);
        }
    }
    static double[] CompositeScores(StatementBook book) {
        int periods = book.Count;
        var result = Enumerable.Repeat(double.NaN, periods).ToArray();
        var values = new Dictionary<string,double[]>(StringComparer.Ordinal);
        foreach (string key in Composite) {
            values[key] = new double[periods];
            for (int p = 0; p < periods; p++) values[key][p] = PeriodMetric(book, p, key);
        }
        for (int p = 0; p < periods; p++) {
            double sum = 0; int used = 0;
            foreach (string key in Composite) {
                double current = values[key][p];
                if (double.IsNaN(current)) continue;
                int count = 0, less = 0, equal = 0;
                for (int j = 0; j <= p; j++) {
                    double value = values[key][j];
                    if (double.IsNaN(value)) continue;
                    count++;
                    if (value < current) less++;
                    else if (value == current) equal++;
                }
                if (count < 4) continue;
                double percentile = (less + 0.5 * equal) / count;
                if (key == "debt_ratio") percentile = 1 - percentile;
                sum += percentile; used++;
            }
            if (used >= 3) result[p] = 100 * sum / used;
        }
        return result;
    }
    static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    static double Correlation(double[] x,double[] y,int from,int to,out int count) {
        count = 0;
        var xs = new List<double>(); var ys = new List<double>();
        for (int i = Math.Max(0, from); i <= to && i < x.Length; i++) {
            if (!Finite(x[i]) || !Finite(y[i])) continue;
            xs.Add(x[i]); ys.Add(y[i]);
        }
        count = xs.Count;
        if (count < 30) return double.NaN;
        double r = Stats.Corr(xs.ToArray(), ys.ToArray());
        if (double.IsNaN(r)) return double.NaN;
        return Math.Max(-1, Math.Min(1, r));
    }
    static double[] RollingCorrelation(double[] x,double[] y,int window) {
        int n = x.Length;
        var result = Enumerable.Repeat(double.NaN, n).ToArray();
        if (window < 20 || n < window) return result;
        double reference = double.NaN;
        for (int i = 0; i < n; i++) if (Finite(x[i])) { reference = x[i]; break; }
        if (double.IsNaN(reference)) return result;
        var sx = new double[n + 1]; var sy = new double[n + 1]; var sxx = new double[n + 1];
        var syy = new double[n + 1]; var sxy = new double[n + 1]; var valid = new int[n + 1];
        var changes = new int[n + 1];
        for (int i = 0; i < n; i++) {
            bool ok = Finite(x[i]) && Finite(y[i]);
            // 先减去参考值再做前缀和，避免大均值相减时的浮点抵消把常数序列算成"有波动"。
            double dx = ok ? x[i] - reference : 0;
            sx[i + 1] = sx[i] + dx;
            sy[i + 1] = sy[i] + (ok ? y[i] : 0);
            sxx[i + 1] = sxx[i] + (ok ? dx * dx : 0);
            syy[i + 1] = syy[i] + (ok ? y[i] * y[i] : 0);
            sxy[i + 1] = sxy[i] + (ok ? dx * y[i] : 0);
            valid[i + 1] = valid[i] + (ok ? 1 : 0);
            changes[i + 1] = changes[i] + (i > 0 && x[i] != x[i - 1] ? 1 : 0);
        }
        for (int i = window - 1; i < n; i++) {
            if (valid[i + 1] - valid[i + 1 - window] != window) continue;
            int a = i + 1 - window;
            if (changes[i + 1] - changes[a + 1] == 0) continue;
            double ax = sx[i + 1] - sx[a], ay = sy[i + 1] - sy[a];
            double axx = sxx[i + 1] - sxx[a], ayy = syy[i + 1] - syy[a], axy = sxy[i + 1] - sxy[a];
            double meanX = ax / window, meanY = ay / window;
            double varianceX = axx / window - meanX * meanX, varianceY = ayy / window - meanY * meanY;
            if (varianceX <= 1e-14 * Math.Max(1e-12, meanX * meanX) || varianceY <= 1e-14 * Math.Max(1e-12, meanY * meanY)) continue;
            double covariance = axy / window - meanX * meanY;
            double r = covariance / Math.Sqrt(varianceX * varianceY);
            if (double.IsNaN(r) || double.IsInfinity(r)) continue;
            result[i] = Math.Max(-1, Math.Min(1, r));
        }
        return result;
    }
}
}
