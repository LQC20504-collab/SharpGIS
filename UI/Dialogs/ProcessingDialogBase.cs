using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.Processing;
using SharpGIS.Services;

namespace SharpGIS.UI.Dialogs
{
    /// <summary>处理对话框基类：输出路径、完成后加载、确定/取消与后台执行。</summary>
    public abstract class ProcessingDialogBase : Form
    {
        protected readonly RasterLayer Layer;
        private readonly TextBox _output;
        private readonly CheckBox _loadAfter;
        private readonly Button _ok;
        private readonly Button _cancel;
        private int _y = 12;
        private int _lastBottom = 12;

        public string OutputPath { get; private set; }
        public bool ShouldLoad { get; private set; }

        protected ProcessingDialogBase(RasterLayer layer, string title, string defaultSuffix,
            string extension = ".tif", int clientWidth = 520)
        {
            Layer = layer;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(clientWidth, 300);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.UIFont;
            AutoScaleMode = AutoScaleMode.None;

            int controlWidth = 300;
            _output = new TextBox
            {
                Left = 118,
                Top = _y,
                Width = controlWidth,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _output.Text = layer != null
                ? ProcessingUtil.SuggestOutputPath(layer.SourcePath, defaultSuffix, extension)
                : ProcessingUtil.SuggestOutputPath("output", defaultSuffix, extension);

            var browse = new Button { Text = "…", Left = 118 + controlWidth + 8, Top = _y - 1, Width = 48, Height = 24 };
            browse.Click += (s, e) => BrowseOutput();

            AddLabel(Lang.T("输出文件"), _y);
            Controls.Add(_output);
            Controls.Add(browse);
            _y += 30;

            _loadAfter = new CheckBox
            {
                Text = Lang.T("完成后加载为图层"),
                Left = 118,
                Top = _y,
                Width = 260,
                Checked = true,
                ForeColor = Theme.Text
            };
            Controls.Add(_loadAfter);
            _y += 30;   // 必须让出这一行，否则派生类的第一行控件会盖住该复选框

            _ok = new Button { Text = Lang.T("开始执行"), Width = 96, Height = 30 };
            _cancel = new Button { Text = Lang.T("取消"), Width = 80, Height = 30, DialogResult = DialogResult.Cancel };
            _ok.Click += (s, e) => OnExecute();
            Controls.Add(_ok);
            Controls.Add(_cancel);
            AcceptButton = _ok;
            CancelButton = _cancel;

            Load += (s, e) =>
            {
                int height = _y + 56;
                ClientSize = new Size(ClientSize.Width, height);
                _ok.Left = ClientSize.Width - 210;
                _ok.Top = ClientSize.Height - 44;
                _cancel.Left = ClientSize.Width - 108;
                _cancel.Top = ClientSize.Height - 44;
            };
        }

        protected void AddLabel(string text, int top)
        {
            Controls.Add(new Label
            {
                Text = text,
                Left = 14,
                Top = top + 3,
                Width = 100,
                Height = 20,
                ForeColor = Theme.TextDim,
                TextAlign = ContentAlignment.MiddleLeft
            });
        }

        /// <summary>添加一行 “标签 + 控件”。</summary>
        /// <summary>添加一行「标签 + 控件」。</summary>
        /// <param name="label">行标签文本。</param>
        /// <param name="control">要放置的控件。</param>
        /// <param name="width">控件宽度。</param>
        /// <param name="height">控件高度；大于 0 时会覆盖控件默认高度。</param>
        /// <returns>传入的控件，便于链式设置。</returns>
        protected T Row<T>(string label, T control, int width = 300, int height = 0) where T : Control
        {
            AddLabel(label, _y);
            control.Left = 118;
            control.Top = _y;
            control.Width = width;
            if (height > 0) control.Height = height;
            Controls.Add(control);
            _y += Math.Max(28, control.Height + 6);
            _lastBottom = _y;
            return control;
        }

        /// <summary>一行内并排放置多个控件（左标签，随后按给定宽度依次排列）。</summary>
        /// <param name="label">行标签。</param>
        /// <param name="controls">要并排放置的控件。</param>
        /// <param name="widths">各控件宽度，需与 controls 长度一致。</param>
        /// <param name="gap">控件间距。</param>
        protected void RowInline(string label, Control[] controls, int[] widths, int gap = 6)
        {
            AddLabel(label, _y);
            int x = 118;
            for (int i = 0; i < controls.Length; i++)
            {
                var control = controls[i];
                control.Left = x;
                control.Top = _y;
                control.Width = i < widths.Length ? widths[i] : 120;
                Controls.Add(control);
                x += control.Width + gap;
            }
            _y += 28;
            _lastBottom = _y;
        }

        /// <summary>
        /// 放置一个占满内容宽度的大控件（如 TabControl），为派生类的分页布局让出整块区域。
        /// 与 <see cref="Row"/> 不同，它不画左侧标签，左边距取 14。
        /// </summary>
        /// <typeparam name="T">控件类型。</typeparam>
        /// <param name="control">要放置的控件。</param>
        /// <param name="height">控件高度；≤0 时使用控件自身高度。</param>
        /// <returns>传入的控件，便于链式设置。</returns>
        protected T Block<T>(T control, int height) where T : Control
        {
            if (control == null) throw new ArgumentNullException("control");
            control.Left = 14;
            control.Top = _y;
            control.Width = ClientSize.Width - 28;
            if (height > 0) control.Height = height;
            Controls.Add(control);
            _y += control.Height + 8;
            _lastBottom = _y;
            return control;
        }

        protected void Separator()
        {
            Controls.Add(new Label
            {
                Left = 14,
                Top = _y,
                Width = 470,
                Height = 1,
                BackColor = Theme.Border
            });
            _y += 10;
        }

        protected void Note(string text, int height = 40)
        {
            Note(text, height, 380);
        }

        /// <summary>添加一段说明文字，可指定宽度（用于加宽的对话框）。</summary>
        /// <param name="text">说明文字。</param>
        /// <param name="height">高度。</param>
        /// <param name="width">宽度。</param>
        protected void Note(string text, int height, int width)
        {
            Controls.Add(new Label
            {
                Text = text,
                Left = 118,
                Top = _y,
                Width = width,
                Height = height,
                ForeColor = Theme.TextDim
            });
            _y += height + 4;
            _lastBottom = _y;
        }

        protected ComboBox Combo(params string[] items)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat
            };
            c.Items.AddRange(items);
            if (c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }

