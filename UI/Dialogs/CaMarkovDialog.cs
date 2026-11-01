using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.Processing;
using SharpGIS.Services;

namespace SharpGIS.UI.Dialogs
{
    /// <summary>
    /// 土地利用变化预测（CA-Markov）参数对话框。
    /// 四页：输入数据 / 转移与 CA / 适宜性 / 评价与输出；所有输入栅格与参数都由用户指定。
    /// </summary>
    public sealed class CaMarkovDialog : ProcessingDialogBase
    {
        private const int PageWidth = 730;

        private LayerStack _stack;
        private List<CaPeriod> _periods = new List<CaPeriod>();
        private List<CaFactor> _factors = new List<CaFactor>();
        private List<CaDriver> _drivers = new List<CaDriver>();

        private TabControl _tabs;

        // 页 1
        private ListBox _periodList;
        private NumericUpDown _noData;
        private NumericUpDown _periodBand;
        private NumericUpDown _periodYear;
        private bool _syncingPeriodEditor;
        private NumericUpDown _classCount;
        private ComboBox _transition;
        private Label _periodHint;
        private Button _analyze;

        // 页 2：类别名称
        private DataGridView _classGrid;
        private Label _classStatLabel;
        private string[] _classNames = new string[0];       // 下标即类别值
        private readonly Dictionary<int, long> _classCounts = new Dictionary<int, long>();
        private string _classStatSource = "";

        // 页 2
        private NumericUpDown _steps;
        private ComboBox _neighborhood;
        private CheckBox _recompute;
        private NumericUpDown _suitWeight;
        private NumericUpDown _neighWeight;
        private Label _weightHint;
        private CheckBox _modulate;
        private TextBox _constraint;

        // 页 3
        private ComboBox _mode;
        private Panel _rfPanel;
        private Panel _factorPanel;
        private Panel _extPanel;
        private Label _noneHint;
        private TextBox _modelPath;
        private TextBox _saveModelPath;
        private CheckBox _forceTrain;
        private ListBox _driverList;
        private NumericUpDown _trees;
        private NumericUpDown _depth;
        private NumericUpDown _imbalance;
        private NumericUpDown _seed;
        private Label _trainResult;
        private ListBox _factorList;
        private NumericUpDown _factorWeight;
        private CheckBox _factorNegative;
        private ComboBox _normalize;
        private NumericUpDown _pLow;
        private NumericUpDown _pHigh;
        private TextBox _externalPath;
        private NumericUpDown _extBand;
        private ComboBox _extBandMode;

        // 页 4
        private CheckBox _validate;
        private CheckBox _samePeriod;
        private CheckBox _writeReport;
        private CheckBox _compare;
        private CheckBox _hotspot;
        private CheckBox _showResult;
        private Label _estimate;

        private CaMarkovParams _snap;
        private bool _suppressWeightEvent;

        /// <summary>本次运行的结果（RunCore 在后台线程写入）。</summary>
        public CaMarkovResult LastResult { get; private set; }

        /// <summary>完成后是否自动打开看图窗口。</summary>
        public bool ShowResultAfter { get; private set; }

        /// <summary>供界面自检切换标签页截图。</summary>
        internal TabControl TabsForTest { get { return _tabs; } }

        /// <summary>创建对话框。</summary>
        /// <param name="layer">当前图层（用于生成默认输出路径）。</param>
        public CaMarkovDialog(RasterLayer layer) : this(layer, null) { }

