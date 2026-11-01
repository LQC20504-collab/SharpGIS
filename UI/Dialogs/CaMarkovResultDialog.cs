using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SharpGIS.Processing;
using SharpGIS.UI.Controls;

using SharpGIS.Services;
namespace SharpGIS.UI.Dialogs
{
    /// <summary>CA-Markov 结果看图窗口：转移矩阵、面积变化、精度、特征重要性、对比图、变化热点。</summary>
    public sealed class CaMarkovResultDialog : Form
    {
        private readonly CaMarkovResult _result;
        private readonly TabControl _tabs;

        /// <summary>供界面自检切换标签页截图。</summary>
        internal TabControl TabsForTest { get { return _tabs; } }

        /// <summary>用一次运行的结果构建看图窗口。</summary>
        /// <param name="result">引擎返回的结果对象。</param>
        /// <param name="title">窗口标题。</param>
        public CaMarkovResultDialog(CaMarkovResult result, string title)
        {
            _result = result ?? new CaMarkovResult();
            Text = string.IsNullOrEmpty(title) ? Lang.T("土地利用变化预测结果") : title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(920, 656);
            MinimumSize = new Size(760, 520);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.UIFont;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;

            var header = new Label
            {
                Left = 12,
                Top = 10,
                Width = ClientSize.Width - 24,
                Height = 56,
                ForeColor = Theme.TextDim,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = BuildHeader()
            };

            _tabs = new TabControl
            {
                Left = 12,
                Top = 62,
                Width = ClientSize.Width - 24,
                Height = ClientSize.Height - 118,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Font = Theme.UIFont
            };
            _tabs.TabPages.Add(BuildMatrixPage());
            _tabs.TabPages.Add(BuildAreaPage());
            _tabs.TabPages.Add(BuildAccuracyPage());
            _tabs.TabPages.Add(BuildImportancePage());
            _tabs.TabPages.Add(BuildImagePage(Lang.T("实测 / 预测 对比"), _result.Figure("compare"),
                Lang.T("三联对比图由引擎生成：末期实测、预测、逐像元差异。")));
            _tabs.TabPages.Add(BuildImagePage(Lang.T("变化热点"), _result.Figure("hotspot"),
                Lang.T("按“源类→目标类”着色的变化热点，图例给出最主要的四种转移类型。")));
            foreach (TabPage page in _tabs.TabPages) WrapPageContent(page);

            var openReport = new Button { Text = Lang.T("打开报告"), Width = 96, Height = 30, Left = 12, Top = ClientSize.Height - 42 };
            openReport.Enabled = !string.IsNullOrEmpty(_result.ReportPath) && File.Exists(_result.ReportPath);
            openReport.Click += (s, e) => CaMarkovEngine.OpenWithShell(_result.ReportPath);

            var openFolder = new Button { Text = Lang.T("打开输出目录"), Width = 110, Height = 30, Left = 116, Top = ClientSize.Height - 42 };
            openFolder.Click += (s, e) => OpenFolder();

            var openRaster = new Button { Text = Lang.T("定位预测栅格"), Width = 110, Height = 30, Left = 234, Top = ClientSize.Height - 42 };
            openRaster.Enabled = !string.IsNullOrEmpty(_result.RasterPath) && File.Exists(_result.RasterPath);
            openRaster.Click += (s, e) => CaMarkovEngine.OpenWithShell(Path.GetDirectoryName(_result.RasterPath));

            var close = new Button
            {
                Text = Lang.T("关闭"),
                Width = 88,
                Height = 30,
                Left = ClientSize.Width - 100,
                Top = ClientSize.Height - 42,
                DialogResult = DialogResult.Cancel
            };
            CancelButton = close;

            MakeLabelsTransparent(this);
            Controls.Add(header);
            Controls.Add(_tabs);
            Controls.Add(openReport);
            Controls.Add(openFolder);
            Controls.Add(openRaster);
            Controls.Add(close);
        }