        protected NumericUpDown Num(double min, double max, double value, int decimals = 0, double step = 1)
        {
            return new NumericUpDown
            {
                Minimum = (decimal)min,
                Maximum = (decimal)max,
                Value = (decimal)Math.Max(min, Math.Min(max, value)),
                DecimalPlaces = decimals,
                Increment = (decimal)step,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        protected TextBox Text2(string text = "")
        {
            return new TextBox
            {
                Text = text,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private void BrowseOutput()
        {
            string ext = System.IO.Path.GetExtension(_output.Text);
            if (string.IsNullOrEmpty(ext)) ext = ".tif";
            using (var dlg = new SaveFileDialog
            {
                Title = Lang.T("选择输出文件"),
                FileName = System.IO.Path.GetFileName(_output.Text),
                Filter = Lang.T("TIFF 影像|*.tif;*.tiff|IMG 影像|*.img|PNG 图片|*.png|JPEG 图片|*.jpg|VRT 虚拟格式|*.vrt|所有文件|*.*")
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _output.Text = dlg.FileName;
            }
        }

        private void OnExecute()
        {
            if (string.IsNullOrWhiteSpace(_output.Text))
            {
                MessageBox.Show(this, Lang.T("请指定输出文件"), Lang.T("提示"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (System.IO.File.Exists(_output.Text))
            {
                // 文件被占用（常见情形：它正作为图层打开着）时 Windows 无法删除覆盖，
                // GDAL 只会抛出 Permission denied，这里提前拦下并给出可用新名。
                if (!ProcessingUtil.IsFileWritable(_output.Text))
                {
                    string suggestion = ProcessingUtil.SuggestFreePath(_output.Text);
                    string message = string.Format(
                        Lang.T("目标文件无法写入：\r\n{0}\r\n\r\n") +
                        Lang.T("常见原因是它已作为图层打开、被其他程序锁定，或文件被设为只读。\r\n\r\n是否改写到：\r\n{1}？"),
                        _output.Text, System.IO.Path.GetFileName(suggestion));
                    if (MessageBox.Show(this, message, Lang.T("文件被占用"),
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                    _output.Text = suggestion;
                }
                else if (MessageBox.Show(this, Lang.T("输出文件已存在，是否覆盖？"), Lang.T("确认"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            string error;
            if (!ValidateInputs(out error))
            {
                MessageBox.Show(this, error, Lang.T("参数有误"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 关键：先把控件值采集为普通字段，再进入后台线程。
            // RunCore 在后台线程执行，任何对控件属性的读取都会触发“线程间操作无效”。
            CaptureParameters();
            string output = _output.Text;

            var job = JobRunner.Run(this, Text, reporter => RunCore(reporter, output));

            if (job.IsCancelled)
            {
                // 算子已尽力清理；这里再兜底清一次旁文件，保持对话框以便改参数重试
                ProcessingUtil.DeleteOutputSet(_output.Text);
                return;
            }
            if (!job.Ok)
            {
                MessageBox.Show(this, job.Error, Lang.T("处理失败"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            OutputPath = job.Path;
            ShouldLoad = _loadAfter.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>校验界面参数；返回 false 时应给出面向用户的中文提示。</summary>
        /// <param name="error">校验失败时的提示文本。</param>
        /// <returns>参数是否可用。</returns>
        protected virtual bool ValidateInputs(out string error)
        {
            error = null;
            return true;
        }

        /// <summary>
        /// 在 UI 线程把界面参数采集为普通字段，供 <see cref="RunCore"/> 在后台线程使用。
        /// 派生类若要新增参数，必须在这里取值；直接在 RunCore 里读控件会在调试运行时抛
        /// “线程间操作无效：从不是创建控件的线程访问它”。
        /// </summary>
        protected virtual void CaptureParameters() { }

        /// <summary>在后台线程执行实际处理。实现中不得访问任何控件成员。</summary>
        /// <param name="reporter">进度上报器，同时承担取消检查。</param>
        /// <param name="outputPath">用户确认的输出文件路径。</param>
        /// <returns>处理完成后的输出文件路径。</returns>
        protected abstract string RunCore(ProgressReporter reporter, string outputPath);

        // ---------------- 常用小工具 ----------------

        protected static int[] ParseBands(string text, int bandCount)
        {
            var list = new List<int>();
            foreach (var part in (text ?? "").Split(new[] { ',', '，', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v;
                if (int.TryParse(part, out v) && v >= 1 && v <= bandCount && !list.Contains(v)) list.Add(v);
            }
            return list.ToArray();
        }

        protected static string[] BandItems(int count)
        {
            var items = new string[count];
            for (int i = 0; i < count; i++) items[i] = Lang.T("波段 ") + (i + 1);
            return items;
        }
    }
}