        /// <summary>创建对话框。</summary>
        /// <param name="layer">当前图层。</param>
        /// <param name="stack">图层栈，用于「从已打开图层添加」。</param>
        public CaMarkovDialog(RasterLayer layer, LayerStack stack)
            : base(layer, Lang.T("土地利用变化预测（CA-Markov）"), "camarkov", ".tif", 780)
        {
            _stack = stack;

            _tabs = new TabControl { Font = Theme.UIFont, Padding = new Point(10, 4) };
            Block(_tabs, 456);

            // ---------------- 页 1：输入数据 ----------------
            var p1 = NewPage(Lang.T("输入数据"));
            p1.Controls.Add(L(Lang.T("时期（旧 → 新，至少两期）"), 8, 10, 400));
            _periodList = new ListBox
            {
                Left = 8, Top = 30, Width = 520, Height = 166,
                BackColor = Theme.PanelAlt, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            _periodList.SelectedIndexChanged += (s, e) => { SyncPeriodEditor(); UpdatePeriodHint(); };
            p1.Controls.Add(_periodList);

            p1.Controls.Add(B(Lang.T("添加文件…"), 536, 30, 104, 26, (s, e) => AddPeriodFromFile()));
            p1.Controls.Add(B(Lang.T("从已打开图层"), 536, 60, 104, 26, (s, e) => AddPeriodFromStack()));
            p1.Controls.Add(B(Lang.T("上移"), 536, 90, 104, 26, (s, e) => MovePeriod(-1)));
            p1.Controls.Add(B(Lang.T("下移"), 536, 120, 104, 26, (s, e) => MovePeriod(1)));
            p1.Controls.Add(B(Lang.T("移除"), 536, 150, 104, 26, (s, e) => RemovePeriod()));

            p1.Controls.Add(L(Lang.T("无效值"), 8, 206, 52));
            _noData = N(-1e9, 1e9, 0, 0, 1, 64, 204, 88);
            p1.Controls.Add(_noData);
            p1.Controls.Add(L(Lang.T("类别数"), 168, 206, 52));
            _classCount = N(0, 256, 0, 0, 1, 224, 204, 72);
            p1.Controls.Add(_classCount);
            p1.Controls.Add(L(Lang.T("0 = 自动推断"), 304, 206, 120, 18, true));

            p1.Controls.Add(L(Lang.T("转移矩阵来源"), 8, 236, 96));
            _transition = C(106, 234, 264,
                Lang.T("最近一期（外推能力最弱）"), Lang.T("最近两期平均（推荐）"), Lang.T("全部时期累计"));
            _transition.SelectedIndex = 1;
            _transition.SelectedIndexChanged += (s, e) => UpdatePeriodHint();
            p1.Controls.Add(_transition);

            // 选中时期的波段与年份都可以直接改（年份默认从文件名识别）
            p1.Controls.Add(L(Lang.T("波段"), 386, 236, 36));
            _periodBand = N(1, 64, 1, 0, 1, 424, 234, 56);
            _periodBand.ValueChanged += (s, e) =>
            {
                if (_syncingPeriodEditor) return;
                int i = _periodList.SelectedIndex;
                if (i < 0 || i >= _periods.Count) return;
                _periods[i].Band = (int)_periodBand.Value;
                RefreshPeriodList();
            };
            p1.Controls.Add(_periodBand);

            p1.Controls.Add(L(Lang.T("年份"), 494, 236, 36));
            _periodYear = N(0, 2200, 0, 0, 1, 532, 234, 70);
            _periodYear.ValueChanged += (s, e) =>
            {
                if (_syncingPeriodEditor) return;
                int i = _periodList.SelectedIndex;
                if (i < 0 || i >= _periods.Count) return;
                _periods[i].Year = (int)_periodYear.Value;
                RefreshPeriodList();
            };
            p1.Controls.Add(_periodYear);

            _periodHint = L("", 8, 264, PageWidth, 62, true);
            p1.Controls.Add(_periodHint);

            _analyze = B(Lang.T("先分析（只看矩阵，约 10 秒）"), 8, 334, 250, 30, (s, e) => OnAnalyze());
            p1.Controls.Add(_analyze);
            p1.Controls.Add(L(Lang.T("分析不写任何文件，只把转移矩阵与面积统计画出来供调参。"),
                268, 340, 460, 20, true));
            p1.Controls.Add(L(Lang.T("年份只用于显示与推算预测年份；真正决定预测跨度的是「步数 × 区间年数」。"),
                8, 372, 700, 20, true));

            // ---------------- 页 2：类别名称 ----------------
            var pc = NewPage(Lang.T("类别"));
            pc.Controls.Add(L(Lang.T("类别值来自栅格本身；点「统计类别」按最后一期逐像元统计，"), 8, 8, 700, 18, true));
            pc.Controls.Add(L(Lang.T("表格只列出「数量大于 0 且不是无效值」的类别，右侧名称可双击直接改。"), 8, 26, 700, 18, true));

            pc.Controls.Add(B(Lang.T("统计类别"), 8, 50, 110, 28, (s, e) => CountClassesIntoGrid()));
            pc.Controls.Add(B(Lang.T("按 CLCD 命名"), 126, 50, 120, 28, (s, e) => ApplyClcdNames()));
            pc.Controls.Add(B(Lang.T("恢复默认名"), 254, 50, 110, 28, (s, e) => ResetClassNames()));
            _classStatLabel = L("", 372, 56, 370, 18, true);
            pc.Controls.Add(_classStatLabel);

            _classGrid = new DataGridView
            {
                Left = 8,
                Top = 86,
                Width = 714,
                Height = 292,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Theme.PanelAlt,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.UIFont,
                GridColor = Theme.Border
            };
            _classGrid.DefaultCellStyle.BackColor = Theme.PanelAlt;
            _classGrid.DefaultCellStyle.ForeColor = Theme.Text;
            _classGrid.DefaultCellStyle.SelectionBackColor = Theme.Selected;
            _classGrid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            _classGrid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel;
            _classGrid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextDim;
            _classGrid.EnableHeadersVisualStyles = false;
            _classGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _classGrid.ColumnHeadersHeight = 24;
            _classGrid.EditingControlShowing += (s, e) =>
            {
                var box = e.Control as TextBox;
                if (box != null) { box.BackColor = Theme.Panel; box.ForeColor = Theme.Text; }
            };
            pc.Controls.Add(_classGrid);

            pc.Controls.Add(L(Lang.T("无效值不参与建模、也不会出现在上表；名称只影响报告与图例，不影响计算。"),
                8, 386, 700, 18, true));

            // ---------------- 页 2：转移与 CA ----------------
            var p2 = NewPage(Lang.T("转移与 CA"));
            p2.Controls.Add(L(Lang.T("预测步数"), 8, 12, 64));
            _steps = N(1, 20, 1, 0, 1, 76, 10, 70);
            p2.Controls.Add(_steps);
            p2.Controls.Add(L(Lang.T("每步 = 一个训练区间（例如 5 年）；步数大于 1 时会逐步重算预算与邻域。"),
                158, 12, 540, 20, true));

            p2.Controls.Add(L(Lang.T("邻域窗口"), 8, 46, 64));
            _neighborhood = C(76, 44, 140, "3 × 3", Lang.T("5 × 5（推荐）"), "7 × 7");
            _neighborhood.SelectedIndex = 1;
            p2.Controls.Add(_neighborhood);
            p2.Controls.Add(L(Lang.T("窗口越大，新增建设用地越容易连片；窗口越小越贴近转移概率的原始分布。"),
                226, 46, 480, 20, true));

            _recompute = new CheckBox
            {
                Text = Lang.T("每个预测步都重算邻域（步数 > 1 时建议开启）"),
                Left = 8, Top = 76, Width = 460, Height = 22, Checked = true, ForeColor = Theme.Text
            };
            p2.Controls.Add(_recompute);

            p2.Controls.Add(L(Lang.T("适宜性权重"), 8, 110, 72));
            _suitWeight = N(0, 1, 0.6, 2, 0.05, 84, 108, 80);
            p2.Controls.Add(_suitWeight);
            p2.Controls.Add(L(Lang.T("邻域权重"), 184, 110, 64));
            _neighWeight = N(0, 1, 0.4, 2, 0.05, 252, 108, 80);
            p2.Controls.Add(_neighWeight);
            _suitWeight.ValueChanged += (s, e) => SyncWeights();
            _neighWeight.ValueChanged += (s, e) => SyncWeights();
            _weightHint = L("", 348, 110, 360, 20, true);
            p2.Controls.Add(_weightHint);

            _modulate = new CheckBox
            {
                Text = Lang.T("用转移概率调制共享适宜性图（因子加权 / 外部图集模式下区分目标类）"),
                Left = 8, Top = 142, Width = 620, Height = 22, Checked = true, ForeColor = Theme.Text
            };
            p2.Controls.Add(_modulate);

            p2.Controls.Add(L(Lang.T("限制条件图"), 8, 178, 72));
            _constraint = Txt(84, 176, 520);
            p2.Controls.Add(_constraint);
            p2.Controls.Add(B("…", 612, 175, 40, 24, (s, e) => BrowseInto(_constraint, Lang.T("选择限制性条件图"))));
            p2.Controls.Add(L(Lang.T("值 > 0.5 的像元允许转变，其余像元锁定为原类别（可留空）。"),
                8, 206, 640, 20, true));

            p2.Controls.Add(L(Lang.T("评分 = 适宜性权重 × 适宜性 + 邻域权重 × 邻域占比，两项都会先归一化；\r\n")
                + Lang.T("邻域占比是目标类在窗口内的像元比例，边界外按 0 计算。"), 8, 240, 700, 40, true));
            SeparatorInto(p2, 292);

            // ---------------- 页 3：适宜性 ----------------
            var p3 = NewPage(Lang.T("适宜性"));
            p3.Controls.Add(L(Lang.T("适宜性来源"), 8, 12, 72));
            _mode = C(84, 10, 300,
                Lang.T("随机森林转移潜力（推荐）"), Lang.T("因子加权（MCE）"), Lang.T("外部适宜性图集"), Lang.T("不使用适宜性"));
            p3.Controls.Add(_mode);
            _mode.SelectedIndexChanged += (s, e) => SyncSuitabilityPanels();

            _rfPanel = new Panel { Left = 8, Top = 42, Width = PageWidth, Height = 372, BackColor = Theme.Panel };
            BuildRfPanel(_rfPanel);
            _factorPanel = new Panel { Left = 8, Top = 42, Width = PageWidth, Height = 372, BackColor = Theme.Panel };
            BuildFactorPanel(_factorPanel);
            _extPanel = new Panel { Left = 8, Top = 42, Width = PageWidth, Height = 372, BackColor = Theme.Panel };
            BuildExternalPanel(_extPanel);
            _noneHint = L(Lang.T("不使用适宜性时只用邻域聚集，适合快速试算；预测的空间分布会明显比随机森林粗糙。"),
                8, 50, 700, 40, true);

            p3.Controls.Add(_rfPanel);
            p3.Controls.Add(_factorPanel);
            p3.Controls.Add(_extPanel);
            p3.Controls.Add(_noneHint);

            // ---------------- 页 4：评价与输出 ----------------
            var p4 = NewPage(Lang.T("评价与输出"));
            _validate = new CheckBox
            {
                Text = Lang.T("输出精度评价（OA / Kappa / PA / UA / F1 + 混淆矩阵）"),
                Left = 8, Top = 10, Width = 520, Height = 22, Checked = true, ForeColor = Theme.Text
            };
            p4.Controls.Add(_validate);

            _samePeriod = new CheckBox
            {
                Text = Lang.T("改用「同期自检」（in-sample）"),
                Left = 8, Top = 38, Width = 260, Height = 22, ForeColor = Theme.Warn
            };
            p4.Controls.Add(_samePeriod);

            p4.Controls.Add(L(Lang.T("精度评价有两种口径，区别只有在“末期有没有参与建模”："), 8, 64, 700, 18, true));
            p4.Controls.Add(L(Lang.T("· 时间外推（默认，推荐）：把最后一期当成“未来”，只用更早的区间建模去预测它，\r\n")
                + Lang.T("  再和真实的末期逐像元比对 —— 这样得到的 OA/Kappa 才代表“预测能力”。"), 8, 82, 700, 34, true));
            p4.Controls.Add(L(Lang.T("· 同期自检（勾选上面那个框）：末期自己参与了建模，再拿它去预测它自己，\r\n")
                + Lang.T("  相当于用答案考自己，OA/Kappa 必然偏高，只能说明拟合得好，不代表能预测未来。"), 8, 118, 700, 34, true));
            p4.Controls.Add(L(Lang.T("只有两期数据时无法做外推，程序会自动退回同期自检，并在报告里注明。"), 8, 154, 700, 18, true));

            _writeReport = new CheckBox { Text = Lang.T("输出文本报告"), Left = 8, Top = 186, Width = 160, Height = 22, Checked = true, ForeColor = Theme.Text };
            _compare = new CheckBox { Text = Lang.T("输出三联对比图"), Left = 180, Top = 186, Width = 160, Height = 22, Checked = true, ForeColor = Theme.Text };
            _hotspot = new CheckBox { Text = Lang.T("输出变化热点图"), Left = 352, Top = 186, Width = 160, Height = 22, Checked = true, ForeColor = Theme.Text };
            p4.Controls.Add(_writeReport);
            p4.Controls.Add(_compare);
            p4.Controls.Add(_hotspot);

            _showResult = new CheckBox
            {
                Text = Lang.T("完成后打开结果图表窗口"),
                Left = 8, Top = 214, Width = 300, Height = 22, Checked = true, ForeColor = Theme.Text
            };
            p4.Controls.Add(_showResult);

            _estimate = L("", 8, 242, 700, 20, true);
            p4.Controls.Add(_estimate);

            p4.Controls.Add(L(Lang.T("报告、图表与预测栅格放在同一目录（<输出名>_report.txt、<输出名>_figs\\）。"),
                8, 272, 700, 18, true));

            // 四页一次性挂到 TabControl（页内容都是手工坐标，加入顺序不影响布局）
            _tabs.TabPages.AddRange(new[] { p1, pc, p2, p3, p4 });
            foreach (TabPage page in _tabs.TabPages) WrapPageContent(page);

            // 无效值一改，类别表格要重新过滤（数量>0 且非无效值）
            _noData.ValueChanged += (s, e) => { RefreshClassGrid(); UpdateClassStatLabel(); };
            RefreshClassGrid();
            UpdateClassStatLabel();
            // 标签/复选框默认底色是系统灰，压在深色底板上会变成白块，统一改成透明
            MakeTextTransparent(this);

            SyncSuitabilityPanels();
            SyncWeights();
            UpdatePeriodHint();
            UpdateEstimate();
            _steps.ValueChanged += (s, e) => UpdateEstimate();
            _trees.ValueChanged += (s, e) => UpdateEstimate();
            _depth.ValueChanged += (s, e) => UpdateEstimate();
            _mode.SelectedIndexChanged += (s, e) => UpdateEstimate();


        }

        // ---------------------------------------------------------------- 页 1 行为

        private void AddPeriodFromFile()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Lang.T("选择一期土地利用栅格"),
                Filter = DatasetService.BuildDialogFilter(),
                Multiselect = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                foreach (var path in dlg.FileNames)
                {
                    int year = GuessYear(Path.GetFileNameWithoutExtension(path));
                    if (year == 0) year = GuessYearFromFile(path);
                    _periods.Add(new CaPeriod { Path = path, Band = 1, Year = year });
                }
                RefreshPeriodList();
                UpdateEstimate();
                if (_periods.Count > 0) CountClassesIntoGrid();
            }
        }

