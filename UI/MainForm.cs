using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.Processing;
using SharpGIS.Services;
using SharpGIS.UI.Panels;

namespace SharpGIS.UI
{
    /// <summary>主窗体：菜单 + 工具栏 + 图层面板 + 地图画布 + 属性面板 + 状态栏。</summary>
    public partial class MainForm : Form
    {
        private readonly LayerStack _stack = new LayerStack();

        private MapCanvas _canvas;
        private LayerPanelControl _layerPanel;
        private TabControl _tabs;
        private SymbologyPanel _symbology;
        private HistogramPanel _histogram;
        private MetadataPanel _metadata;
        private IdentifyPanel _identify;

        private ToolStripButton _btnPan, _btnZoomIn, _btnZoomOut, _btnRect, _btnIdentify;
        private ToolStripButton _btnRotate, _btnResetNorth;

        private ToolStripStatusLabel _lblCoord, _lblPixel, _lblValue, _lblZoom, _lblRender, _lblCrs;
        private string _fullCrsText = "-";

        public MainForm()
        {
            BuildUi();
            WireEvents();
            Text = Lang.T("SharpGIS 1.0 — 栅格遥感影像处理平台");
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1440, 900);
            MinimumSize = new Size(1000, 640);
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;

            Theme.Apply(this);
            UpdateStatus(null);
        }

        // ---------------- 界面搭建 ----------------

        private void BuildUi()
        {
            var menu = BuildMenu();
            var tool = BuildToolStrip();
            var status = BuildStatusStrip();

            _canvas = new MapCanvas { Dock = DockStyle.Fill };

            _layerPanel = new LayerPanelControl { Dock = DockStyle.Fill };

            _symbology = new SymbologyPanel { Dock = DockStyle.Fill };
            _histogram = new HistogramPanel { Dock = DockStyle.Fill };
            _metadata = new MetadataPanel { Dock = DockStyle.Fill };
            _identify = new IdentifyPanel { Dock = DockStyle.Fill };

            _tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.UIFont, Padding = new Point(8, 4) };
            AddTab(Lang.T("符号化"), _symbology);
            AddTab(Lang.T("直方图"), _histogram);
            AddTab(Lang.T("元数据"), _metadata);
            AddTab(Lang.T("识别"), _identify);

