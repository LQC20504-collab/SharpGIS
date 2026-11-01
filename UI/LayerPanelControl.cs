using System;
using System.Drawing;
using System.Windows.Forms;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.UI
{
    /// <summary>图层右键菜单的命令标识。</summary>
    public enum LayerMenuCommand
    {
        /// <summary>缩放到该图层。</summary>
        ZoomTo,
        /// <summary>打开符号化属性页。</summary>
        Style,
        /// <summary>切换到元数据页。</summary>
        Metadata,
        /// <summary>恢复默认符号化。</summary>
        ResetStyle,
        /// <summary>重命名图层。</summary>
        Rename,
        /// <summary>切换可见性。</summary>
        ToggleVisible,
        /// <summary>上移一层。</summary>
        MoveUp,
        /// <summary>下移一层。</summary>
        MoveDown,
        /// <summary>移除图层。</summary>
        Remove,
        /// <summary>导出 / 格式转换。</summary>
        Export,
        /// <summary>生成金字塔。</summary>
        Overviews,
        /// <summary>重投影 / 重采样。</summary>
        Warp,
        /// <summary>复制源文件路径。</summary>
        CopyPath
    }

    /// <summary>右键菜单命令事件参数。</summary>
    public sealed class LayerMenuEventArgs : EventArgs
    {
        /// <summary>命令标识。</summary>
        public LayerMenuCommand Command;
        /// <summary>被右键的图层。</summary>
        public RasterLayer Layer;
    }

    /// <summary>
    /// 图层面板：左侧为可见性复选框 + 缩略图 + 名称/尺寸。
    /// 采用自绘以完全贴合暗色主题（系统 ListView 的勾选框无法着色）。
    /// </summary>
    public class LayerPanelControl : UserControl
    {
        private sealed class Row
        {
            public RasterLayer Layer;
            public Bitmap Thumb;
        }

        private readonly ListView _list;
        private readonly ToolStrip _tools;
        private readonly ContextMenuStrip _menu;
        private ToolStripMenuItem _miVisible, _miUp, _miDown;
        private RasterLayer _menuLayer;
        private bool _syncing;
        private const int PanelRowHeight = 46;
        private static readonly Rectangle CheckRect = new Rectangle(8, 15, 15, 15);

        public LayerStack Layers { get; private set; }

        /// <summary>供自检检查行高与重叠。</summary>
        internal ListView ListForTest { get { return _list; } }

        public event EventHandler SelectedLayerChanged;
        /// <summary>右键菜单命令（面板只负责派发，具体动作由宿主执行）。</summary>
        public event EventHandler<LayerMenuEventArgs> MenuCommand;
        public event EventHandler EditStyleRequested;
        public event EventHandler ZoomToLayerRequested;

        public RasterLayer SelectedLayer
        {
            get
            {
                if (_list.SelectedItems.Count == 0) return null;
                var row = _list.SelectedItems[0].Tag as Row;
                return row != null ? row.Layer : null;
            }
        }

        public LayerPanelControl()
        {
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                CheckBoxes = false,
                OwnerDraw = true,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                Font = Theme.UIFont,
                ShowItemToolTips = true
            };
            // Details 视图的行高由 SmallImageList 的 ImageSize 决定；不设的话行高只有字体高度（约 17px），
            // 而自绘内容有 46px，多图层时下一行会被上一行盖住。
            _list.SmallImageList = new ImageList
            {
                ImageSize = new Size(1, PanelRowHeight),
                ColorDepth = ColorDepth.Depth32Bit
            };
            _list.Columns.Add(Lang.T("图层"), 220);
            _list.DrawSubItem += List_DrawSubItem;
            _list.DrawColumnHeader += (s, e) => { };
            _list.SelectedIndexChanged += (s, e) =>
            {
                var h = SelectedLayerChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            _list.MouseDown += List_MouseDown;
            _list.MouseDoubleClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;   // 右键双击不应跳转
                if (SelectedLayer == null) return;
                var h = EditStyleRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) RemoveSelected();
                else if (e.Control && e.KeyCode == Keys.Up) MoveSelected(true);
                else if (e.Control && e.KeyCode == Keys.Down) MoveSelected(false);
            };
            _list.Resize += (s, e) =>
            {
                if (_list.Columns.Count > 0 && _list.Columns[0].Width != _list.ClientSize.Width - 2)
                    _list.Columns[0].Width = Math.Max(80, _list.ClientSize.Width - 2);
            };

            _tools = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = Theme.CreateToolStripRenderer(),
                BackColor = Theme.Panel,
                Padding = new Padding(4, 1, 4, 1),
                AutoSize = false,
                Height = 27
            };
            AddButton("up", Lang.T("上移图层 (Ctrl+↑)"), (s, e) => MoveSelected(true));
            AddButton("down", Lang.T("下移图层 (Ctrl+↓)"), (s, e) => MoveSelected(false));
            AddButton("remove", Lang.T("移除图层 (Del)"), (s, e) => RemoveSelected());
            _tools.Items.Add(new ToolStripSeparator());
            AddButton("zoom_in", Lang.T("缩放至图层"), (s, e) =>
            {
                if (SelectedLayer == null) return;
                var h = ZoomToLayerRequested;
                if (h != null) h(this, EventArgs.Empty);
            });
            AddButton("histogram", Lang.T("符号化属性"), (s, e) =>
            {
                if (SelectedLayer == null) return;
                var h = EditStyleRequested;
                if (h != null) h(this, EventArgs.Empty);
            });

            _menu = BuildContextMenu();
            _list.ContextMenuStrip = _menu;
            _list.MouseUp += List_MouseUp;

            Controls.Add(_list);
            Controls.Add(_tools);
        }

        /// <summary>供自检检查菜单项数量与启用状态。</summary>
        internal ContextMenuStrip ContextMenuForTest { get { return _menu; } }

        /// <summary>构建图层右键菜单（暗色渲染，分组与主菜单一致）。</summary>
        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = Theme.CreateToolStripRenderer(),
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                Font = Theme.UIFont,
                ShowImageMargin = true
            };

            menu.Items.Add(MenuItem(Lang.T("缩放至图层"), LayerMenuCommand.ZoomTo, "zoom_in"));
            menu.Items.Add(MenuItem(Lang.T("符号化属性…"), LayerMenuCommand.Style, "histogram"));
            menu.Items.Add(MenuItem(Lang.T("元数据"), LayerMenuCommand.Metadata, "layer"));
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(MenuItem(Lang.T("恢复默认符号化"), LayerMenuCommand.ResetStyle, null));
            menu.Items.Add(MenuItem(Lang.T("重命名…"), LayerMenuCommand.Rename, null, Keys.F2));
            menu.Items.Add(new ToolStripSeparator());

            _miVisible = MenuItem(Lang.T("显示 / 隐藏"), LayerMenuCommand.ToggleVisible, null);
            _miVisible.CheckOnClick = false;      // 勾选状态由 SyncMenuState 统一维护
            menu.Items.Add(_miVisible);
            _miUp = MenuItem(Lang.T("上移"), LayerMenuCommand.MoveUp, "up");
            _miDown = MenuItem(Lang.T("下移"), LayerMenuCommand.MoveDown, "down");
            menu.Items.Add(_miUp);
            menu.Items.Add(_miDown);
            menu.Items.Add(MenuItem(Lang.T("移除图层"), LayerMenuCommand.Remove, "remove"));
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(MenuItem(Lang.T("导出…"), LayerMenuCommand.Export, "process"));
            menu.Items.Add(MenuItem(Lang.T("生成金字塔…"), LayerMenuCommand.Overviews, null));
            menu.Items.Add(MenuItem(Lang.T("重投影…"), LayerMenuCommand.Warp, null));
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(MenuItem(Lang.T("复制文件路径"), LayerMenuCommand.CopyPath, null));

            menu.Opening += (s, e) => SyncMenuState(SelectedLayer);
            return menu;
        }

        private ToolStripMenuItem MenuItem(string text, LayerMenuCommand command, string icon, Keys shortcut = Keys.None)
        {
            var item = new ToolStripMenuItem(text) { Tag = command };
            if (!string.IsNullOrEmpty(icon)) item.Image = Icons.Create(icon, 16);
            if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
            item.Click += (s, e) => Dispatch(command);
            return item;
        }

        /// <summary>按当前图层刷新菜单项的勾选与可用状态。</summary>
        /// <param name="layer">被右键的图层，null 表示没有图层。</param>
        private void SyncMenuState(RasterLayer layer)
        {
            _menuLayer = layer;
            bool has = layer != null;

            foreach (ToolStripItem item in _menu.Items)
            {
                var mi = item as ToolStripMenuItem;
                if (mi != null) mi.Enabled = has;
            }

            _miVisible.Checked = has && layer.Visible;
            _miVisible.Text = has && layer.Visible ? Lang.T("隐藏图层") : Lang.T("显示图层");

            int count = Layers != null ? Layers.Count : 0;
            int index = has && Layers != null ? Layers.IndexOf(layer) : -1;
            _miUp.Enabled = has && index >= 0 && index < count - 1;
            _miDown.Enabled = has && index > 0;
        }

        /// <summary>派发右键命令给宿主。</summary>
        /// <param name="command">命令标识。</param>
        private void Dispatch(LayerMenuCommand command)
        {
            var layer = _menuLayer ?? SelectedLayer;
            if (layer == null) return;
            var h = MenuCommand;
            if (h != null) h(this, new LayerMenuEventArgs { Command = command, Layer = layer });
        }

        /// <summary>只重画某一行（例如切换可见性后），避免重建全部缩略图。</summary>
        /// <param name="layer">目标图层。</param>
        internal void InvalidateLayer(RasterLayer layer)
        {
            if (layer == null) return;
            foreach (ListViewItem it in _list.Items)
            {
                var row = it.Tag as Row;
                if (row != null && ReferenceEquals(row.Layer, layer))
                {
                    _list.Invalidate(it.Bounds);
                    return;
                }
            }
        }

        private void List_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = _list.GetItemAt(4, e.Y);
            if (hit == null) return;                 // 空白处不弹菜单

            // 先选中，让宿主侧 CurrentLayer 指向该行
            hit.Selected = true;
            hit.Focused = true;

            var row = hit.Tag as Row;
            SyncMenuState(row != null ? row.Layer : null);
            _menu.Show(_list, e.Location);
        }

        private void AddButton(string icon, string tip, EventHandler onClick)
        {
            var btn = new ToolStripButton
            {
                Image = Icons.Create(icon, 16),
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ToolTipText = tip,
                AutoSize = false,
                Width = 26,
                Height = 23
            };
            btn.Click += onClick;
            _tools.Items.Add(btn);
        }

        public void Bind(LayerStack stack)
        {
            if (Layers != null) Layers.Changed -= OnLayersChanged;
            Layers = stack;
            if (Layers != null) Layers.Changed += OnLayersChanged;
            RefreshList();
        }

        private void OnLayersChanged(object sender, EventArgs e)
        {
            RefreshList();
        }

        public void RefreshList()
        {
            if (Layers == null) return;
            _syncing = true;
            try
            {
                var previous = SelectedLayer;
                _list.BeginUpdate();
                foreach (ListViewItem old in _list.Items)
                {
                    var r = old.Tag as Row;
                    if (r != null && r.Thumb != null) r.Thumb.Dispose();
                }
                _list.Items.Clear();

                var arr = Layers.ToArray();
                // 顶层显示在最上方（与 QGIS 一致）
                for (int i = arr.Length - 1; i >= 0; i--)
                {
                    var layer = arr[i];
                    var row = new Row { Layer = layer };
                    try { row.Thumb = layer.BuildThumbnail(40, 30); } catch { row.Thumb = null; }

                    var item = new ListViewItem(layer.Name)
                    {
                        Tag = row,
                        ToolTipText = string.Format(Lang.T("{0}\n{1} × {2} · {3} 波段 · {4}\n{5}"),
                            layer.SourcePath, layer.Width, layer.Height, layer.DisplayBandCount,
                            layer.TypeText, layer.Style.Describe())
                    };
                    _list.Items.Add(item);
                }
                _list.EndUpdate();

                if (previous != null) SelectLayer(previous);
                else if (_list.Items.Count > 0) _list.Items[0].Selected = true;
            }
            finally
            {
                _syncing = false;
            }
        }

        public void SelectLayer(RasterLayer layer)
        {
            if (layer == null) return;
            foreach (ListViewItem it in _list.Items)
            {
                var row = it.Tag as Row;
                if (row != null && ReferenceEquals(row.Layer, layer))
                {
                    it.Selected = true;
                    it.Focused = true;
                    _list.EnsureVisible(it.Index);
                    break;
                }
            }
        }

        private void List_MouseDown(object sender, MouseEventArgs e)
        {
            var hit = _list.GetItemAt(4, e.Y);
            var row = hit != null ? hit.Tag as Row : null;
            if (hit == null) return;
            // 右键只负责选中，不能顺手切换可见性（否则右键点勾选区会误切换）
            if (e.Button != MouseButtons.Left) { hit.Selected = true; hit.Focused = true; return; }
            if (row == null) return;
            if (_syncing) return;

            var bounds = hit.Bounds;
            var check = new Rectangle(bounds.Left + CheckRect.Left, bounds.Top + CheckRect.Top,
                CheckRect.Width + 4, CheckRect.Height + 4);
            if (check.Contains(e.Location) || e.X < bounds.Left + 26)
            {
                row.Layer.Visible = !row.Layer.Visible;
                _list.Invalidate(bounds);
                if (Layers != null) Layers.NotifyStyleChanged();
                return;
            }
            hit.Selected = true;
        }

        private void List_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            var row = e.Item.Tag as Row;
            if (row == null) return;
            var g = e.Graphics;
            var b = e.Bounds;
            bool selected = e.Item.Selected;

            using (var bg = new SolidBrush(selected ? Theme.Selected : Theme.Panel))
                g.FillRectangle(bg, b);

            // 复选框
            var cb = new Rectangle(b.Left + CheckRect.Left, b.Top + CheckRect.Top, CheckRect.Width, CheckRect.Height);
            using (var pen = new Pen(Theme.TextDim))
                g.DrawRectangle(pen, cb);
            if (row.Layer.Visible)
            {
                using (var brush = new SolidBrush(Theme.Accent))
                    g.FillRectangle(brush, cb.Left + 3, cb.Top + 3, cb.Width - 6, cb.Height - 6);
            }

            // 缩略图
            int thumbX = b.Left + 28;
            var thumbRect = new Rectangle(thumbX, b.Top + (PanelRowHeight - 30) / 2, 40, 30);
            using (var pen = new Pen(Theme.Border))
                g.DrawRectangle(pen, thumbRect);
            if (row.Thumb != null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(row.Thumb, thumbRect);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Default;
            }

            // 名称 + 摘要
            int textX = thumbX + 48;
            var nameRect = new Rectangle(textX, b.Top + 5, b.Width - textX - 6, 18);
            var subRect = new Rectangle(textX, b.Top + 23, b.Width - textX - 6, 16);

            using (var brush = new SolidBrush(selected ? Color.White : Theme.Text))
                g.DrawString(row.Layer.Name, Theme.UIFont, brush, nameRect);

            string sub = string.Format(Lang.T("{0}×{1} · {2}波段 · {3}"),
                row.Layer.Width, row.Layer.Height, row.Layer.DisplayBandCount, row.Layer.Style.Describe());
            // 宽度不足时用末尾省略号，而不是把文字硬裁一半
            using (var small = new Font(Theme.UIFont.FontFamily, 7.6f))
            {
                TextRenderer.DrawText(g, sub, small, subRect, Theme.TextDim,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }

            using (var pen = new Pen(Color.FromArgb(52, 55, 60)))
                g.DrawLine(pen, b.Left + 4, b.Bottom - 1, b.Right - 4, b.Bottom - 1);
        }

        private void MoveSelected(bool up)
        {
            var layer = SelectedLayer;
            if (layer == null || Layers == null) return;
            if (up ? Layers.MoveUp(layer) : Layers.MoveDown(layer))
                Layers.RaiseChanged();
        }

        private void RemoveSelected()
        {
            var layer = SelectedLayer;
            if (layer == null || Layers == null) return;
            Layers.Remove(layer);
        }
    }
}
