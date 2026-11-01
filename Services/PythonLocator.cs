using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using SharpGIS.Services;
namespace SharpGIS.Services
{
    /// <summary>一次 Python 环境探测的结果。</summary>
    public sealed class PythonEnvironment
    {
        /// <summary>解释器完整路径；未找到时为 null。</summary>
        public string ExePath;
        /// <summary>Python 版本号，例如 3.10.20。</summary>
        public string Version;
        /// <summary>缺失的依赖包（numpy/scipy/sklearn/rasterio/matplotlib）。</summary>
        public string[] MissingPackages = new string[0];
        /// <summary>探测失败的可读原因。</summary>
        public string Error;

        /// <summary>是否可以直接用来跑 CA-Markov 引擎。</summary>
        public bool Usable
        {
            get
            {
                return !string.IsNullOrEmpty(ExePath)
                    && !string.IsNullOrEmpty(Version)
                    && MissingPackages.Length == 0;
            }
        }

        /// <summary>缺失依赖时给用户的可复制安装命令。</summary>
        public string InstallHint
        {
            get
            {
                if (MissingPackages.Length == 0) return string.Empty;
                return "\"" + (ExePath ?? "python") + "\" -m pip install "
                     + string.Join(" ", MissingPackages);
            }
        }

        /// <summary>一行摘要，用于界面提示。</summary>
        public string Describe()
        {
            if (!string.IsNullOrEmpty(Error)) return Error;
            if (string.IsNullOrEmpty(ExePath)) return Lang.T("未找到可用的 Python 解释器");
            if (MissingPackages.Length > 0)
                return string.Format(Lang.T("Python {0} 缺少依赖：{1}\r\n可在命令行执行：\r\n{2}"),
                    Version, string.Join(Lang.IsEnglish ? ", " : "、", MissingPackages), InstallHint);
            return string.Format("Python {0} · {1}", Version, ExePath);
        }
    }

    /// <summary>
    /// 定位一个带齐 numpy/scipy/sklearn/rasterio/matplotlib 的 Python 解释器。
    /// 顺序：用户上次选定 → 环境变量 SHARPGIS_PYTHON → 常见 conda/系统路径 → PATH。
    /// 结果缓存，避免每次打开对话框都起进程探测。
    /// </summary>
    public static class PythonLocator
    {
        /// <summary>引擎需要的依赖包。</summary>
        public static readonly string[] RequiredPackages =
            { "numpy", "scipy", "sklearn", "rasterio", "matplotlib" };

        private static PythonEnvironment _cached;
        private static readonly object Gate = new object();

