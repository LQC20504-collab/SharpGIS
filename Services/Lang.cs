using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SharpGIS.Services
{
    /// <summary>
    /// 极简双语支持：中文文案即键，英文译文集中在 LangDict*.cs。
    /// 取不到译文时原样返回中文，因此漏翻只会显示中文，不会出现空白或异常。
    /// 语言在进程启动时确定，切换后需要重启（菜单里会提示）。
    /// </summary>
    public static partial class Lang
    {
        /// <summary>中文语言代码。</summary>
        public const string Chinese = "zh";
        /// <summary>英文语言代码。</summary>
        public const string English = "en";

        private static string _current = Chinese;
        private static Dictionary<string, string> _map;

        /// <summary>当前语言代码（zh / en）。</summary>
        public static string Current { get { return _current; } }

        /// <summary>当前是否为英文界面。</summary>
        public static bool IsEnglish { get { return _current == English; } }

        /// <summary>语言设置的保存位置（%APPDATA%\SharpGIS\language.txt）。</summary>
        public static string SettingsPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SharpGIS");
                return Path.Combine(dir, "language.txt");
            }
        }

        /// <summary>启动时读取上次选择的语言；读不到就用中文。</summary>
        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string code = File.ReadAllText(SettingsPath, Encoding.UTF8).Trim().ToLowerInvariant();
                    if (code == English) { _current = English; return; }
                }
            }
            catch { }
            _current = Chinese;
        }

        /// <summary>写入语言选择（下次启动生效）。</summary>
        /// <param name="code">zh 或 en。</param>
        public static void Save(string code)
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, code == English ? English : Chinese, new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>在进程内切换语言（只影响之后创建的界面）。</summary>
        /// <param name="code">zh 或 en。</param>
        public static void Use(string code)
        {
            _current = code == English ? English : Chinese;
        }

        /// <summary>取译文：中文键 → 当前语言。无译文时原样返回。</summary>
        /// <param name="chinese">源码里的中文文案（同时是字典键）。</param>
        public static string T(string chinese)
        {
            if (string.IsNullOrEmpty(chinese) || _current != English) return chinese;
            string text;
            return Map.TryGetValue(chinese, out text) && !string.IsNullOrEmpty(text) ? text : chinese;
        }

        /// <summary>取译文并格式化（译文里同样用 {0} 占位）。</summary>
        /// <param name="chinese">中文格式串。</param>
        /// <param name="args">格式化参数。</param>
        public static string T(string chinese, params object[] args)
        {
            string format = T(chinese);
            if (args == null || args.Length == 0) return format;
            try { return string.Format(CultureInfo.CurrentCulture, format, args); }
            catch (FormatException) { return format; }
        }

        /// <summary>文本里是否含中日韩字符（自检用：英文模式下不应残留中文）。</summary>
        /// <param name="text">待检查文本。</param>
        public static bool HasChinese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
            return false;
        }

        /// <summary>
        /// 文本里是否含中文标点（自检用：英文模式下不应出现「：」「。」这类符号）。
        /// 只查真正的中文标点，不查 ≈ ≠ → … × Σ ≤ 这类数学符号——
        /// 它们在英文界面里同样是正确的写法。
        /// </summary>
        /// <param name="text">待检查文本。</param>
        public static bool HasChinesePunctuation(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if ("，。；：！？、（）【】《」「』〔〕【】".IndexOf(c) >= 0) return true;
            return false;
        }

        /// <summary>已收录的译文条数（自检用）。</summary>
        public static int EntryCount { get { return Map.Count; } }

        /// <summary>
        /// 抽样若干条译文（自检用）。按中文键排序后取前 count 条，
        /// 保证同一份译文表每次抽样结果一致。
        /// </summary>
        /// <param name="count">最多返回条数。</param>
        /// <returns>中文键 → 译文的键值对列表。</returns>
        public static List<KeyValuePair<string, string>> SampleEntries(int count)
        {
            var list = new List<KeyValuePair<string, string>>(Map);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            if (list.Count > count) list.RemoveRange(count, list.Count - count);
            return list;
        }

        private static Dictionary<string, string> Map
        {
            get
            {
                if (_map == null)
                {
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    RegisterPart1(map);
                    RegisterPart2(map);
                    RegisterPart3(map);
                    _map = map;
                }
                return _map;
            }
        }
    }
}