        private void AddPeriodFromStack()
        {
            if (_stack == null || _stack.Count == 0)
            {
                MessageBox.Show(this, Lang.T("当前没有已打开的图层。"), Lang.T("提示"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var layers = _stack.ToArray();
            var menu = new ContextMenuStrip { Font = Theme.UIFont };
            foreach (var layer in layers)
            {
                var item = new ToolStripMenuItem(layer.Name);
                var captured = layer;
                item.Click += (s, e) =>
                {
                    int year = GuessYear(captured.Name);
                    if (year == 0) year = GuessYearFromFile(captured.SourcePath);
                    _periods.Add(new CaPeriod { Path = captured.SourcePath, Band = 1, Year = year });
                    RefreshPeriodList();
                    CountClassesIntoGrid();
                };
                menu.Items.Add(item);
            }
            menu.Show(System.Windows.Forms.Cursor.Position);
        }

        private void MovePeriod(int delta)
        {
            int i = _periodList.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _periods.Count) return;
            var tmp = _periods[i];
            _periods[i] = _periods[j];
            _periods[j] = tmp;
            RefreshPeriodList();
            _periodList.SelectedIndex = j;
        }

        private void RemovePeriod()
        {
            int i = _periodList.SelectedIndex;
            if (i < 0) return;
            _periods.RemoveAt(i);
            RefreshPeriodList();
        }

        private void RefreshPeriodList()
        {
            int keep = _periodList.SelectedIndex;
            _periodList.BeginUpdate();
            _periodList.Items.Clear();
            for (int i = 0; i < _periods.Count; i++)
            {
                var period = _periods[i];
                string name = string.IsNullOrEmpty(period.Path) ? Lang.T("（未选择）") : Path.GetFileName(period.Path);
                string year = period.Year > 0 ? period.Year.ToString() : Lang.T("年份未识别");
                string band = period.Band > 1 ? Lang.T("  ·  波段 ") + period.Band : "";
                _periodList.Items.Add(string.Format("{0}.  {1}  ·  {2}{3}", i + 1, year, name, band));
            }
            _periodList.EndUpdate();
            if (keep >= 0 && keep < _periodList.Items.Count) _periodList.SelectedIndex = keep;
            else if (_periodList.Items.Count > 0) _periodList.SelectedIndex = _periodList.Items.Count - 1;
            SyncPeriodEditor();
            UpdatePeriodHint();
        }

        /// <summary>把「波段 / 年份」两个编辑框同步成当前选中时期的值。</summary>
        private void SyncPeriodEditor()
        {
            if (_periodBand == null) return;
            int i = _periodList.SelectedIndex;
            bool has = i >= 0 && i < _periods.Count;
            _periodBand.Enabled = has;
            _periodYear.Enabled = has;
            if (!has) return;
            _syncingPeriodEditor = true;
            _periodBand.Value = Math.Max(1, Math.Min(64, _periods[i].Band));
            _periodYear.Value = Math.Max(0, Math.Min(2200, _periods[i].Year));
            _syncingPeriodEditor = false;
        }

        private void UpdatePeriodHint()
        {
            if (_periods.Count < 2)
            {
                _periodHint.Text = Lang.T("还需要 ") + (2 - _periods.Count) + Lang.T(" 期数据。转移矩阵由相邻两期的变化统计得到，")
                    + Lang.T("顺序必须由旧到新。");
                return;
            }
            int intervals = _periods.Count - 1;
            string source = Lang.T("最近一期");
            if (_transition.SelectedIndex == 1) source = intervals >= 2 ? Lang.T("最近两期平均") : Lang.T("最近两期平均（只有 1 个区间，自动退化为最近一期）");
            else if (_transition.SelectedIndex == 2) source = Lang.T("全部 ") + intervals + Lang.T(" 个区间累计");
            int steps = _steps == null ? 1 : (int)_steps.Value;

            string prediction = "";
            int lastYear = _periods[_periods.Count - 1].Year;
            int prevYear = _periods[_periods.Count - 2].Year;
            if (lastYear > 0 && prevYear > 0 && lastYear > prevYear)
            {
                int span = lastYear - prevYear;
                prediction = string.Format(Lang.T("；末期 {0} + {1} 年 × {2} 步 ≈ {3} 年"),
                    lastYear, span, steps, lastYear + span * steps);
            }
            else
            {
                prediction = Lang.T("；年份未识别时只能按「步」理解跨度（可在上方填写年份）");
            }

            _periodHint.Text = string.Format(
                Lang.T("共 {0} 期 · {1} 个区间 · 转移矩阵使用：{2}。\r\n预测从最后一期出发，向后再推 {3} 步{4}。"),
                _periods.Count, intervals, source, steps, prediction);
        }

        // ---------------------------------------------------------------- 页 3 构造

        private void BuildRfPanel(Panel panel)
        {
            panel.Controls.Add(L(Lang.T("模型文件"), 0, 8, 60));
            _modelPath = Txt(64, 6, 420);
            panel.Controls.Add(_modelPath);
            panel.Controls.Add(B("…", 490, 5, 36, 24, (s, e) => BrowseModel()));
            panel.Controls.Add(L(Lang.T("留空表示每次重新训练"), 534, 8, 180, 18, true));

            panel.Controls.Add(L(Lang.T("训练驱动因子（顺序即特征顺序，建议 5~8 个）"), 0, 40, 420));
            _driverList = new ListBox
            {
                Left = 0, Top = 58, Width = 420, Height = 108,
                BackColor = Theme.PanelAlt, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            _driverList.SelectedIndexChanged += (s, e) => { };
            panel.Controls.Add(_driverList);
            panel.Controls.Add(B(Lang.T("添加因子…"), 430, 58, 110, 26, (s, e) => AddDriver()));
            panel.Controls.Add(B(Lang.T("移除"), 430, 86, 110, 26, (s, e) => RemoveDriver()));
            panel.Controls.Add(B(Lang.T("上移"), 430, 114, 110, 26, (s, e) => MoveDriver(-1)));
            panel.Controls.Add(B(Lang.T("下移"), 430, 142, 110, 26, (s, e) => MoveDriver(1)));

            panel.Controls.Add(L(Lang.T("树数"), 0, 180, 40));
            _trees = N(10, 1000, 200, 0, 10, 42, 178, 76);
            panel.Controls.Add(_trees);
            panel.Controls.Add(L(Lang.T("深度"), 130, 180, 40));
            _depth = N(0, 100, 20, 0, 1, 172, 178, 64);
            panel.Controls.Add(_depth);
            panel.Controls.Add(L(Lang.T("采样比"), 248, 180, 48));
            _imbalance = N(0.1, 20, 2.0, 1, 0.1, 298, 178, 64);
            panel.Controls.Add(_imbalance);
            panel.Controls.Add(L(Lang.T("种子"), 374, 180, 40));
            _seed = N(0, 999999, 42, 0, 1, 416, 178, 70);
            panel.Controls.Add(_seed);

            panel.Controls.Add(L(Lang.T("保存模型"), 0, 210, 60));
            _saveModelPath = Txt(64, 208, 420);
            panel.Controls.Add(_saveModelPath);
            panel.Controls.Add(B("…", 490, 207, 36, 24, (s, e) => BrowseInto(_saveModelPath, Lang.T("选择模型保存位置"), true)));
            _forceTrain = new CheckBox { Text = Lang.T("强制重训"), Left = 534, Top = 209, Width = 110, Height = 22, ForeColor = Theme.Text };
            panel.Controls.Add(_forceTrain);

            panel.Controls.Add(B(Lang.T("训练并保存模型"), 0, 238, 160, 28, (s, e) => OnTrain()));
            _trainResult = L("", 0, 272, 700, 62, true);
            panel.Controls.Add(_trainResult);
            panel.Controls.Add(L(Lang.T("训练只使用指定时期的样本；模型训练好后可直接复用，避免每次预测都重跑。"),
                0, 340, 700, 18, true));
        }

        private void BuildFactorPanel(Panel panel)
        {
            panel.Controls.Add(L(Lang.T("因子列表（每个因子的权重与方向可调）"), 0, 8, 420));
            _factorList = new ListBox
            {
                Left = 0, Top = 30, Width = 420, Height = 140,
                BackColor = Theme.PanelAlt, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            _factorList.SelectedIndexChanged += (s, e) => SyncFactorEditor();
            panel.Controls.Add(_factorList);
            panel.Controls.Add(B(Lang.T("添加因子…"), 430, 30, 110, 26, (s, e) => AddFactor()));
            panel.Controls.Add(B(Lang.T("移除"), 430, 60, 110, 26, (s, e) => RemoveFactor()));
            panel.Controls.Add(B(Lang.T("上移"), 430, 90, 110, 26, (s, e) => MoveFactor(-1)));
            panel.Controls.Add(B(Lang.T("下移"), 430, 120, 110, 26, (s, e) => MoveFactor(1)));

            panel.Controls.Add(L(Lang.T("选中因子权重"), 0, 186, 90));
            _factorWeight = N(0.01, 100, 1.0, 2, 0.1, 92, 184, 80);
            panel.Controls.Add(_factorWeight);
            _factorWeight.ValueChanged += (s, e) => ApplyFactorEditor();
            _factorNegative = new CheckBox { Text = Lang.T("负向因子（值越大越不适宜）"), Left = 190, Top = 185, Width = 220, Height = 22, ForeColor = Theme.Text };
            _factorNegative.CheckedChanged += (s, e) => ApplyFactorEditor();
            panel.Controls.Add(_factorNegative);

            panel.Controls.Add(L(Lang.T("标准化"), 0, 218, 50));
            _normalize = C(52, 216, 180, Lang.T("分位数拉伸"), Lang.T("最小-最大"));
            panel.Controls.Add(_normalize);
            panel.Controls.Add(L(Lang.T("下限 %"), 248, 218, 50));
            _pLow = N(0, 49, 2, 1, 0.5, 298, 216, 64);
            panel.Controls.Add(_pLow);
            panel.Controls.Add(L(Lang.T("上限 %"), 374, 218, 50));
            _pHigh = N(51, 100, 98, 1, 0.5, 424, 216, 64);
            panel.Controls.Add(_pHigh);

            panel.Controls.Add(L(Lang.T("适宜性 = Σ(权重 × 标准化后的因子值) / Σ权重；NoData 像元用该因子的中位数填充。"),
                0, 254, 700, 18, true));
            panel.Controls.Add(L(Lang.T("因子栅格必须与土地利用栅格同网格（宽高与地理变换一致）。"),
                0, 278, 700, 18, true));
        }

        private void BuildExternalPanel(Panel panel)
        {
            panel.Controls.Add(L(Lang.T("适宜性图集"), 0, 8, 66));
            _externalPath = Txt(70, 6, 420);
            panel.Controls.Add(_externalPath);
            panel.Controls.Add(B("…", 496, 5, 36, 24, (s, e) => BrowseInto(_externalPath, Lang.T("选择适宜性图集（多波段）"))));

            panel.Controls.Add(L(Lang.T("起始波段"), 0, 44, 60));
            _extBand = N(1, 64, 1, 0, 1, 64, 42, 64);
            panel.Controls.Add(_extBand);
            panel.Controls.Add(L(Lang.T("波段对应"), 148, 44, 60));
            _extBandMode = C(212, 42, 220, Lang.T("按类别值升序对应"), Lang.T("波段号 = 类别值"));
            panel.Controls.Add(_extBandMode);

            panel.Controls.Add(L(Lang.T("每个波段是一张“某目标类的适宜度”图（0~1 或任意量纲，程序会按有效范围归一化）。\r\n")
                + Lang.T("例如 9 类土地利用 + 8 个有效类别 → 需要 8 个波段（无数据类可跳过）。"),
                0, 80, 700, 44, true));
        }

        private void SyncSuitabilityPanels()
        {
            int index = _mode.SelectedIndex;
            _rfPanel.Visible = index == 0;
            _factorPanel.Visible = index == 1;
            _extPanel.Visible = index == 2;
            _noneHint.Visible = index == 3;
        }

        private void SyncWeights()
        {
            if (_suppressWeightEvent) return;
            double sum = (double)_suitWeight.Value + (double)_neighWeight.Value;
            if (sum <= 1e-9)
            {
                _weightHint.Text = Lang.T("两个权重不能同时为 0");
                return;
            }
            _weightHint.Text = string.Format(Lang.T("归一化后：适宜性 {0:0.000} · 邻域 {1:0.000}"),
                (double)_suitWeight.Value / sum, (double)_neighWeight.Value / sum);
        }

        private void SyncFactorEditor()
        {
            int i = _factorList.SelectedIndex;
            bool has = i >= 0 && i < _factors.Count;
            _factorWeight.Enabled = has;
            _factorNegative.Enabled = has;
            if (!has) return;
            _suppressWeightEvent = true;
            _factorWeight.Value = (decimal)Math.Max(0.01, Math.Min(100, _factors[i].Weight));
            _factorNegative.Checked = _factors[i].Negative;
            _suppressWeightEvent = false;
        }

        private void ApplyFactorEditor()
        {
            int i = _factorList.SelectedIndex;
            if (i < 0 || i >= _factors.Count) return;
            _factors[i].Weight = (double)_factorWeight.Value;
            _factors[i].Negative = _factorNegative.Checked;
            RefreshFactorList();
        }

        // ---------------------------------------------------------------- 因子 / 驱动因子

        private void AddFactor()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Lang.T("选择驱动因子栅格"),
                Filter = DatasetService.BuildDialogFilter(),
                Multiselect = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                foreach (var path in dlg.FileNames)
                    _factors.Add(new CaFactor { Path = path, Weight = 1.0, Negative = false });
                RefreshFactorList();
            }
        }

        private void RemoveFactor()
        {
            int i = _factorList.SelectedIndex;
            if (i < 0 || i >= _factors.Count) return;
            _factors.RemoveAt(i);
            RefreshFactorList();
        }

        private void MoveFactor(int delta)
        {
            int i = _factorList.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _factors.Count) return;
            var tmp = _factors[i];
            _factors[i] = _factors[j];
            _factors[j] = tmp;
            RefreshFactorList();
            _factorList.SelectedIndex = j;
        }

        private void RefreshFactorList()
        {
            int keep = _factorList.SelectedIndex;
            _factorList.BeginUpdate();
            _factorList.Items.Clear();
            foreach (var f in _factors) _factorList.Items.Add(f.ToString());
            _factorList.EndUpdate();
            if (keep >= 0 && keep < _factorList.Items.Count) _factorList.SelectedIndex = keep;
            SyncFactorEditor();
        }

        private void AddDriver()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Lang.T("选择驱动因子栅格（随机森林特征）"),
                Filter = DatasetService.BuildDialogFilter(),
                Multiselect = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                foreach (var path in dlg.FileNames)
                    _drivers.Add(new CaDriver
                    {
                        Name = Path.GetFileNameWithoutExtension(path),
                        Path = path,
                        Band = 1
                    });
                RefreshDriverList();
            }
        }

        private void RemoveDriver()
        {
            int i = _driverList.SelectedIndex;
            if (i < 0 || i >= _drivers.Count) return;
            _drivers.RemoveAt(i);
            RefreshDriverList();
        }

        private void MoveDriver(int delta)
        {
            int i = _driverList.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _drivers.Count) return;
            var tmp = _drivers[i];
            _drivers[i] = _drivers[j];
            _drivers[j] = tmp;
            RefreshDriverList();
            _driverList.SelectedIndex = j;
        }

        private void RefreshDriverList()
        {
            int keep = _driverList.SelectedIndex;
            _driverList.BeginUpdate();
            _driverList.Items.Clear();
            for (int i = 0; i < _drivers.Count; i++)
                _driverList.Items.Add(string.Format("{0}. {1}", i + 1, _drivers[i]));
            _driverList.EndUpdate();
            if (keep >= 0 && keep < _driverList.Items.Count) _driverList.SelectedIndex = keep;
        }

        // ---------------------------------------------------------------- 训练 / 分析

        private void OnTrain()
        {
            string error = ValidateCore(true);
            if (error != null) { Warn(error); return; }

            var p = CaptureCore();
            p.Mode = CaPredictionMode.Train;
            if (string.IsNullOrWhiteSpace(p.SaveModelPath))
            {
                p.SaveModelPath = DefaultModelPath();
                _saveModelPath.Text = p.SaveModelPath;
            }
            string modelPath = p.SaveModelPath;
            CaMarkovResult result = null;

            var job = JobRunner.Run(this, Lang.T("训练转移潜力模型"), reporter =>
            {
                result = CaMarkovEngine.Run(new CaMarkovRequest
                {
                    Params = p,
                    ModelToSave = modelPath,
                    FigureDirectory = Path.Combine(Path.GetTempPath(), "SharpGIS", "camarkov_preview"),
                    TimeoutMinutes = 60
                }, reporter);
                return modelPath;
            });

            if (job.IsCancelled) return;
            if (!job.Ok) { Warn(job.Error); return; }

            _modelPath.Text = modelPath;
            _trainResult.Text = Lang.T("模型已保存：") + modelPath + "\r\n" + BuildImportanceText(result);
            UpdateEstimate();
        }

        private void OnAnalyze()
        {
            string error = ValidateCore(false, true);
            if (error != null) { Warn(error); return; }

            var p = CaptureCore();
            CaMarkovResult result = null;
            var job = JobRunner.Run(this, Lang.T("转移矩阵分析"), reporter =>
            {
                result = CaMarkovEngine.Analyze(p,
                    Path.Combine(Path.GetTempPath(), "SharpGIS", "camarkov_preview"), reporter);
                return "ok";
            });
            if (job.IsCancelled) return;
            if (!job.Ok) { Warn(job.Error); return; }

            using (var viewer = new CaMarkovResultDialog(result, Lang.T("转移分析（不写文件）")))
                viewer.ShowDialog(this);
        }

        private static string BuildImportanceText(CaMarkovResult r)
        {
            if (r == null || r.FeatureImportance.Count == 0) return "";
            var parts = new List<string>();
            foreach (var item in r.FeatureImportance)
                parts.Add(string.Format("{0} {1:0.000}", item.Name, item.Importance));
            return Lang.T("特征重要性：") + string.Join(" · ", parts);
        }

        private string DefaultModelPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SharpGIS", "camarkov", "models");
            string name = "camarkov_rf_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".npz";
            return Path.Combine(dir, name);
        }

        private void BrowseModel()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Lang.T("选择随机森林模型（.npz）"),
                Filter = Lang.T("SharpGIS CA-Markov 模型|*.npz|所有文件|*.*")
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) _modelPath.Text = dlg.FileName;
            }
        }

        // ---------------------------------------------------------------- 校验与采集

        /// <summary>核心校验（返回 null 表示通过）。</summary>
        /// <param name="forTraining">true 时按训练模式校验（必须有驱动因子）。</param>
        /// <param name="forAnalyze">true 时只校验时期与网格（「先分析」不需要适宜性设置）。</param>
        private string ValidateCore(bool forTraining, bool forAnalyze = false)
        {
            if (_periods.Count < 2)
                return Lang.T("至少需要两期土地利用数据（当前 ") + _periods.Count + Lang.T(" 期）。");

            foreach (var period in _periods)
            {
                if (!File.Exists(period.Path))
                    return Lang.T("文件不存在：") + period.Path;
            }

            string gridError = CheckGrids();
            if (gridError != null) return gridError;

            if (!forAnalyze)
            {
                if (_mode.SelectedIndex == 0 || forTraining)
                {
                    if (_drivers.Count == 0)
                        return Lang.T("随机森林至少需要一个驱动因子（如高程、坡度、距道路距离、人口密度）。");
                    foreach (var d in _drivers)
                        if (!File.Exists(d.Path)) return Lang.T("驱动因子文件不存在：") + d.Path;
                }
                if (_mode.SelectedIndex == 1 && _factors.Count == 0)
                    return Lang.T("因子加权模式至少需要一个驱动因子。");
                if (_mode.SelectedIndex == 2 && !File.Exists(_externalPath.Text ?? ""))
                    return Lang.T("请指定外部适宜性图集文件（多波段栅格）。");
                if (!string.IsNullOrWhiteSpace(_constraint.Text) && !File.Exists(_constraint.Text))
                    return Lang.T("限制性条件图不存在：") + _constraint.Text;

                double wSum = (double)_suitWeight.Value + (double)_neighWeight.Value;
                if (wSum <= 1e-9) return Lang.T("适宜性权重与邻域权重不能同时为 0。");
            }
            return null;
        }

        /// <summary>检查所有输入栅格是否同网格（宽高 + 6 个仿射系数）。</summary>
        private string CheckGrids()
        {
            RasterLayer first = null;
            try
            {
                first = DatasetService.Open(_periods[0].Path);
            }
            catch (Exception ex)
            {
                return Lang.T("无法打开第 1 期：\r\n") + ex.Message;
            }

            try
            {
                for (int i = 1; i < _periods.Count; i++)
                {
                    using (var layer = DatasetService.Open(_periods[i].Path))
                    {
                        string reason;
                        if (!RasterGrid.Same(first, layer, out reason))
                            return string.Format(Lang.T("第 {0} 期与第 1 期网格不一致：{1}\r\n")
                                + Lang.T("请先用「重投影 / 重采样」对齐到同一网格。"), i + 1, reason);
                    }
                }

                var extras = new List<KeyValuePair<string, string>>();
                foreach (var f in _factors) extras.Add(new KeyValuePair<string, string>(Lang.T("因子"), f.Path));
                foreach (var d in _drivers) extras.Add(new KeyValuePair<string, string>(Lang.T("驱动因子"), d.Path));
                if (!string.IsNullOrWhiteSpace(_constraint.Text))
                    extras.Add(new KeyValuePair<string, string>(Lang.T("限制条件图"), _constraint.Text));
                if (!string.IsNullOrWhiteSpace(_externalPath.Text))
                    extras.Add(new KeyValuePair<string, string>(Lang.T("适宜性图集"), _externalPath.Text));

                foreach (var extra in extras)
                {
                    if (!File.Exists(extra.Value)) continue;
                    using (var layer = DatasetService.Open(extra.Value))
                    {
                        string reason;
                        if (!RasterGrid.Same(first, layer, out reason))
                            return string.Format(Lang.T("{0} {1} 与土地利用栅格网格不一致：{2}"),
                                extra.Key, Path.GetFileName(extra.Value), reason);
                    }
                }
            }
            catch (Exception ex)
            {
                return Lang.T("校验网格时出错：\r\n") + ex.Message;
            }
            finally
            {
                if (first != null) first.Dispose();
            }
            return null;
        }

        /// <summary>在 UI 线程把控件值快照为参数对象。</summary>
        private CaMarkovParams CaptureCore()
        {
            var p = new CaMarkovParams
            {
                Mode = CaPredictionMode.Predict,
                NoData = (double)_noData.Value,
                ClassCount = (int)_classCount.Value,
                ClassNames = ParseNames(),
                TransitionSource = (CaTransitionSource)Math.Max(0, _transition.SelectedIndex),
                Steps = (int)_steps.Value,
                NeighborhoodSize = 3 + Math.Max(0, _neighborhood.SelectedIndex) * 2,
                RecomputeNeighborhood = _recompute.Checked,
                Suitability = (CaSuitabilityMode)Math.Max(0, _mode.SelectedIndex),
                NormalizeMethod = _normalize.SelectedIndex == 1 ? CaNormalizeMethod.MinMax : CaNormalizeMethod.Quantile,
                PercentileLow = (double)_pLow.Value,
                PercentileHigh = (double)_pHigh.Value,
                ConstraintPath = string.IsNullOrWhiteSpace(_constraint.Text) ? null : _constraint.Text.Trim(),
                ConstraintBand = 1,
                ExternalPath = string.IsNullOrWhiteSpace(_externalPath.Text) ? null : _externalPath.Text.Trim(),
                ExternalBand = (int)_extBand.Value,
                ExternalBandMode = _extBandMode.SelectedIndex == 1 ? CaBandMode.ClassValue : CaBandMode.Order,
                ModelPath = string.IsNullOrWhiteSpace(_modelPath.Text) ? null : _modelPath.Text.Trim(),
                ForceTrain = _forceTrain.Checked,
                SaveModelPath = string.IsNullOrWhiteSpace(_saveModelPath.Text) ? null : _saveModelPath.Text.Trim(),
                Trees = (int)_trees.Value,
                MaxDepth = (int)_depth.Value,
                Imbalance = (double)_imbalance.Value,
                Seed = (int)_seed.Value,
                SuitabilityWeight = (double)_suitWeight.Value,
                NeighborhoodWeight = (double)_neighWeight.Value,
                ModulateByProbability = _modulate.Checked,
                Validate = _validate.Checked,
                ValidationSamePeriod = _samePeriod.Checked,
                WriteReport = _writeReport.Checked,
                WriteCompareImage = _compare.Checked,
                WriteHotspotImage = _hotspot.Checked,
            };
            foreach (var period in _periods)
                p.Periods.Add(new CaPeriod { Path = period.Path, Band = period.Band, Year = period.Year });
            foreach (var f in _factors)
                p.Factors.Add(new CaFactor { Path = f.Path, Band = f.Band, Weight = f.Weight, Negative = f.Negative });
            foreach (var d in _drivers)
                p.TrainingDrivers.Add(new CaDriver { Name = d.Name, Path = d.Path, Band = d.Band });
            return p;
        }

        /// <summary>从类别表格收集名称，返回按类别值排列的数组（空位补默认名）。</summary>
        private string[] ParseNames()
        {
            HarvestClassNames();
            return (string[])_classNames.Clone();
        }

        // ---------------------------------------------------------------- 类别表格

        /// <summary>按最后一期逐像元统计各类别数量，填入表格。</summary>
        private void CountClassesIntoGrid()
        {
            if (_periods.Count == 0)
            {
                Warn(Lang.T("请先添加至少一期数据。"));
                return;
            }
            var last = _periods[_periods.Count - 1];
            if (!File.Exists(last.Path))
            {
                Warn(Lang.T("文件不存在：") + last.Path);
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                var counts = CountClasses(last.Path, last.Band);
                _classCounts.Clear();
                foreach (var pair in counts) _classCounts[pair.Key] = pair.Value;

                _classStatSource = Path.GetFileName(last.Path);
                if (_classNames.Length == 0) InitNamesFromCounts();
                RefreshClassGrid();
                UpdateClassStatLabel();
            }
            catch (Exception ex)
            {
                Warn(Lang.T("统计类别失败：\r\n") + ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>读取整幅栅格统计每个整数值的像元数（按块读，byte 走快路径）。</summary>
        private static Dictionary<int, long> CountClasses(string path, int band)
        {
            var counts = new Dictionary<int, long>();
            using (var layer = DatasetService.Open(path))
            {
                if (band < 1 || band > layer.BandCount) band = 1;
                int w = layer.Width, h = layer.Height;
                var gdalBand = layer.Ds.GetRasterBand(band);
                try
                {
                    bool isByte = layer.BandTypes[band - 1] == OSGeo.GDAL.DataType.GDT_Byte;
                    const int blockRows = 512;
                    if (isByte)
                    {
                        var buffer = new byte[w * blockRows];
                        for (int y = 0; y < h; y += blockRows)
                        {
                            int rows = Math.Min(blockRows, h - y);
                            gdalBand.ReadRaster(0, y, w, rows, buffer, w, rows, 0, 0);
                            int total = w * rows;
                            for (int i = 0; i < total; i++)
                            {
                                int v = buffer[i];
                                long c;
                                counts.TryGetValue(v, out c);
                                counts[v] = c + 1;
                            }
                        }
                    }
                    else
                    {
                        var buffer = new float[w * blockRows];
                        for (int y = 0; y < h; y += blockRows)
                        {
                            int rows = Math.Min(blockRows, h - y);
                            gdalBand.ReadRaster(0, y, w, rows, buffer, w, rows, 0, 0);
                            int total = w * rows;
                            for (int i = 0; i < total; i++)
                            {
                                double value = buffer[i];
                                if (double.IsNaN(value)) continue;
                                int v = (int)Math.Round(value);
                                if (v < 0 || v > 255) continue;
                                long c;
                                counts.TryGetValue(v, out c);
                                counts[v] = c + 1;
                            }
                        }
                    }
                }
                finally { gdalBand.Dispose(); }
            }
            return counts;
        }

        /// <summary>首次拿到统计结果时给出一套默认名称：像 CLCD 的 9 类沿用其名称，否则用「类别 N」。</summary>
        private void InitNamesFromCounts()
        {
            int max = 0;
            foreach (var pair in _classCounts) if (pair.Key > max) max = pair.Key;
            if (max <= 0) return;

            bool looksLikeClcd = max == 8 && _classCounts.Count >= 6;
            _classNames = new string[max + 1];
            var clcd = DefaultClassNames();
            for (int v = 0; v <= max; v++)
                _classNames[v] = looksLikeClcd && v < clcd.Length ? clcd[v] : Lang.T("类别 ") + v;
        }

        /// <summary>重建类别表格：只列「数量 > 0 且不是无效值」的类别。</summary>
        private void RefreshClassGrid()
        {
            if (_classGrid == null) return;
            HarvestClassNames();

            int noData = (int)_noData.Value;
            var values = new List<int>();
            if (_classCounts.Count > 0)
            {
                foreach (var pair in _classCounts)
                    if (pair.Value > 0 && pair.Key != noData) values.Add(pair.Key);
            }
            else
            {
                for (int v = 0; v < _classNames.Length; v++)
                    if (v != noData && !string.IsNullOrEmpty(_classNames[v])) values.Add(v);
            }
            values.Sort();

            if (values.Count > 0) EnsureNameCapacity(values[values.Count - 1] + 1);

            _classGrid.SuspendLayout();
            _classGrid.Rows.Clear();
            if (_classGrid.Columns.Count == 0) BuildClassColumns();
            foreach (int v in values)
            {
                long count;
                _classCounts.TryGetValue(v, out count);
                int index = _classGrid.Rows.Add(
                    v,
                    _classCounts.Count > 0 ? count.ToString("#,0") : "—",
                    v < _classNames.Length && !string.IsNullOrEmpty(_classNames[v])
                        ? _classNames[v] : Lang.T("类别 ") + v);
                _classGrid.Rows[index].Tag = v;
            }
            _classGrid.ResumeLayout();
        }

        private void BuildClassColumns()
        {
            var value = new DataGridViewTextBoxColumn
            {
                HeaderText = Lang.T("类别值"),
                ReadOnly = true,
                FillWeight = 18
            };
            var count = new DataGridViewTextBoxColumn
            {
                HeaderText = Lang.T("像元数（最后一期）"),
                ReadOnly = true,
                FillWeight = 34
            };
            count.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            var name = new DataGridViewTextBoxColumn
            {
                HeaderText = Lang.T("类别名称（可双击修改）"),
                ReadOnly = false,
                FillWeight = 48
            };
            _classGrid.Columns.AddRange(new DataGridViewColumn[] { value, count, name });
        }

        /// <summary>把表格里编辑过的名称收进 _classNames。</summary>
        private void HarvestClassNames()
        {
            if (_classGrid == null) return;
            foreach (DataGridViewRow row in _classGrid.Rows)
            {
                if (row.IsNewRow) continue;
                int value;
                if (!int.TryParse(Convert.ToString(row.Cells[0].Value), out value)) continue;
                if (value < 0 || value > 255) continue;
                EnsureNameCapacity(value + 1);
                string name = Convert.ToString(row.Cells[2].Value);
                _classNames[value] = (name ?? string.Empty).Trim();
            }
        }

        private void EnsureNameCapacity(int size)
        {
            if (_classNames.Length >= size) return;
            var old = _classNames;
            _classNames = new string[size];
            for (int i = 0; i < size; i++)
                _classNames[i] = i < old.Length && !string.IsNullOrEmpty(old[i]) ? old[i] : Lang.T("类别 ") + i;
        }

        private void ApplyClcdNames()
        {
            HarvestClassNames();
            var clcd = DefaultClassNames();
            for (int v = 0; v < _classNames.Length; v++)
                if (!string.IsNullOrEmpty(_classNames[v]) || _classCounts.ContainsKey(v))
                    _classNames[v] = v < clcd.Length ? clcd[v] : Lang.T("类别 ") + v;
            RefreshClassGrid();
        }

        private void ResetClassNames()
        {
            HarvestClassNames();
            for (int v = 0; v < _classNames.Length; v++) _classNames[v] = Lang.T("类别 ") + v;
            RefreshClassGrid();
        }

        private void UpdateClassStatLabel()
        {
            if (_classStatLabel == null) return;
            if (_classCounts.Count == 0)
            {
                _classStatLabel.Text = Lang.T("尚未统计");
                return;
            }
            long total = 0, valid = 0;
            int noData = (int)_noData.Value;
            foreach (var pair in _classCounts)
            {
                total += pair.Value;
                if (pair.Key != noData) valid += pair.Value;
            }
            _classStatLabel.Text = string.Format(Lang.T("统计自 {0} · 有效像元 {1:#,0} / 总计 {2:#,0}"),
                _classStatSource, valid, total);
        }

        /// <summary>基类在后台线程前调用：把控件值整体快照。</summary>
        protected override void CaptureParameters()
        {
            _snap = CaptureCore();
            ShowResultAfter = _showResult.Checked;
            LastResult = null;
            // 输出路径由基类在成功后才赋值，这里不处理；RunCore 会拿到 outputPath 并推导报告/图目录
        }

        /// <summary>基类在 UI 线程校验：返回 false 时弹「参数有误」。</summary>
        protected override bool ValidateInputs(out string error)
        {
            error = ValidateCore(false);
            return error == null;
        }

        /// <summary>后台线程执行：只使用快照，绝不碰控件。</summary>
        protected override string RunCore(ProgressReporter reporter, string outputPath)
        {
            var p = _snap;
            string dir = Path.GetDirectoryName(outputPath);
            string baseName = Path.GetFileNameWithoutExtension(outputPath);
            string figDir = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, baseName + "_figs");

            LastResult = CaMarkovEngine.Run(new CaMarkovRequest
            {
                Params = p,
                RasterPath = outputPath,
                ReportPath = p.WriteReport && !string.IsNullOrEmpty(dir)
                    ? Path.Combine(dir, baseName + "_report.txt") : null,
                FigureDirectory = figDir,
                ModelToSave = p.SaveModelPath,
                TimeoutMinutes = 60
            }, reporter);
            return outputPath;
        }

        // ---------------------------------------------------------------- 杂项

        private void UpdateEstimate()
        {
            if (_estimate == null || _steps == null) return;
            int steps = (int)_steps.Value;
            int trees = _trees == null ? 200 : (int)_trees.Value;
            bool rf = _mode != null && _mode.SelectedIndex == 0;
            int intervals = Math.Max(1, _periods.Count - 1);

            string text;
            if (rf)
            {
                // 经验值：3.7M 有效像元下，每棵树每遍约 3.2 秒；另有约 35 秒固定开销（读盘/训练/出图）
                int passes = ValidationEnabled() ? 2 : 1;
                double seconds = 35 + trees * 3.2 * passes + steps * 3.0;
                text = string.Format(Lang.T("预计耗时：约 {0}（{1} 棵树、{2} 步{3}）。树数越大越接近参考配置，也越慢。"),
                    Human(seconds), trees, steps,
                    ValidationEnabled() ? Lang.T("，含一次外推精度评价（相当于再预测一遍）") : "");
            }
            else
            {
                text = string.Format(Lang.T("预计耗时：约 {0}（{1} 步，不含随机森林训练）。"),
                    Human(10 + 8 * steps), steps);
            }
            _estimate.Text = text;
        }

        private static string Human(double seconds)
        {
            if (seconds < 90) return string.Format(Lang.T("{0:0} 秒"), seconds);
            return string.Format(Lang.T("{0:0.#} 分钟"), seconds / 60.0);
        }

        private bool ValidationEnabled()
        {
            return _validate == null || _validate.Checked;
        }

        /// <summary>把标签与复选框的底色改成透明，避免深色底板上出现灰白块。</summary>
        private static void MakeTextTransparent(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label || child is CheckBox) child.BackColor = Color.Transparent;
                MakeTextTransparent(child);
            }
        }

        private TabPage NewPage(string title)
        {
            return new TabPage(title) { ForeColor = Theme.Text };
        }

        /// <summary>
        /// TabPage 的底色由系统主题绘制、改不动；把页内控件整体挪进一块深色面板里，
        /// 这样标签的“透明”才会按深色计算（否则会显示成灰白块）。
        /// </summary>
        private static void WrapPageContent(TabPage page)
        {
            var children = new Control[page.Controls.Count];
            page.Controls.CopyTo(children, 0);
            page.Controls.Clear();
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
            host.Controls.AddRange(children);
            page.Controls.Add(host);
        }

        private void SeparatorInto(Control parent, int y)
        {
            parent.Controls.Add(new Label
            {
                Left = 8, Top = y, Width = PageWidth - 16, Height = 1, BackColor = Theme.Border
            });
        }

        private static Label L(string text, int x, int y, int w, int h = 18, bool dim = false)
        {
            return new Label
            {
                Text = text,
                Left = x,
                Top = y,
                Width = w,
                Height = h,
                ForeColor = dim ? Theme.TextDim : Theme.Text
            };
        }

        private static Button B(string text, int x, int y, int w, int h, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                Left = x,
                Top = y,
                Width = w,
                Height = h,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat
            };
            button.FlatAppearance.BorderColor = Theme.Border;
            if (onClick != null) button.Click += onClick;
            return button;
        }

        private static TextBox Txt(int x, int y, int w, string value = "")
        {
            return new TextBox
            {
                Left = x,
                Top = y,
                Width = w,
                Text = value ?? "",
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private static NumericUpDown N(double min, double max, double value, int decimals,
            double step, int x, int y, int w)
        {
            return new NumericUpDown
            {
                Left = x,
                Top = y,
                Width = w,
                Minimum = (decimal)min,
                Maximum = (decimal)max,
                DecimalPlaces = decimals,
                Increment = (decimal)step,
                Value = (decimal)Math.Max(min, Math.Min(max, value)),
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private static ComboBox C(int x, int y, int w, params string[] items)
        {
            var combo = new ComboBox
            {
                Left = x,
                Top = y,
                Width = w,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat
            };
            combo.Items.AddRange(items);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            return combo;
        }

        private void BrowseInto(TextBox box, string title, bool save = false)
        {
            if (save)
            {
                using (var dlg = new SaveFileDialog
                {
                    Title = title,
                    Filter = Lang.T("SharpGIS CA-Markov 模型|*.npz|所有文件|*.*"),
                    FileName = Path.GetFileName(box.Text ?? "")
                })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.FileName;
                }
            }
            else
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = title,
                    Filter = DatasetService.BuildDialogFilter()
                })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.FileName;
                }
            }
        }

        private void Warn(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            MessageBox.Show(this, message, Lang.T("提示"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string[] DefaultClassNames()
        {
            return new[] { Lang.T("无效值"), Lang.T("耕地"), Lang.T("森林"), Lang.T("草地"), Lang.T("灌木"), Lang.T("水体"), Lang.T("冰雪"), Lang.T("裸地"), Lang.T("不透水面") };
        }

        /// <summary>
        /// 从文本里识别年份：取每一段连续数字的前 4 位，落在 1970~2100 视为年份。
        /// 支持 "CLCD_2019"、"LULC_2010-05"、"data20240501"（取 2024）等常见写法。
        /// </summary>
        private static int GuessYear(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int i = 0;
            while (i < text.Length)
            {
                if (!char.IsDigit(text[i])) { i++; continue; }
                int start = i;
                while (i < text.Length && char.IsDigit(text[i])) i++;
                if (i - start >= 4)
                {
                    int year;
                    if (int.TryParse(text.Substring(start, 4), out year) && year >= 1970 && year <= 2100)
                        return year;
                }
            }
            return 0;
        }

        /// <summary>文件名里没有年份时，再试一次影像自带的采集时间元数据。</summary>
        private static int GuessYearFromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
            try
            {
                using (var layer = DatasetService.Open(path))
                {
                    string[] keys = { "TIFFTAG_DATETIME", "ACQUISITION_DATE", "DATE_ACQUIRED", "TIMESTAMP" };
                    foreach (var key in keys)
                    {
                        string text = null;
                        try { text = layer.Ds.GetMetadataItem(key, ""); } catch { }
                        int year = GuessYear(text);
                        if (year > 0) return year;
                    }
                }
            }
            catch { }
            return 0;
        }
    }
}
