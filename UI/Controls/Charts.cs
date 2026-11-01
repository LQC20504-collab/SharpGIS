using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using SharpGIS.Services;
namespace SharpGIS.UI.Controls
{
    /// <summary>矩阵热力图：用于转移概率、转移计数、转移预算与混淆矩阵。</summary>
    public sealed class MatrixHeatmap : Control
    {
        private double[][] _values = new double[0][];
        private string[] _rowNames = new string[0];
        private string[] _colNames = new string[0];
        private readonly ToolTip _tip = new ToolTip();

        /// <summary>标题（画在顶部）。</summary>
        public string Title = "";
        /// <summary>数值格式。</summary>
        public string ValueFormat = "0.00";
        /// <summary>是否在格子里写字（格子太小时自动省略）。</summary>
        public bool ShowValues = true;
        /// <summary>行/列标题。</summary>
        public string RowTitle = Lang.T("源类别");
        /// <summary>列标题。</summary>
        public string ColTitle = Lang.T("目标类别");

        /// <summary>创建一个双缓冲热力图控件。</summary>
        public MatrixHeatmap()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.UIFont;
        }

        /// <summary>设置矩阵与标题。</summary>
        /// <param name="values">二维数值。</param>
        /// <param name="rowNames">行名。</param>
        /// <param name="colNames">列名。</param>
        public void SetData(double[][] values, string[] rowNames, string[] colNames)
        {
            _values = values ?? new double[0][];
            _rowNames = rowNames ?? new string[0];
            _colNames = colNames ?? new string[0];
            Invalidate();
        }

        /// <summary>重绘。</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (_values.Length == 0 || _values[0].Length == 0)
            {
                DrawCentered(g, Lang.T("没有可显示的数据"));
                return;
            }

            int rows = _values.Length;
            int cols = _values[0].Length;
            int titleH = string.IsNullOrEmpty(Title) ? 6 : 26;
            int left = 78;
            int top = titleH + (cols > 0 ? 52 : 8);
            int right = 18;
            int bottom = 46;
            int cellW = Math.Max(12, (Width - left - right) / Math.Max(1, cols));
            int cellH = Math.Max(12, (Height - top - bottom) / Math.Max(1, rows));
            int size = Math.Min(cellW, cellH);
            cellW = size;
            cellH = size;

            using (var titleFont = new Font(Font.FontFamily, 9.5f, FontStyle.Bold))
            using (var brush = new SolidBrush(Theme.Text))
            {
                if (!string.IsNullOrEmpty(Title))
                    g.DrawString(Title, titleFont, brush, left - 64, 6);
            }

