using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PositionLab {
sealed class Settings {
    internal int Window = 252, ChartMode, ChartRange = 120;
    internal string Initial = "100000", Fee = "8", Slippage = "5", Cash = "0";
    internal int Width, Height, Left, Top;
    internal bool Maximized;

    internal static string Folder {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PositionLab"); }
    }
    internal static string FileName { get { return Path.Combine(Folder, "settings.ini"); } }

    internal bool HasBounds {
        get { return Width >= 800 && Height >= 600; }
    }

    internal static Settings Load() { return Load(FileName); }
    internal static Settings Load(string path) {
        var result = new Settings();
        try {
            if (!File.Exists(path)) return result;
            foreach (string line in File.ReadAllLines(path, new UTF8Encoding(false, true))) {
                int split = line.IndexOf('=');
                if (split <= 0) continue;
                string key = line.Substring(0, split).Trim(), value = line.Substring(split + 1).Trim();
                switch (key) {
                    case "window": result.Window = Range(value, 20, 2000, result.Window); break;
                    case "chartMode": result.ChartMode = Range(value, 0, FactorCharts.Tabs.Length - 1, result.ChartMode); break;
                    case "chartRange": result.ChartRange = Range(value, 60, 250, result.ChartRange); break;
                    case "initial": result.Initial = Amount(value, 1, 1e14, result.Initial); break;
                    case "fee": result.Fee = Amount(value, 0, 1000, result.Fee); break;
                    case "slippage": result.Slippage = Amount(value, 0, 1000, result.Slippage); break;
                    case "cash": result.Cash = Amount(value, 0, 1e19, result.Cash); break;
                    case "width": result.Width = Range(value, 0, 20000, result.Width); break;
                    case "height": result.Height = Range(value, 0, 20000, result.Height); break;
                    case "left": result.Left = Range(value, -20000, 20000, result.Left); break;
                    case "top": result.Top = Range(value, -20000, 20000, result.Top); break;
                    case "maximized": result.Maximized = value == "1"; break;
                }
            }
        } catch { return new Settings(); }
        return result;
    }
    internal void Save() { Save(FileName); }
    internal void Save(string path) {
        try {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            var text = new StringBuilder();
            text.AppendLine("# PositionLab 设置。删除本文件即可恢复默认值。");
            text.AppendLine("window=" + Window.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("chartMode=" + ChartMode.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("chartRange=" + ChartRange.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("initial=" + Initial);
            text.AppendLine("fee=" + Fee);
            text.AppendLine("slippage=" + Slippage);
            text.AppendLine("cash=" + Cash);
            text.AppendLine("width=" + Width.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("height=" + Height.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("left=" + Left.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("top=" + Top.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("maximized=" + (Maximized ? "1" : "0"));
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
        } catch { }
    }
    static int Range(string text, int min, int max, int fallback) {
        int value;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max ? value : fallback;
    }
    static string Amount(string text, double min, double max, string fallback) {
        double value;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max ? text : fallback;
    }
}
}
