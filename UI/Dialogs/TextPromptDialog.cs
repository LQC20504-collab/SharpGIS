using System;
using System.Drawing;
using System.Windows.Forms;

using SharpGIS.Services;
namespace SharpGIS.UI.Dialogs
{
    /// <summary>
    /// 单行文本输入对话框（项目里没有系统 InputBox，这里给一个暗色风格的最小实现）。
    /// 用于图层重命名等只需要一个字符串的场景。
    /// </summary>
    internal sealed class TextPromptDialog : Form
    {
        private readonly TextBox _input;

        private TextPromptDialog(string title, string label, string initial)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(380, 132);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.UIFont;
            AutoScaleMode = AutoScaleMode.None;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12, 10, 12, 10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            layout.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.TextDim
            }, 0, 0);

            _input = new TextBox
            {
                Text = initial ?? string.Empty,
                Dock = DockStyle.Fill,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            layout.Controls.Add(_input, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            var cancel = new Button { Text = Lang.T("取消"), Width = 84, Height = 26, DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = Lang.T("确定"), Width = 84, Height = 26, DialogResult = DialogResult.OK };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons, 0, 2);

            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;

            Theme.Apply(this);
            _input.SelectAll();
        }

        /// <summary>弹出输入框；用户确认且内容非空时返回 true。</summary>
        /// <param name="owner">父窗口。</param>
        /// <param name="title">对话框标题。</param>
        /// <param name="label">输入框上方的说明文字。</param>
        /// <param name="initial">初始文本。</param>
        /// <param name="result">用户输入的文本（已去除首尾空白）。</param>
        /// <returns>是否取得了可用的新文本。</returns>
        internal static bool TryGet(IWin32Window owner, string title, string label, string initial, out string result)
        {
            result = null;
            using (var dlg = new TextPromptDialog(title, label, initial))
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
                string text = (dlg._input.Text ?? string.Empty).Trim();
                if (text.Length == 0) return false;
                if (string.Equals(text, initial, StringComparison.Ordinal)) return false;
                result = text;
                return true;
            }
        }
    }
}
