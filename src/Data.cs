using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace PositionLab {
static class Data {
    internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    internal const int Limit = 200000;
    internal static double Number(string text, double min, double max) {
        double n;
        if (!double.TryParse(text, NumberStyles.Float, Inv, out n) || double.IsNaN(n) || n < min || n > max)
            throw new FormatException("数值超出范围或格式无效：" + text);
        return n;
    }
    internal static DateTime Date(string text) {
        DateTime value;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", Inv, DateTimeStyles.None, out value))
            throw new FormatException("日期须为 YYYY-MM-DD：" + text);
        return value;
    }
    internal static string Day(DateTime d) { return d.ToString("yyyy-MM-dd", Inv); }
    internal static string Symbol(string s) {
        if (s.Length == 0 || s.Length > 40 || s.Any(char.IsControl)) throw new FormatException("标的代码长度须为 1–40，且不能包含控制字符。");
        return s.ToUpperInvariant();
    }
    internal static IEnumerable<string[]> Csv(string file, string columns) {
        if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new FormatException("文件超过 32 MB。");
        using (var reader = new TextFieldParser(file, new UTF8Encoding(false, true), true)) {
            reader.SetDelimiters(",");
            reader.TrimWhiteSpace = true;
            var header = reader.ReadFields();
            var required = columns.Split(',');
            if (header == null || header.Any(string.IsNullOrWhiteSpace) || header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Length)
                throw new FormatException("CSV 表头为空或有重复列。");
            var indexes = required.Select(k => Array.FindIndex(header, x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (indexes.Any(i => i < 0)) throw new FormatException("CSV 缺列。必需表头为 " + columns);
            int count = 0;
            while (!reader.EndOfData) {
                long line = reader.LineNumber;
                var row = reader.ReadFields();
                if (++count > Limit) throw new FormatException("CSV 最多支持 200,000 条记录。");
                if (row.Length != header.Length) throw new FormatException("第 " + line + " 行列数与表头不符。");
                yield return indexes.Select(i => row[i]).ToArray();
            }
            if (count == 0) throw new FormatException("CSV 没有数据行。");
        }
    }
    internal static Bar BarRow(string[] row) {
        var b = new Bar { Date = Date(row[1]), Open = Number(row[2], 1e-8, 1e10), High = Number(row[3], 1e-8, 1e10),
            Low = Number(row[4], 1e-8, 1e10), Close = Number(row[5], 1e-8, 1e10), Volume = Number(row[6], 0, 1e16) };
        if (b.Low > Math.Min(b.Open, b.Close) || b.High < Math.Max(b.Open, b.Close))
            throw new FormatException("OHLC 价格关系无效：" + row[0] + " " + row[1]);
        return b;
    }
    internal static Dictionary<string, Series> Market(string file) {
        var groups = new Dictionary<string, List<Bar>>();
        foreach (var row in Csv(file, "symbol,date,open,high,low,close,volume")) {
            string key = Symbol(row[0]);
            List<Bar> list;
            if (!groups.TryGetValue(key, out list)) groups.Add(key, list = new List<Bar>());
            list.Add(BarRow(row));
        }
        return groups.ToDictionary(p => p.Key, p => new Series(p.Key, p.Value));
    }
    internal static Dictionary<DateTime, Context> Contexts(string file, string symbol) {
        var result = new Dictionary<DateTime, Context>();
        foreach (var r in Csv(file,"symbol,available_date,roe,pe,sentiment")) {
            string key = Symbol(r[0]); DateTime day = Date(r[1]);
            var item = new Context { Roe = Number(r[2], -1e5, 1e5), Pe = Number(r[3], -1e6, 1e6), News = Number(r[4], -1, 1) };
            if (key != symbol) continue;
            if (result.ContainsKey(day)) throw new FormatException("资料可用日重复：" + Day(day));
            result.Add(day, item);
        }
        return result;
    }
    internal static List<Holding> Holdings(string file) {
        var holdings = new List<Holding>();
        foreach (var r in Csv(file,"symbol,quantity,cost,bought_date,sector"))
            holdings.Add(new Holding { Symbol = Symbol(r[0]), Qty = Number(r[1], 1e-8, 1e12), Cost = Number(r[2], 1e-8, 1e10),
                Bought = Date(r[3]), Sector = r[4].Length == 0 ? "未分类" : r[4] });
        return holdings;
    }
    internal static string Escape(string s) {
        if (s.Length > 0 && "=+@-\t\r".IndexOf(s[0]) >= 0) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
sealed class Bar { internal DateTime Date; internal double Open, High, Low, Close, Volume; }
sealed class Context { internal double Roe, Pe, News; }
sealed class Holding { internal string Symbol, Sector; internal double Qty, Cost; internal DateTime Bought; }
sealed class Series {
    internal readonly string Symbol;
    internal readonly Bar[] Bars;
    internal readonly int Gaps;
    internal readonly Dictionary<string, double[]> V = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
    internal static readonly string[] Fields = { "OPEN","HIGH","LOW","CLOSE","PRICE","VOLUME","RET1","MOM20","MA5","MA10","MA20","MA30","MA60","MA120",
        "EMA12","EMA26","RSI14","DIF","DEA","MACD","ATR14","BOLLUP","BOLLMID","BOLLDOWN","VOLMA5","VOLRATIO","HHV20","LLV20","VOL20" };
    internal Series(string symbol, IEnumerable<Bar> bars) {
        Symbol = symbol;
        Bars = bars.OrderBy(b => b.Date).ToArray();
        int n = Bars.Length;
        int gaps = 0;
        for (int i = 1; i < n; i++) {
            if (Bars[i].Date == Bars[i-1].Date) throw new FormatException(symbol + " 行情日期重复：" + Data.Day(Bars[i].Date));
            if ((Bars[i].Date - Bars[i-1].Date).TotalDays > 10) gaps++;
        }
        Gaps = gaps;
        foreach (string field in Fields) V.Add(field, Enumerable.Repeat(double.NaN, n).ToArray());
        double fast = Bars[0].Close, slow = fast, signal = 0, gain = 0, loss = 0, atr = 0;
        int[] periods = { 5, 10, 20, 30, 60, 120 };
        for (int i = 0; i < n; i++) {
            var b = Bars[i]; double c = b.Close;
            V["OPEN"][i] = b.Open; V["HIGH"][i] = b.High; V["LOW"][i] = b.Low; V["CLOSE"][i] = V["PRICE"][i] = c; V["VOLUME"][i] = b.Volume;
            if (i >= 20) V["MOM20"][i] = c / Bars[i-20].Close - 1;
            foreach (int p in periods) if (i >= p-1) {
                double sum = 0;
                for (int j = i-p+1; j <= i; j++) sum += Bars[j].Close;
                V["MA"+p][i] = sum / p;
            }
            fast += (c-fast)*2/13; slow += (c-slow)*2/27;
            double dif = fast-slow; signal += (dif-signal)*2/10;
            if (i >= 11) V["EMA12"][i] = fast;
            if (i >= 25) { V["EMA26"][i] = slow; V["DIF"][i] = dif; }
            if (i >= 33) { V["DEA"][i] = signal; V["MACD"][i] = 2*(dif-signal); }
            if (i > 0) {
                double prev = Bars[i-1].Close, delta = c-prev;
                V["RET1"][i] = c/prev-1;
                double tr = Math.Max(b.High-b.Low, Math.Max(Math.Abs(b.High-prev), Math.Abs(b.Low-prev)));
                if (i <= 14) { gain += Math.Max(delta,0)/14; loss += Math.Max(-delta,0)/14; atr += tr/14; }
                else { gain = (gain*13+Math.Max(delta,0))/14; loss = (loss*13+Math.Max(-delta,0))/14; atr = (atr*13+tr)/14; }
                if (i >= 14) { V["RSI14"][i] = loss == 0 ? (gain == 0 ? 50 : 100) : 100-100/(1+gain/loss); V["ATR14"][i] = atr; }
            }
            if (i >= 4) {
                double volume = 0;
                for (int j = i-4; j <= i; j++) volume += Bars[j].Volume;
                V["VOLMA5"][i] = volume/5;
                if (i >= 5) {
                    double previous = 0;
                    for (int j = i-5; j < i; j++) previous += Bars[j].Volume/5;
                    if (previous > 0) V["VOLRATIO"][i] = b.Volume/previous;
                }
            }
            if (i >= 19) {
                double mid = V["MA20"][i], squared = 0, high = b.High, low = b.Low;
                for (int j = i-19; j <= i; j++) {
                    double delta = Bars[j].Close-mid; squared += delta*delta;
                    high = Math.Max(high,Bars[j].High); low = Math.Min(low,Bars[j].Low);
                }
                double spread = 2*Math.Sqrt(squared/20);
                V["BOLLMID"][i] = mid; V["BOLLUP"][i] = mid+spread; V["BOLLDOWN"][i] = mid-spread;
                V["HHV20"][i] = high; V["LLV20"][i] = low;
                if (i >= 20) { var returns = new double[20]; Array.Copy(V["RET1"],i-19,returns,0,20); V["VOL20"][i] = Stats.Std(returns)*Math.Sqrt(252); }
            }
        }
    }
}
static class Stats {
    internal static double Std(double[] x) {
        if (x.Length < 2) return double.NaN;
        double mean = x.Average();
        return Math.Sqrt(x.Sum(d => (d-mean)*(d-mean))/(x.Length-1));
    }
    internal static double Percentile(double[] values, int i, int window, out int n) {
        n = 0; double count = 0, current = values[i];
        if (double.IsNaN(current)) return double.NaN;
        for (int j = Math.Max(0,i-window); j < i; j++) if (!double.IsNaN(values[j])) {
            n++; count += values[j] < current ? 1 : values[j] == current ? .5 : 0;
        }
        return n < 20 ? double.NaN : count/n;
    }
    internal static double Corr(double[] a, double[] b) {
        double sa = Std(a), sb = Std(b);
        if (sa == 0 || sb == 0 || double.IsNaN(sa) || double.IsNaN(sb)) return double.NaN;
        double ma = a.Average(), mb = b.Average(), sum = 0;
        for (int i=0;i<a.Length;i++) sum += ((a[i]-ma)/sa)*((b[i]-mb)/sb);
        return Math.Max(-1, Math.Min(1,sum/(a.Length-1)));
    }
    internal static double Drawdown(IEnumerable<double> values) {
        double peak = 0, worst = 0;
        foreach (double x in values) { peak = Math.Max(peak,x); if (peak > 0) worst = Math.Max(worst,1-x/peak); }
        return worst;
    }
}
}


