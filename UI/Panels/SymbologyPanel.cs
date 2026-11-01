using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.UI.Panels
{
    /// <summary>符号化面板：波段选择、色带、拉伸、色调、混合与重采样，修改即时生效。</summary>
    public class SymbologyPanel : UserControl
    {
        private RasterLayer _layer;
        private bool _syncing;
        private int _y;
        private readonly List<Control[]> _rowPairs = new List<Control[]>();
        private Button _resetButton;

        private readonly ComboBox _mode, _ramp, _stretch, _blend, _resample, _bandGray;
        private readonly ComboBox _bandR, _bandG, _bandB;
        private readonly NumericUpDown _stdDev, _pctLow, _pctHigh, _customMin, _customMax;
        private readonly TrackBar _gamma, _brightness, _contrast, _opacity;
        private readonly CheckBox _invert;
        private readonly Label _info;

        /// <summary>符号化参数被修改后触发（宿主据此触发重绘）。</summary>
        public event EventHandler StyleEdited;

        public SymbologyPanel()
        {
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            AutoScroll = true;
            Font = Theme.UIFont;

            _mode = Combo(new[] { Lang.T("单波段灰度"), Lang.T("多波段彩色（RGB）"), Lang.T("调色板") });
            _mode.SelectedIndexChanged += (s, e) => { if (!_syncing) Apply(); };

            _bandGray = Combo(new string[0]);
            _bandR = Combo(new string[0]);
            _bandG = Combo(new string[0]);
            _bandB = Combo(new string[0]);
            _bandGray.SelectedIndexChanged += (s, e) => { if (!_syncing) Apply(); };
            _bandR.SelectedIndexChanged += (s, e) => { if (!_syncing) Apply(); };
            _bandG.SelectedIndexChanged += (s, e) => { if (!_syncing) Apply(); };
            _bandB.SelectedIndexChanged += (s, e) => { if (!_syncing) Apply(); };

            _ramp = Combo(new string[0]);
            foreach (var r in ColorRamp.BuiltIn) _ramp.Items.Add(r.Name);
            _ramp.SelectedIndexChanged += (s, e) => { if (!_syncing) Apply(); };

            _invert = new CheckBox { Text = Lang.T("反相"), AutoSize = true, ForeColor = Theme.Text };
            _invert.CheckedChanged += (s, e) => { if (!_syncing) Apply(); };

            _stretch = Combo(new[] { Lang.T("无拉伸（按类型满量程）"), Lang.T("最小-最大值"), Lang.T("标准差"), Lang.T("百分比裁剪"), Lang.T("直方图均衡"), Lang.T("自定义区间") });
            _stretch.SelectedIndexChanged += (s, e) => { if (!_syncing) { SyncStretchInputs(); Apply(); } };

            _stdDev = Num(1, 5, 1, 2);
            _pctLow = Num(0, 49, 1, 2);
            _pctHigh = Num(0, 49, 1, 2);
            _customMin = Num(-1e9m, 1e9m, 1, 0);
            _customMax = Num(-1e9m, 1e9m, 1, 255);
            foreach (var n in new[] { _stdDev, _pctLow, _pctHigh, _customMin, _customMax })
                n.ValueChanged += (s, e) => { if (!_syncing) Apply(); };

            _gamma = Slider(10, 400, 100);
            _brightness = Slider(-50, 50, 0);
            _contrast = Slider(20, 300, 100);
            _opacity = Slider(0, 100, 100);
            foreach (var t in new[] { _gamma, _brightness, _contrast, _opacity })
            {
                t.ValueChanged += (s, e) => { if (!_syncing) Apply(); };
                t.MouseUp += (s, e) => { if (!_syncing) Apply(); };
            }

            _blend = Combo(new[] { Lang.T("正常"), Lang.T("正片叠底"), Lang.T("滤色") });
            _blend.SelectedIndexChanged += (s, e) => { if (!_syncing) Apply(); };
            _resample = Combo(new[] { Lang.T("最近邻"), Lang.T("双线性"), Lang.T("立方"), Lang.T("平均值") });
            _resample.SelectedIndexChanged += (s, e) => { if (!_syncing) Apply(); };

            _info = new Label
            {
                AutoSize = false,
                ForeColor = Theme.TextDim,
                Height = 46,
                Text = Lang.T("未选择图层")
            };

            _y = 6;
            Header(Lang.T("符号化方式"));
            Row(Lang.T("模式"), _mode);
            Row(Lang.T("灰度波段"), _bandGray);
            Row(Lang.T("R 波段"), _bandR);
            Row(Lang.T("G 波段"), _bandG);
            Row(Lang.T("B 波段"), _bandB);

            Header(Lang.T("色带与拉伸"));
            Row(Lang.T("色带"), _ramp);
            Row("", _invert);
            Row(Lang.T("拉伸"), _stretch);
            Row(Lang.T("标准差倍数"), _stdDev);
            Row(Lang.T("裁剪下限 %"), _pctLow);
            Row(Lang.T("裁剪上限 %"), _pctHigh);
            Row(Lang.T("自定义最小"), _customMin);
            Row(Lang.T("自定义最大"), _customMax);

            Header(Lang.T("色调与合成"));
            Row("Gamma ×100", _gamma);
            Row(Lang.T("亮度"), _brightness);
            Row(Lang.T("对比度 %"), _contrast);
            Row(Lang.T("透明度 %"), _opacity);
            Row(Lang.T("混合模式"), _blend);
            Row(Lang.T("重采样"), _resample);

            _resetButton = new Button
            {
                Text = Lang.T("恢复默认符号化"),
                Left = 10,
                Top = _y + 6,
                Width = 190,
                Height = 26
            };
            _resetButton.Click += (s, e) => ResetStyle();
            Controls.Add(_resetButton);

            Controls.Add(_info);
            _info.Left = 10;
            _info.Top = _y + 40;
            _info.Width = 260;

            _resample.SelectedIndex = 1;
            _mode.SelectedIndex = 0;
            _stretch.SelectedIndex = 3;
            _ramp.SelectedIndex = 0;
            _blend.SelectedIndex = 0;
        }

        private ComboBox Combo(string[] items)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat,
                Font = Theme.UIFont
            };
            foreach (var i in items) c.Items.Add(i);
            return c;
        }

        private NumericUpDown Num(decimal min, decimal max, decimal step, decimal value)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Increment = step,
                Value = Math.Max(min, Math.Min(max, value)),
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.UIFont,
                DecimalPlaces = 0
            };
        }

        private TrackBar Slider(int min, int max, int value)
        {
            return new TrackBar
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                TickStyle = TickStyle.None,
                SmallChange = 1,
                LargeChange = 5,
                BackColor = Theme.Panel,
                Height = 22,
                AutoSize = false
            };
        }

        private void Header(string text)
        {
            var lbl = new Label
            {
                Text = text,
                Left = 8,
                Top = _y,
                Width = 270,
                Height = 20,
                ForeColor = Theme.Accent,
                Font = new Font(Theme.UIFont, FontStyle.Bold)
            };
            Controls.Add(lbl);
            _rowPairs.Add(new Control[] { lbl, null });
            _y += 24;
        }

        private void Row(string label, Control control)
        {
            var lbl = new Label
            {
                Text = label,
                Left = 10,
                Top = _y + 3,
                Width = 92,
                Height = 20,
                ForeColor = Theme.TextDim
            };
            control.Left = 104;
            control.Top = _y;
            control.Width = 172;
            control.Height = control is TrackBar ? 24 : 22;
            Controls.Add(lbl);
            Controls.Add(control);
            _rowPairs.Add(new Control[] { lbl, control });
            _y += control is TrackBar ? 26 : 26;
        }

        /// <summary>按面板实际宽度重排各行，避免窄面板下出现横向滚动而遮住标签。</summary>
        private void RelayoutRows()
        {
            if (_rowPairs.Count == 0) return;
            int scrollBar = SystemInformation.VerticalScrollBarWidth;
            int avail = ClientSize.Width - 16 - scrollBar;
            if (avail < 160) avail = 160;

            const int labelWidth = 84;
            const int gap = 6;

            foreach (var pair in _rowPairs)
            {
                var label = pair[0];
                if (pair[1] == null)
                {
                    label.Left = 8;
                    label.Width = avail;
                    continue;
                }
                label.Left = 8;
                label.Width = labelWidth;
                var control = pair[1];
                control.Left = 8 + labelWidth + gap;
                control.Width = Math.Max(110, avail - labelWidth - gap);
            }

            if (_resetButton != null) _resetButton.Width = Math.Max(140, avail);
            if (_info != null)
            {
                _info.Left = 8;
                _info.Width = avail;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RelayoutRows();
        }

        public void Bind(RasterLayer layer)
        {
            _layer = layer;
            SyncFromLayer();
        }

        private void SyncFromLayer()
        {
            _syncing = true;
            try
            {
                if (_layer == null)
                {
                    _info.Text = Lang.T("未选择图层");
                    Enabled = false;
                    return;
                }
                Enabled = true;

                var bands = new string[_layer.BandCount];
                for (int i = 0; i < _layer.BandCount; i++)
                    // 括号整体随语言切换：硬拼全角括号会在英文界面留下中文符号
                    bands[i] = Lang.T("波段 {0}（{1}）", i + 1,
                        _layer.BandTypes[i].ToString().Replace("GDT_", ""));

                Fill(_bandGray, bands);
                Fill(_bandR, bands);
                Fill(_bandG, bands);
                Fill(_bandB, bands);

                var s = _layer.Style;
                _mode.SelectedIndex = s.Mode == RenderMode.Rgb ? 1 : s.Mode == RenderMode.Palette ? 2 : 0;
                _bandGray.SelectedIndex = Math.Max(0, Math.Min(bands.Length - 1, s.GrayBand - 1));
                _bandR.SelectedIndex = Math.Max(0, Math.Min(bands.Length - 1, s.RgbBands[0] - 1));
                _bandG.SelectedIndex = Math.Max(0, Math.Min(bands.Length - 1, s.RgbBands[1] - 1));
                _bandB.SelectedIndex = Math.Max(0, Math.Min(bands.Length - 1, s.RgbBands[2] - 1));

                // 用稳定的中文键（Key）比对，而不是显示名——
                // 显示名随语言变化，跨语言时会匹配不上。
                int rampIdx = 0;
                for (int i = 0; i < ColorRamp.BuiltIn.Length; i++)
                    if (ColorRamp.BuiltIn[i].Key == s.Ramp.Key) rampIdx = i;
                _ramp.SelectedIndex = rampIdx;
                _invert.Checked = s.InvertRamp;

                var st = s.StretchFor(0);
                _stretch.SelectedIndex = StretchIndex(st.Mode);
                _stdDev.Value = st.StdDevFactor;
                _pctLow.Value = (decimal)Math.Max(0, Math.Min(49, st.PercentLow));
                _pctHigh.Value = (decimal)Math.Max(0, Math.Min(49, st.PercentHigh));
                if (st.Mode == StretchMode.CustomMinMax)
                {
                    _customMin.Value = ClampDecimal((decimal)st.CustomMin, _customMin);
                    _customMax.Value = ClampDecimal((decimal)st.CustomMax, _customMax);
                }
                else
                {
                    var stats = _layer.Stats(s.Mode == RenderMode.Rgb ? s.RgbBands[0] : s.GrayBand);
                    if (stats != null)
                    {
                        _customMin.Value = ClampDecimal((decimal)stats.Min, _customMin);
                        _customMax.Value = ClampDecimal((decimal)stats.Max, _customMax);
                    }
                }

                _gamma.Value = (int)Math.Round(s.Gamma * 100);
                _brightness.Value = (int)Math.Round(s.Brightness * 100);
                _contrast.Value = (int)Math.Round(s.Contrast * 100);
                _opacity.Value = (int)Math.Round(s.Opacity * 100);
                _blend.SelectedIndex = (int)s.Blend;
                _resample.SelectedIndex = (int)s.Resample;

                _info.Text = string.Format(Lang.T("{0} × {1} · {2} 波段 · {3}\n当前：{4}"),
                    _layer.Width, _layer.Height, _layer.DisplayBandCount, _layer.TypeText, s.Describe());
            }
            finally
            {
                _syncing = false;
            }
        }

        private static void Fill(ComboBox box, string[] items)
        {
            box.Items.Clear();
            box.Items.AddRange(items);
        }

        private static decimal ClampDecimal(decimal v, NumericUpDown box)
        {
            if (v < box.Minimum) return box.Minimum;
            if (v > box.Maximum) return box.Maximum;
            return v;
        }

        private static int StretchIndex(StretchMode m)
        {
            switch (m)
            {
                case StretchMode.None: return 0;
                case StretchMode.MinMax: return 1;
                case StretchMode.StdDev: return 2;
                case StretchMode.PercentClip: return 3;
                case StretchMode.HistogramEqualize: return 4;
                default: return 5;
            }
        }

        private static StretchMode StretchFromIndex(int i)
        {
            switch (i)
            {
                case 0: return StretchMode.None;
                case 1: return StretchMode.MinMax;
                case 2: return StretchMode.StdDev;
                case 3: return StretchMode.PercentClip;
                case 4: return StretchMode.HistogramEqualize;
                default: return StretchMode.CustomMinMax;
            }
        }

        private void SyncStretchInputs()
        {
            // 输入可用性跟随拉伸方式
            var mode = StretchFromIndex(_stretch.SelectedIndex);
            _stdDev.Enabled = mode == StretchMode.StdDev;
            _pctLow.Enabled = _pctHigh.Enabled = mode == StretchMode.PercentClip;
            _customMin.Enabled = _customMax.Enabled = mode == StretchMode.CustomMinMax;
        }

        private void Apply()
        {
            if (_layer == null) return;
            var s = _layer.Style;

            s.Mode = _mode.SelectedIndex == 1 ? RenderMode.Rgb : _mode.SelectedIndex == 2 ? RenderMode.Palette : RenderMode.Gray;
            s.GrayBand = _bandGray.SelectedIndex + 1;
            s.RgbBands = new[] { _bandR.SelectedIndex + 1, _bandG.SelectedIndex + 1, _bandB.SelectedIndex + 1 };
            s.Ramp = ColorRamp.ByName(_ramp.SelectedItem as string ?? Lang.T("灰阶"));
            s.InvertRamp = _invert.Checked;

            var stretchMode = StretchFromIndex(_stretch.SelectedIndex);
            foreach (var st in new[] { s.GrayStretch, s.RStretch, s.GStretch, s.BStretch })
            {
                st.Mode = stretchMode;
                st.StdDevFactor = (int)_stdDev.Value;
                st.PercentLow = (double)_pctLow.Value;
                st.PercentHigh = (double)_pctHigh.Value;
                if (stretchMode == StretchMode.CustomMinMax)
                {
                    st.CustomMin = (double)_customMin.Value;
                    st.CustomMax = (double)_customMax.Value;
                }
            }

            s.Gamma = Math.Max(0.1, _gamma.Value / 100.0);
            s.Brightness = _brightness.Value / 100.0;
            s.Contrast = Math.Max(0.2, _contrast.Value / 100.0);
            s.Opacity = _opacity.Value / 100.0;
            s.Blend = (BlendMode)Math.Max(0, _blend.SelectedIndex);
            s.Resample = (ResampleMode)Math.Max(0, _resample.SelectedIndex);

            _layer.Touch();
            SyncStretchInputs();

            var h = StyleEdited;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void ResetStyle()
        {
            if (_layer == null) return;
            var def = _layer.CreateDefaultStyle();
            _layer.Style = def;
            _layer.Touch();
            SyncFromLayer();
            var h = StyleEdited;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
