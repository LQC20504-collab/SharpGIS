using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using SharpGIS.Processing;

using SharpGIS.Services;
namespace SharpGIS.Services
{
    /// <summary>一次 Python 子进程执行的结果。</summary>
    public sealed class PythonRunResult
    {
        /// <summary>进程退出码：0 成功 / 1 参数或数据错误 / 2 运行时异常。</summary>
        public int ExitCode;
        /// <summary>标准错误输出（诊断信息与异常栈）。</summary>
        public string StdErr;
        /// <summary>最后一条 @@INFO 文本，用于失败时给出更贴切的提示。</summary>
        public string LastInfo;
        /// <summary>引擎发出的 @@WARN 警告文本。</summary>
        public readonly List<string> Warnings = new List<string>();
        /// <summary>是否因用户取消而中止。</summary>
        public bool Cancelled;
    }

    /// <summary>
    /// 启动 Python 引擎子进程并按行协议解析进度。
    /// 协议：@@PROGRESS &lt;0..1&gt; &lt;阶段&gt; / @@INFO &lt;文本&gt; / @@WARN &lt;文本&gt; / @@DONE
    /// 取消时先 Kill，再用 taskkill /T 清掉 joblib 派生的子进程。
    /// </summary>
    public static class PythonJobRunner
    {
        /// <summary>执行一次引擎调用。</summary>
        /// <param name="exePath">python.exe 路径。</param>
        /// <param name="scriptPath">camarkov_engine.py 路径。</param>
        /// <param name="jobPath">参数 JSON 路径。</param>
        /// <param name="reporter">进度上报器（同时也是取消信号来源）。</param>
        /// <param name="timeoutMinutes">超时分钟数。</param>
        /// <returns>进程结果；被取消时抛 <see cref="OperationCanceledException"/>。</returns>
        public static PythonRunResult Run(string exePath, string scriptPath, string jobPath,
            ProgressReporter reporter, int timeoutMinutes = 30)
        {
            if (string.IsNullOrWhiteSpace(exePath)) throw new ArgumentNullException("exePath");
            if (!File.Exists(scriptPath))
                throw new InvalidOperationException(Lang.T("找不到 CA-Markov 引擎脚本：\r\n") + scriptPath
                    + Lang.T("\r\n请确认程序目录下的 Engine 文件夹完整。"));
            if (!File.Exists(jobPath))
                throw new InvalidOperationException(Lang.T("找不到参数文件：") + jobPath);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "-u \"" + scriptPath + "\" --job \"" + jobPath + "\"",
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // 注意：绝不能给 psi.EnvironmentVariables 赋值。某些环境里同一个变量同时存在
            // 大小写两种写法（HTTPS_PROXY / https_proxy），.NET Framework 复制环境块会抛
            // “已添加项”异常，任何子进程都起不来。编码与无缓冲都在引擎侧处理（-u + reconfigure）。
            var result = new PythonRunResult();
            var stderr = new StringBuilder();
            var stderrLock = new object();

            using (var process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    try { ParseLine(e.Data, reporter, result); }
                    catch { }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (stderrLock)
                    {
                        stderr.AppendLine(e.Data);
                        if (stderr.Length > 64 * 1024) stderr.Remove(0, stderr.Length - 48 * 1024);
                    }
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(Lang.T("无法启动 Python 进程：\r\n") + exePath
                        + "\r\n" + ex.Message);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var deadline = DateTime.UtcNow.AddMinutes(Math.Max(1, timeoutMinutes));
                while (!process.HasExited)
                {
                    if (reporter != null && reporter.IsCancellationRequested)
                    {
                        KillTree(process);
                        result.Cancelled = true;
                        result.StdErr = Snapshot(stderr, stderrLock);
                        throw new OperationCanceledException(Lang.T("操作已取消"));
                    }
                    if (DateTime.UtcNow > deadline)
                    {
                        KillTree(process);
                        throw new InvalidOperationException(
                            Lang.T("Python 引擎运行超过 ") + timeoutMinutes + Lang.T(" 分钟，已强制结束。"));
                    }
                    Thread.Sleep(60);
                }

                process.WaitForExit(5000);
                result.ExitCode = process.ExitCode;
                result.StdErr = Snapshot(stderr, stderrLock);
                return result;
            }
        }

        /// <summary>解析一行协议输出。</summary>
        internal static void ParseLine(string line, ProgressReporter reporter, PythonRunResult result)
        {
            if (string.IsNullOrEmpty(line)) return;

            if (line.StartsWith("@@PROGRESS ", StringComparison.Ordinal))
            {
                string rest = line.Substring(11).Trim();
                int space = rest.IndexOf(' ');
                string fracText = space < 0 ? rest : rest.Substring(0, space);
                string message = space < 0 ? string.Empty : rest.Substring(space + 1).Trim();
                double frac;
                if (double.TryParse(fracText, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out frac) && reporter != null)
                {
                    // 不能调用 Report：它在取消时会抛异常，而这里位于 IO 回调线程
                    reporter.Publish(frac, message);
                }
            }
            else if (line.StartsWith("@@INFO ", StringComparison.Ordinal))
            {
                result.LastInfo = line.Substring(7).Trim();
            }
            else if (line.StartsWith("@@WARN ", StringComparison.Ordinal))
            {
                result.Warnings.Add(line.Substring(7).Trim());
            }
        }

        /// <summary>强制结束进程树（joblib 会派生子进程，只杀父进程会留下孤儿）。</summary>
        private static void KillTree(Process process)
        {
            try
            {
                int pid = process.Id;
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/T /F /PID " + pid,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var killer = Process.Start(psi))
                {
                    if (killer != null)
                    {
                        killer.StandardOutput.ReadToEnd();
                        killer.StandardError.ReadToEnd();
                        killer.WaitForExit(5000);
                    }
                }
            }
            catch { }
            try { if (!process.HasExited) process.Kill(); } catch { }
        }

        private static string Snapshot(StringBuilder sb, object gate)
        {
            lock (gate) { return sb.ToString(); }
        }
    }
}
