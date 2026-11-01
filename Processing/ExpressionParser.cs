using System;
using System.Collections.Generic;
using System.Globalization;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
    /// <summary>波段计算器表达式：词法 + 递归下降语法分析，编译成可直接对像素求值的委托。</summary>
    public sealed class CompiledExpression
    {
        internal Func<double[], double> Evaluator;
        /// <summary>
        /// 表达式用到的占位符槽位号（1 基，升序去重）。
        /// 注意这是“槽位号”，实际读取哪个文件的哪个波段由 <see cref="BandMapping"/> 决定。
        /// </summary>
        public int[] UsedSlots = new int[0];
        public string Source;

        public double Evaluate(double[] bandValues)
        {
            if (Evaluator == null) return double.NaN;
            return Evaluator(bandValues);
        }
    }

    /// <summary>一个可在界面上一键插入的函数说明。</summary>
    public sealed class FunctionDoc
    {
        /// <summary>函数名（也是按钮文字）。</summary>
        public string Name;
        /// <summary>插入到表达式里的文本（通常带一个左括号，便于接着输入参数）。</summary>
        public string Insert;
        /// <summary>插入后光标相对片段起点的偏移。</summary>
        public int CaretOffset;
        /// <summary>用法签名，例如 <c>clamp(x, lo, hi)</c>。</summary>
        public string Usage;
        /// <summary>一句话说明。</summary>
        public string Description;
        /// <summary>示例。</summary>
        public string Example;
        /// <summary>是否支持把当前选中的文本包进括号。</summary>
        public bool WrapSelection;

        /// <summary>拼成鼠标悬浮提示的多行文本。</summary>
        /// <returns>可直接赋给 ToolTip 的文本。</returns>
        public string Tip()
        {
            return Lang.T("用法：") + Usage + "\r\n" + Description + Lang.T("\r\n示例：") + Example;
        }
    }

    public static class ExpressionParser
    {
        private enum Kind { Number, Ident, Op, LParen, RParen, Comma, End }

        private sealed class Token
        {
            public Kind Kind;
            public string Text;
            public double Number;
            public int Pos;
        }

        /// <summary>编译表达式；bandCount 表示可用槽位数（b1…bN 的 N）。</summary>
        public static CompiledExpression Compile(string expression, int bandCount)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException(Lang.T("表达式为空"));

            var tokens = Tokenize(expression);
            var used = new SortedSet<int>();
            int pos = 0;
            var body = ParseConditional(tokens, ref pos, bandCount, used);
            if (tokens[pos].Kind != Kind.End)
                throw new FormatException(Lang.T("表达式在第 ") + (tokens[pos].Pos + 1) + Lang.T(" 个字符处有多余内容: ") + tokens[pos].Text);

            var list = new List<int>(used);
            return new CompiledExpression
            {
                Evaluator = body,
                UsedSlots = list.ToArray(),
                Source = expression
            };
        }

        // ---------------- 词法 ----------------

        private static List<Token> Tokenize(string s)
        {
            var list = new List<Token>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    int start = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                    {
                        i++;
                        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                        while (i < s.Length && char.IsDigit(s[i])) i++;
                    }
                    string txt = s.Substring(start, i - start);
                    double v;
                    if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        throw new FormatException(Lang.T("非法数字: ") + txt);
                    list.Add(new Token { Kind = Kind.Number, Number = v, Text = txt, Pos = start });
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    list.Add(new Token { Kind = Kind.Ident, Text = s.Substring(start, i - start), Pos = start });
                    continue;
                }

                if (c == '(') { list.Add(new Token { Kind = Kind.LParen, Text = "(", Pos = i }); i++; continue; }
                if (c == ')') { list.Add(new Token { Kind = Kind.RParen, Text = ")", Pos = i }); i++; continue; }
                if (c == ',') { list.Add(new Token { Kind = Kind.Comma, Text = ",", Pos = i }); i++; continue; }

                string two = i + 1 < s.Length ? s.Substring(i, 2) : null;
                if (two == "<=" || two == ">=" || two == "==" || two == "!=" || two == "&&" || two == "||")
                {
                    list.Add(new Token { Kind = Kind.Op, Text = two, Pos = i });
                    i += 2;
                    continue;
                }

                if ("+-*/^%<>?:".IndexOf(c) >= 0)
                {
                    list.Add(new Token { Kind = Kind.Op, Text = c.ToString(), Pos = i });
                    i++;
                    continue;
                }

                throw new FormatException(Lang.T("无法识别的字符 '{0}'（位置 {1}）", c, i + 1));
            }
            list.Add(new Token { Kind = Kind.End, Text = "", Pos = s.Length });
            return list;
        }

        // ---------------- 语法 ----------------

        private static Func<double[], double> ParseConditional(List<Token> t, ref int p, int bandCount, SortedSet<int> used)
        {
            var cond = ParseOr(t, ref p, bandCount, used);
            if (IsOp(t, p, "?"))
            {
                p++;
                var a = ParseConditional(t, ref p, bandCount, used);
                if (!IsOp(t, p, ":")) throw new FormatException(Lang.T("条件表达式缺少 ':'"));
                p++;
                var b = ParseConditional(t, ref p, bandCount, used);
                return env => cond(env) != 0 ? a(env) : b(env);
            }
            return cond;
        }

        private static Func<double[], double> ParseOr(List<Token> t, ref int p, int bandCount, SortedSet<int> used)
        {
            var left = ParseAnd(t, ref p, bandCount, used);
            while (IsOp(t, p, "||"))
            {
                p++;
                var right = ParseAnd(t, ref p, bandCount, used);
                var l = left; var r = right;
                left = env => (l(env) != 0 || r(env) != 0) ? 1 : 0;
            }
            return left;
        }

        private static Func<double[], double> ParseAnd(List<Token> t, ref int p, int bandCount, SortedSet<int> used)
        {
            var left = ParseEquality(t, ref p, bandCount, used);
            while (IsOp(t, p, "&&"))
            {
                p++;
                var right = ParseEquality(t, ref p, bandCount, used);
                var l = left; var r = right;
                left = env => (l(env) != 0 && r(env) != 0) ? 1 : 0;
            }
            return left;
        }

        private static Func<double[], double> ParseEquality(List<Token> t, ref int p, int bandCount, SortedSet<int> used)
        {
            var left = ParseRelational(t, ref p, bandCount, used);
            while (IsOp(t, p, "==") || IsOp(t, p, "!="))
            {
                string op = t[p].Text;
                p++;
                var right = ParseRelational(t, ref p, bandCount, used);
                var l = left; var r = right;
                if (op == "==") left = env => l(env) == r(env) ? 1 : 0;
                else left = env => l(env) != r(env) ? 1 : 0;
            }
            return left;
        }

        private static Func<double[], double> ParseRelational(List<Token> t, ref int p, int bandCount, SortedSet<int> used)
        {
            var left = ParseAdditive(t, ref p, bandCount, used);
            while (IsOp(t, p, "<") || IsOp(t, p, "<=") || IsOp(t, p, ">") || IsOp(t, p, ">="))
            {
                string op = t[p].Text;
                p++;
                var right = ParseAdditive(t, ref p, bandCount, used);
                var l = left; var r = right;
                switch (op)
                {
                    case "<": left = env => l(env) < r(env) ? 1 : 0; break;
                    case "<=": left = env => l(env) <= r(env) ? 1 : 0; break;
                    case ">": left = env => l(env) > r(env) ? 1 : 0; break;
                    default: left = env => l(env) >= r(env) ? 1 : 0; break;
                }
            }
            return left;
        }

        private static Func<double[], double> ParseAdditive(List<Token> t, ref int p, int bandCount, SortedSet<int> used)
        {
            var left = ParseMultiplicative(t, ref p, bandCount, used);
            while (IsOp(t, p, "+") || IsOp(t, p, "-"))
            {
                string op = t[p].Text;
                p++;
                var right = ParseMultiplicative(t, ref p, bandCount, used);
                var l = left; var r = right;
                if (op == "+") left = env => l(env) + r(env);
                else left = env => l(env) - r(env);
            }
            return left;
        }

        private static Func<double[], double> ParseMultiplicative(List<Token> t, ref int p, int bandCount, SortedSet<int> used)
        {
            var left = ParseUnary(t, ref p, bandCount, used);
            while (IsOp(t, p, "*") || IsOp(t, p, "/") || IsOp(t, p, "%"))
            {
                string op = t[p].Text;
                p++;
                var right = ParseUnary(t, ref p, bandCount, used);
                var l = left; var r = right;
                if (op == "*") left = env => l(env) * r(env);
                else if (op == "/") left = env => { double d = r(env); return d == 0 ? double.NaN : l(env) / d; };
                else left = env => { double d = r(env); return d == 0 ? double.NaN : l(env) % d; };
            }
            return left;
        }

        private static Func<double[], double> ParseUnary(List<Token> t, ref int p, int bandCount, SortedSet<int> used)
        {
            if (IsOp(t, p, "-"))
            {
                p++;
                var inner = ParseUnary(t, ref p, bandCount, used);
                return env => -inner(env);
            }
            if (IsOp(t, p, "+"))
            {
                p++;
                return ParseUnary(t, ref p, bandCount, used);
            }
            return ParsePower(t, ref p, bandCount, used);
        }

        private static Func<double[], double> ParsePower(List<Token> t, ref int p, int bandCount, SortedSet<int> used)
        {
            var left = ParsePrimary(t, ref p, bandCount, used);
            if (IsOp(t, p, "^"))
            {
                p++;
                var right = ParseUnary(t, ref p, bandCount, used); // 右结合
                var l = left; var r = right;
                return env => Math.Pow(l(env), r(env));
            }
            return left;
        }

        private static Func<double[], double> ParsePrimary(List<Token> t, ref int p, int bandCount, SortedSet<int> used)
        {
            var tok = t[p];

            if (tok.Kind == Kind.Number)
            {
                p++;
                double v = tok.Number;
                return env => v;
            }

            if (tok.Kind == Kind.LParen)
            {
                p++;
                var inner = ParseConditional(t, ref p, bandCount, used);
                if (t[p].Kind != Kind.RParen) throw new FormatException(Lang.T("缺少右括号 ')'"));
                p++;
                return inner;
            }

            if (tok.Kind == Kind.Ident)
            {
                string name = tok.Text;
                p++;

                // 波段变量 b1..bN
                if (name.Length > 1 && (name[0] == 'b' || name[0] == 'B') && char.IsDigit(name[1]))
                {
                    int bandNo;
                    if (!int.TryParse(name.Substring(1), out bandNo))
                        throw new FormatException(Lang.T("非法波段变量: ") + name);
                    if (bandNo < 1 || bandNo > bandCount)
                        throw new FormatException(string.Format(
                            Lang.T("占位符 {0} 超出范围：当前映射表共 {1} 个槽位（b1…b{1}）"), name, bandCount));
                    used.Add(bandNo);
                    // idx 是槽位下标（不是物理波段下标）；实际数据由 BandCalculator 按映射表填进 env
                    int idx = bandNo - 1;
                    return env => env[idx];
                }

                // 常量
                string lower = name.ToLowerInvariant();
                if (lower == "pi") return env => Math.PI;
                if (lower == "e") return env => Math.E;
                if (lower == "nan") return env => double.NaN;

                // 函数
                if (t[p].Kind != Kind.LParen)
                    throw new FormatException(Lang.T("未知标识符: ") + name + Lang.T("（函数需要带括号）"));
                p++;
                var args = new List<Func<double[], double>>();
                if (t[p].Kind != Kind.RParen)
                {
                    while (true)
                    {
                        args.Add(ParseConditional(t, ref p, bandCount, used));
                        if (t[p].Kind == Kind.Comma) { p++; continue; }
                        break;
                    }
                }
                if (t[p].Kind != Kind.RParen) throw new FormatException(Lang.T("函数 ") + name + Lang.T(" 缺少右括号"));
                p++;
                return BuildFunction(lower, args);
            }

            throw new FormatException(Lang.T("表达式缺少操作数（位置 {0}）", tok.Pos + 1));
        }

        private static Func<double[], double> BuildFunction(string name, List<Func<double[], double>> a)
        {
            Action<int> need = n =>
            {
                if (a.Count != n) throw new FormatException(string.Format(Lang.T("函数 {0} 需要 {1} 个参数，实际 {2} 个"), name, n, a.Count));
            };

            switch (name)
            {
                case "sqrt": need(1); return env => { double v = a[0](env); return v < 0 ? double.NaN : Math.Sqrt(v); };
                case "abs": need(1); return env => Math.Abs(a[0](env));
                case "log":
                case "ln": need(1); return env => { double v = a[0](env); return v <= 0 ? double.NaN : Math.Log(v); };
                case "log10": need(1); return env => { double v = a[0](env); return v <= 0 ? double.NaN : Math.Log10(v); };
                case "exp": need(1); return env => Math.Exp(a[0](env));
                case "sin": need(1); return env => Math.Sin(a[0](env));
                case "cos": need(1); return env => Math.Cos(a[0](env));
                case "tan": need(1); return env => Math.Tan(a[0](env));
                case "floor": need(1); return env => Math.Floor(a[0](env));
                case "ceil": need(1); return env => Math.Ceiling(a[0](env));
                case "round": need(1); return env => Math.Round(a[0](env));
                case "sign": need(1); return env => Math.Sign(a[0](env));
                case "isnan": need(1); return env => double.IsNaN(a[0](env)) ? 1 : 0;
                case "min":
                    if (a.Count < 1) throw new FormatException(Lang.T("min 至少需要 1 个参数"));
                    return env => { double m = a[0](env); for (int i = 1; i < a.Count; i++) m = Math.Min(m, a[i](env)); return m; };
                case "max":
                    if (a.Count < 1) throw new FormatException(Lang.T("max 至少需要 1 个参数"));
                    return env => { double m = a[0](env); for (int i = 1; i < a.Count; i++) m = Math.Max(m, a[i](env)); return m; };
                case "clamp":
                    need(3);
                    return env =>
                    {
                        double v = a[0](env), lo = a[1](env), hi = a[2](env);
                        return v < lo ? lo : (v > hi ? hi : v);
                    };
                case "pow": need(2); return env => Math.Pow(a[0](env), a[1](env));
                case "if":
                case "ifelse":
                    need(3);
                    return env => a[0](env) != 0 ? a[1](env) : a[2](env);
                case "normalizeddifference":
                case "nd":
                    need(2);
                    return env =>
                    {
                        double x = a[0](env), y = a[1](env);
                        double s = x + y;
                        return Math.Abs(s) < 1e-12 ? double.NaN : (x - y) / s;
                    };
                default:
                    throw new FormatException(Lang.T("未知函数: ") + name);
            }
        }

        private static bool IsOp(List<Token> t, int p, string op)
        {
            var tok = t[p];
            return tok.Kind == Kind.Op && tok.Text == op;
        }

        /// <summary>界面“函数面板”使用的函数清单（顺序即按钮顺序）。</summary>
        public static FunctionDoc[] Functions
        {
            get
            {
                return new[]
                {
                    F("sqrt", "sqrt(", 5, "sqrt(x)", Lang.T("开平方；x 为负数时结果为 NaN"), "sqrt(b1)", true),
                    F("abs", "abs(", 4, "abs(x)", Lang.T("取绝对值"), "abs(b1 - b2)", true),
                    F("log", "log(", 4, Lang.T("log(x)  等价 ln(x)"), Lang.T("自然对数；x ≤ 0 时结果 NaN"), "log(b1 + 1)", true),
                    F("log10", "log10(", 6, "log10(x)", Lang.T("以 10 为底的对数；x ≤ 0 时结果 NaN"), "log10(b1 + 1)", true),
                    F("exp", "exp(", 4, "exp(x)", Lang.T("自然指数 e^x"), "exp(-b1 / 100)", true),
                    F("pow", "pow(, )", 4, "pow(x, y)", Lang.T("x 的 y 次幂（y 可为小数）"), "pow(b1, 0.5)", false),
                    F("min", "min(, )", 4, "min(a, b, …)", Lang.T("取最小值，可传多个参数"), "min(b1, b2)", false),
                    F("max", "max(, )", 4, "max(a, b, …)", Lang.T("取最大值，可传多个参数"), "max(b1, b2)", false),
                    F("clamp", "clamp(, , )", 6, "clamp(x, lo, hi)", Lang.T("把 x 限制在 [lo, hi] 区间内"), "clamp(b1, 0, 255)", false),
                    F("if", "if(, , )", 3, Lang.T("if(条件, 真值, 假值)"), Lang.T("条件不为 0 取真值，否则取假值；比较可用 > < =="), "if(b1 > 100, 255, b1)", false),
                    F("nd", "normalizedDifference(, )", 21, "normalizedDifference(a, b)", Lang.T("归一化差值 (a-b)/(a+b)，NDVI/NDWI 都用它"), "normalizedDifference(b4, b3)", false),
                    F("floor", "floor(", 6, "floor(x)", Lang.T("向下取整"), "floor(b1 / 10)", true),
                    F("ceil", "ceil(", 5, "ceil(x)", Lang.T("向上取整"), "ceil(b1 / 10)", true),
                    F("round", "round(", 6, "round(x)", Lang.T("四舍五入取整"), "round(b1 / 10)", true),
                    F("sign", "sign(", 5, "sign(x)", Lang.T("符号：正数 1、负数 -1、零 0"), "sign(b1 - b2)", true),
                    F("sin", "sin(", 4, "sin(x)", Lang.T("正弦（x 为弧度）"), "sin(b1)", true),
                    F("cos", "cos(", 4, "cos(x)", Lang.T("余弦（x 为弧度）"), "cos(b1)", true),
                    F("isnan", "isnan(", 6, "isnan(x)", Lang.T("x 为 NaN 时返回 1，否则 0"), "if(isnan(b1), 0, b1)", true)
                };
            }
        }

        private static FunctionDoc F(string name, string insert, int caret, string usage,
            string description, string example, bool wrap)
        {
            return new FunctionDoc
            {
                Name = name,
                Insert = insert,
                CaretOffset = caret,
                Usage = usage,
                Description = description,
                Example = example,
                WrapSelection = wrap
            };
        }

        /// <summary>与 <see cref="Templates"/> 一一对应的说明文本。</summary>
        public static string[] TemplateDescriptions
        {
            get
            {
                return new[]
                {
                    Lang.T("直接把某个波段当结果输出"),
                    Lang.T("NDVI：植被指数，近红外与红波段"),
                    Lang.T("简单比值指数，抑制光照差异"),
                    Lang.T("多波段加权合成（权重可按需修改）"),
                    Lang.T("阈值分段：大于 100 取 255，否则放大两倍"),
                    Lang.T("三波段平均值，常用于降噪")
                };
            }
        }

        /// <summary>常用表达式模板（对话框里可一键填入）。</summary>
        public static string[] Templates
        {
            get
            {
                return new[]
                {
                    "b1",
                    "normalizedDifference(b4, b3)",
                    "(b1 - b2) / (b1 + b2)",
                    "b1 * 0.5 + b2 * 0.3 + b3 * 0.2",
                    "if(b1 > 100, 255, b1 * 2)",
                    "(b1 + b2 + b3) / 3"
                };
            }
        }
    }
}
