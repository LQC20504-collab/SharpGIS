using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using SharpGIS.Services;
namespace SharpGIS
{
    internal static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        private const int AttachParentProcess = -1;

        [STAThread]
        private static int Main(string[] args)
        {
            // WinExe 默认没有控制台；命令行模式下尝试挂到父控制台，便于查看自检输出
            bool hasArgs = args != null && args.Length > 0;
            if (hasArgs) TryAttachConsole();

            // 语言必须在任何界面类型初始化之前确定（静态字段里已经用了 Lang.T）
            Lang.Load();
            var cli = CommandLine.Parse(args);
            if (cli.Has("lang")) Lang.Use(cli.Get("lang"));

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            GdalBootstrap.Ensure();
            if (!GdalBootstrap.Usable)
                Console.WriteLine(GdalBootstrap.LastError);

            if (cli.Has("probe"))
            {
                int code = SelfTest.ApiProbe.Run(cli.Get("out"));
                ConsoleBridge.Detach();
                return code;
            }

            if (cli.Has("selftest"))
            {
                int code = SelfTest.SelfTestRunner.Run(cli);
                ConsoleBridge.Detach();
                return code;
            }

            if (!GdalBootstrap.Usable)
            {
                MessageBox.Show(GdalBootstrap.LastError, Lang.T("SharpGIS 启动失败"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Application.Run(new UI.MainForm());
            ConsoleBridge.Detach();
            return 0;
        }

        private static void TryAttachConsole()
        {
            try
            {
                AttachConsole(AttachParentProcess);
                var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(stdout);
                var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                Console.SetError(stderr);
                ConsoleBridge.Attached = true;
            }
            catch { }
        }
    }

    internal static class ConsoleBridge
    {
        public static bool Attached;

        public static void Write(string text)
        {
            if (Attached)
            {
                try { Console.WriteLine(text); } catch { }
            }
        }

        /// <summary>命令行模式收尾：只刷新输出。进程退出时控制台自然释放，
        /// 主动 FreeConsole 会让父 shell 丢失子进程状态（bash 会把退出码报成 127）。</summary>
        public static void Detach()
        {
            if (!Attached) return;
            try { Console.Out.Flush(); } catch { }
        }
    }

    /// <summary>极简命令行解析：--key=value 或 --flag。</summary>
    internal sealed class CommandLine
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _map =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static CommandLine Parse(string[] args)
        {
            var cl = new CommandLine();
            if (args == null) return cl;
            foreach (var raw in args)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string s = raw.TrimStart('-', '/');
                int eq = s.IndexOf('=');
                if (eq > 0) cl._map[s.Substring(0, eq)] = s.Substring(eq + 1);
                else cl._map[s] = "1";
            }
            return cl;
        }

        public bool Has(string key) { return _map.ContainsKey(key); }

        public string Get(string key, string fallback = null)
        {
            string v;
            return _map.TryGetValue(key, out v) ? v : fallback;
        }
    }
}
