using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.Processing;

using SharpGIS.Services;
namespace SharpGIS.UI.Dialogs
{
    /// <summary>波段计算器：b1/b2 是占位符，可映射到任意已打开（同网格）文件的波段。</summary>
    public sealed class BandCalcDialog : ProcessingDialogBase
    {
        private sealed class MapRow
        {
            public int Slot;
            public Label Caption;
            public ComboBox Box;
        }

        private sealed class BandPickItem
        {
            public BandSource Source;
            public string Text;
            public override string ToString() { return Text; }
        }

        private const int ContentWidth = 496;

        private readonly TextBox _expr;
        private readonly Label _hint, _check;
        private readonly ComboBox _template;
        private readonly ToolTip _tips;
        private readonly LayerStack _stack;

        // 这三个在 BuildMappingHost 里创建（构造函数调用），不能是 readonly
        private TreeView _tree;
        private Panel _mapHost, _mapPanel;
        private readonly List<MapRow> _rows = new List<MapRow>();
        private readonly Dictionary<int, BandSource> _map = new Dictionary<int, BandSource>();
        private FlowLayoutPanel _funcGrid;

        private int[] _rowSlots = new int[0];
        private int _activeSlot;
        private bool _syncing;

        private string _snapExpression;
        private BandMapping _snapMapping;

        /// <summary>只针对当前图层（不显示其它已打开文件）。</summary>
        /// <param name="layer">当前图层。</param>
        public BandCalcDialog(RasterLayer layer) : this(layer, null) { }

        /// <summary>带图层栈，可把其它同网格文件的波段映射到占位符。</summary>
        /// <param name="layer">当前图层（决定输出网格与默认 1:1 映射）。</param>
        /// <param name="stack">当前打开的图层栈，可为 null。</param>
        public BandCalcDialog(RasterLayer layer, LayerStack stack)
            : base(layer, Lang.T("波段计算器"), "bandcalc", ".tif", 660)
        {
            _stack = stack;

            _tips = new ToolTip
            {
                AutoPopDelay = 20000,
                InitialDelay = 300,
                ReshowDelay = 100,
                ShowAlways = true
            };

            // 默认表达式按实际波段数给一个“一定能算”的，避免一打开就红字报错
            string initial = layer.BandCount >= 4
                ? "normalizedDifference(b4, b3)"
                : layer.BandCount >= 3 ? "(b1 + b2 + b3) / 3" : "b1";
            var exprBox = Text2(initial);
            exprBox.Multiline = true;                       // 必须先置为多行，Row 设定的高度才会生效
            _expr = Row(Lang.T("表达式"), exprBox, ContentWidth, 52);

            var templateItems = new List<string> { Lang.T("（选择模板插入…）") };
            templateItems.AddRange(ExpressionParser.Templates);
            _template = Row(Lang.T("常用模板"), Combo(templateItems.ToArray()), ContentWidth);
            _template.SelectedIndex = 0;                    // 占位项：不覆盖用户已填的表达式
            _tips.SetToolTip(_template, Lang.T("选中一个模板会把上面的表达式整体替换为该模板，可再手动修改"));
            _template.SelectedIndexChanged += (s, e) =>
            {
                int index = _template.SelectedIndex - 1;
                if (index >= 0 && index < ExpressionParser.Templates.Length)
                {
                    _expr.Text = ExpressionParser.Templates[index];
                    _expr.SelectionStart = _expr.TextLength;
                    DoValidate();
                }
                UpdateTemplateTip();
            };

            _funcGrid = BuildFunctionGrid();
            // 按钮 56 宽 + 左右各 2 边距 = 60，按内容宽度算每行能放几个，避免行数写死导致裁切
            int perRow = Math.Max(1, ContentWidth / 60);
            int funcRows = (ExpressionParser.Functions.Length + perRow - 1) / perRow;
            Row(Lang.T("函数"), _funcGrid, ContentWidth, funcRows * 28 + 6);

            Row(Lang.T("波段映射"), BuildMappingHost(), ContentWidth, 200);

            var validate = new Button { Text = Lang.T("校验表达式"), Width = 100, Height = 26 };
            validate.Click += (s, e) => DoValidate();
            _check = new Label
            {
                Text = "",
                Width = 390,
                Height = 22,
                ForeColor = Theme.Accent
            };
            RowInline("", new Control[] { validate, _check }, new[] { 100, 390 });

            _hint = Row("", new Label
            {
                Text = "",
                Width = ContentWidth,
                Height = 54,
                ForeColor = Theme.TextDim
            }, ContentWidth);

            _hint.Text = string.Format(
                Lang.T("b1…b{0} 是占位符，默认等于本图层第 1…{0} 波段；可在“波段映射”里改成其它已打开文件的波段。\r\n") +
                Lang.T("点函数按钮插入到光标处，选中文字会自动包进括号。"),
                layer.BandCount);
            Note(Lang.T("结果输出为 Float32 单波段 GeoTIFF；地理参考取自当前图层。"), 40, ContentWidth);

            _expr.TextChanged += (s, e) => DoValidate();
            UpdateTemplateTip();
            DoValidate();
        }

