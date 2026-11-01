using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using SharpGIS.Processing;
using SharpGIS.UI;

using SharpGIS.Services;
namespace SharpGIS.Services
{
    /// <summary>后台任务的三种结局。</summary>
    public enum JobOutcome
    {
        /// <summary>成功完成并返回输出路径。</summary>
        Succeeded,
        /// <summary>执行失败，<see cref="JobResult.Error"/> 为可读原因。</summary>
        Failed,
        /// <summary>用户取消，或算子因取消而中止。</summary>
        Cancelled
    }

    /// <summary>后台任务的执行结果，用于区分“失败”与“已取消”（不再依赖错误文案比较）。</summary>
    public struct JobResult
    {
        /// <summary>结局类型。</summary>
        public JobOutcome Outcome;
        /// <summary>成功时的输出文件路径。</summary>
        public string Path;
        /// <summary>失败时的可读错误信息。</summary>
        public string Error;

        /// <summary>是否成功。</summary>
        public bool Ok { get { return Outcome == JobOutcome.Succeeded; } }

        /// <summary>是否被取消。</summary>
        public bool IsCancelled { get { return Outcome == JobOutcome.Cancelled; } }

        internal static JobResult Succeeded(string path)
        {
            return new JobResult { Outcome = JobOutcome.Succeeded, Path = path };
        }

        internal static JobResult Failed(string error)
        {
            return new JobResult { Outcome = JobOutcome.Failed, Error = error };
        }

        internal static JobResult CancelledResult()
        {
            return new JobResult { Outcome = JobOutcome.Cancelled };
        }
    }

    /// <summary>后台任务执行器：模态进度框 + 取消，异常与取消统一归一化。</summary>
    public static class JobRunner
    {
        /// <summary>在后台线程执行一次处理任务，并显示可取消的模态进度框。</summary>
        /// <param name="owner">进度框的父窗口。</param>
        /// <param name="title">进度框标题，通常为算子名称。</param>
        /// <param name="work">实际执行体，接收进度上报器并返回输出文件路径。</param>
        /// <returns>包含结局、输出路径或错误信息的结果对象。</returns>
        public static JobResult Run(IWin32Window owner, string title, Func<ProgressReporter, string> work)
        {
            using (var form = new ProgressForm(title))
            {
                var reporter = new ProgressReporter(form.SetProgress, () => form.Cancelled, 0.01);
                var thread = new Thread(() =>
                {
                    try
                    {
                        form.Result = work(reporter);
                    }
                    catch (OperationCanceledException)
                    {
                        form.WorkerCanceled = true;
                    }
                    catch (Exception ex)
                    {
                        form.Error = ex;
                    }
                    finally
                    {
                        form.Finish();
                    }
                });
                thread.IsBackground = true;
                thread.Name = "SharpGIS-Job";
                thread.Start();

                form.ShowDialog(owner);
                // 等算子把自己产生的半成品清理完再判定结果
                thread.Join(5000);

                return Classify(form.Result, form.Error, form.WorkerCanceled, form.Cancelled);
            }
        }

        /// <summary>
        /// 结果归类（纯函数，便于自检直接断言）。优先级：异常 → 取消 → 有产物 → 用户取消但无异常。
        /// </summary>
        /// <param name="result">算子返回的输出路径，未完成时为 null。</param>
        /// <param name="error">算子抛出的异常，未失败时为 null。</param>
        /// <param name="workerCanceled">工作线程是否捕获到取消异常。</param>
        /// <param name="userCancelled">用户是否点击过取消。</param>
        /// <returns>归一化后的任务结果。</returns>
        internal static JobResult Classify(string result, Exception error, bool workerCanceled, bool userCancelled)
        {
            if (error != null) return JobResult.Failed(error.Message);
            if (workerCanceled) return JobResult.CancelledResult();
            if (!string.IsNullOrEmpty(result)) return JobResult.Succeeded(result);
            if (userCancelled) return JobResult.CancelledResult();
            return JobResult.Failed(Lang.T("算子未返回输出路径"));
        }
    }

    /// <summary>进度框。工作线程只写字段，界面刷新全部在 UI 线程的定时器里完成。</summary>
    public sealed class ProgressForm : Form
    {
        private readonly ProgressBar _bar;
        private readonly Label _label;
        private readonly Button _cancel;
        private readonly System.Windows.Forms.Timer _timer;

        private volatile int _percent;
        private volatile string _message = Lang.T("正在处理…");
        private volatile bool _finished;
        private volatile bool _cancelled;

        internal string Result;
        internal Exception Error;
        internal volatile bool WorkerCanceled;

        /// <summary>用户是否点击了取消。</summary>
        public bool Cancelled { get { return _cancelled; } }

        /// <summary>创建一个固定大小的模态进度框。</summary>
        /// <param name="title">窗口标题。</param>
        public ProgressForm(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 132);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.UIFont;
            AutoScaleMode = AutoScaleMode.None;

            _label = new Label
            {
                Left = 16,
                Top = 14,
                Width = 388,
                Height = 22,
                Text = Lang.T("准备中…"),
                ForeColor = Theme.Text
            };

            _bar = new ProgressBar
            {
                Left = 16,
                Top = 42,
                Width = 388,
                Height = 18,
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100
            };

            _cancel = new Button
            {
                Text = Lang.T("取消"),
                Left = 324,
                Top = 76,
                Width = 80,
                Height = 28
            };
            _cancel.Click += (s, e) =>
            {
                _cancelled = true;
                _cancel.Enabled = false;
                _label.Text = Lang.T("正在取消…");
            };

            Controls.Add(_label);
            Controls.Add(_bar);
            Controls.Add(_cancel);

            _timer = new System.Windows.Forms.Timer { Interval = 100 };
            _timer.Tick += (s, e) =>
            {
                _bar.Value = Math.Max(0, Math.Min(100, _percent));
                if (!_cancelled) _label.Text = _message ?? "";
                if (_finished) { _timer.Stop(); Close(); }
            };
            _timer.Start();

            FormClosing += (s, e) =>
            {
                if (!_finished && !_cancelled)
                {
                    e.Cancel = true;
                    _cancelled = true;
                    _cancel.Enabled = false;
                    _label.Text = Lang.T("正在取消…");
                }
            };
        }

        /// <summary>由工作线程调用的进度写入，只更新字段，不触碰控件。</summary>
        /// <param name="complete">0~1 的完成度。</param>
        /// <param name="message">进度消息。</param>
        public void SetProgress(double complete, string message)
        {
            _percent = (int)Math.Round(Math.Max(0, Math.Min(1, complete)) * 100);
            if (!string.IsNullOrEmpty(message)) _message = message;
        }

        /// <summary>由工作线程在结束时调用，触发定时器关闭窗口。</summary>
        public void Finish()
        {
            _finished = true;
        }
    }
}
