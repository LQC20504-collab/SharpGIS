using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SharpGIS.UI
{
    /// <summary>暗色主题：统一调色板与控件渲染器（自绘菜单/工具栏，避免系统浅色主题破坏观感）。</summary>
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(38, 40, 44);
        public static readonly Color Panel = Color.FromArgb(45, 47, 52);
        public static readonly Color PanelAlt = Color.FromArgb(53, 56, 62);
        public static readonly Color Border = Color.FromArgb(66, 70, 78);
        public static readonly Color Text = Color.FromArgb(224, 228, 232);
        public static readonly Color TextDim = Color.FromArgb(150, 156, 166);
        public static readonly Color Accent = Color.FromArgb(64, 156, 226);
        public static readonly Color AccentDim = Color.FromArgb(46, 110, 160);
        public static readonly Color Selected = Color.FromArgb(58, 78, 100);
        public static readonly Color Warn = Color.FromArgb(230, 170, 70);

        public static readonly Font UIFont = new Font("Microsoft YaHei UI", 8.8f);
        public static readonly Font MonoFont = new Font("Consolas", 9f);

        public static void Apply(Control root)
        {
            if (root == null) return;
            if (root is Form)
            {
                root.BackColor = Background;
                root.ForeColor = Text;
            }
            else if (root is Panel || root is GroupBox || root is TabPage || root is SplitContainer)
            {
                root.BackColor = Panel;
                root.ForeColor = Text;
            }

            foreach (Control c in root.Controls)
            {
                if (c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox)
                {
                    c.BackColor = PanelAlt;
                    c.ForeColor = Text;
                }
                else if (c is Button)
                {
                    var b = (Button)c;
                    b.BackColor = PanelAlt;
                    b.ForeColor = Text;
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Border;
                    b.UseVisualStyleBackColor = false;
                }
                else if (c is Label || c is CheckBox || c is RadioButton)
                {
                    c.BackColor = Color.Transparent;
                    c.ForeColor = Text;
                }
                else if (c is GroupBox)
                {
                    c.ForeColor = TextDim;
                }
                else if (c is TabControl || c is TableLayoutPanel || c is FlowLayoutPanel)
                {
                    c.BackColor = Panel;
                    c.ForeColor = Text;
                }
                else if (c is ListView)
                {
                    c.BackColor = Panel;
                    c.ForeColor = Text;
                }
                Apply(c);
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>让标题栏跟随暗色主题（Win10 1809+ / Win11），失败静默忽略。</summary>
        public static void TryEnableDarkTitleBar(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(handle, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(handle, 19, ref on, 4);
            }
            catch { }
        }

        public static ToolStripRenderer CreateToolStripRenderer()
        {
            return new DarkToolStripRenderer();
        }

        public static void StyleMenuStrip(MenuStrip menu)
        {
            menu.BackColor = Panel;
            menu.ForeColor = Text;
            menu.Renderer = CreateToolStripRenderer();
            menu.Font = UIFont;
        }

        public static void StyleToolStrip(ToolStrip strip)
        {
            strip.BackColor = Panel;
            strip.ForeColor = Text;
            strip.Renderer = CreateToolStripRenderer();
            strip.GripStyle = ToolStripGripStyle.Hidden;
            strip.Font = UIFont;
            strip.Padding = new Padding(4, 2, 4, 2);
        }

        public static void StyleStatusStrip(StatusStrip strip)
        {
            strip.BackColor = Panel;
            strip.ForeColor = TextDim;
            strip.Renderer = CreateToolStripRenderer();
            strip.Font = UIFont;
        }
    }

    internal sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(e.ToolStrip is ToolStripDropDown ? Theme.PanelAlt : Theme.Panel))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            if (!item.Selected && !item.Pressed) return;
            var rect = new Rectangle(1, 1, item.Width - 2, item.Height - 2);
            using (var brush = new SolidBrush(item.Pressed ? Theme.Selected : Theme.PanelAlt))
                e.Graphics.FillRectangle(brush, rect);
            using (var pen = new Pen(item.Pressed ? Theme.AccentDim : Theme.Border))
                e.Graphics.DrawRectangle(pen, rect);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            var rect = new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2);
            using (var brush = new SolidBrush(Theme.Selected))
                e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextDim;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Border))
            {
                if (e.Vertical)
                    e.Graphics.DrawLine(pen, e.Item.Width / 2, 4, e.Item.Width / 2, e.Item.Height - 4);
                else
                    e.Graphics.DrawLine(pen, 6, e.Item.Height / 2, e.Item.Width - 6, e.Item.Height / 2);
            }
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.PanelAlt; } }
        public override Color ImageMarginGradientBegin { get { return Theme.PanelAlt; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.PanelAlt; } }
        public override Color ImageMarginGradientEnd { get { return Theme.PanelAlt; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.AccentDim; } }
        public override Color MenuItemSelected { get { return Theme.Selected; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color StatusStripGradientBegin { get { return Theme.Panel; } }
        public override Color StatusStripGradientEnd { get { return Theme.Panel; } }
        public override Color ToolStripBorder { get { return Theme.Border; } }
        public override Color ToolStripGradientBegin { get { return Theme.Panel; } }
        public override Color ToolStripGradientMiddle { get { return Theme.Panel; } }
        public override Color ToolStripGradientEnd { get { return Theme.Panel; } }
    }

    /// <summary>运行时用 GDI+ 画的矢量图标，避免依赖外部图片资源。</summary>
    public static class Icons
    {
        public static Bitmap Create(string kind, int size = 18)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var c = Theme.Text;
                using (var pen = new Pen(c, 1.6f))
                using (var brush = new SolidBrush(c))
                using (var accent = new SolidBrush(Theme.Accent))
                {
                    float s = size;
                    switch (kind)
                    {
                        case "open":
                            g.DrawRectangle(pen, s * 0.12f, s * 0.25f, s * 0.5f, s * 0.55f);
                            g.FillPolygon(accent, new[]
                            {
                                new PointF(s * 0.35f, s * 0.28f), new PointF(s * 0.62f, s * 0.28f),
                                new PointF(s * 0.7f, s * 0.4f), new PointF(s * 0.88f, s * 0.4f),
                                new PointF(s * 0.88f, s * 0.78f), new PointF(s * 0.35f, s * 0.78f)
                            });
                            break;
                        case "pan":
                            g.DrawLine(pen, s * 0.5f, s * 0.15f, s * 0.5f, s * 0.85f);
                            g.DrawLine(pen, s * 0.15f, s * 0.5f, s * 0.85f, s * 0.5f);
                            g.FillEllipse(accent, s * 0.42f, s * 0.42f, s * 0.16f, s * 0.16f);
                            break;
                        case "zoom_in":
                        case "zoom_out":
                            g.DrawEllipse(pen, s * 0.15f, s * 0.15f, s * 0.5f, s * 0.5f);
                            g.DrawLine(pen, s * 0.62f, s * 0.62f, s * 0.88f, s * 0.88f);
                            g.DrawLine(pen, s * 0.28f, s * 0.4f, s * 0.52f, s * 0.4f);
                            if (kind == "zoom_in") g.DrawLine(pen, s * 0.4f, s * 0.28f, s * 0.4f, s * 0.52f);
                            break;
                        case "full":
                            g.DrawRectangle(pen, s * 0.15f, s * 0.15f, s * 0.7f, s * 0.7f);
                            g.FillRectangle(accent, s * 0.32f, s * 0.32f, s * 0.36f, s * 0.36f);
                            break;
                        case "one_to_one":
                            g.DrawRectangle(pen, s * 0.15f, s * 0.2f, s * 0.7f, s * 0.6f);
                            g.DrawString("1:1", new Font("Segoe UI", s * 0.34f, FontStyle.Bold), brush, -1, s * 0.24f);
                            break;
                        case "rect_zoom":
                            g.DrawRectangle(pen, s * 0.15f, s * 0.2f, s * 0.6f, s * 0.5f);
                            g.DrawLine(pen, s * 0.6f, s * 0.6f, s * 0.88f, s * 0.88f);
                            g.DrawLine(pen, s * 0.72f, s * 0.88f, s * 0.88f, s * 0.88f);
                            g.DrawLine(pen, s * 0.88f, s * 0.72f, s * 0.88f, s * 0.88f);
                            break;
                        case "identify":
                            g.DrawEllipse(pen, s * 0.12f, s * 0.12f, s * 0.55f, s * 0.55f);
                            g.DrawLine(pen, s * 0.42f, s * 0.42f, s * 0.5f, s * 0.5f);
                            g.DrawLine(pen, s * 0.62f, s * 0.62f, s * 0.9f, s * 0.9f);
                            g.FillEllipse(accent, s * 0.32f, s * 0.32f, s * 0.14f, s * 0.14f);
                            break;
                        case "histogram":
                            float[] hs = { 0.2f, 0.45f, 0.8f, 0.6f, 0.35f, 0.55f, 0.25f };
                            for (int i = 0; i < hs.Length; i++)
                            {
                                float bw = s * 0.1f;
                                float x = s * 0.1f + i * bw * 1.15f;
                                float hgt = hs[i] * s * 0.7f;
                                g.FillRectangle(i % 2 == 0 ? brush : accent, x, s * 0.85f - hgt, bw * 0.85f, hgt);
                            }
                            break;
                        case "process":
                            g.DrawRectangle(pen, s * 0.15f, s * 0.15f, s * 0.7f, s * 0.7f);
                            for (int i = 0; i < 3; i++)
                            {
                                float y = s * (0.3f + i * 0.18f);
                                g.FillEllipse(accent, s * 0.24f, y, s * 0.1f, s * 0.1f);
                                g.DrawLine(pen, s * 0.4f, y + s * 0.05f, s * 0.74f, y + s * 0.05f);
                            }
                            break;
                        case "rotate":
                            // 3/4 圆弧 + 箭头，表示“旋转视图”
                            g.DrawArc(pen, s * 0.16f, s * 0.16f, s * 0.68f, s * 0.68f, -55, 275);
                            g.FillPolygon(accent, new[]
                            {
                                new PointF(s * 0.78f, s * 0.1f), new PointF(s * 0.92f, s * 0.38f),
                                new PointF(s * 0.62f, s * 0.36f)
                            });
                            g.FillEllipse(brush, s * 0.45f, s * 0.45f, s * 0.1f, s * 0.1f);
                            break;
                        case "reset_north":
                            // 圆环 + 向上的箭头，表示“回到正北”
                            g.DrawEllipse(pen, s * 0.14f, s * 0.14f, s * 0.72f, s * 0.72f);
                            g.DrawLine(pen, s * 0.5f, s * 0.76f, s * 0.5f, s * 0.34f);
                            g.FillPolygon(accent, new[]
                            {
                                new PointF(s * 0.5f, s * 0.2f), new PointF(s * 0.68f, s * 0.42f),
                                new PointF(s * 0.32f, s * 0.42f)
                            });
                            break;
                        case "up":
                            g.FillPolygon(brush, new[]
                            {
                                new PointF(s * 0.5f, s * 0.22f), new PointF(s * 0.78f, s * 0.6f),
                                new PointF(s * 0.22f, s * 0.6f)
                            });
                            break;
                        case "down":
                            g.FillPolygon(brush, new[]
                            {
                                new PointF(s * 0.5f, s * 0.78f), new PointF(s * 0.78f, s * 0.4f),
                                new PointF(s * 0.22f, s * 0.4f)
                            });
                            break;
                        case "remove":
                        case "close":
                            g.DrawLine(pen, s * 0.25f, s * 0.25f, s * 0.75f, s * 0.75f);
                            g.DrawLine(pen, s * 0.75f, s * 0.25f, s * 0.25f, s * 0.75f);
                            break;
                        case "layer":
                            for (int i = 0; i < 3; i++)
                            {
                                var pts = new[]
                                {
                                    new PointF(s * 0.5f, s * (0.2f + i * 0.18f)),
                                    new PointF(s * 0.85f, s * (0.34f + i * 0.18f)),
                                    new PointF(s * 0.5f, s * (0.48f + i * 0.18f)),
                                    new PointF(s * 0.15f, s * (0.34f + i * 0.18f))
                                };
                                g.DrawPolygon(i == 2 ? pen : new Pen(Theme.TextDim, 1.2f), pts);
                            }
                            break;
                        default:
                            g.DrawEllipse(pen, s * 0.25f, s * 0.25f, s * 0.5f, s * 0.5f);
                            break;
                    }
                }
            }
            return bmp;
        }
    }
}
