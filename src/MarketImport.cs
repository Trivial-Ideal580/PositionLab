using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PositionLab {
sealed class ImportReport {
    internal string File = "", Format = "CSV", Encoding = "UTF-8", Delimiter = ",", Sheet = "", SymbolSource = "";
    internal readonly List<string> Notes = new List<string>();
    internal readonly List<string> Mapping = new List<string>();
    internal int Rows, Skipped, Duplicates, HeaderRows;
    internal string[] Symbols = new string[0];
    internal string Summary {
        get {
            var text = new StringBuilder();
            text.Append(Format);
            if (Encoding.Length > 0) text.Append(" · " + Encoding);
            if (Delimiter.Length > 0 && Delimiter != ",") text.Append(" · 分隔符 " + (Delimiter == "\t" ? "Tab" : Delimiter));
            if (Sheet.Length > 0) text.Append(" · 工作表 " + Sheet);
            text.Append(" · " + Rows + " 行 · " + Symbols.Length + " 个标的");
            if (HeaderRows > 0) text.Append(" · 跳过 " + HeaderRows + " 行表头前内容");
            if (Skipped > 0) text.Append(" · 丢弃 " + Skipped + " 行");
            if (Duplicates > 0) text.Append(" · 去重 " + Duplicates + " 行");
            return text.ToString();
        }
    }
}
static class MarketImport {
    const int Limit = 200000;
    static readonly string[] SymbolNames = { "代码", "股票代码", "证券代码", "标的", "标的代码", "合约代码", "symbol", "ticker", "code", "windcode", "seccode", "stockcode", "instrument" };
    static readonly string[] DateNames = { "日期", "交易日期", "交易日", "时间", "交易时间", "日期时间", "date", "tradedate", "tradedt", "dt", "time", "datetime" };
    static readonly string[] OpenNames = { "开盘", "开盘价", "今开", "open", "openprice" };
    static readonly string[] HighNames = { "最高", "最高价", "high", "highprice" };
    static readonly string[] LowNames = { "最低", "最低价", "low", "lowprice" };
    static readonly string[] CloseNames = { "收盘", "收盘价", "最新价", "close", "closeprice", "last", "price" };
    static readonly string[] VolumeNames = { "成交量", "成交股数", "成交手数", "交易量", "volume", "vol", "turnovervolume" };
    static readonly string[] Ignored = { "名称", "简称", "name", "secname", "securityname", "成交额", "成交金额", "amount", "turnover", "涨跌幅", "振幅", "换手率" };
    internal static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d", "yyyyMMdd",
        "yyyy-MM-dd HH:mm:ss", "yyyy/M/d HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyyMMddHHmmss", "yyyy.MM.dd" };

    internal static Dictionary<string,Series> Load(string path, ImportReport report) {
        if (!System.IO.File.Exists(path)) throw new FormatException("文件不存在：" + path);
        report.File = System.IO.Path.GetFileName(path);
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new FormatException("文件超过 64 MB。");
        var rows = ReadRows(path, report);
        if (rows.Count == 0) throw new FormatException("文件没有可识别的数据行。");
        int headerIndex = FindHeader(rows);
        if (headerIndex < 0) throw new FormatException("找不到表头。行情至少需要日期和收盘价两列，可用列名见「数据与声明」。");
        report.HeaderRows = headerIndex;
        var header = rows[headerIndex];
        var map = MapColumns(header, report);
        if (!map.ContainsKey("date") || !map.ContainsKey("close")) throw new FormatException("缺少日期或收盘价列。识别到的表头：" + string.Join(" | ", header));
        double volumeScale = 1;
        if (map.ContainsKey("volume")) {
            string name = Normalize(header[map["volume"]]);
            if (name.Contains("手")) { volumeScale = 100; report.Notes.Add("成交量按「手」乘以 100 换算为股。"); }
        }
        string fallbackSymbol = SymbolFromName(path);
        var groups = new Dictionary<string, List<Bar>>(StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, HashSet<DateTime>>(StringComparer.OrdinalIgnoreCase);
        int dataRows = 0;
        for (int i = headerIndex + 1; i < rows.Count; i++) {
            var row = rows[i];
            if (row.Length == 0 || row.All(string.IsNullOrWhiteSpace)) continue;
            if (++dataRows > Limit) throw new FormatException("最多支持 " + Limit + " 条记录。");
            report.Rows++;
            DateTime day;
            double close;
            if (!TryDate(Cell(row, map, "date"), out day) || !TryNumber(Cell(row, map, "close"), out close) || close <= 0) { report.Skipped++; continue; }
            double open, high, low, volume;
            if (!TryNumber(Cell(row, map, "open"), out open) || open <= 0) open = close;
            if (!TryNumber(Cell(row, map, "high"), out high) || high <= 0) high = Math.Max(open, close);
            if (!TryNumber(Cell(row, map, "low"), out low) || low <= 0) low = Math.Min(open, close);
            if (!TryNumber(Cell(row, map, "volume"), out volume) || volume < 0) volume = 0;
            volume *= volumeScale;
            if (low > Math.Min(open, close)) low = Math.Min(open, close);
            if (high < Math.Max(open, close)) high = Math.Max(open, close);
            string symbol = map.ContainsKey("symbol") ? Cell(row, map, "symbol") : (fallbackSymbol ?? "");
            symbol = CleanSymbol(symbol);
            if (symbol.Length == 0) { report.Skipped++; continue; }
            if (!groups.ContainsKey(symbol)) { groups[symbol] = new List<Bar>(); seen[symbol] = new HashSet<DateTime>(); }
            if (!seen[symbol].Add(day)) { report.Duplicates++; continue; }
            groups[symbol].Add(new Bar { Date = day, Open = open, High = high, Low = low, Close = close, Volume = volume });
        }
        if (groups.Count == 0) throw new FormatException("没有解析出任何有效行情。请检查日期格式与数值列。");
        report.SymbolSource = map.ContainsKey("symbol") ? "来自标的列" : ("来自文件名 " + report.File);
        var market = new Dictionary<string, Series>();
        foreach (var pair in groups) {
            if (pair.Value.Count < 2) { report.Notes.Add(pair.Key + " 只有 " + pair.Value.Count + " 行，已跳过。"); continue; }
            market[pair.Key] = new Series(pair.Key, pair.Value);
        }
        if (market.Count == 0) throw new FormatException("每个标的至少需要两行行情。");
        report.Symbols = market.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return market;
    }

    static List<string[]> ReadRows(string path, ImportReport report) {
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".xlsx" || extension == ".xlsm") return Xlsx.Read(path, report);
        if (extension == ".xls") throw new FormatException("不支持旧版 .xls 二进制格式，请在 Excel 里另存为 .xlsx 或 CSV。");
        byte[] bytes = System.IO.File.ReadAllBytes(path);
        string encodingName;
        string text = Decode(bytes, out encodingName);
        report.Encoding = encodingName;
        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
        string delimiter = DetectDelimiter(text);
        report.Delimiter = delimiter;
        var rows = new List<string[]>();
        bool quoted = false;
        var current = new List<string>();
        var field = new StringBuilder();
        for (int i = 0; i < text.Length; i++) {
            char ch = text[i];
            if (quoted) {
                if (ch == '"') {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                } else field.Append(ch);
                continue;
            }
            if (ch == '"') { quoted = true; continue; }
            if (delimiter.IndexOf(ch) >= 0) { current.Add(field.ToString()); field.Length = 0; continue; }
            if (ch == '\r') continue;
            if (ch == '\n') { current.Add(field.ToString()); rows.Add(current.ToArray()); current.Clear(); field.Length = 0; continue; }
            field.Append(ch);
        }
        if (field.Length > 0 || current.Count > 0) { current.Add(field.ToString()); rows.Add(current.ToArray()); }
        return rows;
    }

    static string Decode(byte[] bytes, out string encodingName) {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { encodingName = "UTF-8 BOM"; return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3); }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { encodingName = "UTF-16 LE"; return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2); }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { encodingName = "UTF-16 BE"; return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); }
        try {
            var strict = new UTF8Encoding(false, true);
            string value = strict.GetString(bytes);
            encodingName = "UTF-8";
            return value;
        } catch (DecoderFallbackException) { }
        try {
            var gb = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            string value = gb.GetString(bytes);
            encodingName = "GBK";
            return value;
        } catch (Exception) { }
        encodingName = "GB18030";
        return Encoding.GetEncoding(54936).GetString(bytes);
    }

    static string DetectDelimiter(string text) {
        int limit = Math.Min(text.Length, 8000);
        int commas = 0, tabs = 0, semicolons = 0;
        for (int i = 0; i < limit; i++) {
            if (text[i] == ',') commas++;
            else if (text[i] == '\t') tabs++;
            else if (text[i] == ';') semicolons++;
        }
        if (tabs >= commas && tabs >= semicolons && tabs > 0) return "\t";
        if (semicolons > commas) return ";";
        return ",";
    }

    static int FindHeader(List<string[]> rows) {
        for (int i = 0; i < Math.Min(rows.Count, 12); i++) {
            int hits = 0; bool date = false, close = false;
            foreach (string cell in rows[i]) {
                string name = Normalize(cell);
                if (name.Length == 0) continue;
                if (Matches(name, DateNames)) { date = true; hits++; }
                else if (Matches(name, CloseNames)) { close = true; hits++; }
                else if (Matches(name, OpenNames) || Matches(name, HighNames) || Matches(name, LowNames) || Matches(name, VolumeNames) || Matches(name, SymbolNames)) hits++;
            }
            if (date && close && hits >= 2) return i;
        }
        return -1;
    }

    static Dictionary<string, int> MapColumns(string[] header, ImportReport report) {
        var map = new Dictionary<string, int>();
        for (int i = 0; i < header.Length; i++) {
            string name = Normalize(header[i]);
            if (name.Length == 0) continue;
            if (!map.ContainsKey("symbol") && Matches(name, SymbolNames)) { map["symbol"] = i; report.Mapping.Add("标的 ← " + header[i].Trim()); continue; }
            if (!map.ContainsKey("date") && Matches(name, DateNames)) { map["date"] = i; report.Mapping.Add("日期 ← " + header[i].Trim()); continue; }
            if (!map.ContainsKey("open") && Matches(name, OpenNames)) { map["open"] = i; report.Mapping.Add("开盘 ← " + header[i].Trim()); continue; }
            if (!map.ContainsKey("high") && Matches(name, HighNames)) { map["high"] = i; report.Mapping.Add("最高 ← " + header[i].Trim()); continue; }
            if (!map.ContainsKey("low") && Matches(name, LowNames)) { map["low"] = i; report.Mapping.Add("最低 ← " + header[i].Trim()); continue; }
            if (!map.ContainsKey("close") && Matches(name, CloseNames)) { map["close"] = i; report.Mapping.Add("收盘 ← " + header[i].Trim()); continue; }
            if (!map.ContainsKey("volume") && Matches(name, VolumeNames)) { map["volume"] = i; report.Mapping.Add("成交量 ← " + header[i].Trim()); continue; }
        }
        if (!map.ContainsKey("close") && map.ContainsKey("open")) {
            // 部分导出只有开高低收之外的单一价格列，回退到开盘价会误导，这里不做猜测。
        }
        return map;
    }

    static string Cell(string[] row, Dictionary<string, int> map, string key) {
        int index;
        if (!map.TryGetValue(key, out index) || index >= row.Length) return "";
        return row[index].Trim();
    }

    internal static string Normalize(string text) {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(text.Length);
        bool insideBracket = false;
        foreach (char raw in text) {
            char ch = raw;
            if (ch == '（' || ch == '(' || ch == '[' || ch == '【') { insideBracket = true; continue; }
            if (ch == '）' || ch == ')' || ch == ']' || ch == '】') { insideBracket = false; continue; }
            if (insideBracket) continue;
            if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-' || ch == '　' || ch == '\'' || ch == '"') continue;
            if (ch >= 'Ａ' && ch <= 'Ｚ') ch = (char)(ch - 'Ａ' + 'A');
            builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }

    static bool Matches(string name, string[] aliases) {
        foreach (string alias in aliases) if (name == alias) return true;
        return false;
    }

    internal static bool TryDate(string text, out DateTime value) {
        value = default(DateTime);
        if (string.IsNullOrWhiteSpace(text)) return false;
        string trimmed = text.Trim();
        double serial;
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out serial) && serial >= 20000 && serial <= 60000) {
            try { value = DateTime.FromOADate(serial).Date; return true; } catch (ArgumentException) { }
        }
        foreach (string format in DateFormats) {
            if (DateTime.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) { value = value.Date; return true; }
        }
        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) { value = value.Date; return true; }
        return false;
    }

    internal static bool TryNumber(string text, out double value) {
        value = double.NaN;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string trimmed = text.Trim().Replace(",", "").Replace(" ", "");
        if (trimmed.Length == 0 || trimmed == "-" || trimmed == "--" || trimmed == "—" || trimmed == "N/A" || trimmed == "NA") return false;
        double scale = 1;
        if (trimmed.EndsWith("万")) { scale = 1e4; trimmed = trimmed.Substring(0, trimmed.Length - 1); }
        else if (trimmed.EndsWith("亿")) { scale = 1e8; trimmed = trimmed.Substring(0, trimmed.Length - 1); }
        bool percent = trimmed.EndsWith("%");
        if (percent) trimmed = trimmed.Substring(0, trimmed.Length - 1);
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        if (double.IsNaN(value) || double.IsInfinity(value)) return false;
        value *= scale;
        if (percent) value /= 100;
        return true;
    }

    internal static string CleanSymbol(string text) {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string value = text.Trim().ToUpperInvariant();
        foreach (string suffix in new[] { ".SH", ".SZ", ".BJ", ".HK", ".OF" }) if (value.EndsWith(suffix)) { value = value.Substring(0, value.Length - suffix.Length); break; }
        if (value.StartsWith("SH") || value.StartsWith("SZ") || value.StartsWith("BJ")) {
            string digits = value.Substring(2);
            if (digits.Length > 0 && digits.All(char.IsDigit)) value = digits;
        } else {
            int dot = value.IndexOf('.');
            if (dot > 0 && value.Substring(0, dot).All(char.IsDigit)) value = value.Substring(0, dot);
        }
        if (value.Length == 0 || value.Length > 40 || value.Any(char.IsControl)) return "";
        return value;
    }

    internal static string SymbolFromName(string path) {
        string stem = System.IO.Path.GetFileNameWithoutExtension(path);
        string cleaned = CleanSymbol(stem);
        return cleaned.Length > 0 ? cleaned : null;
    }
}
static class Xlsx {
    internal static List<string[]> Read(string path, ImportReport report) {
        using (var archive = ZipFile.OpenRead(path)) {
            var shared = SharedStrings(archive);
            string sheetPath = FirstSheet(archive, report);
            XDocument document;
            using (var stream = archive.GetEntry(sheetPath).Open()) document = XDocument.Load(stream);
            XNamespace ns = document.Root.Name.Namespace;
            var rows = new List<string[]>();
            foreach (var row in document.Descendants(ns + "row")) {
                var map = new SortedDictionary<int, string>();
                int width = 0;
                foreach (var cell in row.Elements(ns + "c")) {
                    int column = ColumnOf((string)cell.Attribute("r"));
                    if (column < 0) continue;
                    string type = (string)cell.Attribute("t");
                    string value = "";
                    if (type == "s") {
                        var node = cell.Element(ns + "v");
                        int index;
                        if (node != null && int.TryParse(node.Value, out index) && index >= 0 && index < shared.Count) value = shared[index];
                    } else if (type == "inlineStr") {
                        var text = cell.Descendants(ns + "t").FirstOrDefault();
                        value = text == null ? "" : text.Value;
                    } else {
                        var node = cell.Element(ns + "v");
                        value = node == null ? "" : node.Value;
                    }
                    map[column] = value;
                    if (column + 1 > width) width = column + 1;
                }
                var cells = new string[width];
                for (int k = 0; k < width; k++) cells[k] = map.ContainsKey(k) ? map[k] : "";
                rows.Add(cells);
            }
            report.Format = "XLSX";
            report.Encoding = "";
            report.Delimiter = "";
            return rows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        }
    }
    static List<string> SharedStrings(ZipArchive archive) {
        var list = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return list;
        using (var stream = entry.Open()) {
            var document = XDocument.Load(stream);
            XNamespace ns = document.Root.Name.Namespace;
            foreach (var item in document.Descendants(ns + "si"))
                list.Add(string.Concat(item.Descendants(ns + "t").Select(t => t.Value)));
        }
        return list;
    }
    static string FirstSheet(ZipArchive archive, ImportReport report) {
        string target = null;
        var workbook = archive.GetEntry("xl/workbook.xml");
        if (workbook != null) {
            using (var stream = workbook.Open()) {
                var document = XDocument.Load(stream);
                XNamespace ns = document.Root.Name.Namespace;
                var first = document.Descendants(ns + "sheet").FirstOrDefault();
                if (first != null) {
                    var name = first.Attribute("name");
                    report.Sheet = name == null ? "" : name.Value;
                    var id = first.Attributes().FirstOrDefault(a => a.Name.LocalName == "id");
                    if (id != null) target = Relation(archive, id.Value);
                }
            }
        }
        if (target != null) return target;
        var fallback = archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml"));
        if (fallback == null) throw new FormatException("Excel 文件里没有工作表。");
        return fallback.FullName;
    }
    static string Relation(ZipArchive archive, string id) {
        var entry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (entry == null) return null;
        using (var stream = entry.Open()) {
            var document = XDocument.Load(stream);
            foreach (var node in document.Descendants()) {
                if (node.Name.LocalName != "Relationship") continue;
                var idAttribute = node.Attribute("Id");
                if (idAttribute == null || idAttribute.Value != id) continue;
                string target = node.Attribute("Target").Value.Replace("\\", "/");
                if (target.StartsWith("/")) return target.TrimStart('/');
                return "xl/" + target.Replace("../", "");
            }
        }
        return null;
    }
    static int ColumnOf(string reference) {
        if (string.IsNullOrEmpty(reference)) return -1;
        int column = 0, index = 0;
        while (index < reference.Length && char.IsLetter(reference[index])) {
            column = column * 26 + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }
        return column - 1;
    }
}
}