        // ---------------- 波段映射区 ----------------

        private Control BuildMappingHost()
        {
            _mapHost = new Panel { BackColor = Theme.Panel };

            _mapPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Panel
            };

            _tree = new TreeView
            {
                Dock = DockStyle.Left,
                Width = 236,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                LineColor = Theme.Border,
                HideSelection = false,
                FullRowSelect = true,
                ItemHeight = 20,
                Font = Theme.UIFont
            };
            _tree.NodeMouseDoubleClick += (s, e) =>
            {
                var src = e.Node != null ? e.Node.Tag as BandSource : null;
                if (src != null) AssignToActiveRow(src);
            };

            _mapHost.Controls.Add(_mapPanel);
            _mapHost.Controls.Add(_tree);
            BuildTree();
            return _mapHost;
        }

        /// <summary>列出当前图层与（可能存在的）其它已打开图层；网格不一致的置灰并注明原因。</summary>
        private void BuildTree()
        {
            var layers = new List<RasterLayer> { Layer };
            if (_stack != null)
            {
                foreach (var l in _stack.ToArray())
                    if (!ReferenceEquals(l, Layer)) layers.Add(l);
            }

            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                foreach (var layer in layers)
                {
                    string reason;
                    bool same = RasterGrid.Same(Layer, layer, out reason);

                    string text = layer.Name;
                    if (ReferenceEquals(layer, Layer)) text += Lang.T("（当前图层）");
                    else if (!same) text += Lang.T("（网格不一致：{0}）", reason);
                    else if (!RasterGrid.SameProjection(Layer, layer)) text += Lang.T("（投影不同）");

                    var node = new TreeNode(text) { Tag = layer };
                    for (int b = 1; b <= layer.BandCount; b++)
                        node.Nodes.Add(new TreeNode(BandSource.BandText(layer, b)) { Tag = new BandSource(layer, b) });

                    if (!same)
                    {
                        node.ForeColor = Theme.TextDim;
                        foreach (TreeNode child in node.Nodes) child.ForeColor = Theme.TextDim;
                    }
                    else if (!RasterGrid.SameProjection(Layer, layer))
                    {
                        node.ForeColor = Theme.Warn;
                        foreach (TreeNode child in node.Nodes) child.ForeColor = Theme.Warn;
                    }

                    _tree.Nodes.Add(node);
                    if (ReferenceEquals(layer, Layer)) node.Expand();
                }
            }
            finally
            {
                _tree.EndUpdate();
            }
        }

        /// <summary>把某个波段赋给当前活动的映射行。</summary>
        /// <param name="src">波段引用。</param>
        private void AssignToActiveRow(BandSource src)
        {
            MapRow row = null;
            foreach (var r in _rows) if (r.Slot == _activeSlot) { row = r; break; }
            if (row == null && _rows.Count > 0) row = _rows[0];
            if (row == null) return;

            _map[row.Slot] = src;
            _syncing = true;
            try { SelectInCombo(row.Box, src); }
            finally { _syncing = false; }
            DoValidate();
        }

        /// <summary>按表达式里出现的占位符重建映射行；已有的选择按槽位号保留。</summary>
        /// <param name="slots">表达式用到的槽位号（升序）。</param>
        private void SyncMappingRows(int[] slots)
        {
            if (SameSlots(slots, _rowSlots))
            {
                RefreshRowSelections();
                return;
            }

            _syncing = true;
            _mapPanel.SuspendLayout();
            try
            {
                foreach (var row in _rows)
                {
                    _mapPanel.Controls.Remove(row.Caption);
                    _mapPanel.Controls.Remove(row.Box);
                    row.Caption.Dispose();
                    row.Box.Dispose();
                }
                _rows.Clear();

                for (int i = 0; i < slots.Length; i++)
                {
                    int slot = slots[i];
                    BandSource current;
                    if (!_map.TryGetValue(slot, out current))
                    {
                        // 默认 1:1：bN → 当前图层第 N 波段（超出范围则留空，由校验提示）
                        current = slot <= Layer.BandCount ? new BandSource(Layer, slot) : null;
                        _map[slot] = current;
                    }

                    var caption = new Label
                    {
                        Text = "b" + slot + " →",
                        AutoSize = true,
                        ForeColor = Theme.TextDim,
                        Left = 4,
                        Top = i * 30 + 6
                    };

                    var box = new ComboBox
                    {
                        Left = 56,
                        Top = i * 30 + 3,
                        Width = 182,
                        DropDownWidth = 400,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        BackColor = Theme.PanelAlt,
                        ForeColor = Theme.Text,
                        FlatStyle = FlatStyle.Flat
                    };
                    FillBandCombo(box, current);
                    SelectInCombo(box, current);

                    int captured = slot;
                    box.SelectedIndexChanged += (s, e) =>
                    {
                        if (_syncing) return;
                        var item = box.SelectedItem as BandPickItem;
                        _map[captured] = item != null ? item.Source : null;
                        _activeSlot = captured;
                        DoValidate();
                    };
                    box.GotFocus += (s, e) => _activeSlot = captured;

                    _mapPanel.Controls.Add(caption);
                    _mapPanel.Controls.Add(box);
                    _rows.Add(new MapRow { Slot = slot, Caption = caption, Box = box });
                }

                _rowSlots = slots;
            }
            finally
            {
                _mapPanel.ResumeLayout();
                _syncing = false;
            }

            if (slots.Length > 0 && !Contains(slots, _activeSlot)) _activeSlot = slots[0];
        }

        /// <summary>填充映射下拉：未指定 → 当前图层全部波段 → 其它同网格图层的波段。</summary>
        /// <param name="box">目标下拉框。</param>
        /// <param name="current">当前已选中的波段，可能为 null。</param>
        private void FillBandCombo(ComboBox box, BandSource current)
        {
            box.Items.Clear();
            box.Items.Add(new BandPickItem { Source = null, Text = Lang.T("（未指定）") });

            for (int b = 1; b <= Layer.BandCount; b++)
                box.Items.Add(new BandPickItem { Source = new BandSource(Layer, b), Text = BandSource.BandText(Layer, b) });

            if (_stack != null)
            {
                foreach (var l in _stack.ToArray())
                {
                    if (ReferenceEquals(l, Layer)) continue;
                    string reason;
                    if (!RasterGrid.Same(Layer, l, out reason)) continue;    // 网格不一致的不列出

                    bool sameProjection = RasterGrid.SameProjection(Layer, l);
                    for (int b = 1; b <= l.BandCount; b++)
                    {
                        var src = new BandSource(l, b);
                        box.Items.Add(new BandPickItem
                        {
                            Source = src,
                            Text = src.DisplayName + (sameProjection ? string.Empty : " ⚠")
                        });
                    }
                }
            }

            // 已选中的波段若不在列表里（例如图层已被关闭），补一条，避免选中态丢失
            if (current != null && FindIndex(box, current) < 0)
                box.Items.Add(new BandPickItem { Source = current, Text = current.DisplayName });
        }

        private static int FindIndex(ComboBox box, BandSource src)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                var item = box.Items[i] as BandPickItem;
                if (item != null && item.Source != null && item.Source.Equals(src)) return i;
            }
            return -1;
        }

        private static void SelectInCombo(ComboBox box, BandSource src)
        {
            int index = src == null ? 0 : FindIndex(box, src);
            box.SelectedIndex = index >= 0 ? index : 0;
        }

        /// <summary>刷新每行下拉的条目（图层集合可能变化）并保持选中。</summary>
        private void RefreshRowSelections()
        {
            _syncing = true;
            try
            {
                foreach (var row in _rows)
                {
                    BandSource current;
                    _map.TryGetValue(row.Slot, out current);
                    FillBandCombo(row.Box, current);
                    SelectInCombo(row.Box, current);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private static bool SameSlots(int[] a, int[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool Contains(int[] values, int value)
        {
            foreach (var v in values) if (v == value) return true;
            return false;
        }

        /// <summary>从表达式里提取占位符槽位号（b1、B2…），忽略 b0 与非数字。</summary>
        /// <param name="text">表达式文本。</param>
        /// <returns>升序去重的槽位号。</returns>
        private static int[] ExtractSlots(string text)
        {
            var set = new SortedSet<int>();
            if (!string.IsNullOrEmpty(text))
            {
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(text, @"\bb(\d{1,3})\b",
                             System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    int n;
                    if (int.TryParse(m.Groups[1].Value, out n) && n >= 1) set.Add(n);
                }
            }
            return new List<int>(set).ToArray();
        }

        private BandMapping BuildMapping()
        {
            return BandMapping.FromSlots(Layer.BandCount, _map);
        }

        // ---------------- 函数面板 ----------------

        /// <summary>按 <see cref="ExpressionParser.Functions"/> 生成函数按钮，每个按钮带用法提示。</summary>
        private FlowLayoutPanel BuildFunctionGrid()
        {
            var grid = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                AutoSize = false,
                BackColor = Theme.Panel,
                Padding = new Padding(0, 2, 0, 0)
            };

            foreach (var fn in ExpressionParser.Functions)
            {
                var button = new Button
                {
                    Text = fn.Name,
                    Width = 56,
                    Height = 24,
                    Margin = new Padding(2)
                };
                button.Click += (s, e) => InsertFunction(fn);
                _tips.SetToolTip(button, fn.Tip());
                grid.Controls.Add(button);
            }
            return grid;
        }

        /// <summary>把函数片段插入到光标处；若选中了文本且该函数支持，则把选中内容包进括号。</summary>
        /// <param name="fn">要插入的函数。</param>
        private void InsertFunction(FunctionDoc fn)
        {
            int start = _expr.SelectionStart;
            int length = _expr.SelectionLength;
            string text = _expr.Text ?? string.Empty;

            if (start < 0 || start > text.Length) start = text.Length;
            string selected = length > 0 && start + length <= text.Length
                ? text.Substring(start, length) : string.Empty;

            string insert;
            int caret;
            if (selected.Length > 0 && fn.WrapSelection)
            {
                // 先把选区从原文里摘掉，再用函数把它包起来，否则原文会残留一份
                text = text.Remove(start, length);
                insert = fn.Name + "(" + selected + ")";
                caret = insert.Length;
            }
            else
            {
                if (selected.Length > 0) text = text.Remove(start, length);
                insert = fn.Insert;
                caret = Math.Min(fn.CaretOffset, insert.Length);
            }

            _expr.Text = text.Substring(0, start) + insert + text.Substring(start);
            _expr.SelectionStart = start + caret;
            _expr.SelectionLength = 0;
            _expr.Focus();
            DoValidate();
        }

        /// <summary>模板下拉的悬浮提示显示当前模板的用途。</summary>
        private void UpdateTemplateTip()
        {
            int index = _template.SelectedIndex - 1;      // 第 0 项是占位提示
            var descriptions = ExpressionParser.TemplateDescriptions;
            if (index >= 0 && index < descriptions.Length)
                _tips.SetToolTip(_template, descriptions[index]);
            else
                _tips.SetToolTip(_template, Lang.T("选中一个模板会把上面的表达式整体替换为该模板，可再手动修改"));
        }

        // ---------------- 校验与执行 ----------------

        private void DoValidate()
        {
            if (_syncing) return;

            SyncMappingRows(ExtractSlots(_expr.Text));

            string err = BandCalculator.Validate(_expr.Text, BuildMapping(), Layer);
            if (err == null)
            {
                string warning = ProjectionWarning();
                if (warning != null)
                {
                    _check.Text = warning;
                    _check.ForeColor = Theme.Warn;
                }
                else
                {
                    _check.Text = Lang.T("✓ 表达式可用");
                    _check.ForeColor = System.Drawing.Color.FromArgb(120, 200, 120);
                }
            }
            else
            {
                _check.Text = "✗ " + err;
                _check.ForeColor = Theme.Warn;
            }
        }

        /// <summary>投影不同但网格一致的提示（不阻塞计算）。</summary>
        private string ProjectionWarning()
        {
            var names = BuildMapping().ProjectionMismatches(Layer);
            if (names == null || names.Length == 0) return null;
            return "⚠ " + string.Join(Lang.IsEnglish ? ", " : "、", names)
                + Lang.T(" 与当前图层投影不同，结果按当前图层的地理参考写出");
        }

        protected override bool ValidateInputs(out string error)
        {
            error = BandCalculator.Validate(_expr.Text, BuildMapping(), Layer);
            return error == null;
        }

        protected override void CaptureParameters()
        {
            // 只在 UI 线程读控件；后台线程的 RunCore 只用这两个快照
            _snapExpression = _expr.Text;
            _snapMapping = BuildMapping();
        }

        protected override string RunCore(ProgressReporter reporter, string outputPath)
        {
            return BandCalculator.Run(Layer, _snapExpression, _snapMapping, outputPath, reporter);
        }

        // ---------------- 自检挂钩 ----------------

        /// <summary>供自检读取表达式内容。</summary>
        internal TextBox ExpressionForTest { get { return _expr; } }

        /// <summary>当前映射行对应的槽位号（升序）。</summary>
        internal int[] MappingRowSlotsForTest { get { return _rowSlots; } }

        /// <summary>波段树。</summary>
        internal TreeView BandTreeForTest { get { return _tree; } }

        /// <summary>当前映射涉及到的图层。</summary>
        internal RasterLayer[] MappedLayersForTest { get { return BuildMapping().DistinctLayers(); } }

        /// <summary>供自检直接指定映射。</summary>
        /// <param name="slot">槽位号。</param>
        /// <param name="layer">目标图层。</param>
        /// <param name="band">波段号。</param>
        internal void SetMappingForTest(int slot, RasterLayer layer, int band)
        {
            _map[slot] = new BandSource(layer, band);
            RefreshRowSelections();
            DoValidate();
        }

        /// <summary>供自检读取某个槽位当前映射到的波段。</summary>
        /// <param name="slot">槽位号。</param>
        /// <returns>波段引用；未指定返回 null。</returns>
        internal BandSource MappingForTest(int slot)
        {
            BandSource src;
            return _map.TryGetValue(slot, out src) ? src : null;
        }

        /// <summary>供自检读取函数按钮。</summary>
        internal Button[] FunctionButtonsForTest
        {
            get
            {
                var list = new List<Button>();
                if (_funcGrid != null)
                {
                    foreach (Control child in _funcGrid.Controls)
                    {
                        var b = child as Button;
                        if (b != null) list.Add(b);
                    }
                }
                return list.ToArray();
            }
        }

        /// <summary>供自检读取某个控件的悬浮提示文本。</summary>
        internal string ToolTipForTest(Control c)
        {
            return _tips != null && c != null ? _tips.GetToolTip(c) : null;
        }
    }

    /// <summary>DEM 分析。</summary>
    public sealed class DemDialog : ProcessingDialogBase
    {
        private readonly ComboBox _band, _algorithm, _ramp;
        private readonly NumericUpDown _azimuth, _altitude, _zFactor, _rampMin, _rampMax;
        private readonly CheckBox _edges, _percent;

        private DemParams _snapParams;

        public DemDialog(RasterLayer layer) : base(layer, Lang.T("DEM 分析"), "dem")
        {
            _band = Row(Lang.T("输入波段"), Combo(BandItems(layer.BandCount)));
            _algorithm = Row(Lang.T("算法"), Combo(DemService.AlgorithmNames));
            _azimuth = Row(Lang.T("光源方位角"), Num(0, 360, 315));
            _altitude = Row(Lang.T("光源高度角"), Num(0, 90, 45));
            _zFactor = Row(Lang.T("高程夸张系数 Z"), Num(0.01, 100, 1, 2, 0.1));
            _percent = Row(Lang.T("坡度单位"), new CheckBox { Text = Lang.T("以百分比表示（默认度）"), ForeColor = Theme.Text, Width = 300 });
            _edges = Row(Lang.T("边缘处理"), new CheckBox { Text = Lang.T("计算边缘（避免一圈 NoData）"), Checked = true, ForeColor = Theme.Text, Width = 300 });
            _ramp = Row(Lang.T("彩色渲染色带"), Combo(RampNames()));
            _rampMin = Row(Lang.T("色带最小值"), Num(-1e6, 1e6, 0, 2, 10));
            _rampMax = Row(Lang.T("色带最大值"), Num(-1e6, 1e6, 255, 2, 10));

            _algorithm.SelectedIndexChanged += (s, e) => SyncControls();
            SyncControls();
        }

        private static string[] RampNames()
        {
            var list = new string[ColorRamp.BuiltIn.Length];
            for (int i = 0; i < list.Length; i++) list[i] = ColorRamp.BuiltIn[i].Name;
            return list;
        }

        private void SyncControls()
        {
            bool relief = _algorithm.SelectedIndex == 3;
            bool hillshade = _algorithm.SelectedIndex == 0;
            _ramp.Enabled = _rampMin.Enabled = _rampMax.Enabled = relief;
            _azimuth.Enabled = _altitude.Enabled = _zFactor.Enabled = hillshade;
            _percent.Enabled = _algorithm.SelectedIndex == 1;
        }

        protected override void CaptureParameters()
        {
            _snapParams = new DemParams
            {
                Band = _band.SelectedIndex + 1,
                Algorithm = DemService.AlgorithmKey(_algorithm.SelectedItem as string),
                Azimuth = (double)_azimuth.Value,
                Altitude = (double)_altitude.Value,
                ZFactor = (double)_zFactor.Value,
                SlopePercent = _percent.Checked,
                ComputeEdges = _edges.Checked,
                Ramp = ColorRamp.ByName(_ramp.SelectedItem as string),
                RampMin = (double)_rampMin.Value,
                RampMax = (double)_rampMax.Value
            };
        }

        protected override string RunCore(ProgressReporter reporter, string outputPath)
        {
            return DemService.Run(Layer, _snapParams, outputPath, reporter);
        }
    }

    /// <summary>滤波。</summary>
    public sealed class FilterDialog : ProcessingDialogBase
    {
        private readonly ComboBox _band, _kind, _size;
        private readonly NumericUpDown _sigma;

        private int _snapBand, _snapSize;
        private FilterKind _snapKind;
        private double _snapSigma;

        public FilterDialog(RasterLayer layer) : base(layer, Lang.T("空域滤波"), "filter")
        {
            _band = Row(Lang.T("输入波段"), Combo(BandItems(layer.BandCount)));
            _kind = Row(Lang.T("滤波类型"), Combo(FilterService.KindNames));
            _size = Row(Lang.T("窗口大小"), Combo("3 × 3", "5 × 5", "7 × 7"));
            _sigma = Row(Lang.T("高斯 σ"), Num(0.1, 20, 1.4, 2, 0.1));
            _kind.SelectedIndexChanged += (s, e) => _sigma.Enabled = _kind.SelectedIndex == 2;
            _sigma.Enabled = false;
            Note(Lang.T("Byte 数据（Sobel 除外）输出仍为 Byte；其余类型输出 Float32。分块处理，支持大图。"), 40);
        }

        protected override void CaptureParameters()
        {
            _snapBand = _band.SelectedIndex + 1;
            _snapKind = FilterService.KindFromName(_kind.SelectedItem as string);
            _snapSize = _size.SelectedIndex == 1 ? 5 : _size.SelectedIndex == 2 ? 7 : 3;
            _snapSigma = (double)_sigma.Value;
        }

        protected override string RunCore(ProgressReporter reporter, string outputPath)
        {
            return FilterService.Run(Layer, _snapBand, _snapKind, _snapSize, _snapSigma, outputPath, reporter);
        }
    }

    /// <summary>影像增强。</summary>
    public sealed class EnhanceDialog : ProcessingDialogBase
    {
        private readonly ComboBox _kind, _band;
        private readonly CheckBox _allBands;
        private readonly NumericUpDown _low, _high;

        private EnhanceKind _snapKind;
        private bool _snapAllBands;
        private int _snapBand;
        private double _snapLow, _snapHigh;

        public EnhanceDialog(RasterLayer layer) : base(layer, Lang.T("影像增强"), "enhance")
        {
            _kind = Row(Lang.T("增强方式"), Combo(EnhanceService.KindNames));
            _allBands = Row(Lang.T("作用范围"), new CheckBox { Text = Lang.T("全部波段"), Checked = true, ForeColor = Theme.Text, Width = 200 });
            _band = Row(Lang.T("单波段模式"), Combo(BandItems(layer.BandCount)));
            _low = Row(Lang.T("累计裁剪下限 %"), Num(0, 49, 2));
            _high = Row(Lang.T("累计裁剪上限 %"), Num(0, 49, 2));
            _allBands.CheckedChanged += (s, e) => _band.Enabled = !_allBands.Checked;
            _band.Enabled = false;
            Note(Lang.T("输出 8bit GeoTIFF，便于后续做分类或导入其它软件。"), 32);
        }

        protected override void CaptureParameters()
        {
            _snapKind = _kind.SelectedIndex == 1 ? EnhanceKind.EqualizeToByte : EnhanceKind.StretchToByte;
            _snapAllBands = _allBands.Checked;
            _snapBand = _band.SelectedIndex + 1;
            _snapLow = (double)_low.Value;
            _snapHigh = (double)_high.Value;
        }

        protected override string RunCore(ProgressReporter reporter, string outputPath)
        {
            return EnhanceService.Run(Layer, _snapKind, _snapAllBands, _snapBand,
                _snapLow, _snapHigh, outputPath, reporter);
        }
    }

    /// <summary>影像融合。</summary>
    public sealed class FusionDialog : ProcessingDialogBase
    {
        private readonly ComboBox _msLayer, _r, _g, _b, _panLayer, _panBand, _method;
        private readonly LayerStack _stack;

        private RasterLayer _snapMs, _snapPan;
        private int[] _snapBands;
        private int _snapPanBand;
        private FusionMethod _snapMethod;

        public FusionDialog(RasterLayer current, LayerStack stack)
            : base(current, Lang.T("影像融合（全色锐化）"), "fusion")
        {
            _stack = stack;
            var layers = stack.ToArray();
            var names = new string[layers.Length];
            for (int i = 0; i < names.Length; i++) names[i] = layers[i].Name;

            _msLayer = Row(Lang.T("多光谱图层"), Combo(names));
            _r = Row(Lang.T("R 波段"), Combo(BandItems(current.BandCount)));
            _g = Row(Lang.T("G 波段"), Combo(BandItems(current.BandCount)));
            _b = Row(Lang.T("B 波段"), Combo(BandItems(current.BandCount)));
            _panLayer = Row(Lang.T("全色图层"), Combo(names));
            _panBand = Row(Lang.T("全色波段"), Combo(BandItems(current.BandCount)));
            _method = Row(Lang.T("融合方法"), Combo(FusionService.MethodNames));

            if (current.BandCount >= 3) { _r.SelectedIndex = 0; _g.SelectedIndex = 1; _b.SelectedIndex = 2; }

            _msLayer.SelectedIndexChanged += (s, e) => RefreshBands();
            _panLayer.SelectedIndexChanged += (s, e) => RefreshPanBands();

            // 默认：多光谱取当前图层，全色取波段数最少的图层
            _msLayer.SelectedIndex = IndexOf(layers, current);
            int panIdx = IndexOfSmallest(layers);
            if (panIdx >= 0) _panLayer.SelectedIndex = panIdx;
            RefreshPanBands();

            Note(Lang.T("要求全色与多光谱已完成几何配准（范围一致）。输出为 8bit 三波段 GeoTIFF。"), 40);
        }

        private static int IndexOf(RasterLayer[] layers, RasterLayer target)
        {
            for (int i = 0; i < layers.Length; i++) if (ReferenceEquals(layers[i], target)) return i;
            return 0;
        }

        private static int IndexOfSmallest(RasterLayer[] layers)
        {
            int best = -1, bestBands = int.MaxValue;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].BandCount < bestBands) { bestBands = layers[i].BandCount; best = i; }
            return best;
        }

        private RasterLayer LayerAt(ComboBox box)
        {
            var arr = _stack.ToArray();
            int i = box.SelectedIndex;
            return i >= 0 && i < arr.Length ? arr[i] : null;
        }

        private void RefreshBands()
        {
            var layer = LayerAt(_msLayer);
            if (layer == null) return;
            Fill(_r, BandItems(layer.BandCount));
            Fill(_g, BandItems(layer.BandCount));
            Fill(_b, BandItems(layer.BandCount));
            if (layer.BandCount >= 3) { _r.SelectedIndex = 0; _g.SelectedIndex = 1; _b.SelectedIndex = 2; }
        }

        private void RefreshPanBands()
        {
            var layer = LayerAt(_panLayer);
            if (layer == null) return;
            Fill(_panBand, BandItems(layer.BandCount));
            _panBand.SelectedIndex = 0;
        }

        private static void Fill(ComboBox box, string[] items)
        {
            box.Items.Clear();
            box.Items.AddRange(items);
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        protected override bool ValidateInputs(out string error)
        {
            error = null;
            var ms = LayerAt(_msLayer);
            var pan = LayerAt(_panLayer);
            if (ms == null || pan == null) { error = Lang.T("请选择多光谱与全色图层"); return false; }
            if (ms == pan) { error = Lang.T("多光谱与全色图层不能是同一个"); return false; }
            if (ms.BandCount < 3) { error = Lang.T("多光谱图层至少要有 3 个波段"); return false; }
            return true;
        }

        protected override void CaptureParameters()
        {
            _snapMs = LayerAt(_msLayer);
            _snapPan = LayerAt(_panLayer);
            _snapBands = new[] { _r.SelectedIndex + 1, _g.SelectedIndex + 1, _b.SelectedIndex + 1 };
            _snapPanBand = _panBand.SelectedIndex + 1;
            _snapMethod = FusionService.MethodFromName(_method.SelectedItem as string);
        }

        protected override string RunCore(ProgressReporter reporter, string outputPath)
        {
            return FusionService.Run(_snapMs, _snapBands, _snapPan, _snapPanBand,
                _snapMethod, outputPath, reporter);
        }
    }

    /// <summary>KMeans 非监督分类。</summary>
    public sealed class KMeansDialog : ProcessingDialogBase
    {
        private readonly TextBox _bands;
        private readonly NumericUpDown _k, _iterations, _seed;
        private readonly CheckBox _loadPalette;

        private KMeansOptions _snapOptions;

        public KMeansDialog(RasterLayer layer) : base(layer, Lang.T("非监督分类（KMeans）"), "kmeans")
        {
            _bands = Row(Lang.T("参与波段"), Text2(layer.BandCount >= 3 ? "1,2,3" : "1"), 160);
            _k = Row(Lang.T("类别数 K"), Num(2, 30, 6));
            _iterations = Row(Lang.T("最大迭代次数"), Num(1, 100, 20));
            _seed = Row(Lang.T("随机种子"), Num(1, 999999, 12345));
            _loadPalette = Row("", new CheckBox { Text = Lang.T("输出调色板（便于直接显示）"), Checked = true, ForeColor = Theme.Text, Width = 300 });
            Note(Lang.T("算法：KMeans++ 初始化 + 抽样训练（每边最多 256 个样本）+ 分块分类，输出 Byte 类别图。"), 40);
        }

        protected override bool ValidateInputs(out string error)
        {
            var bands = ParseBands(_bands.Text, Layer.BandCount);
            if (bands.Length == 0) { error = Lang.T("请填写参与分类的波段号，例如 1,2,3"); return false; }
            error = null;
            return true;
        }

        protected override void CaptureParameters()
        {
            _snapOptions = new KMeansOptions
            {
                Bands = ParseBands(_bands.Text, Layer.BandCount),
                K = (int)_k.Value,
                MaxIterations = (int)_iterations.Value,
                Seed = (int)_seed.Value,
                WritePalette = _loadPalette.Checked
            };
        }

        protected override string RunCore(ProgressReporter reporter, string outputPath)
        {
            var result = KMeansService.Run(Layer, _snapOptions, outputPath, reporter);
            return result.OutputPath;
        }
    }
}
