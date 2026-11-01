using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using SharpGIS.Services;

namespace SharpGIS.Processing
{
    /// <summary>一次引擎调用的请求：参数 + 输出位置。</summary>
    public sealed class CaMarkovRequest
    {
        /// <summary>算法参数。</summary>
        public CaMarkovParams Params = new CaMarkovParams();
        /// <summary>预测栅格输出路径（analyze 模式忽略）。</summary>
        public string RasterPath;
        /// <summary>报告输出路径；为空表示不写报告。</summary>
        public string ReportPath;
        /// <summary>图表输出目录；为空表示不出图。</summary>
        public string FigureDirectory;
        /// <summary>训练后另存模型的路径；为空表示不保存。</summary>
        public string ModelToSave;
        /// <summary>超时分钟数。</summary>
        public int TimeoutMinutes = 30;
    }

    /// <summary>
    /// CA-Markov 引擎的外部进程封装：负责准备参数文件、启动 Python、解析指标。
    /// 算法本身全在 Engine\camarkov_engine.py 里，C# 侧只做编排与结果解析。
    /// </summary>
    public static class CaMarkovEngine
    {
        /// <summary>引擎脚本路径（随程序输出目录一起复制）。</summary>
        public static string ScriptPath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    Path.Combine("Engine", "camarkov_engine.py"));
            }
        }

        /// <summary>引擎脚本是否存在。</summary>
        public static bool ScriptAvailable
        {
            get { return File.Exists(ScriptPath); }
        }

        /// <summary>执行一次引擎调用。</summary>
        /// <param name="request">参数与输出位置。</param>
        /// <param name="reporter">进度上报器（同时提供取消信号）。</param>
        /// <returns>解析后的结果。</returns>
        public static CaMarkovResult Run(CaMarkovRequest request, ProgressReporter reporter)
        {
            if (request == null) throw new ArgumentNullException("request");
            var p = request.Params ?? new CaMarkovParams();

            if (!ScriptAvailable)
                throw new InvalidOperationException(
                    Lang.T("找不到 CA-Markov 引擎脚本：\r\n") + ScriptPath
                    + Lang.T("\r\n请确认程序目录下的 Engine 文件夹完整（camarkov_engine.py）。"));

            var env = PythonLocator.Find();
            if (!env.Usable)
                throw new InvalidOperationException(Lang.T("无法运行 CA-Markov 引擎：\r\n") + env.Describe());

            string workDir = Path.Combine(Path.GetTempPath(), "SharpGIS",
                "camarkov_" + Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(workDir);

            // 子进程工作目录是 Engine 目录，所有路径必须先转成绝对路径
            p.MakePathsAbsolute();
            if (!string.IsNullOrWhiteSpace(request.RasterPath))
                request.RasterPath = Path.GetFullPath(request.RasterPath);
            if (!string.IsNullOrWhiteSpace(request.ReportPath))
                request.ReportPath = Path.GetFullPath(request.ReportPath);
            if (!string.IsNullOrWhiteSpace(request.FigureDirectory))
                request.FigureDirectory = Path.GetFullPath(request.FigureDirectory);
            if (!string.IsNullOrWhiteSpace(request.ModelToSave))
                request.ModelToSave = Path.GetFullPath(request.ModelToSave);

            string metricsPath = Path.Combine(workDir, "metrics.json");
            var outputs = new Dictionary<string, object>
            {
                { "metrics", metricsPath },
                { "fig_dir", string.IsNullOrWhiteSpace(request.FigureDirectory) ? null : request.FigureDirectory },
                { "raster", p.Mode == CaPredictionMode.Analyze ? null : request.RasterPath },
                { "report", string.IsNullOrWhiteSpace(request.ReportPath) ? null : request.ReportPath },
                { "model", string.IsNullOrWhiteSpace(request.ModelToSave) ? null : request.ModelToSave },
                { "compare_png", null },
            };

            string jobJson = CaMarkovParams.SerializeJson(p.ToJob(outputs));
            string jobPath = Path.Combine(workDir, "job.json");
            // UTF-8 无 BOM：Python 侧 json.load 对 BOM 敏感
            File.WriteAllText(jobPath, jobJson, new UTF8Encoding(false));

            try
            {
                if (reporter != null) reporter.ThrowIfCancellationRequested();

                var run = PythonJobRunner.Run(env.ExePath, ScriptPath, jobPath, reporter,
                    request.TimeoutMinutes);

                if (run.ExitCode != 0)
                {
                    string detail = LastLines(run.StdErr, 40);
                    string head = run.ExitCode == 1
                        ? Lang.T("CA-Markov 参数或数据有误")
                        : Lang.T("CA-Markov 引擎运行失败（退出码 {0}）", run.ExitCode);
                    if (!string.IsNullOrEmpty(run.LastInfo)) head += Lang.T("\r\n最后阶段：") + run.LastInfo;
                    throw new InvalidOperationException(head
                        + (string.IsNullOrEmpty(detail) ? "" : "\r\n\r\n" + detail));
                }

                if (!File.Exists(metricsPath))
                    throw new InvalidOperationException(Lang.T("引擎没有生成指标文件，可能被提前中断：\r\n")
                        + metricsPath);

                var json = File.ReadAllText(metricsPath, Encoding.UTF8);
                var result = CaMarkovResult.Parse(json);
                if (result.Classes.Count == 0)
                    throw new InvalidOperationException(
                        Lang.T("引擎返回的指标为空或格式不符：\r\n") + FirstChars(json, 400));
                result.MetricsPath = metricsPath;
                if (string.IsNullOrEmpty(result.RasterPath)) result.RasterPath = request.RasterPath;
                if (string.IsNullOrEmpty(result.ReportPath)) result.ReportPath = request.ReportPath;
                if (string.IsNullOrEmpty(result.ModelPath)) result.ModelPath = request.ModelToSave;
                foreach (var w in run.Warnings)
                    if (!result.Warnings.Contains(w)) result.Warnings.Add(w);
                return result;
            }
            finally
            {
                // 指标已解析为对象，临时目录可以整个删掉
                try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { }
            }
        }

        /// <summary>只做转移矩阵分析（「先分析」按钮）。</summary>
        /// <param name="p">参数；内部会复制一份并切换到 Analyze 模式。</param>
        /// <param name="figureDirectory">图表目录，可空。</param>
        /// <param name="reporter">进度上报器。</param>
        public static CaMarkovResult Analyze(CaMarkovParams p, string figureDirectory,
            ProgressReporter reporter)
        {
            var copy = p.Clone();
            copy.Mode = CaPredictionMode.Analyze;
            copy.Validate = false;
            copy.WriteReport = false;
            return Run(new CaMarkovRequest
            {
                Params = copy,
                FigureDirectory = figureDirectory,
                TimeoutMinutes = 10,
            }, reporter);
        }

        /// <summary>取文本开头若干字符，用于把解析失败的内容回显出来。</summary>
        private static string FirstChars(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return Lang.T("（空文件）");
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        /// <summary>取标准错误输出的末尾若干行，避免把整段栈写进消息框。</summary>
        private static string LastLines(string text, int maxLines)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int start = Math.Max(0, lines.Length - maxLines);
            var sb = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (line.Length == 0) continue;
                sb.AppendLine(line);
            }
            return sb.ToString().Trim();
        }

        /// <summary>用系统默认程序打开文本报告。</summary>
        /// <param name="path">报告路径。</param>
        public static void OpenWithShell(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { }
        }
    }
}
