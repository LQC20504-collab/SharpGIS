using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.Services;

namespace SharpGIS.UI.Panels
{
    /// <summary>元数据面板：驱动、尺寸、波段、投影、变换、NoData、压缩与统计。</summary>
    public class MetadataPanel : UserControl
    {
        private readonly TextBox _text;
        public MetadataPanel()
        {
            BackColor = Theme.Panel;
            Font = Theme.UIFont;

            _text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,          // 长行折行，避免右侧被硬裁
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                Font = Theme.MonoFont
            };
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 4), BackColor = Theme.Panel };
            host.Controls.Add(_text);
            Controls.Add(host);
        }

        public void Bind(RasterLayer layer)
        {
            if (layer == null) { _text.Text = Lang.T("未选择图层"); return; }

            var sb = new StringBuilder();
            sb.Append(DatasetService.DescribeMetadata(layer));
            if (layer.IsRectified)
                sb.AppendLine(Lang.T("备注: 源影像带旋转，已包装为北向上临时 VRT 显示（波段数含 alpha 掩膜）"));
            sb.AppendLine();
            sb.AppendLine(Lang.T("—— 各波段统计（抽样，已剔除无效像素）——"));
            for (int i = 1; i <= layer.BandCount; i++)
            {
                if (i == layer.AlphaBandIndex) { sb.AppendLine(string.Format(Lang.T("波段 {0}: alpha 掩膜"), i)); continue; }
                var s = layer.Stats(i);
                if (s == null || !s.Valid) { sb.AppendLine(string.Format(Lang.T("波段 {0}: 统计不可用"), i)); continue; }
                sb.AppendLine(string.Format(Lang.T("波段 {0}: min={1:G6} max={2:G6} mean={3:G6} std={4:G6} P2={5:G6} P98={6:G6}"),
                    i, s.Min, s.Max, s.Mean, s.StdDev, s.Percentile(2), s.Percentile(98)));
            }
            _text.Text = sb.ToString();
        }
    }

    /// <summary>识别面板：显示点击像素的坐标与各波段值。</summary>
    public class IdentifyPanel : UserControl
    {
        private readonly Label _header;
        private readonly ListView _list;
        private RasterLayer _layer;
        private int _col, _row;

        public IdentifyPanel()
        {
            BackColor = Theme.Panel;
            Font = Theme.UIFont;

            _header = new Label
            {
                Dock = DockStyle.Top,
                Height = 62,
                Padding = new Padding(8, 6, 8, 0),
                ForeColor = Theme.TextDim,
                Text = Lang.T("在地图上使用识别工具（或双击）点击像素")
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                Font = Theme.UIFont,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _list.Columns.Add(Lang.T("波段"), 70);
            _list.Columns.Add(Lang.T("数值"), 110);
            _list.Columns.Add(Lang.T("说明"), 120);

            var copy = new Button { Text = Lang.T("复制结果"), Dock = DockStyle.Bottom, Height = 26 };
            copy.Click += (s, e) => CopyToClipboard();

            Controls.Add(_list);
            Controls.Add(copy);
            Controls.Add(_header);
        }

        public void ShowIdentify(IdentifyEventArgs args)
        {
            if (args == null || args.Layer == null) return;
            _layer = args.Layer;
            _col = args.Col;
            _row = args.Row;

            _header.Text = string.Format(Lang.T("{0}\n行列: {1}, {2}    坐标: {3:F3}, {4:F3}"),
                args.Layer.Name, args.Col, args.Row, args.GeoX, args.GeoY);

            _list.BeginUpdate();
            _list.Items.Clear();
            for (int i = 0; i < args.Values.Length; i++)
            {
                string note = string.Empty;
                if (i + 1 == args.Layer.AlphaBandIndex) note = "alpha";
                else if (i < args.Layer.ColorInterps.Length)
                    note = args.Layer.ColorInterps[i].ToString().Replace("GCI_", "");
                var item = new ListViewItem(new[]
                {
                    Lang.T("波段 ") + (i + 1),
                    double.IsNaN(args.Values[i]) ? "NoData" : args.Values[i].ToString("G"),
                    note
                });
                _list.Items.Add(item);
            }
            _list.EndUpdate();
        }

        public void Clear()
        {
            _list.Items.Clear();
            _header.Text = Lang.T("在地图上使用识别工具（或双击）点击像素");
        }

        private void CopyToClipboard()
        {
            if (_layer == null) return;
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(Lang.T("{0}  行={1} 列={2}"), _layer.Name, _row, _col));
            foreach (ListViewItem item in _list.Items)
                sb.AppendLine(item.SubItems[0].Text + "\t" + item.SubItems[1].Text);
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }
    }
}