        /// <summary>保存用户选定解释器的文件（%APPDATA%\SharpGIS\camarkov_python.txt）。</summary>
        public static string SettingsPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SharpGIS");
                return Path.Combine(dir, "camarkov_python.txt");
            }
        }

        /// <summary>用户手工选定的解释器路径；未设置时为空串。</summary>
        public static string SavedPath
        {
            get
            {
                try
                {
                    if (File.Exists(SettingsPath)) return File.ReadAllText(SettingsPath, Encoding.UTF8).Trim();
                }
                catch { }
                return string.Empty;
            }
            set
            {
                try
                {
                    string dir = Path.GetDirectoryName(SettingsPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    if (string.IsNullOrEmpty(value))
                    {
                        if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
                    }
                    else
                    {
                        File.WriteAllText(SettingsPath, value.Trim(), new UTF8Encoding(false));
                    }
                }
                catch { }
                lock (Gate) { _cached = null; }
            }
        }

        /// <summary>找到可用解释器（带缓存）。</summary>
        /// <param name="forceRefresh">true 时忽略缓存重新探测。</param>
        public static PythonEnvironment Find(bool forceRefresh = false)
        {
            lock (Gate)
            {
                if (!forceRefresh && _cached != null) return _cached;
            }

            PythonEnvironment fallback = null;
            foreach (var candidate in Candidates())
            {
                var env = Probe(candidate);
                if (env.Usable)
                {
                    lock (Gate) { _cached = env; }
                    return env;
                }
                if (fallback == null && !string.IsNullOrEmpty(env.ExePath)) fallback = env;
            }

            if (fallback == null)
                fallback = new PythonEnvironment { Error = Lang.T("未找到 Python 解释器。请安装 Python 3.8+ 并执行：\r\n")
                    + "python -m pip install " + string.Join(" ", RequiredPackages) };
            lock (Gate) { _cached = fallback; }
            return fallback;
        }

        /// <summary>按优先级列出候选解释器路径。</summary>
        public static string[] Candidates()
        {
            var list = new List<string>();
            void Add(string p)
            {
                if (string.IsNullOrWhiteSpace(p)) return;
                try { p = p.Trim().Trim('"'); } catch { }
                if (!list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
            }

            Add(SavedPath);
            Add(Environment.GetEnvironmentVariable("SHARPGIS_PYTHON"));
            Add(Environment.GetEnvironmentVariable("CONDA_PYTHON_EXE"));

            // 常见 conda 安装位置（本机与通用布局）
            Action<string> addRoot = root =>
            {
                Add(Path.Combine(root, "python.exe"));
                string envs = Path.Combine(root, "envs");
                try
                {
                    if (Directory.Exists(envs))
                    {
                        var dirs = Directory.GetDirectories(envs);
                        Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                        foreach (var d in dirs) Add(Path.Combine(d, "python.exe"));
                    }
                }
                catch { }
            };

            addRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "miniconda3"));
            addRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "anaconda3"));
            addRoot(@"C:\ProgramData\miniconda3");
            addRoot(@"C:\ProgramData\anaconda3");
            addRoot(@"C:\Python310");
            addRoot(@"C:\Python311");
            addRoot(@"C:\Python312");
            addRoot(@"C:\Python313");
            addRoot(@"C:\Python314");

            // conda 装在非系统盘（如 D:\Users\<用户>\miniconda3）也很常见，扫一遍固定磁盘
            try
            {
                string user = Environment.UserName;
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    string root = drive.RootDirectory.FullName;
                    addRoot(Path.Combine(root, "miniconda3"));
                    addRoot(Path.Combine(root, "anaconda3"));
                    addRoot(Path.Combine(root, "Users", user, "miniconda3"));
                    addRoot(Path.Combine(root, "Users", user, "anaconda3"));
                }
            }
            catch { }

            // PATH
            try
            {
                string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var part in pathVar.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    string exe = Path.Combine(part.Trim(), "python.exe");
                    if (File.Exists(exe)) Add(exe);
                }
            }
            catch { }

            var existing = new List<string>();
            foreach (var p in list)
            {
                try { if (File.Exists(p)) existing.Add(p); } catch { }
            }
            return existing.ToArray();
        }

        /// <summary>探测一个解释器：取版本号并检查依赖包是否可导入。</summary>
        public static PythonEnvironment Probe(string exePath)
        {
            var env = new PythonEnvironment { ExePath = exePath };
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                env.Error = Lang.T("解释器不存在：") + exePath;
                return env;
            }

            // 只做 find_spec（毫秒级），不真正 import matplotlib，避免拖慢对话框
            const string code =
                "import sys,importlib.util as u\r\n" +
                "mods=['numpy','scipy','sklearn','rasterio','matplotlib']\r\n" +
                "miss=[m for m in mods if u.find_spec(m) is None]\r\n" +
                "print('VERSION='+sys.version.split()[0])\r\n" +
                "print('MISSING='+','.join(miss))\r\n";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "-c \"" + code.Replace("\"", "\\\"") + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                // 注意：不要给 ProcessStartInfo.EnvironmentVariables 赋值。
                // 某些环境里同一个变量同时存在大小写两种写法（如 HTTPS_PROXY / https_proxy），
                // .NET Framework 复制环境块时会抛“已添加项”异常，导致任何子进程都起不来。

                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    string err = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(10000))
                    {
                        try { p.Kill(); } catch { }
                        env.Error = Lang.T("解释器探测超时：") + exePath;
                        return env;
                    }

                    foreach (var raw in output.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("VERSION=", StringComparison.Ordinal))
                            env.Version = line.Substring(8).Trim();
                        else if (line.StartsWith("MISSING=", StringComparison.Ordinal))
                        {
                            string rest = line.Substring(8).Trim();
                            env.MissingPackages = rest.Length == 0
                                ? new string[0]
                                : rest.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        }
                    }

                    if (string.IsNullOrEmpty(env.Version))
                    {
                        env.Error = Lang.T("无法启动解释器：") + exePath
                            + (string.IsNullOrWhiteSpace(err) ? "" : "\r\n" + Shorten(err, 300));
                    }
                }
            }
            catch (Exception ex)
            {
                env.Error = Lang.T("探测 Python 失败：") + ex.Message;
            }
            return env;
        }

        /// <summary>截断长文本，保留末尾（异常栈的关键信息通常在末尾）。</summary>
        internal static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
            return "…" + text.Substring(text.Length - max);
        }
    }
}
