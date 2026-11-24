using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PositionLab {
sealed class ConvertResult {
    internal bool Ok;
    internal string Name = "", Buy = "", Sell = "";
    internal readonly List<string> Notes = new List<string>();
    internal readonly List<string> Problems = new List<string>();
    internal string Rule { get { return "NAME=" + Name + "\r\nBUY=" + Buy + "\r\nSELL=" + Sell + "\r\n"; } }
    internal string Report() {
        var text = new StringBuilder();
        text.AppendLine("结果：" + (Ok ? "成功" : "失败"));
        text.AppendLine("BUY=" + Buy);
        text.AppendLine("SELL=" + Sell);
        if (Problems.Count > 0) {
            text.AppendLine();
            text.AppendLine("无法转换：");
            foreach (string problem in Problems) text.AppendLine("- " + problem);
        }
        if (Notes.Count > 0) {
            text.AppendLine();
            text.AppendLine("说明：");
            foreach (string note in Notes) text.AppendLine("- " + note);
        }
        return text.ToString();
    }
}
static class PythonConvert {
    const int PrecOr = 1, PrecAnd = 2, PrecNot = 3, PrecCompare = 4, PrecAdd = 5, PrecMul = 6, PrecUnary = 7, PrecAtom = 8;
    static readonly Dictionary<string,string> Columns = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        {"close","CLOSE"},{"open","OPEN"},{"high","HIGH"},{"low","LOW"},{"volume","VOLUME"},{"vol","VOLUME"},
        {"adj_close","CLOSE"},{"adjclose","CLOSE"},{"price","PRICE"}
    };
    static readonly string[] BuyNames = { "buy", "entry", "long_entry", "buy_signal", "is_buy", "entry_signal", "signal_buy" };
    static readonly string[] SellNames = { "sell", "exit", "long_exit", "sell_signal", "is_sell", "exit_signal", "signal_sell", "go_flat" };
    static readonly Regex TokenPattern = new Regex(
        @"'[^']*'|""[^""]*""|[A-Za-z_][A-Za-z0-9_]*|[0-9]+(?:\.[0-9]*)?|\*\*|//|<<|>>|<=|>=|==|!=|[-+*/%&|^~<>()\[\].,:]",
        RegexOptions.Compiled);

    sealed class Node {
        internal string Text = "";
        internal int Prec = PrecAtom;
        internal string Field;
        internal int Lag;
        internal bool Bare;
        internal string Name = "";
    }
    sealed class Step {
        internal string Kind = "";
        internal string Name = "";
        internal string Index = "";
        internal List<Node> Args = new List<Node>();
    }

    internal static ConvertResult Convert(string source, string name) {
        var result = new ConvertResult { Name = name };
        var symbols = new Dictionary<string,Node>(StringComparer.OrdinalIgnoreCase);
        string[] returns = null;
        foreach (string raw in Statements(source)) {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            string lower = line.ToLowerInvariant();
            if (lower.StartsWith("import ") || lower.StartsWith("from ")) { result.Notes.Add("忽略导入语句。"); continue; }
            if (lower.StartsWith("def ") || lower.StartsWith("class ")) { result.Notes.Add("跳过定义行，继续解析函数体。"); continue; }
            if (lower.StartsWith("print(") || lower.StartsWith("print (")) { result.Notes.Add("忽略 print 语句。"); continue; }
            if (lower.StartsWith("return")) { returns = SplitReturn(line.Substring(6)); continue; }
            if (Regex.IsMatch(lower, @"^(for|while|if|elif|else|try|except|finally|with|raise|assert|pass)\b")) {
                result.Problems.Add("不支持控制流或语句块：" + Clip(line));
                continue;
            }
            if (lower.Contains("read_csv") || lower.Contains("read_excel") || lower.Contains("read_parquet") || lower.Contains("read_json")) {
                result.Notes.Add("忽略数据读取行，回测数据请用「导入行情」提供。");
                continue;
            }
            int split = AssignmentIndex(line);
            if (split <= 0) { result.Notes.Add("忽略无法识别的行：" + Clip(line)); continue; }
            string target = line.Substring(0, split).Trim();
            string expression = line.Substring(split + 1).Trim();
            string key = TargetName(target);
            if (key == null) { result.Problems.Add("不支持该赋值目标：" + Clip(line)); continue; }
            try {
                symbols[key] = Parser.Parse(expression, symbols);
            } catch (FormatException error) {
                symbols.Remove(key);
                result.Problems.Add(Clip(line) + "  →  " + error.Message);
            }
        }
        var buy = Find(symbols, BuyNames); var sell = Find(symbols, SellNames);
        if ((buy == null || sell == null) && returns != null && returns.Length >= 2) {
            if (buy == null) buy = Lookup(symbols, returns[0]);
            if (sell == null) sell = Lookup(symbols, returns[1]);
        }
        if (buy == null || sell == null) {
            result.Problems.Add("没有找到买卖信号：请把变量命名为 buy/sell、entry/exit，或写成 return buy, sell。");
            return result;
        }
        result.Buy = buy.Text; result.Sell = sell.Text;
        try {
            Rule.Compile(buy.Text);
            Rule.Compile(sell.Text);
            result.Ok = true;
        } catch (FormatException error) {
            result.Problems.Add("生成的表达式未通过校验：" + error.Message + "（原始输出：" + buy.Text + " / " + sell.Text + "）");
        }
        if (result.Problems.Count > 0) {
            result.Ok = false;
            result.Problems.Add("存在无法转换的语句，已拒绝输出不完整的规则。");
        }
        return result;
    }

    static Node Find(Dictionary<string,Node> symbols, string[] names) {
        foreach (string name in names) {
            Node node;
            if (symbols.TryGetValue(name, out node)) return node;
        }
        return null;
    }
    static Node Lookup(Dictionary<string,Node> symbols, string name) {
        Node node;
        return name != null && symbols.TryGetValue(name.Trim(), out node) ? node : null;
    }
    static string[] SplitReturn(string text) {
        return text.Replace("(", "").Replace(")", "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    }
    static string TargetName(string target) {
        if (Regex.IsMatch(target, @"^[A-Za-z_][A-Za-z0-9_]*$")) return target;
        var match = Regex.Match(target, @"^[A-Za-z_][A-Za-z0-9_]*\s*\[\s*['""]([A-Za-z_][A-Za-z0-9_]*)['""]\s*\]$");
        return match.Success ? match.Groups[1].Value : null;
    }
    static int AssignmentIndex(string line) {
        for (int i = 0; i < line.Length; i++) {
            if (line[i] != '=') continue;
            char next = i + 1 < line.Length ? line[i + 1] : ' ';
            char previous = i > 0 ? line[i - 1] : ' ';
            if (next == '=' || previous == '=' || previous == '<' || previous == '>' || previous == '!' || previous == '+' || previous == '-' || previous == '*' || previous == '/') continue;
            return i;
        }
        return -1;
    }
    static string Clip(string text) {
        string value = text.Trim().Replace("\r", " ").Replace("\n", " ");
        return value.Length > 90 ? value.Substring(0, 90) + "…" : value;
    }
    static string[] Lines(string source) {
        var lines = new List<string>();
        var buffer = new StringBuilder();
        int depth = 0;
        foreach (string raw in source.Split('\n')) {
            string line = StripComment(raw).TrimEnd();
            if (line.Trim().Length == 0 && buffer.Length == 0) continue;
            if (buffer.Length > 0) buffer.Append(' ');
            buffer.Append(line.Trim());
            foreach (char ch in line) {
                if (ch == '(' || ch == '[' || ch == '{') depth++;
                else if (ch == ')' || ch == ']' || ch == '}') depth--;
            }
            if (depth <= 0) { lines.Add(buffer.ToString()); buffer.Length = 0; depth = 0; }
        }
        if (buffer.Length > 0) lines.Add(buffer.ToString());
        return lines.ToArray();
    }
    static string StripComment(string line) {
        bool single = false, dbl = false;
        for (int i = 0; i < line.Length; i++) {
            char ch = line[i];
            if (ch == '\'' && !dbl) single = !single;
            else if (ch == '"' && !single) dbl = !dbl;
            else if (ch == '#' && !single && !dbl) return line.Substring(0, i);
        }
        return line;
    }
    static IEnumerable<string> Statements(string source) { return Lines(source); }

    internal static string TranslateExpression(string expression, IEnumerable<string> variables) {
        var symbols = new Dictionary<string,Node>(StringComparer.OrdinalIgnoreCase);
        foreach (string variable in variables) symbols[variable] = new Node { Text = variable, Prec = PrecAtom };
        return Parser.Parse(expression, symbols).Text;
    }
    internal sealed class PyLine {
        internal int Indent;
        internal string Text = "";
    }
    internal static List<PyLine> Scan(string source) {
        var result = new List<PyLine>();
        foreach (string raw in source.Replace("\r\n", "\n").Split('\n')) {
            string line = StripComment(raw).TrimEnd();
            if (line.Trim().Length == 0) continue;
            if (line.TrimStart().StartsWith("import ") || line.TrimStart().StartsWith("from ")) continue;
            if (line.TrimStart().StartsWith("def ") || line.TrimStart().StartsWith("class ")) continue;
            int indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            result.Add(new PyLine { Indent = indent, Text = line.Trim() });
        }
        return result;
    }
    internal static string ConvertScript(string source, string name) {
        var lines = Scan(source);
        var variables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines) {
            int split = AssignmentIndex(line.Text);
            if (split > 0) variables.Add(TargetName(line.Text.Substring(0, split).Trim()) ?? "");
            var forMatch = Regex.Match(line.Text, @"^for\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+range\(([^)]*)\)\s*:$");
            if (forMatch.Success) variables.Add(forMatch.Groups[1].Value);
        }
        variables.Remove("");
        string buyName = BuyNames.FirstOrDefault(n => variables.Contains(n));
        string sellName = SellNames.FirstOrDefault(n => variables.Contains(n));
        if (buyName == null || sellName == null) throw new FormatException("没有找到 buy/sell 信号变量。");
        var body = new StringBuilder();
        int index = 0;
        Emit(lines, ref index, lines.Count > 0 ? lines[0].Indent : 0, body, "  ", variables, buyName, sellName);
        var text = new StringBuilder();
        text.AppendLine("NAME=" + name);
        text.AppendLine("ON BAR");
        text.AppendLine("  LET " + buyName + " = 0");
        text.AppendLine("  LET " + sellName + " = 0");
        text.Append(body);
        text.AppendLine("  IF " + buyName + " THEN");
        text.AppendLine("    BUY 1");
        text.AppendLine("  END");
        text.AppendLine("  IF " + sellName + " THEN");
        text.AppendLine("    SELL 1");
        text.AppendLine("  END");
        text.AppendLine("END");
        return text.ToString();
    }
    static void Emit(List<PyLine> lines, ref int index, int indent, StringBuilder output, string pad, SortedSet<string> variables, string buyName, string sellName) {
        while (index < lines.Count) {
            var line = lines[index];
            if (line.Indent < indent) return;
            if (line.Indent > indent) throw new FormatException("缩进不一致：" + Clip(line.Text));
            string text = line.Text;
            string lower = text.ToLowerInvariant();
            if (lower.StartsWith("if ") && text.EndsWith(":")) {
                string condition = TranslateExpression(text.Substring(3, text.Length - 4).Trim(), variables);
                output.AppendLine(pad + "IF " + condition + " THEN");
                index++;
                Emit(lines, ref index, NextIndent(lines, index), output, pad + "  ", variables, buyName, sellName);
                while (index < lines.Count && lines[index].Indent == indent && (lines[index].Text.ToLowerInvariant().StartsWith("elif ") || lines[index].Text.ToLowerInvariant() == "else:")) {
                    var branch = lines[index];
                    if (branch.Text.ToLowerInvariant() == "else:") {
                        output.AppendLine(pad + "ELSE");
                        index++;
                        Emit(lines, ref index, NextIndent(lines, index), output, pad + "  ", variables, buyName, sellName);
                        break;
                    }
                    string alternative = TranslateExpression(branch.Text.Substring(5, branch.Text.Length - 6).Trim(), variables);
                    output.AppendLine(pad + "ELSE");
                    output.AppendLine(pad + "  IF " + alternative + " THEN");
                    index++;
                    Emit(lines, ref index, NextIndent(lines, index), output, pad + "    ", variables, buyName, sellName);
                    output.AppendLine(pad + "  END");
                }
                output.AppendLine(pad + "END");
                continue;
            }
            if (lower == "else:" || lower.StartsWith("elif ")) throw new FormatException("ELIF/ELSE 没有对应的 IF：" + Clip(text));
            if (lower.StartsWith("if:")) throw new FormatException("IF 缺少条件。");
            var forMatch = Regex.Match(text, @"^for\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+range\(([^)]*)\)\s*:$", RegexOptions.IgnoreCase);
            if (forMatch.Success) {
                string variable = forMatch.Groups[1].Value;
                string[] parts = forMatch.Groups[2].Value.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
                string start = parts.Length == 1 ? "0" : TranslateExpression(parts[0], variables);
                string stop = parts.Length == 1 ? TranslateExpression(parts[0], variables) : TranslateExpression(parts[1], variables);
                string step = parts.Length > 2 ? " STEP " + TranslateExpression(parts[2], variables) : "";
                output.AppendLine(pad + "FOR " + variable + " = " + start + " TO (" + stop + " - 1)" + step);
                index++;
                Emit(lines, ref index, NextIndent(lines, index), output, pad + "  ", variables, buyName, sellName);
                output.AppendLine(pad + "END");
                continue;
            }
            if (lower.StartsWith("return")) { index++; continue; }
            if (lower.StartsWith("print(")) { index++; continue; }
            if (lower.StartsWith("for ") || lower.StartsWith("while ")) throw new FormatException("只支持 for ... in range(...) 循环：" + Clip(text));
            int split = AssignmentIndex(text);
            if (split <= 0) throw new FormatException("无法识别的语句：" + Clip(text));
            string target = text.Substring(0, split).Trim();
            string expression = text.Substring(split + 1).Trim();
            if (target.EndsWith("+") || target.EndsWith("-") || target.EndsWith("*") || target.EndsWith("/")) {
                string op = target.Substring(target.Length - 1);
                string name = target.Substring(0, target.Length - 1).Trim();
                output.AppendLine(pad + "SET " + name + " = " + name + " " + op + " (" + TranslateExpression(expression, variables) + ")");
                index++;
                continue;
            }
            string key = TargetName(target);
            if (key == null) throw new FormatException("不支持的赋值目标：" + Clip(text));
            output.AppendLine(pad + "LET " + key + " = " + TranslateExpression(expression, variables));
            index++;
        }
    }
    static int NextIndent(List<PyLine> lines, int index) { return index < lines.Count ? lines[index].Indent : int.MaxValue; }
    sealed class Parser {
        readonly List<string> tokens;
        int cursor;
        readonly Dictionary<string,Node> symbols;
        Parser(string input, Dictionary<string,Node> symbols) {
            this.symbols = symbols;
            tokens = TokenPattern.Matches(input).Cast<Match>().Select(m => m.Value).ToList();
            if (tokens.Count > 800) throw new FormatException("表达式过长。");
        }
        internal static Node Parse(string input, Dictionary<string,Node> symbols) { return new Parser(input, symbols).ParseExpression(); }
        Node ParseExpression() {
            var node = ParseOr();
            if (cursor < tokens.Count) throw new FormatException("无法识别的片段：" + tokens[cursor]);
            return node;
        }
        string Peek { get { return cursor < tokens.Count ? tokens[cursor] : ""; } }
        bool Take(string token) { if (Peek != token) return false; cursor++; return true; }
        Node ParseOr() {
            var node = ParseAnd();
            while (Take("or") || Take("|")) node = Bool("OR", PrecOr, node, ParseAnd());
            return node;
        }
        Node ParseAnd() {
            var node = ParseNot();
            while (Take("and") || Take("&")) node = Bool("AND", PrecAnd, node, ParseNot());
            return node;
        }
        Node ParseNot() {
            if (Take("not") || Take("~")) return Unary("NOT", PrecNot, ParseNot());
            return ParseCompare();
        }
        Node ParseCompare() {
            var left = ParseAdd();
            string op = Peek;
            if (!(op == ">" || op == "<" || op == ">=" || op == "<=" || op == "==" || op == "!=")) return left;
            cursor++;
            var right = ParseAdd();
            if (Peek == ">" || Peek == "<" || Peek == ">=" || Peek == "<=" || Peek == "==" || Peek == "!=")
                throw new FormatException("不支持链式比较，请拆成两个条件用 AND 连接。");
            return new Node { Text = Wrap(left, PrecCompare) + " " + op + " " + Wrap(right, PrecCompare + 1), Prec = PrecCompare };
        }
        Node ParseAdd() {
            var node = ParseMul();
            while (true) {
                if (Take("+")) node = Arith("+", PrecAdd, node, ParseMul(), false);
                else if (Take("-")) node = Arith("-", PrecAdd, node, ParseMul(), true);
                else return node;
            }
        }
        Node ParseMul() {
            var node = ParseUnary();
            while (true) {
                if (Take("*")) node = Arith("*", PrecMul, node, ParseUnary(), false);
                else if (Take("/")) node = Arith("/", PrecMul, node, ParseUnary(), true);
                else if (Take("%")) throw new FormatException("不支持取模运算。");
                else if (Take("**")) throw new FormatException("不支持乘方运算。");
                else return node;
            }
        }
        Node ParseUnary() {
            if (Take("-")) return Unary("-", PrecUnary, ParseUnary());
            if (Take("+")) return ParseUnary();
            return ParsePostfix();
        }
        Node ParsePostfix() {
            var node = ParseAtom();
            var steps = new List<Step>();
            while (true) {
                if (Take(".")) {
                    string name = Peek;
                    if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$")) throw new FormatException("属性名无效。");
                    cursor++;
                    steps.Add(new Step { Kind = "attr", Name = name });
                    if (Peek == "(") steps.Add(new Step { Kind = "call", Name = "(", Args = Arguments() });
                    continue;
                }
                if (Take("[")) {
                    string index = Peek;
                    cursor++;
                    if (!Take("]")) throw new FormatException("缺少右方括号。");
                    steps.Add(new Step { Kind = "index", Index = index.Trim('\'', '"') });
                    continue;
                }
                break;
            }
            return Translate(node, steps);
        }
        Node ParseAtom() {
            string token = Peek;
            if (token.Length == 0) throw new FormatException("表达式不完整。");
            if (Take("(")) {
                var inner = ParseOr();
                if (!Take(")")) throw new FormatException("缺少右括号。");
                return inner;
            }
            if (token[0] == '\'' || token[0] == '"') {
                cursor++;
                string value = token.Trim('\'', '"');
                if (Columns.ContainsKey(value)) return Field(Columns[value], 0);
                throw new FormatException("字符串常量只支持列名，遇到：" + value);
            }
            if (char.IsDigit(token[0]) || (token[0] == '.' && token.Length > 1)) {
                cursor++;
                return Number(double.Parse(token, CultureInfo.InvariantCulture));
            }
            if (token == "True" || token == "true") { cursor++; return Number(1); }
            if (token == "False" || token == "false") { cursor++; return Number(0); }
            if (!Regex.IsMatch(token, @"^[A-Za-z_][A-Za-z0-9_]*$")) throw new FormatException("无法识别的记号：" + token);
            cursor++;
            if (Peek == "(") {
                var args = Arguments();
                return Call(token, args);
            }
            Node variable;
            if (symbols.TryGetValue(token, out variable)) return variable;
            string column;
            if (Columns.TryGetValue(token, out column)) return Field(column, 0);
            if (Peek == "." || Peek == "[") return new Node { Text = token, Prec = PrecAtom, Bare = true, Name = token };
            throw new FormatException("未知变量：" + token + "。请先在同一文件里赋值，或改用列名 close/open/high/low/volume。");
        }
        List<Node> Arguments() {
            if (!Take("(")) throw new FormatException("缺少左括号。");
            var list = new List<Node>();
            if (Take(")")) return list;
            do {
                if (Regex.IsMatch(Peek, @"^[A-Za-z_][A-Za-z0-9_]*$") && cursor + 1 < tokens.Count && tokens[cursor + 1] == "=" && tokens[cursor + 1] != "==") {
                    cursor += 2;
                }
                list.Add(ParseOr());
            } while (Take(","));
            if (!Take(")")) throw new FormatException("缺少右括号。");
            return list;
        }
        Node Translate(Node node, List<Step> steps) {
            int i = 0;
            if (node.Bare && steps.Count >= 2 && steps[0].Kind == "attr" && steps[1].Kind == "call") {
                node = Qualified(node, steps[0].Name, steps[1].Args);
                i = 2;
            } else if (steps.Count >= 1 && steps[0].Kind == "attr" && Columns.ContainsKey(steps[0].Name)) {
                node = Field(Columns[steps[0].Name], 0);
                i = 1;
            } else if (steps.Count >= 1 && steps[0].Kind == "index" && Columns.ContainsKey(steps[0].Index)) {
                node = Field(Columns[steps[0].Index], 0);
                i = 1;
            }
            while (i < steps.Count) {
                var step = steps[i];
                if (step.Kind == "attr" && step.Name == "rolling" && i + 3 < steps.Count &&
                    steps[i + 1].Kind == "call" && steps[i + 2].Kind == "attr" && steps[i + 3].Kind == "call") {
                    int window = Integer(steps[i + 1].Args, "rolling 窗口");
                    if (steps[i + 2].Name == "mean") { node = Call("AVG", node, Number(window)); i += 4; continue; }
                    throw new FormatException("只支持 rolling(n).mean()，不支持 rolling()." + steps[i + 2].Name + "()。");
                }
                if (step.Kind == "attr" && step.Name == "ewm" && i + 3 < steps.Count &&
                    steps[i + 1].Kind == "call" && steps[i + 2].Kind == "attr" && steps[i + 2].Name == "mean" && steps[i + 3].Kind == "call") {
                    int span = Integer(steps[i + 1].Args, "ewm span");
                    if (span != 12 && span != 26) throw new FormatException("ewm 只支持 span=12 或 26（对应 EMA12／EMA26）。");
                    node = new Node { Text = "EMA" + span, Prec = PrecAtom };
                    i += 4; continue;
                }
                if (step.Kind == "attr" && step.Name == "shift" && i + 1 < steps.Count && steps[i + 1].Kind == "call") {
                    int lag = Integer(steps[i + 1].Args, "shift 偏移");
                    if (node.Field != null && node.Lag == 0) node = Field(node.Field, lag);
                    else node = Call("LAG", node, Number(lag));
                    i += 2; continue;
                }
                if (step.Kind == "attr" && step.Name == "pct_change" && i + 1 < steps.Count && steps[i + 1].Kind == "call") {
                    if (node.Field != "CLOSE") throw new FormatException("pct_change 只支持收盘价（映射为 RET1）。");
                    node = new Node { Text = "RET1", Prec = PrecAtom };
                    i += 2; continue;
                }
                if (step.Kind == "attr" && step.Name == "abs" && i + 1 < steps.Count && steps[i + 1].Kind == "call") {
                    node = Call("ABS", node);
                    i += 2; continue;
                }
                throw new FormatException(step.Kind == "index" ? "不支持动态下标（如 close[i]）。" : "暂不支持 ." + (step.Kind == "call" ? "调用" : step.Name) + "。");
            }
            return node;
        }
        Node Qualified(Node module, string name, List<Node> args) {
            if (!module.Bare) throw new FormatException("不支持的链式调用。");
            return Call(module.Name + "." + name, args.ToArray());
        }
        Node Call(string name, params Node[] args) {
            var list = args.ToList();
            return Call(name, list);
        }
        Node Call(string name, List<Node> args) {
            string upper = name.ToUpperInvariant();
            switch (upper) {
                case "ABS": case "NP.ABS": case "MATH.ABS":
                    Need(name, args, 1); return Fn("ABS", args[0]);
                case "MAX": case "NP.MAXIMUM": case "MATH.MAX":
                    Need(name, args, 2); return Fn("MAX", args[0], args[1]);
                case "MIN": case "NP.MINIMUM": case "MATH.MIN":
                    Need(name, args, 2); return Fn("MIN", args[0], args[1]);
                case "NP.WHERE": case "WHERE":
                    Need(name, args, 3); return Fn("IF", args[0], args[1], args[2]);
                case "TA.CROSS": case "CROSS":
                    Need(name, args, 2); return Fn("CROSS", args[0], args[1]);
                case "TA.SMA": case "SMA": case "PANDAS.SMA":
                    Need(name, args, 2); return Fn("AVG", args[0], args[1]);
                case "TA.RSI": case "RSI": {
                    Need(name, args, 2);
                    if (args[0].Field != "CLOSE" || !IsConstant(args[1], 14)) throw new FormatException("RSI 只支持 RSI(close, 14)（映射为 RSI14）。");
                    return new Node { Text = "RSI14", Prec = PrecAtom };
                }
                case "TA.EMA": case "EMA": {
                    Need(name, args, 2);
                    if (args[0].Field != "CLOSE" || !(IsConstant(args[1], 12) || IsConstant(args[1], 26))) throw new FormatException("EMA 只支持 close 的 12／26 周期（映射为 EMA12／EMA26）。");
                    return new Node { Text = IsConstant(args[1], 12) ? "EMA12" : "EMA26", Prec = PrecAtom };
                }
                case "LAG":
                    Need(name, args, 2); return Fn("LAG", args[0], args[1]);
                case "AVG":
                    Need(name, args, 2); return Fn("AVG", args[0], args[1]);
            }
            throw new FormatException("不支持的函数：" + name + "()。");
        }
        static void Need(string name, List<Node> args, int count) {
            if (args.Count != count) throw new FormatException(name + "() 需要 " + count + " 个参数，实际 " + args.Count + " 个。");
        }
        static bool IsConstant(Node node, double value) {
            double parsed;
            return double.TryParse(node.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && Math.Abs(parsed - value) < 1e-9;
        }
        static int Integer(List<Node> args, string what) {
            if (args.Count != 1) throw new FormatException(what + "需要一个整数参数。");
            double parsed;
            if (!double.TryParse(args[0].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || parsed < 1 || parsed > 252 || parsed != Math.Floor(parsed))
                throw new FormatException(what + "须为 1–252 的整数。");
            return (int)parsed;
        }
        static Node Fn(string name, params Node[] args) {
            return new Node { Text = name + "(" + string.Join(", ", args.Select(a => a.Text)) + ")", Prec = PrecAtom };
        }
        static Node Number(double value) {
            return new Node { Text = value.ToString("0.####", CultureInfo.InvariantCulture), Prec = PrecAtom };
        }
        static Node Field(string field, int lag) {
            return new Node { Text = lag == 0 ? field : field + "[" + lag + "]", Prec = PrecAtom, Field = field, Lag = lag };
        }
        static Node Bool(string op, int prec, Node left, Node right) {
            return new Node { Text = Wrap(left, prec) + " " + op + " " + Wrap(right, prec), Prec = prec };
        }
        static Node Arith(string op, int prec, Node left, Node right, bool tightenRight) {
            return new Node { Text = Wrap(left, prec) + " " + op + " " + Wrap(right, prec + (tightenRight ? 1 : 0)), Prec = prec };
        }
        static Node Unary(string op, int prec, Node inner) {
            return new Node { Text = op == "-" ? "-" + Wrap(inner, prec) : op + " " + Wrap(inner, prec), Prec = prec };
        }
        static string Wrap(Node node, int minimum) {
            return node.Prec < minimum ? "(" + node.Text + ")" : node.Text;
        }
    }
}
}