        /// <summary>标签底色改透明，避免在深色底板上出现灰白块。</summary>
        private static void MakeLabelsTransparent(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label) child.BackColor = Color.Transparent;
                MakeLabelsTransparent(child);
            }
        }

        private string BuildHeader()
        {
            string text = string.Format(Lang.T("预测步数 {0} · 邻域 {1}×{1} · 类别 {2} 个 · 耗时 {3:0.0} 秒"),
                _result.Steps, Math.Max(1, _result.NeighborhoodSize), _result.ClassCount,
                _result.ElapsedSeconds);
            if (!string.IsNullOrEmpty(_result.ValidationNote))
                text += Lang.T("\r\n精度评价：") + _result.ValidationNote;
            if (_result.Warnings.Count > 0)
                text += Lang.T("\r\n提示：") + _result.Warnings[0];
            return text;
        }

        private void OpenFolder()
        {
            string path = _result.RasterPath;
            if (string.IsNullOrEmpty(path)) path = _result.ReportPath;
            if (string.IsNullOrEmpty(path)) return;
            try { CaMarkovEngine.OpenWithShell(Path.GetDirectoryName(path)); } catch { }
        }

        // ---------------------------------------------------------------- 各页

        private TabPage NewPage(string title)
        {
            return new TabPage(title) { ForeColor = Theme.Text };
        }

        /// <summary>把页内控件装进深色面板，否则标签的透明会按 TabPage 的浅色底计算。</summary>
        private static void WrapPageContent(TabPage page)
        {
            var children = new Control[page.Controls.Count];
            page.Controls.CopyTo(children, 0);
            page.Controls.Clear();
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
            host.Controls.AddRange(children);
            page.Controls.Add(host);
        }

        private TabPage BuildMatrixPage()
        {
            var page = NewPage(Lang.T("转移矩阵"));
            var heat = new MatrixHeatmap
            {
                Dock = DockStyle.Fill,
                ValueFormat = "0.0000"
            };
            var bar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Theme.Panel };
            var mode = new ComboBox
            {
                Left = 4,
                Top = 3,
                Width = 190,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat
            };
            mode.Items.AddRange(new object[] { Lang.T("转移概率（行归一化）"), Lang.T("转移计数（像元）"), Lang.T("转移预算（像元）") });
            mode.SelectedIndex = 0;

            Action refresh = () =>
            {
                string[] names = _result.Names;
                if (mode.SelectedIndex == 1)
                {
                    heat.ValueFormat = "#,0";
                    heat.Title = Lang.T("转移计数矩阵 C[i,j]（像元）");
                    heat.SetData(_result.Counts, names, names);
                }
                else if (mode.SelectedIndex == 2)
                {
                    heat.ValueFormat = "#,0";
                    heat.Title = Lang.T("转移预算矩阵 B[i,j]（行和 = 当期该类像元数）");
                    heat.SetData(_result.Budget, names, names);
                }
                else
                {
                    heat.ValueFormat = "0.0000";
                    heat.Title = Lang.T("Markov 转移概率矩阵 P[i,j]");
                    heat.SetData(_result.Probability, names, names);
                }
            };
            mode.SelectedIndexChanged += (s, e) => refresh();

            bar.Controls.Add(mode);
            // 先加 Fill 再加 Top，停靠顺序才是“上一条 + 其余填满”
            page.Controls.Add(heat);
            page.Controls.Add(bar);
            refresh();
            return page;
        }

        private TabPage BuildAreaPage()
        {
            var page = NewPage(Lang.T("面积变化"));
            string unit = _result.PixelAreaKm2 > 0 ? " km²" : Lang.T(" 像元");
            var bars = new GroupedBars
            {
                Dock = DockStyle.Fill,
                Title = Lang.T("各类面积：基期 / 末期实测 / 预测"),
                Unit = unit,
                ValueFormat = "#,0.00"
            };

            var names = new List<string>();
            var baseValues = new List<double>();
            var endValues = new List<double>();
            var predValues = new List<double>();
            foreach (var row in _result.Classes)
            {
                if (row.BaseArea <= 0 && row.EndArea <= 0 && row.PredArea <= 0) continue;
                names.Add(row.Name);
                baseValues.Add(row.BaseArea);
                endValues.Add(row.EndArea);
                predValues.Add(row.PredArea);
            }
            bars.SetData(names.ToArray(), new[]
            {
                new KeyValuePair<string, double[]>(Lang.T("基期"), baseValues.ToArray()),
                new KeyValuePair<string, double[]>(Lang.T("末期实测"), endValues.ToArray()),
                new KeyValuePair<string, double[]>(Lang.T("预测"), predValues.ToArray()),
            });
            page.Controls.Add(bars);
            return page;
        }

        private TabPage BuildAccuracyPage()
        {
            var page = NewPage(Lang.T("精度评价"));
            if (_result.Accuracy == null)
            {
                page.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = Theme.TextDim,
                    Text = Lang.T("本次运行没有做精度评价。\r\n勾选「输出精度评价」后重跑即可看到 OA / Kappa / PA / UA / F1 与混淆矩阵。")
                });
                return page;
            }

            var cards = new MetricCards
            {
                Dock = DockStyle.Top,
                Height = 76
            };
            var items = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(Lang.T("总体精度 OA"), _result.Accuracy.Oa.ToString("P2")),
                new KeyValuePair<string, string>("Kappa", _result.Accuracy.Kappa.ToString("F4")),
                new KeyValuePair<string, string>(Lang.T("验证像元"), _result.Accuracy.Total.ToString("#,0")),
            };
            if (_result.BaselineOa.HasValue)
                items.Add(new KeyValuePair<string, string>(Lang.T("未变化基线 OA"), _result.BaselineOa.Value.ToString("P2")));
            cards.SetData(items);

            var conf = new MatrixHeatmap
            {
                Dock = DockStyle.Fill,
                Title = Lang.T("混淆矩阵（行=实测，列=预测）"),
                ValueFormat = "#,0",
                RowTitle = Lang.T("实测"),
                ColTitle = Lang.T("预测")
            };
            var names = _result.Names;
            var confusion = _result.Accuracy.Confusion;
            var matrix = new double[confusion == null ? 0 : confusion.Length][];
            for (int i = 0; i < matrix.Length; i++)
            {
                var row = confusion[i];
                matrix[i] = new double[row.Length];
                for (int j = 0; j < row.Length; j++) matrix[i][j] = row[j];
            }
            conf.SetData(matrix, names, names);

            page.Controls.Add(conf);
            page.Controls.Add(cards);
            return page;
        }

        private TabPage BuildImportancePage()
        {
            var page = NewPage(Lang.T("特征重要性"));
            if (_result.FeatureImportance.Count == 0)
            {
                page.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = Theme.TextDim,
                    Text = Lang.T("本次没有训练随机森林（适宜性来源不是「随机森林」），因此没有特征重要性。")
                });
                return page;
            }

            var names = new List<string>();
            var values = new List<double>();
            foreach (var item in _result.FeatureImportance)
            {
                names.Add(item.Name);
                values.Add(item.Importance);
            }

            var bars = new ImportanceBars
            {
                Dock = DockStyle.Fill
            };
            bars.SetData(names.ToArray(), values.ToArray());

            var caption = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Theme.TextDim,
                // 括号整体翻译：拆成「驱动因子重要性（」+ 内容 +「）」在英文下会留下全角括号
                Text = _result.RfSummary == null
                    ? Lang.T("驱动因子重要性")
                    : Lang.T("驱动因子重要性（{0}）", _result.RfSummary)
            };
            page.Controls.Add(bars);
            page.Controls.Add(caption);
            return page;
        }

        private TabPage BuildImagePage(string title, string path, string hint)
        {
            var page = NewPage(title);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                page.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = Theme.TextDim,
                    Text = Lang.T("没有生成这张图。\r\n") + hint
                });
                return page;
            }

            var box = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Theme.PanelAlt,
                ImageLocation = path
            };
            page.Controls.Add(box);
            return page;
        }

        // ---------------------------------------------------------------- 测试辅助

        /// <summary>构造一份假结果，供界面自检渲染各页（不依赖 Python）。</summary>
        internal static CaMarkovResult Sample()
        {
            var r = new CaMarkovResult
            {
                Mode = "predict",
                Steps = 2,
                NeighborhoodSize = 5,
                PixelAreaKm2 = 0.000900534,
                ElapsedSeconds = 12.5,
                ValidationNote = Lang.T("时间外推检验（示例数据）"),
                RfSummary = Lang.T("200 棵树 · 深度 20 · 样本 624000"),
                BaselineOa = 0.9021,
                Accuracy = new CaAccuracy
                {
                    Oa = 0.9470,
                    Kappa = 0.9230,
                    Total = 3728737,
                    Pa = new[] { 0, 0.96, 0.94, 0.9, 0.8, 0.95, 0.7, 0.6, 0.93 },
                    Ua = new[] { 0, 0.95, 0.92, 0.88, 0.7, 0.96, 0.6, 0.5, 0.94 },
                    F1 = new[] { 0, 0.955, 0.93, 0.89, 0.75, 0.955, 0.65, 0.55, 0.935 },
                },
            };
            string[] names = { Lang.T("无效值"), Lang.T("耕地"), Lang.T("森林"), Lang.T("草地"), Lang.T("灌木"), Lang.T("水体"), Lang.T("冰雪"), Lang.T("裸地"), Lang.T("不透水面") };
            double[] baseCounts = { 0, 1390077, 821275, 0, 18, 225977, 0, 285, 1291105 };
            double[] predCounts = { 0, 1320000, 822000, 0, 16, 194800, 0, 285, 1392000 };
            var confusion = new long[names.Length][];
            for (int i = 0; i < names.Length; i++)
            {
                confusion[i] = new long[names.Length];
                for (int j = 0; j < names.Length; j++)
                    confusion[i][j] = i == j ? (long)(baseCounts[i] * 0.93) : (long)(baseCounts[i] * 0.007);
            }
            r.Accuracy.Confusion = confusion;
            for (int i = 0; i < names.Length; i++)
            {
                r.Classes.Add(new CaAreaRow
                {
                    Value = i,
                    Name = names[i],
                    BaseArea = baseCounts[i] * 0.000900534,
                    EndArea = baseCounts[i] * 0.000900534,
                    PredArea = predCounts[i] * 0.000900534,
                    ChangePercent = baseCounts[i] > 0 ? (predCounts[i] - baseCounts[i]) / baseCounts[i] * 100 : 0,
                    BasePixels = (int)baseCounts[i],
                    EndPixels = (int)baseCounts[i],
                    PredPixels = (int)predCounts[i],
                });
            }

            int n = names.Length;
            r.Counts = new double[n][];
            r.Probability = new double[n][];
            r.Budget = new double[n][];
            var rnd = new Random(7);
            for (int i = 0; i < n; i++)
            {
                r.Counts[i] = new double[n];
                r.Probability[i] = new double[n];
                r.Budget[i] = new double[n];
                double sum = 0;
                for (int j = 0; j < n; j++)
                {
                    double v = i == j ? 800 + rnd.Next(200) : rnd.Next(60);
                    r.Counts[i][j] = baseCounts[i] > 0 && i > 0 ? v : 0;
                    sum += r.Counts[i][j];
                }
                for (int j = 0; j < n; j++)
                {
                    r.Probability[i][j] = sum > 0 ? r.Counts[i][j] / sum : 0;
                    r.Budget[i][j] = r.Counts[i][j];
                }
            }

            r.FeatureImportance.Add(new CaFeatureImportance { Name = Lang.T("距道路"), Importance = 0.31 });
            r.FeatureImportance.Add(new CaFeatureImportance { Name = Lang.T("人口密度"), Importance = 0.24 });
            r.FeatureImportance.Add(new CaFeatureImportance { Name = Lang.T("高程"), Importance = 0.18 });
            r.FeatureImportance.Add(new CaFeatureImportance { Name = Lang.T("坡度"), Importance = 0.12 });
            r.FeatureImportance.Add(new CaFeatureImportance { Name = Lang.T("距水系"), Importance = 0.09 });
            r.FeatureImportance.Add(new CaFeatureImportance { Name = Lang.T("坡向"), Importance = 0.04 });
            r.FeatureImportance.Add(new CaFeatureImportance { Name = Lang.T("距铁路"), Importance = 0.02 });
            r.Warnings.Add(Lang.T("示例数据：仅用于界面自检"));
            return r;
        }
    }
}