            var rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 5,
                BackColor = Theme.Border
            };
            rightSplit.Panel1.Controls.Add(_canvas);
            rightSplit.Panel2.Controls.Add(_tabs);

            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 5,
                BackColor = Theme.Border
            };
            mainSplit.Panel1.Controls.Add(_layerPanel);
            mainSplit.Panel2.Controls.Add(rightSplit);

            Controls.Add(mainSplit);
            Controls.Add(tool);
            Controls.Add(menu);
            Controls.Add(status);
            MainMenuStrip = menu;

            var leftHeader = new Panel { Dock = DockStyle.Top, Height = 24, BackColor = Theme.PanelAlt };
            leftHeader.Controls.Add(new Label
            {
                Text = Lang.T("  图层"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.TextDim,
                Font = Theme.UIFont
            });
            mainSplit.Panel1.Controls.Add(leftHeader);
            leftHeader.BringToFront();

            Load += (s, e) =>
            {
                try
                {
                    mainSplit.Panel1MinSize = 170;
                    mainSplit.Panel2MinSize = 600;
                    mainSplit.SplitterDistance = 250;
                }
                catch { }
                try
                {
                    rightSplit.Panel1MinSize = 320;
                    rightSplit.Panel2MinSize = 300;
                    rightSplit.SplitterDistance = Math.Max(340, rightSplit.Width - 340);
                }
                catch { }
                _layerPanel.Bind(_stack);
                _canvas.SetLayers(_stack);
                Theme.TryEnableDarkTitleBar(Handle);
            };
        }

        private void AddTab(string title, Control content)
        {
            var page = new TabPage(title) { BackColor = Theme.Panel, Padding = new Padding(2) };
            page.Controls.Add(content);
            _tabs.TabPages.Add(page);
        }

        private MenuStrip BuildMenu()
        {
            var menu = new MenuStrip { Dock = DockStyle.Top };

            var file = new ToolStripMenuItem(Lang.T("文件(&F)"));
            file.DropDownItems.Add(Item(Lang.T("打开栅格影像…"), Keys.Control | Keys.O, (s, e) => OpenDialog()));
            file.DropDownItems.Add(Item(Lang.T("关闭当前图层"), Keys.Control | Keys.W, (s, e) => CloseCurrentLayer()));
            file.DropDownItems.Add(new ToolStripSeparator());
            var recent = new ToolStripMenuItem(Lang.T("最近打开"));
            recent.DropDownOpening += (s, e) => FillRecentMenu(recent);
            file.DropDownItems.Add(recent);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item(Lang.T("导出当前视图为图片…"), Keys.None, (s, e) => ExportViewImage()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item(Lang.T("退出"), Keys.Alt | Keys.F4, (s, e) => Close()));

            var view = new ToolStripMenuItem(Lang.T("视图(&V)"));
            view.DropDownItems.Add(Item(Lang.T("放大"), Keys.Control | Keys.Oemplus, (s, e) => _canvas.ZoomBy(1.5)));
            view.DropDownItems.Add(Item(Lang.T("缩小"), Keys.Control | Keys.OemMinus, (s, e) => _canvas.ZoomBy(1 / 1.5)));
            view.DropDownItems.Add(Item(Lang.T("缩放到全图"), Keys.Control | Keys.Shift | Keys.F, (s, e) => _canvas.ZoomToFull()));
            view.DropDownItems.Add(Item(Lang.T("1:1 像素"), Keys.Control | Keys.D1, (s, e) => _canvas.ZoomToPixelSize()));
            view.DropDownItems.Add(Item(Lang.T("缩放到图层"), Keys.None, (s, e) => ZoomToSelectedLayer()));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Item(Lang.T("旋转工具（三次单击：中心 → 手柄 → 提交）"), Keys.None, (s, e) => SetTool(MapTool.Rotate)));
            // Keys.Shift|Keys.R 不是合法的 ShortcutKeys 组合（会抛 InvalidEnumArgumentException），
            // 快捷键在 MainForm_KeyDown 里处理
            view.DropDownItems.Add(Item(Lang.T("重置北向"), Keys.None, (s, e) => ResetNorth()));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Item(Lang.T("上一视图"), Keys.Alt | Keys.Left, (s, e) => _canvas.GoBack()));
            view.DropDownItems.Add(Item(Lang.T("下一视图"), Keys.Alt | Keys.Right, (s, e) => _canvas.GoForward()));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Item(Lang.T("刷新显示"), Keys.F5, (s, e) => _canvas.RenderNow(Rendering.RenderQuality.Full)));

            var layer = new ToolStripMenuItem(Lang.T("图层(&L)"));
            layer.DropDownItems.Add(Item(Lang.T("上移"), Keys.Control | Keys.Up, (s, e) => MoveSelected(true)));
            layer.DropDownItems.Add(Item(Lang.T("下移"), Keys.Control | Keys.Down, (s, e) => MoveSelected(false)));
            layer.DropDownItems.Add(Item(Lang.T("移除"), Keys.Delete, (s, e) => CloseCurrentLayer()));
            layer.DropDownItems.Add(new ToolStripSeparator());
            layer.DropDownItems.Add(Item(Lang.T("符号化属性…"), Keys.None, (s, e) => _tabs.SelectedIndex = 0));

            var process = new ToolStripMenuItem(Lang.T("处理(&P)"));
            foreach (var entry in BuildProcessItems()) process.DropDownItems.Add(entry);

            var help = new ToolStripMenuItem(Lang.T("帮助(&H)"));
            help.DropDownItems.Add(Item(Lang.T("快捷键一览"), Keys.F1, (s, e) => ShowShortcuts()));
            help.DropDownItems.Add(Item(Lang.T("关于 SharpGIS"), Keys.None, (s, e) => ShowAbout()));

            var language = new ToolStripMenuItem(Lang.T("语言(&G)"));
            language.DropDownItems.Add(LanguageItem("中文", Lang.Chinese));
            language.DropDownItems.Add(LanguageItem("English", Lang.English));

            menu.Items.Add(file);
            menu.Items.Add(view);
            menu.Items.Add(layer);
            menu.Items.Add(process);
            menu.Items.Add(help);
            menu.Items.Add(language);

            Controls.Add(menu);
            Theme.StyleMenuStrip(menu);
            return menu;
        }

        private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler onClick)
        {
            var it = new ToolStripMenuItem(text);
            if (shortcut != Keys.None) it.ShortcutKeys = shortcut;
            if (onClick != null) it.Click += onClick;
            return it;
        }

        private ToolStrip BuildToolStrip()
        {
            var tool = new ToolStrip { Dock = DockStyle.Top, ImageScalingSize = new Size(18, 18) };
            Theme.StyleToolStrip(tool);

            tool.Items.Add(ToolButton("open", Lang.T("打开栅格影像 (Ctrl+O)"), (s, e) => OpenDialog()));
            tool.Items.Add(new ToolStripSeparator());

            _btnPan = ToolButton("pan", Lang.T("平移（拖动地图）"), (s, e) => SetTool(MapTool.Pan));
            _btnZoomIn = ToolButton("zoom_in", Lang.T("放大（点击）"), (s, e) => SetTool(MapTool.ZoomIn));
            _btnZoomOut = ToolButton("zoom_out", Lang.T("缩小（点击）"), (s, e) => SetTool(MapTool.ZoomOut));
            _btnRect = ToolButton("rect_zoom", Lang.T("框选缩放"), (s, e) => SetTool(MapTool.ZoomRect));
            _btnRotate = ToolButton("rotate", Lang.T("旋转视图：单击定中心 → 单击定手柄 → 移动旋转，再单击提交（R）"), (s, e) => SetTool(MapTool.Rotate));
            tool.Items.AddRange(new ToolStripItem[] { _btnPan, _btnZoomIn, _btnZoomOut, _btnRect, _btnRotate });

            tool.Items.Add(new ToolStripSeparator());
            tool.Items.Add(ToolButton("full", Lang.T("缩放到全图 (Ctrl+Shift+F)"), (s, e) => _canvas.ZoomToFull()));
            tool.Items.Add(ToolButton("one_to_one", Lang.T("1:1 像素 (Ctrl+1)"), (s, e) => _canvas.ZoomToPixelSize()));
            _btnResetNorth = ToolButton("reset_north", Lang.T("重置北向（Shift+R）"), (s, e) => ResetNorth());
            tool.Items.Add(_btnResetNorth);
            tool.Items.Add(new ToolStripSeparator());
            _btnIdentify = ToolButton("identify", Lang.T("识别像素值"), (s, e) => SetTool(MapTool.Identify));
            tool.Items.Add(_btnIdentify);
            tool.Items.Add(ToolButton("histogram", Lang.T("直方图"), (s, e) => _tabs.SelectedIndex = 1));

            tool.Items.Add(new ToolStripSeparator());
            var process = new ToolStripDropDownButton(Lang.T("处理"))
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = Lang.T("栅格处理工具箱"),
                Padding = new Padding(6, 0, 6, 0)
            };
            foreach (var entry in BuildProcessItems()) process.DropDownItems.Add(entry);
            tool.Items.Add(process);

            Controls.Add(tool);
            return tool;
        }

        /// <summary>处理菜单项（菜单栏与工具栏共用同一份定义）。</summary>
        private ToolStripItem[] BuildProcessItems()
        {
            return new ToolStripItem[]
            {
                new ToolStripMenuItem(Lang.T("格式转换 / 导出…"), null, (s, e) => OnExport()),
                new ToolStripMenuItem(Lang.T("重投影 / 重采样…"), null, (s, e) => OnWarp()),
                new ToolStripMenuItem(Lang.T("按范围裁剪…"), null, (s, e) => OnCrop()),
                new ToolStripMenuItem(Lang.T("生成金字塔…"), null, (s, e) => OnOverviews()),
                new ToolStripSeparator(),
                new ToolStripMenuItem(Lang.T("波段计算器…"), null, (s, e) => OnBandCalc()),
                new ToolStripMenuItem(Lang.T("DEM 分析（坡度/坡向/山体阴影）…"), null, (s, e) => OnDem()),
                new ToolStripMenuItem(Lang.T("滤波（均值/中值/高斯/拉普拉斯锐化/Sobel）…"), null, (s, e) => OnFilter()),
                new ToolStripMenuItem(Lang.T("增强（线性拉伸/直方图均衡）…"), null, (s, e) => OnEnhance()),
                new ToolStripMenuItem(Lang.T("影像融合（Brovey/IHS/HPF）…"), null, (s, e) => OnFusion()),
                new ToolStripMenuItem(Lang.T("非监督分类（KMeans）…"), null, (s, e) => OnClassify()),
                new ToolStripSeparator(),
                new ToolStripMenuItem(Lang.T("土地利用变化预测（CA-Markov）…"), null, (s, e) => OnCaMarkov())
            };
        }

        /// <summary>构造一个语言单选菜单项。</summary>
        /// <param name="text">显示文字（语言自称，不翻译）。</param>
        /// <param name="code">语言代码。</param>
        private ToolStripMenuItem LanguageItem(string text, string code)
        {
            var item = new ToolStripMenuItem(text)
            {
                Checked = Lang.Current == code,
                CheckOnClick = false
            };
            item.Click += (s, e) => SwitchLanguage(code);
            return item;
        }

        /// <summary>切换界面语言：保存选择并提示重启。</summary>
        /// <param name="code">目标语言代码。</param>
        private void SwitchLanguage(string code)
        {
            if (Lang.Current == code) return;
            Lang.Save(code);
            // 句号随语言切换：中文用「。」，英文用「.」
            string period = Lang.IsEnglish ? "." : "。";
            var answer = MessageBox.Show(this,
                Lang.T("界面语言已改为") + " " + (code == Lang.English ? "English" : "中文") + period + "\r\n\r\n"
                + Lang.T("切换语言需要重启程序才能生效，现在重启吗？（已打开的图层不会保留）"),
                Lang.T("切换语言"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) Application.Restart();
        }

        private void ShowShortcuts()
        {
            Info(Lang.T("快捷键：\r\n") +
                 Lang.T("Ctrl+O  打开栅格影像\r\n") +
                 Lang.T("Ctrl+W  关闭当前图层\r\n") +
                 Lang.T("Ctrl+Shift+F  缩放到全图\r\n") +
                 Lang.T("Ctrl+1  1:1 像素\r\n") +
                 Lang.T("Ctrl+ + / -  放大 / 缩小\r\n") +
                 Lang.T("Alt+← / →  上一视图 / 下一视图\r\n") +
                 Lang.T("Delete  移除当前图层\r\n") +
                 Lang.T("F5  刷新显示\r\n") +
                 Lang.T("R  旋转工具（单击定中心 → 单击定手柄 → 移动旋转，再单击提交）\r\n") +
                 Lang.T("Shift+R  重置北向\r\n") +
                 Lang.T("Esc  取消旋转 / 回到平移工具\r\n") +
                 Lang.T("滚轮  以光标为锚点缩放；拖动  平移；双击图层  打开符号化"));
        }

        private void ShowAbout()
        {
            Info(Lang.T("SharpGIS 1.0\r\n\r\nC# + GDAL 实现的栅格 GIS（QGIS 栅格功能子集）\r\n") +
                 Lang.T("GDAL 版本：") + GdalBootstrap.GdalVersion + "\r\n" +
                 Lang.T("支持多图层叠加、符号化、直方图、识别、重投影、裁剪、波段计算、DEM、滤波、融合、分类等。\r\n") +
                 Lang.T("不包含矢量功能。"));
        }

        private ToolStripButton ToolButton(string icon, string tip, EventHandler onClick)
        {
            var b = new ToolStripButton
            {
                Image = Icons.Create(icon, 18),
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ToolTipText = tip,
                AutoSize = false,
                Width = 30,
                Height = 26
            };
            b.Click += onClick;
            return b;
        }

        private StatusStrip BuildStatusStrip()
        {
            var status = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false };
            Theme.StyleStatusStrip(status);

            _lblCoord = new ToolStripStatusLabel(Lang.T("坐标: -")) { AutoSize = false, Width = 200, TextAlign = ContentAlignment.MiddleLeft };
            _lblPixel = new ToolStripStatusLabel(Lang.T("行列: -")) { AutoSize = false, Width = 110, TextAlign = ContentAlignment.MiddleLeft };
            _lblValue = new ToolStripStatusLabel(Lang.T("值: -")) { AutoSize = false, Width = 210, TextAlign = ContentAlignment.MiddleLeft };
            _lblCrs = new ToolStripStatusLabel(Lang.T("投影: -")) { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _lblZoom = new ToolStripStatusLabel(Lang.T("缩放: -")) { AutoSize = false, Width = 175, TextAlign = ContentAlignment.MiddleRight };
            _lblRender = new ToolStripStatusLabel("") { AutoSize = false, Width = 140, TextAlign = ContentAlignment.MiddleRight };

            status.Items.AddRange(new ToolStripItem[]
            {
                _lblCoord, _lblPixel, _lblValue, _lblCrs, _lblZoom, _lblRender
            });
            // 弹簧项的实际宽度由布局决定，尺寸变化后要按新宽度重新裁剪文本
            status.Resize += (s, e) => ApplyCrsText();
            Controls.Add(status);
            return status;
        }

        // ---------------- 事件接线 ----------------

        private void WireEvents()
        {
            _canvas.ViewChanged += (s, e) => UpdateStatus(null);
            _canvas.RenderCompleted += (s, e) =>
            {
                UpdateZoomLabel();
                if (_canvas.LastStats != null) _lblRender.Text = _canvas.LastStats.ToString();
            };
            _canvas.CursorInfoChanged += (s, e) => UpdateStatus(e);
            _canvas.IdentifyRequested += (s, e) =>
            {
                _tabs.SelectedIndex = 3;
                _identify.ShowIdentify(e);
            };

            _layerPanel.SelectedLayerChanged += (s, e) => BindSelectedLayer();
            _layerPanel.EditStyleRequested += (s, e) => _tabs.SelectedIndex = 0;
            _layerPanel.ZoomToLayerRequested += (s, e) => ZoomToSelectedLayer();
            _layerPanel.MenuCommand += (s, e) => HandleLayerMenu(e);

            _symbology.StyleEdited += (s, e) =>
            {
                _stack.NotifyStyleChanged();
                _canvas.RequestRender(true);
                BindSelectedLayer();
            };
            _histogram.StyleEdited += (s, e) =>
            {
                _stack.NotifyStyleChanged();
                _canvas.RequestRender(true);
            };

            DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return;
                foreach (var f in files) OpenRaster(f, true);
            };

            KeyDown += MainForm_KeyDown;
            FormClosing += (s, e) =>
            {
                try { _stack.Clear(); } catch { }
            };
            SetTool(MapTool.Pan);
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.O) { OpenDialog(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { CloseCurrentLayer(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { _canvas.RenderNow(Rendering.RenderQuality.Full); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape)
            {
                // 先撤销/退出旋转，再退回平移工具
                if (!_canvas.CancelRotate(true)) SetTool(MapTool.Pan);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.R && !e.Control && !e.Alt)
            {
                if (e.Shift) ResetNorth(); else SetTool(MapTool.Rotate);
                e.Handled = true;
            }
        }

        private void SetTool(MapTool tool)
        {
            _canvas.Tool = tool;
            UpdateToolButtons(tool);
        }

        private void UpdateToolButtons(MapTool tool)
        {
            if (_btnPan == null) return;
            foreach (var b in new[] { _btnPan, _btnZoomIn, _btnZoomOut, _btnRect, _btnIdentify, _btnRotate })
                b.Checked = false;
            switch (tool)
            {
                case MapTool.Pan: _btnPan.Checked = true; break;
                case MapTool.ZoomIn: _btnZoomIn.Checked = true; break;
                case MapTool.ZoomOut: _btnZoomOut.Checked = true; break;
                case MapTool.ZoomRect: _btnRect.Checked = true; break;
                case MapTool.Identify: _btnIdentify.Checked = true; break;
                case MapTool.Rotate: _btnRotate.Checked = true; break;
            }
        }

        // ---------------- 图层操作 ----------------

        private void OpenDialog()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Lang.T("打开栅格影像（可多选）"),
                Filter = DatasetService.BuildDialogFilter(),
                CheckFileExists = true,
                Multiselect = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                // 一次选多个文件时逐个打开；只有单选才在打开时缩放到该图层，
                // 多选统一在全部加载完后缩放到第一个成功的图层，避免视图来回跳。
                bool multi = dlg.FileNames.Length > 1;
                RasterLayer first = null;
                foreach (var file in dlg.FileNames)
                {
                    var layer = OpenRaster(file, !multi);
                    if (first == null) first = layer;
                }
                if (multi && first != null)
                {
                    _canvas.ZoomToLayer(first);
                    Info(string.Format(Lang.T("已打开 {0} 个文件"), dlg.FileNames.Length));
                }
            }
        }

        /// <summary>坐标系不一致时是否自动重投影对齐（自检/无交互场景使用）。</summary>
        internal bool AutoAlignCrs { get; set; }

        /// <summary>打开栅格文件并加入图层栈；坐标系不一致时可自动重投影对齐。</summary>
        /// <param name="path">影像文件路径。</param>
        /// <param name="zoomToLayer">打开后是否缩放到该图层。</param>
        /// <returns>成功时返回新图层，失败返回 null。</returns>
        public RasterLayer OpenRaster(string path, bool zoomToLayer)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                var layer = DatasetService.Open(path);

                // 与已有图层的坐标系不一致时，提示重投影对齐（类似 QGIS 的即时重投影）
                var reference = _stack.ToArray().Length > 0 ? _stack.ToArray()[0] : null;
                if (reference != null && NeedsReprojection(reference, layer))
                {
                    var answer = AutoAlignCrs ? DialogResult.Yes : MessageBox.Show(this,
                        string.Format(Lang.T("新图层的坐标系与工程不一致：\r\n\r\n工程：{0}\r\n新图层：{1}\r\n\r\n是否重投影到工程坐标系后再加载？\r\n（选择“否”将按原坐标直接叠加，位置可能错位）"),
                            reference.EpsgText, layer.EpsgText),
                        Lang.T("坐标系不一致"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                    if (answer == DialogResult.Cancel) { layer.Dispose(); return null; }
                    if (answer == DialogResult.Yes)
                    {
                        var warped = ReprojectToProject(layer, reference);
                        if (warped == null) { layer.Dispose(); return null; }
                        layer = warped;
                    }
                }
                _stack.Add(layer);
                RecentFiles.Add(path);
                _layerPanel.RefreshList();
                _layerPanel.SelectLayer(layer);

                if (zoomToLayer || _stack.Count == 1)
                    _canvas.ZoomToLayer(layer);
                else
                    _canvas.RequestRender(true);

                BindSelectedLayer();
                return layer;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Lang.T("打开失败"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>供自检使用。</summary>
        internal MapCanvas Canvas { get { return _canvas; } }
        internal LayerPanelControl LayerPanel { get { return _layerPanel; } }
        internal LayerStack Stack { get { return _stack; } }

        private RasterLayer CurrentLayer
        {
            get { return _layerPanel.SelectedLayer ?? _stack.TopVisible(); }
        }

        private void CloseCurrentLayer()
        {
            var layer = CurrentLayer;
            if (layer == null) return;
            _stack.Remove(layer);
            _layerPanel.RefreshList();
            BindSelectedLayer();
            _canvas.RequestRender(true);
        }

        private void MoveSelected(bool up)
        {
            var layer = CurrentLayer;
            if (layer == null) return;
            if (up ? _stack.MoveUp(layer) : _stack.MoveDown(layer))
                _stack.RaiseChanged();
        }

        /// <summary>执行图层右键菜单的命令。</summary>
        /// <param name="e">命令与被右键的图层。</param>
        private void HandleLayerMenu(LayerMenuEventArgs e)
        {
            var layer = e.Layer;
            if (layer == null) return;

            switch (e.Command)
            {
                case LayerMenuCommand.ZoomTo:
                    _canvas.ZoomToLayer(layer);
                    break;
                case LayerMenuCommand.Style:
                    _tabs.SelectedIndex = 0;
                    break;
                case LayerMenuCommand.Metadata:
                    _tabs.SelectedIndex = 2;
                    break;
                case LayerMenuCommand.ResetStyle:
                    ResetStyleOf(layer);
                    break;
                case LayerMenuCommand.Rename:
                    RenameLayer(layer);
                    break;
                case LayerMenuCommand.ToggleVisible:
                    layer.Visible = !layer.Visible;
                    _stack.NotifyStyleChanged();
                    _layerPanel.InvalidateLayer(layer);
                    _canvas.RequestRender(false);
                    break;
                case LayerMenuCommand.MoveUp:
                    if (_stack.MoveUp(layer)) _stack.RaiseChanged();
                    break;
                case LayerMenuCommand.MoveDown:
                    if (_stack.MoveDown(layer)) _stack.RaiseChanged();
                    break;
                case LayerMenuCommand.Remove:
                    _stack.Remove(layer);
                    break;
                case LayerMenuCommand.Export:
                    OnExport();
                    break;
                case LayerMenuCommand.Overviews:
                    OnOverviews();
                    break;
                case LayerMenuCommand.Warp:
                    OnWarp();
                    break;
                case LayerMenuCommand.CopyPath:
                    CopyLayerPath(layer);
                    break;
            }
        }

        /// <summary>把图层符号化恢复为默认值（等价于符号化面板里的“恢复默认符号化”）。</summary>
        /// <param name="layer">目标图层。</param>
        private void ResetStyleOf(RasterLayer layer)
        {
            layer.Style = layer.CreateDefaultStyle();
            layer.Touch();
            _stack.NotifyStyleChanged();
            _canvas.RequestRender(true);
            if (ReferenceEquals(layer, _layerPanel.SelectedLayer)) BindSelectedLayer();
        }

        /// <summary>重命名图层（只改显示名，不动文件）。</summary>
        /// <param name="layer">目标图层。</param>
        private void RenameLayer(RasterLayer layer)
        {
            string name;
            if (!Dialogs.TextPromptDialog.TryGet(this, Lang.T("重命名图层"), Lang.T("图层名称"), layer.Name, out name)) return;

            layer.Name = name;
            _layerPanel.RefreshList();
            _layerPanel.SelectLayer(layer);
            _canvas.RequestRender(false);
        }

        /// <summary>把图层的源文件路径复制到剪贴板。</summary>
        /// <param name="layer">目标图层。</param>
        private void CopyLayerPath(RasterLayer layer)
        {
            try
            {
                Clipboard.SetText(string.IsNullOrEmpty(layer.SourcePath) ? layer.Path : layer.SourcePath);
            }
            catch
            {
                // 剪贴板被其他程序占用时忽略，不打断操作
            }
        }

        private void ZoomToSelectedLayer()
        {
            var layer = CurrentLayer;
            if (layer != null) _canvas.ZoomToLayer(layer);
        }

        private void FillRecentMenu(ToolStripMenuItem menu)
        {
            menu.DropDownItems.Clear();
            var list = RecentFiles.Load();
            if (list.Count == 0)
            {
                menu.DropDownItems.Add(new ToolStripMenuItem(Lang.T("（空）")) { Enabled = false });
                return;
            }
            foreach (var path in list)
            {
                var item = new ToolStripMenuItem(Path.GetFileName(path)) { ToolTipText = path };
                item.Click += (s, e) => OpenRaster(path, true);
                menu.DropDownItems.Add(item);
            }
            menu.DropDownItems.Add(new ToolStripSeparator());
            var clear = new ToolStripMenuItem(Lang.T("清空列表"));
            clear.Click += (s, e) => RecentFiles.Clear();
            menu.DropDownItems.Add(clear);
        }

        private void BindSelectedLayer()
        {
            var layer = CurrentLayer;
            _symbology.Bind(layer);
            _histogram.Bind(layer);
            _metadata.Bind(layer);
            if (layer == null) _identify.Clear();
            _fullCrsText = layer != null ? layer.EpsgText : "-";
            ApplyCrsText();
        }

        /// <summary>刷新状态栏的缩放/角度文本（角度拼进同一项，避免新增状态栏项导致总宽溢出）。</summary>
        private void UpdateZoomLabel()
        {
            if (_lblZoom == null) return;
            double angle = _canvas.View.Angle;
            if (Math.Abs(angle) >= 0.05)
            {
                _lblZoom.Text = string.Format(Lang.T("缩放 1:{0:N0} · {1:0}°"), SafeScale(_canvas.View.Mupp), angle);
                _lblZoom.ToolTipText = string.Format(Lang.T("当前视图已旋转 {0:0.0}°（顺时针为正）"), angle);
            }
            else
            {
                _lblZoom.Text = string.Format(Lang.T("缩放 1:{0:N0}"), SafeScale(_canvas.View.Mupp));
                _lblZoom.ToolTipText = Lang.T("缩放比例");
            }
        }

        /// <summary>把视图恢复为正北，并刷新状态栏。</summary>
        private void ResetNorth()
        {
            _canvas.ResetNorth();
            UpdateZoomLabel();
        }

        /// <summary>按状态栏当前宽度刷新投影文本（宽度不足时截断加省略号）。</summary>
        private void ApplyCrsText()
        {
            if (_lblCrs == null) return;
            _lblCrs.Text = FitStatusText(Lang.T("投影: ") + _fullCrsText, _lblCrs);
            _lblCrs.ToolTipText = Lang.T("投影: ") + _fullCrsText;
        }

        /// <summary>按控件像素宽度压缩文本，超出时加省略号（状态栏用）。</summary>
        /// <param name="text">原始文本。</param>
        /// <param name="item">目标状态栏项，提供宽度与字体。</param>
        /// <returns>适配后的文本。</returns>
        private static string FitStatusText(string text, ToolStripItem item)
        {
            if (string.IsNullOrEmpty(text)) return "-";
            if (item == null) return text;
            int max = Math.Max(24, item.Width - 10);
            if (TextRenderer.MeasureText(text, item.Font).Width <= max) return text;

            for (int len = text.Length - 1; len > 1; len--)
            {
                string candidate = text.Substring(0, len) + "…";
                if (TextRenderer.MeasureText(candidate, item.Font).Width <= max) return candidate;
            }
            return "…";
        }

        private void UpdateStatus(CursorInfoEventArgs info)
        {
            if (_canvas == null) return;
            UpdateZoomLabel();
            if (_canvas.LastStats != null)
                _lblRender.Text = _canvas.LastStats.ToString();

            if (info == null)
            {
                _lblCoord.Text = Lang.T("坐标: -");
                _lblPixel.Text = Lang.T("行列: -");
                _lblValue.Text = Lang.T("值: -");
                return;
            }

            _lblCoord.Text = string.Format(Lang.T("坐标: {0:F2}, {1:F2}"), info.GeoX, info.GeoY);
            if (!info.ValidPixel)
            {
                _lblPixel.Text = Lang.T("行列: -");
                _lblValue.Text = Lang.T("值: -");
                return;
            }
            _lblPixel.Text = string.Format(Lang.T("行列: {0}, {1}"), info.Col, info.Row);

            var sb = new System.Text.StringBuilder(Lang.T("值: "));
            if (info.Values != null)
            {
                int max = Math.Min(info.Values.Length, 4);
                for (int i = 0; i < max; i++)
                {
                    if (i > 0) sb.Append("  ");
                    sb.Append("B" + (i + 1) + "=" + (double.IsNaN(info.Values[i]) ? "n/a" : info.Values[i].ToString("0.##")));
                }
                if (info.Values.Length > max) sb.Append(" …");
            }
            _lblValue.Text = FitStatusText(sb.ToString(), _lblValue);
        }

        private static double SafeScale(double mupp)
        {
            if (mupp <= 0 || double.IsNaN(mupp)) return 0;
            // 假设地图单位为米（对地理坐标系仅是近似）
            return mupp * 96 / 0.0254;
        }

        // ---------------- 文件导出 ----------------

        private void ExportViewImage()
        {
            if (_stack.Count == 0) { Info(Lang.T("请先打开影像")); return; }
            using (var dlg = new SaveFileDialog
            {
                Title = Lang.T("导出当前视图"),
                Filter = Lang.T("PNG 图片|*.png|JPEG 图片|*.jpg"),
                FileName = "view_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (var bmp = _canvas.RenderToBitmap(Math.Max(400, _canvas.Width), Math.Max(300, _canvas.Height)))
                    {
                        var fmt = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                            ? System.Drawing.Imaging.ImageFormat.Jpeg
                            : System.Drawing.Imaging.ImageFormat.Png;
                        bmp.Save(dlg.FileName, fmt);
                    }
                    Info(Lang.T("已导出: ") + dlg.FileName);
                }
                catch (Exception ex) { Warn(Lang.T("导出失败: ") + ex.Message); }
            }
        }

        private static bool NeedsReprojection(RasterLayer reference, RasterLayer layer)
        {
            if (reference == null || layer == null) return false;
            if (!reference.HasProjection || !layer.HasProjection) return false;
            if (string.Equals(reference.EpsgText, layer.EpsgText, StringComparison.Ordinal)) return false;
            // 坐标系列名不同时，若范围不重叠【或】像元尺寸量级不同，就视为需要重投影
            return !reference.Extent.Intersects(layer.Extent) ||
                   Math.Abs(reference.PixelSizeX - layer.PixelSizeX) > 1e-9 * Math.Max(1, reference.PixelSizeX);
        }

        /// <summary>把图层重投影到工程坐标系（输出临时 VRT，源文件保持只读）。</summary>
        private RasterLayer ReprojectToProject(RasterLayer layer, RasterLayer reference)
        {
            string vrtDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SharpGIS");
            try { Directory.CreateDirectory(vrtDir); } catch { }
            string vrt = System.IO.Path.Combine(vrtDir, "reproj_" + Guid.NewGuid().ToString("N") + ".vrt");

            var job = JobRunner.Run(this, Lang.T("重投影到工程坐标系 ") + reference.EpsgText,
                reporter => WarpService.Run(layer, new WarpRequest
                {
                    OutputPath = vrt,
                    OutputFormat = "VRT",
                    TargetSrs = reference.ProjectionWkt,
                    Resample = Lang.T("双线性"),
                    CreationOptions = new string[0]
                }, reporter));

            if (job.IsCancelled) return null;
            if (!job.Ok)
            {
                Warn(Lang.T("重投影失败：") + job.Error);
                return null;
            }

            try
            {
                var warped = DatasetService.Open(vrt);
                warped.Name = layer.Name;
                warped.SourcePathOverride(layer.SourcePath);
                warped.TempFilePath = vrt;
                return warped;
            }
            catch (Exception ex)
            {
                Warn(Lang.T("打开重投影结果失败：") + ex.Message);
                return null;
            }
        }

        /// <summary>自检专用：切换到指定标签页。</summary>
        internal void SelectTab(int index)
        {
            if (index >= 0 && index < _tabs.TabPages.Count) _tabs.SelectedIndex = index;
        }

        /// <summary>自检专用：把整个窗体画到位图并保存。</summary>
        internal void DrawToBitmapSafe(string path)
        {
            using (var bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private void Info(string text) { MessageBox.Show(this, text, "SharpGIS", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        private void Warn(string text) { MessageBox.Show(this, text, "SharpGIS", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