            double max = 0;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < _values[i].Length; j++)
                    if (_values[i][j] > max) max = _values[i][j];
            if (max <= 0) max = 1;

            using (var cellFont = new Font(Font.FontFamily, Math.Max(6.4f, Math.Min(8.4f, size / 6.2f))))
            using (var textBrush = new SolidBrush(Theme.Text))
            using (var dimBrush = new SolidBrush(Theme.TextDim))
            using (var border = new Pen(Theme.Background, 1f))
            {
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        double v = j < _values[i].Length ? _values[i][j] : 0;
                        var rect = new Rectangle(left + j * cellW, top + i * cellH, cellW, cellH);
                        using (var fill = new SolidBrush(HeatColor(v / max)))
                            g.FillRectangle(fill, rect);
                        g.DrawRectangle(border, rect);

                        if (ShowValues && v > 0 && cellW >= 30 && cellH >= 18)
                        {
                            string text = v.ToString(ValueFormat);
                            var sizeText = g.MeasureString(text, cellFont);
                            bool dark = v / max > 0.55;
                            using (var b = new SolidBrush(dark ? Color.Black : Theme.Text))
                                g.DrawString(text, cellFont, b,
                                    rect.X + (cellW - sizeText.Width) / 2,
                                    rect.Y + (cellH - sizeText.Height) / 2);
                        }
                    }
                }

                // 行列标题（列名过长时旋转 45°）
                for (int j = 0; j < cols && j < _colNames.Length; j++)
                {
                    string name = _colNames[j];
                    var s = g.MeasureString(name, cellFont);
                    var state = g.Save();
                    g.TranslateTransform(left + j * cellW + cellW / 2f, top - 6);
                    g.RotateTransform(-42);
                    g.DrawString(name, cellFont, dimBrush, -s.Width + 4, -s.Height + 2);
                    g.Restore(state);
                }
                for (int i = 0; i < rows && i < _rowNames.Length; i++)
                {
                    g.DrawString(_rowNames[i], cellFont, dimBrush,
                        left - 8 - g.MeasureString(_rowNames[i], cellFont).Width,
                        top + i * cellH + (cellH - 14) / 2f);
                }

                if (!string.IsNullOrEmpty(ColTitle))
                    g.DrawString(ColTitle, cellFont, dimBrush, left + cols * cellW - 40, top + rows * cellH + 22);
            }
        }

        /// <summary>鼠标移动时在气泡里显示格子数值。</summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int rows = _values.Length;
            int cols = rows > 0 ? _values[0].Length : 0;
            if (rows == 0 || cols == 0) { _tip.SetToolTip(this, ""); return; }

            int titleH = string.IsNullOrEmpty(Title) ? 6 : 26;
            int left = 78;
            int top = titleH + (cols > 0 ? 52 : 8);
            int cellW = Math.Max(12, (Width - left - 18) / Math.Max(1, cols));
            int cellH = Math.Max(12, (Height - top - 46) / Math.Max(1, rows));
            int size = Math.Min(cellW, cellH);
            int j = (e.X - left) / Math.Max(1, size);
            int i = (e.Y - top) / Math.Max(1, size);
            if (i < 0 || j < 0 || i >= rows || j >= cols) { _tip.SetToolTip(this, ""); return; }
            double v = j < _values[i].Length ? _values[i][j] : 0;
            string rn = i < _rowNames.Length ? _rowNames[i] : i.ToString();
            string cn = j < _colNames.Length ? _colNames[j] : j.ToString();
            _tip.SetToolTip(this, string.Format("{0} → {1}{3}{2}",
                rn, cn, v.ToString(ValueFormat), Lang.IsEnglish ? ": " : "："));
        }

        private void DrawCentered(Graphics g, string text)
        {
            using (var brush = new SolidBrush(Theme.TextDim))
            {
                var size = g.MeasureString(text, Font);
                g.DrawString(text, Font, brush, (Width - size.Width) / 2, (Height - size.Height) / 2);
            }
        }

        /// <summary>按 0~1 归一化取热力色（深蓝 → 青 → 黄 → 红）。</summary>
        internal static Color HeatColor(double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            // 与暗色主题协调的低饱和渐变
            Color[] stops =
            {
                Color.FromArgb(38, 46, 58), Color.FromArgb(36, 88, 128),
                Color.FromArgb(42, 140, 140), Color.FromArgb(214, 178, 74),
                Color.FromArgb(206, 84, 66)
            };
            double x = t * (stops.Length - 1);
            int i = (int)Math.Floor(x);
            if (i >= stops.Length - 1) return stops[stops.Length - 1];
            double f = x - i;
            var a = stops[i];
            var b = stops[i + 1];
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * f),
                (int)Math.Round(a.G + (b.G - a.G) * f),
                (int)Math.Round(a.B + (b.B - a.B) * f));
        }
    }

    /// <summary>分组柱状图：基期 / 末期 / 预测 三类面积对比。</summary>
    public sealed class GroupedBars : Control
    {
        private string[] _categories = new string[0];
        private readonly List<KeyValuePair<string, double[]>> _series = new List<KeyValuePair<string, double[]>>();
        private readonly ToolTip _tip = new ToolTip();

        /// <summary>标题。</summary>
        public string Title = "";
        /// <summary>Y 轴单位后缀。</summary>
        public string Unit = "";
        /// <summary>数值格式。</summary>
        public string ValueFormat = "#,0.00";

        /// <summary>创建一个双缓冲柱状图。</summary>
        public GroupedBars()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.UIFont;
        }

        /// <summary>设置数据。</summary>
        /// <param name="categories">类别名。</param>
        /// <param name="series">系列名与数值数组（与 categories 等长）。</param>
        public void SetData(string[] categories, IEnumerable<KeyValuePair<string, double[]>> series)
        {
            _categories = categories ?? new string[0];
            _series.Clear();
            if (series != null) _series.AddRange(series);
            Invalidate();
        }

        /// <summary>重绘。</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (_categories.Length == 0 || _series.Count == 0) return;

            int left = 66, top = string.IsNullOrEmpty(Title) ? 34 : 48, right = 16, bottom = 52;
            var plot = new Rectangle(left, top, Math.Max(10, Width - left - right),
                Math.Max(10, Height - top - bottom));

            double max = 0;
            foreach (var s in _series)
                foreach (var v in s.Value)
                    if (v > max) max = v;
            if (max <= 0) max = 1;
            max = NiceCeiling(max);

            using (var axis = new Pen(Theme.Border))
            using (var grid = new Pen(Color.FromArgb(60, 64, 72)))
            using (var textBrush = new SolidBrush(Theme.TextDim))
            {
                for (int t = 0; t <= 4; t++)
                {
                    double v = max * t / 4.0;
                    int y = plot.Bottom - (int)(plot.Height * t / 4.0);
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    string label = v.ToString("#,0.#");
                    g.DrawString(label, Font, textBrush, plot.Left - 8 - g.MeasureString(label, Font).Width, y - 8);
                }
                g.DrawLine(axis, plot.Left, plot.Top, plot.Left, plot.Bottom);
            }

            int groups = _categories.Length;
            int seriesCount = _series.Count;
            int groupW = Math.Max(18, plot.Width / Math.Max(1, groups));
            int barW = Math.Max(4, (groupW - 14) / Math.Max(1, seriesCount));
            Color[] colors =
            {
                Color.FromArgb(127, 140, 155), Color.FromArgb(44, 139, 214), Color.FromArgb(226, 115, 78),
                Color.FromArgb(120, 178, 110), Color.FromArgb(180, 130, 200)
            };

            for (int i = 0; i < groups; i++)
            {
                int gx = plot.Left + i * groupW + 7;
                for (int s = 0; s < seriesCount; s++)
                {
                    var values = _series[s].Value;
                    double v = i < values.Length ? values[i] : 0;
                    int h = (int)Math.Round(plot.Height * (v / max));
                    if (v > 0 && h < 1) h = 1;
                    var rect = new Rectangle(gx + s * barW, plot.Bottom - h, Math.Max(3, barW - 2), h);
                    using (var brush = new SolidBrush(colors[s % colors.Length]))
                        g.FillRectangle(brush, rect);
                }

                var name = _categories[i];
                var size = g.MeasureString(name, Font);
                var state = g.Save();
                g.TranslateTransform(gx + groupW / 2f - 4, plot.Bottom + 6);
                if (size.Width > groupW - 6) g.RotateTransform(32);
                using (var b = new SolidBrush(Theme.TextDim))
                    g.DrawString(name, Font, b, 0, 0);
                g.Restore(state);
            }

            if (!string.IsNullOrEmpty(Title))
            {
                using (var b = new SolidBrush(Theme.Text))
                using (var f = new Font(Font.FontFamily, 9.5f, FontStyle.Bold))
                    g.DrawString(Title, f, b, 10, 8);
            }
            DrawLegend(g, plot.Left + 4, 26, colors);
        }

        /// <summary>把最大值向上取整到“好看”的刻度（1 / 2 / 2.5 / 5 × 10^n），使 Y 轴刻度整齐。</summary>
        internal static double NiceCeiling(double value)
        {
            if (value <= 0) return 1;
            double exp = Math.Pow(10, Math.Floor(Math.Log10(value)));
            double norm = value / exp;
            double nice;
            if (norm <= 1) nice = 1;
            else if (norm <= 2) nice = 2;
            else if (norm <= 2.5) nice = 2.5;
            else if (norm <= 5) nice = 5;
            else nice = 10;
            return nice * exp;
        }

        private void DrawLegend(Graphics g, int x, int y, Color[] colors)        {
            using (var b = new SolidBrush(Theme.TextDim))
            {
                for (int s = 0; s < _series.Count; s++)
                {
                    using (var brush = new SolidBrush(colors[s % colors.Length]))
                        g.FillRectangle(brush, x, y, 10, 10);
                    g.DrawString(_series[s].Key, Font, b, x + 14, y - 2);
                    x += 16 + (int)g.MeasureString(_series[s].Key, Font).Width;
                }
            }
        }

        /// <summary>悬停显示具体数值。</summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int left = 66, top = string.IsNullOrEmpty(Title) ? 34 : 48;
            int plotW = Math.Max(10, Width - left - 16);
            int groups = _categories.Length;
            if (groups == 0) return;
            int groupW = Math.Max(18, plotW / Math.Max(1, groups));
            int i = (e.X - left) / Math.Max(1, groupW);
            if (i < 0 || i >= groups || e.Y < top - 20) { _tip.SetToolTip(this, ""); return; }
            string text = _categories[i];
            foreach (var s in _series)
            {
                double v = i < s.Value.Length ? s.Value[i] : 0;
                text += "\r\n" + s.Key + (Lang.IsEnglish ? ": " : "：")
                    + v.ToString(ValueFormat) + Unit;
            }
            _tip.SetToolTip(this, text);
        }
    }

    /// <summary>指标卡片：一行若干个大号数字（OA / Kappa / 像元数 …）。</summary>
    public sealed class MetricCards : Control
    {
        private readonly List<KeyValuePair<string, string>> _items =
            new List<KeyValuePair<string, string>>();

        /// <summary>创建一个卡片控件。</summary>
        public MetricCards()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Panel;
            Font = Theme.UIFont;
        }

        /// <summary>设置卡片内容（标题, 值）。</summary>
        public void SetData(IEnumerable<KeyValuePair<string, string>> items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            Invalidate();
        }

        /// <summary>重绘。</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (_items.Count == 0) return;

            int gap = 8;
            int cardW = Math.Max(80, (Width - gap * (_items.Count + 1)) / Math.Max(1, _items.Count));
            int cardH = Math.Min(Height - 8, 64);

            using (var fill = new SolidBrush(Theme.PanelAlt))
            using (var border = new Pen(Theme.Border))
            using (var titleBrush = new SolidBrush(Theme.TextDim))
            using (var valueBrush = new SolidBrush(Theme.Text))
            using (var valueFont = new Font(Font.FontFamily, 13.5f, FontStyle.Bold))
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var rect = new Rectangle(gap + i * (cardW + gap), 4, cardW, cardH);
                    g.FillRectangle(fill, rect);
                    g.DrawRectangle(border, rect);
                    g.DrawString(_items[i].Key, Font, titleBrush, rect.X + 8, rect.Y + 6);
                    var size = g.MeasureString(_items[i].Value, valueFont);
                    g.DrawString(_items[i].Value, valueFont, valueBrush,
                        rect.X + 8, rect.Y + rect.Height - size.Height - 4);
                }
            }
        }
    }

    /// <summary>横向条形图：特征重要性等“名称 + 数值”的排序对比。</summary>
    public sealed class ImportanceBars : Control
    {
        private string[] _names = new string[0];
        private double[] _values = new double[0];

        /// <summary>创建一个条形图控件。</summary>
        public ImportanceBars()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.UIFont;
        }

        /// <summary>设置数据。</summary>
        public void SetData(string[] names, double[] values)
        {
            _names = names ?? new string[0];
            _values = values ?? new double[0];
            Invalidate();
        }

        /// <summary>重绘。</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (_names.Length == 0) return;

            int left = 110, right = 60, top = 12, bottom = 12;
            int rowH = Math.Max(16, (Height - top - bottom) / Math.Max(1, _names.Length));
            int barMax = Math.Max(20, Width - left - right);
            double max = 0.0001;
            foreach (var v in _values) if (v > max) max = v;

            using (var textBrush = new SolidBrush(Theme.Text))
            using (var dimBrush = new SolidBrush(Theme.TextDim))
            {
                for (int i = 0; i < _names.Length; i++)
                {
                    int y = top + i * rowH;
                    g.DrawString(_names[i], Font, textBrush, 4, y + (rowH - 14) / 2f);
                    int w = (int)Math.Round(barMax * (_values[i] / max));
                    if (_values[i] > 0 && w < 2) w = 2;
                    var rect = new Rectangle(left, y + 3, Math.Max(1, w), Math.Max(8, rowH - 8));
                    using (var brush = new LinearGradientBrush(rect,
                        Theme.AccentDim, Theme.Accent, LinearGradientMode.Horizontal))
                        g.FillRectangle(brush, rect);
                    g.DrawString(_values[i].ToString("0.###"), Font, dimBrush,
                        left + w + 6, y + (rowH - 14) / 2f);
                }
            }
        }
    }
}
