using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.UI.Panels
{
    /// <summary>直方图面板：显示统计直方图与累积分布，可拖动两个端点设定拉伸区间。</summary>
    public class HistogramPanel : UserControl
    {
        private RasterLayer _layer;
        private readonly ComboBox _bandBox;
        private readonly HistogramView _view;
        private readonly Label _summary;
        private readonly CheckBox _liveApply;

        /// <summary>拖动端点把区间应用为拉伸后触发。</summary>
        public event EventHandler StyleEdited;

        public HistogramPanel()
        {
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.UIFont;

            var top = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Theme.Panel };
            var lbl = new Label { Text = Lang.T("波段"), Left = 8, Top = 7, Width = 36, ForeColor = Theme.TextDim };
            _bandBox = new ComboBox
            {
                Left = 46,
                Top = 3,
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat
            };
            _bandBox.SelectedIndexChanged += (s, e) => RefreshStats();
            top.Controls.Add(lbl);
            top.Controls.Add(_bandBox);

            _summary = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                ForeColor = Theme.TextDim,
                Padding = new Padding(8, 4, 8, 4),
                Text = Lang.T("未选择图层")
            };

            _liveApply = new CheckBox
            {
                Text = Lang.T("拖动端点即时应用为拉伸区间"),
                Dock = DockStyle.Bottom,
                Height = 24,
                ForeColor = Theme.Text,
                Checked = true,
                Padding = new Padding(6, 0, 0, 0)
            };
            _liveApply.CheckedChanged += (s, e) => ApplyRange();

            _view = new HistogramView { Dock = DockStyle.Fill, BackColor = Theme.Panel };
            _view.RangeChanged += (s, e) => { if (_liveApply.Checked) ApplyRange(); };

            Controls.Add(_view);
            Controls.Add(_liveApply);
            Controls.Add(_summary);
            Controls.Add(top);
        }

        public void Bind(RasterLayer layer)
        {
            _layer = layer;
            _bandBox.Items.Clear();
            if (layer != null)
            {
                for (int i = 0; i < layer.BandCount; i++)
                    _bandBox.Items.Add(Lang.T("波段 ") + (i + 1));
                int idx = Math.Max(0, Math.Min(layer.BandCount - 1,
                    (layer.Style.Mode == RenderMode.Rgb ? layer.Style.RgbBands[0] : layer.Style.GrayBand) - 1));
                _bandBox.SelectedIndex = idx;
            }
            RefreshStats();
        }

        private void RefreshStats()
        {
            if (_layer == null || _bandBox.SelectedIndex < 0)
            {
                _view.SetData(null, null);
                _summary.Text = Lang.T("未选择图层");
                return;
            }

            int bandNo = _bandBox.SelectedIndex + 1;
            var stats = _layer.Stats(bandNo);
            if (stats == null || !stats.Valid)
            {
                _view.SetData(null, null);
                _summary.Text = Lang.T("统计不可用");
                return;
            }

            double lo, hi;
            var cfg = _layer.Style.Mode == RenderMode.Rgb
                ? (_bandBox.SelectedIndex == 0 ? _layer.Style.RStretch
                    : _bandBox.SelectedIndex == 1 ? _layer.Style.GStretch : _layer.Style.BStretch)
                : _layer.Style.GrayStretch;
            cfg.Resolve(stats, out lo, out hi);

            _view.SetData(stats, new[] { lo, hi });
            _summary.Text = string.Format(
                Lang.T("最小 {0:G5}   最大 {1:G5}\n均值 {2:G5}   标准差 {3:G5}\n中位数 {4:G5}   P2/P98 {5:G4} / {6:G4}\n当前拉伸 {7:G5} ~ {8:G5}"),
                stats.Min, stats.Max, stats.Mean, stats.StdDev,
                stats.Percentile(50), stats.Percentile(2), stats.Percentile(98), lo, hi);
        }

        private void ApplyRange()
        {
            if (_layer == null) return;
            var stats = _view.Stats;
            if (stats == null) return;
            double lo = _view.LoValue, hi = _view.HiValue;
            if (!(hi > lo)) return;

            if (_layer.Style.Mode == RenderMode.Rgb)
            {
                var target = _bandBox.SelectedIndex == 0 ? _layer.Style.RStretch
                    : _bandBox.SelectedIndex == 1 ? _layer.Style.GStretch : _layer.Style.BStretch;
                target.Mode = StretchMode.CustomMinMax;
                target.CustomMin = lo;
                target.CustomMax = hi;
            }
            else
            {
                var st = _layer.Style.GrayStretch;
                st.Mode = StretchMode.CustomMinMax;
                st.CustomMin = lo;
                st.CustomMax = hi;
            }

            _layer.Touch();
            var h = StyleEdited;
            if (h != null) h(this, EventArgs.Empty);
        }
    }

    /// <summary>直方图绘制与端点拖动的自定义控件。</summary>
    public class HistogramView : Control
    {
        public BandStats Stats { get; private set; }
        public double LoValue { get; private set; }
        public double HiValue { get; private set; }
        public event EventHandler RangeChanged;

        private int _dragHandle; // 0 无，1 低端，2 高端

        public HistogramView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        public void SetData(BandStats stats, double[] range)
        {
            Stats = stats;
            if (stats != null && range != null && range.Length == 2)
            {
                LoValue = range[0];
                HiValue = range[1];
            }
            Invalidate();
        }

        private Rectangle PlotRect
        {
            get { return new Rectangle(10, 8, Math.Max(10, Width - 20), Math.Max(10, Height - 28)); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var plot = PlotRect;
            using (var pen = new Pen(Theme.Border))
                g.DrawRectangle(pen, plot);

            if (Stats == null || Stats.Histogram == null || Stats.HistogramTotal <= 0)
            {
                using (var brush = new SolidBrush(Theme.TextDim))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(Lang.T("无统计数据"), Theme.UIFont, brush, plot, sf);
                }
                return;
            }

            var hist = Stats.Histogram;
            int bins = hist.Length;
            int max = 1;
            for (int i = 0; i < bins; i++) if (hist[i] > max) max = hist[i];

            // 直方图柱
            using (var brush = new SolidBrush(Color.FromArgb(150, Theme.Accent)))
            {
                for (int i = 0; i < bins; i++)
                {
                    float x = plot.Left + i * plot.Width / (float)bins;
                    float w = plot.Width / (float)bins;
                    float h = hist[i] / (float)max * (plot.Height - 4);
                    g.FillRectangle(brush, x, plot.Bottom - h, Math.Max(1f, w - 0.4f), h);
                }
            }

            // 累积分布曲线
            if (Stats.Cdf != null)
            {
                using (var pen = new Pen(Color.FromArgb(230, 200, 160, 60), 1.3f))
                {
                    PointF prev = PointF.Empty;
                    for (int i = 0; i < bins; i++)
                    {
                        float x = plot.Left + (i + 0.5f) * plot.Width / bins;
                        float y = plot.Bottom - (float)(Stats.Cdf[i] * (plot.Height - 4));
                        if (i > 0) g.DrawLine(pen, prev, new PointF(x, y));
                        prev = new PointF(x, y);
                    }
                }
            }

            // 当前拉伸端点
            float xLo = ValueToX(LoValue, plot);
            float xHi = ValueToX(HiValue, plot);
            using (var brush = new SolidBrush(Color.FromArgb(30, Theme.Accent)))
                g.FillRectangle(brush, xLo, plot.Top, Math.Max(1, xHi - xLo), plot.Height);

            using (var pen = new Pen(Theme.Accent, 1.5f))
            {
                g.DrawLine(pen, xLo, plot.Top, xLo, plot.Bottom);
                g.DrawLine(pen, xHi, plot.Top, xHi, plot.Bottom);
            }
            using (var brush = new SolidBrush(Theme.Accent))
            {
                g.FillRectangle(brush, xLo - 3, plot.Bottom - 6, 6, 6);
                g.FillRectangle(brush, xHi - 3, plot.Bottom - 6, 6, 6);
            }

            using (var brush = new SolidBrush(Theme.TextDim))
            {
                g.DrawString(ValueToText(Stats.Min), Theme.UIFont, brush, plot.Left, plot.Bottom + 4);
                string right = ValueToText(Stats.Max);
                var size = g.MeasureString(right, Theme.UIFont);
                g.DrawString(right, Theme.UIFont, brush, plot.Right - size.Width, plot.Bottom + 4);
                string mid = string.Format("{0:G4} ~ {1:G4}", LoValue, HiValue);
                g.DrawString(mid, Theme.UIFont, brush, plot.Left + plot.Width / 2f - g.MeasureString(mid, Theme.UIFont).Width / 2f, plot.Bottom + 12);
            }
        }

        private static string ValueToText(double v)
        {
            if (Math.Abs(v) >= 10000) return v.ToString("G4");
            return v.ToString("G5");
        }

        private float ValueToX(double value, Rectangle plot)
        {
            double span = Stats.HistMax - Stats.HistMin;
            if (Math.Abs(span) < 1e-12) span = 1;
            double t = (value - Stats.HistMin) / span;
            return plot.Left + (float)Math.Max(0, Math.Min(1, t)) * plot.Width;
        }

        private double XToValue(int x)
        {
            var plot = PlotRect;
            double t = (x - plot.Left) / (double)Math.Max(1, plot.Width);
            t = Math.Max(0, Math.Min(1, t));
            return Stats.HistMin + t * (Stats.HistMax - Stats.HistMin);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Stats == null) return;
            var plot = PlotRect;
            float xLo = ValueToX(LoValue, plot);
            float xHi = ValueToX(HiValue, plot);
            _dragHandle = Math.Abs(e.X - xLo) <= Math.Abs(e.X - xHi) ? 1 : 2;
            UpdateHandle(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragHandle == 0) return;
            UpdateHandle(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragHandle = 0;
        }

        private void UpdateHandle(int x)
        {
            double v = XToValue(x);
            if (_dragHandle == 1) LoValue = Math.Min(v, HiValue - 1e-9);
            else HiValue = Math.Max(v, LoValue + 1e-9);
            Invalidate();
            var h = RangeChanged;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
