using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.Processing;
using SharpGIS.Services;
using SharpGIS.UI;
using SharpGIS.UI.Dialogs;

namespace SharpGIS.SelfTest
{
    /// <summary>
    /// 界面与功能巡检：
    /// 1) 控件越界 / 文本截断 / 同级控件重叠的自动检测（多窗口尺寸、多标签页、全部对话框）；
    /// 2) 交互桩（缩放、平移、历史、可见性、排序、删除、识别、符号化）；
    /// 3) 九个处理对话框的参数接线（反射调用 RunCore，真实产出文件）。
    /// </summary>
    internal static class AuditTests
    {
        private static readonly int[][] WindowSizes =
        {
            new[] { 1440, 900 },
            new[] { 1280, 720 },
            new[] { 1024, 768 }
        };

        public static void Run(TestLog log, string dataDir, string outDir)
        {
            log.Info("--- step=audit ---");
            string tm = Path.Combine(dataDir, "TMtest.tif");
            string multi = Path.Combine(dataDir, "multi.tif");
            string pan = Path.Combine(dataDir, "pan.tif");
            if (!File.Exists(tm)) { log.Fail("TMtest.tif 存在", tm); return; }

            Directory.CreateDirectory(Path.Combine(outDir, "audit"));

            // ---------- 1) 主窗体多尺寸布局巡检 ----------
            MainForm form = new MainForm { AutoAlignCrs = true };
            try
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(0, 0);
                form.AutoScaleMode = AutoScaleMode.None;
                form.Show();
                Pump(2);

                var layer = form.OpenRaster(tm, true);
                if (layer == null) { log.Fail("巡检：打开主影像", "返回 null"); return; }
                Pump(2);
                form.Canvas.RenderNow(Rendering.RenderQuality.Full);
                Pump(1);

                foreach (var size in WindowSizes)
                {
                    form.Size = new Size(size[0], size[1]);
                    Pump(2);
                    string tag = size[0] + "x" + size[1];

                    for (int tab = 0; tab < 4; tab++)
                    {
                        form.SelectTab(tab);
                        Pump(1);
                        var issues = new List<string>();
                        AuditControls(form, "主窗体", form, issues, 0);
                        string shot = Path.Combine(outDir, "audit", "main_" + tag + "_tab" + tab + ".png");
                        SaveForm(form, shot);
                        Report(log, "主窗体 " + tag + " / 标签" + TabName(tab), issues, shot);
                    }
                }

                // 恢复默认尺寸用于后续交互
                form.Size = new Size(1440, 900);
                Pump(2);

                // ---------- 2) 交互功能 ----------
                InteractionChecks(log, form, layer, multi);

                // ---------- 3) 九个处理对话框布局 ----------
                form.Size = new Size(1440, 900);
                Pump(1);
                DialogLayoutChecks(log, form, layer, outDir);
                CaMarkovWiringCheck(log, form, layer, dataDir);

                // ---------- 3.5) 菜单下拉可达性 ----------
                MenuChecks(log, form, outDir);
                LayerMenuChecks(log, form, dataDir);

                // ---------- 4) 对话框 → 算子 的参数接线 ----------
                DialogFunctionalChecks(log, form, layer, multi, pan, outDir);

                // ---------- 4.5) 处理结果回灌到界面（等价于“完成后加载为图层”） ----------
                ResultRoundTripChecks(log, form, outDir);

                // ---------- 4.8) 真实点击“开始执行”的完整链路（OnExecute → JobRunner → 后台线程） ----------
                ExecuteButtonChecks(log, dataDir, outDir);

                // ---------- 5) 后台任务进度框（模态）一次真实跑通 ----------
                JobRunnerCheck(log, form, layer, outDir);
            }
            catch (Exception ex)
            {
                log.Fail("巡检异常", ex.GetType().Name + ": " + ex.Message);
                log.Info(ex.ToString());
            }
            finally
            {
                try { form.Hide(); form.Dispose(); } catch { }
            }
        }

        private static string TabName(int i)
        {
            return i == 0 ? "符号化" : i == 1 ? "直方图" : i == 2 ? "元数据" : "识别";
        }

        // ---------------- 布局检测 ----------------

        /// <summary>递归检测越界、文本截断与同级重叠。</summary>
        private static void AuditControls(Control root, string scope, Control parent, List<string> issues, int depth)
        {
            if (depth > 6) return;
            var siblings = new List<Control>();

            foreach (Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                var b = c.Bounds;

                // 可滚动容器：纵向滚动是预期的，但横向溢出会让标签被裁掉，必须报出来
                bool scrollable = parent is ScrollableControl && ((ScrollableControl)parent).AutoScroll;
                if (scrollable)
                {
                    if (b.Right > parent.ClientSize.Width + 2)
                        issues.Add(string.Format("{0} · {1} “{2}”横向溢出（内容宽 {3} > 可视 {4}），需要横向滚动",
                            scope, c.GetType().Name, SafeText(c), b.Right, parent.ClientSize.Width));
                }
                else if (b.Right > parent.ClientSize.Width + 2 || b.Bottom > parent.ClientSize.Height + 2)
                {
                    issues.Add(string.Format("{0} · {1} “{2}”越界：{3} 超出父容器 {4}",
                        scope, c.GetType().Name, SafeText(c), b, parent.ClientSize));
                }

                if (b.Width <= 1 || b.Height <= 1)
                    issues.Add(string.Format("{0} · {1} “{2}”尺寸为 0", scope, c.GetType().Name, SafeText(c)));

                var lbl = c as Label;
                if (lbl != null && !lbl.AutoSize && !string.IsNullOrEmpty(lbl.Text))
                {
                    var need = TextRenderer.MeasureText(lbl.Text, lbl.Font,
                        new Size(Math.Max(1, lbl.Width), int.MaxValue), TextFormatFlags.WordBreak);
                    if (need.Height > lbl.Height + 2)
                        issues.Add(string.Format("{0} · 标签文本被截断：“{1}” 需 {2}px，实际 {3}px",
                            scope, Shorten(lbl.Text), need.Height, lbl.Height));
                }

                if (c.Dock == DockStyle.None) siblings.Add(c);
                AuditControls(c, scope, c, issues, depth + 1);
            }

            var tabs = parent as TabControl;
            if (tabs != null && tabs.TabCount > 0)
            {
                for (int i = 0; i < tabs.TabCount; i++)
                {
                    var r = tabs.GetTabRect(i);
                    if (r.Right > tabs.ClientSize.Width + 1 || r.Bottom > tabs.ClientSize.Height)
                        issues.Add(string.Format("{0} · 标签页“{1}”被挤出可见区：{2} 超出 {3}",
                            scope, tabs.TabPages[i].Text, r, tabs.ClientSize));
                }
            }

            var strip = parent as StatusStrip;
            if (strip != null)
            {
                int sum = 0;
                foreach (ToolStripItem item in strip.Items) sum += item.Width;
                if (sum > strip.ClientSize.Width + 2)
                    issues.Add(string.Format("{0} · 状态栏项总宽 {1} > 可视宽 {2}，右侧内容会被裁掉",
                        scope, sum, strip.ClientSize.Width));
            }

            for (int i = 0; i < siblings.Count; i++)
            {
                for (int j = i + 1; j < siblings.Count; j++)
                {
                    var inter = Rectangle.Intersect(siblings[i].Bounds, siblings[j].Bounds);
                    if (inter.Width > 6 && inter.Height > 6)
                        issues.Add(string.Format("{0} · 控件重叠：“{1}” 与 “{2}” 交叠 {3}×{4}",
                            scope, SafeText(siblings[i]), SafeText(siblings[j]), inter.Width, inter.Height));
                }
            }
        }

        private static void Report(TestLog log, string name, List<string> issues, string shot)
        {
            if (issues.Count == 0)
            {
                log.Pass(name, "无越界/截断/重叠 → " + Path.GetFileName(shot));
                return;
            }
            log.Fail(name, issues.Count + " 处问题：" + string.Join(" | ", issues.Take(4)));
            foreach (var extra in issues.Skip(4)) log.Info("      · " + extra);
        }

        private static string SafeText(Control c)
        {
            try
            {
                var t = c.Text;
                if (string.IsNullOrEmpty(t)) return c.Name ?? "";
                return Shorten(t);
            }
            catch { return c.Name ?? ""; }
        }

        private static string Shorten(string s)
        {
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 24 ? s : s.Substring(0, 24) + "…";
        }

        private static void SaveForm(Form f, string path)
        {
            try
            {
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(path, ImageFormat.Png);
                }
            }
            catch { }
        }

        // ---------------- 交互功能 ----------------

        private static void InteractionChecks(TestLog log, MainForm form, RasterLayer layer, string multiPath)
        {
            var canvas = form.Canvas;

            // 缩放：全图 → 1:1 → 放大/缩小 → 历史
            canvas.ZoomToFull(); Pump(1);
            double muppFull = canvas.View.Mupp;
            canvas.ZoomToPixelSize(); Pump(1);
            double mupp11 = canvas.View.Mupp;
            log.Check(Math.Abs(mupp11 - layer.PixelSizeX) < 1e-6, "1:1 缩放对齐像元尺寸",
                mupp11.ToString("G6"));

            canvas.ZoomBy(2.0); Pump(1);
            double muppZoom = canvas.View.Mupp;
            canvas.GoBack(); Pump(1);
            bool backOk = Math.Abs(canvas.View.Mupp - mupp11) < 1e-9;
            canvas.GoForward(); Pump(1);
            bool fwdOk = Math.Abs(canvas.View.Mupp - muppZoom) < 1e-9;
            log.Check(backOk && fwdOk, "视图前进/后退", string.Format("back={0} fwd={1}", backOk, fwdOk));

            // 平移
            var before = canvas.View.CenterX;
            canvas.View.PanByPixels(60, 0);
            canvas.RenderNow(Rendering.RenderQuality.Fast);
            log.Check(Math.Abs(canvas.View.CenterX - before) > 0, "平移改变视图中心",
                (canvas.View.CenterX - before).ToString("G4"));

            // 可见性开关
            layer.Visible = false;
            form.Stack.NotifyStyleChanged();
            canvas.RenderNow(Rendering.RenderQuality.Fast);
            bool hidden = canvas.LastStats != null && canvas.LastStats.LayersDrawn == 0;
            layer.Visible = true;
            form.Stack.NotifyStyleChanged();
            canvas.RenderNow(Rendering.RenderQuality.Fast);
            bool shown = canvas.LastStats != null && canvas.LastStats.LayersDrawn == 1;
            log.Check(hidden && shown, "图层可见性开关影响渲染", string.Format("隐藏={0} 显示={1}", hidden, shown));

            // 排序与删除（第二个图层）
            if (File.Exists(multiPath))
            {
                var second = form.OpenRaster(multiPath, false);
                Pump(1);
                if (second != null)
                {
                    int beforeCount = form.Stack.Count;
                    // 新图层默认在顶层：先下移到底，再上移回顶
                    bool movedDown = form.Stack.MoveDown(second);
                    var orderDown = form.Stack.ToArray();
                    bool nowBottom = orderDown.Length >= 2 && ReferenceEquals(orderDown[0], second);
                    bool movedUp = form.Stack.MoveUp(second);
                    var orderUp = form.Stack.ToArray();
                    bool nowTop = orderUp.Length >= 2 && ReferenceEquals(orderUp[orderUp.Length - 1], second);
                    log.Check(movedDown && nowBottom && movedUp && nowTop, "图层上移/下移改变叠加顺序",
                        string.Format("下移={0}(置底 {1}) 上移={2}(置顶 {3})", movedDown, nowBottom, movedUp, nowTop));

                    form.Stack.Remove(second);
                    Pump(1);
                    log.Check(form.Stack.Count == beforeCount - 1, "移除图层", form.Stack.Count.ToString());
                }
            }

            // 识别
            bool identifyFired = false;
            int bandCount = 0;
            EventHandler<IdentifyEventArgs> handler = (s, e) =>
            {
                identifyFired = true;
                bandCount = e.Values != null ? e.Values.Length : 0;
            };
            canvas.IdentifyRequested += handler;
            canvas.DoIdentifyForTest(layer, layer.Width / 2, layer.Height / 2);
            canvas.IdentifyRequested -= handler;
            log.Check(identifyFired && bandCount == layer.BandCount, "识别工具返回全部波段值",
                bandCount + " 个波段");

            // 符号化：同一视图下两种设置的结果必须明显不同
            canvas.ZoomToLayer(layer);
            Pump(1);
            var style = layer.Style;
            style.Mode = RenderMode.Gray;
            style.GrayBand = 1;
            style.Ramp = ColorRamp.Gray;
            style.Opacity = 1.0;
            style.Blend = BlendMode.Normal;
            style.Gamma = 1.0;
            layer.Touch();
            form.Stack.NotifyStyleChanged();

            string auditDir = Path.GetDirectoryName(log.LogPath);
            using (var bmpGray = canvas.RenderToBitmap(700, 560))
            {
                bmpGray.Save(Path.Combine(auditDir, "audit_symbology_gray.png"), ImageFormat.Png);

                style.Ramp = ColorRamp.Turbo;
                style.Gamma = 1.3;
                style.Opacity = 0.7;
                style.Blend = BlendMode.Multiply;
                layer.Touch();
                form.Stack.NotifyStyleChanged();

                using (var bmpColor = canvas.RenderToBitmap(700, 560))
                {
                    bmpColor.Save(Path.Combine(auditDir, "audit_symbology_turbo.png"), ImageFormat.Png);
                    double diff = MeanAbsDiff(bmpGray, bmpColor);
                    log.Check(diff > 8, "符号化改动立即反映到画面",
                        "灰度→伪彩+gamma+混合，平均色差 " + diff.ToString("F1"));
                }
            }

            // 复原默认样式，避免影响后续截图
            layer.Style = layer.CreateDefaultStyle();
            layer.Touch();
            form.Stack.NotifyStyleChanged();
            Pump(1);
        }

        // ---------------- 对话框布局 ----------------

        private static void CaMarkovWiringCheck(TestLog log, MainForm form, RasterLayer layer, string dataDir)
        {
            string tm = Path.Combine(dataDir, "TMtest.tif");
            var dialog = new CaMarkovDialog(layer, form.Stack);
            try
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(0, 0);
                dialog.Show();
                Pump(1);

                var periods = GetField<System.Collections.IList>(dialog, "_periods");
                if (periods == null) { log.Fail("CA-Markov 参数接线", "找不到时期列表字段"); return; }
                periods.Clear();
                periods.Add(new CaPeriod { Path = tm, Band = 1, Year = 2010 });
                periods.Add(new CaPeriod { Path = tm, Band = 1, Year = 2015 });

                var refresh = dialog.GetType().GetMethod("RefreshPeriodList",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (refresh != null) refresh.Invoke(dialog, null);
                Pump(1);

                SetNum(dialog, "_steps", 4);
                SetCombo(dialog, "_transition", 2);     // 全部时期累计
                SetCombo(dialog, "_neighborhood", 2);   // 7 × 7
                SetCombo(dialog, "_mode", 1);           // 因子加权
                SetNumDec(dialog, "_suitWeight", 0.3m);
                SetNumDec(dialog, "_neighWeight", 0.7m);

                // 类别表格：真实统计一遍（TMtest 1.7M 像元），再改第一行名称
                var counter = dialog.GetType().GetMethod("CountClassesIntoGrid",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (counter == null) { log.Fail("CA-Markov 类别统计", "找不到 CountClassesIntoGrid"); return; }
                counter.Invoke(dialog, null);
                Pump(1);

                var guess = typeof(CaMarkovDialog).GetMethod("GuessYear",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (guess != null)
                {
                    int y1 = (int)guess.Invoke(null, new object[] { "CLCD_2019_aligned" });
                    int y2 = (int)guess.Invoke(null, new object[] { "data20240501" });
                    int y3 = (int)guess.Invoke(null, new object[] { "LULC_aligned" });
                    log.Check(y1 == 2019 && y2 == 2024 && y3 == 0, "CA-Markov 年份自动识别",
                        string.Format("CLCD_2019→{0}，data20240501→{1}，无年份→{2}", y1, y2, y3));
                }

                var grid = GetField<DataGridView>(dialog, "_classGrid");
                int rows = grid == null ? 0 : grid.Rows.Count;
                log.Check(grid != null && rows > 0, "CA-Markov 类别统计（按最后一期逐像元）",
                    rows + " 行");
                int renamedValue = -1;
                if (grid != null && rows > 0)
                {
                    renamedValue = Convert.ToInt32(grid.Rows[0].Cells[0].Value);
                    grid.Rows[0].Cells[2].Value = "测试类A";
                }

                var capture = dialog.GetType().GetMethod("CaptureParameters",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (capture == null) { log.Fail("CA-Markov 参数接线", "找不到 CaptureParameters"); return; }
                capture.Invoke(dialog, null);

                var snap = GetField<CaMarkovParams>(dialog, "_snap");
                if (snap == null) { log.Fail("CA-Markov 参数接线", "CaptureParameters 没有生成快照"); return; }

                bool nameOk = renamedValue >= 0
                    && snap.ClassNames.Length > renamedValue
                    && snap.ClassNames[renamedValue] == "测试类A";
                log.Check(nameOk, "CA-Markov 类别名称写入参数",
                    renamedValue >= 0 ? ("值 " + renamedValue + " → " + snap.ClassNames[renamedValue]) : "无行");

                bool ok = snap.Periods.Count == 2
                    && snap.Steps == 4
                    && snap.TransitionSource == CaTransitionSource.AllPeriods
                    && snap.Suitability == CaSuitabilityMode.FactorWeighted
                    && snap.NeighborhoodSize == 7
                    && Math.Abs(snap.SuitabilityWeight - 0.3) < 1e-9
                    && Math.Abs(snap.NeighborhoodWeight - 0.7) < 1e-9;
                log.Check(ok, "CA-Markov 对话框参数接线",
                    string.Format("期数{0} 步数{1} 来源{2} 模式{3} 邻域{4} 权重{5}/{6}",
                        snap.Periods.Count, snap.Steps, snap.TransitionSource,
                        snap.Suitability, snap.NeighborhoodSize,
                        snap.SuitabilityWeight, snap.NeighborhoodWeight));
            }
            catch (Exception ex)
            {
                log.Fail("CA-Markov 参数接线", ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { dialog.Hide(); dialog.Dispose(); } catch { }
            }
        }

        private static void SetNumDec(object target, string field, decimal value)
        {
            var c = GetField<NumericUpDown>(target, field);
            if (c != null) c.Value = Math.Max(c.Minimum, Math.Min(c.Maximum, value));
        }

        private static void DialogLayoutChecks(TestLog log, MainForm form, RasterLayer layer, string outDir)
        {
            var dialogs = BuildDialogs(form, layer);
            foreach (var entry in dialogs)
            {
                var dlg = entry.Item2;
                string name = entry.Item1;
                try
                {
                    dlg.StartPosition = FormStartPosition.Manual;
                    dlg.Location = new Point(30, 30);
                    dlg.Show();
                    Pump(1);

                    var issues = new List<string>();
                    AuditControls(dlg, name, dlg, issues, 0);

                    // 对话框不应超过 768 高的小屏可用区
                    if (dlg.Height > 720)
                        issues.Add(name + " · 对话框高 " + dlg.Height + "px，超出 768 屏可用高度");

                    string shot = Path.Combine(outDir, "audit", "dlg_" + name + ".png");
                    SaveForm(dlg, shot);
                    Report(log, "对话框布局 " + name, issues, shot);
                }
                catch (Exception ex)
                {
                    log.Fail("对话框布局 " + name, ex.Message);
                }
                finally
                {
                    try { dlg.Hide(); dlg.Dispose(); } catch { }
                }
            }
        }

        private static List<Tuple<string, Form>> BuildDialogs(MainForm form, RasterLayer layer)
        {
            var stack = form.Stack;
            return new List<Tuple<string, Form>>
            {
                Tuple.Create<string, Form>("ExportDialog", new ExportDialog(layer)),
                Tuple.Create<string, Form>("WarpDialog_重投影", new WarpDialog(layer, layer.Extent, layer.PixelSizeX, 0, stack)),
                Tuple.Create<string, Form>("WarpDialog_裁剪", new WarpDialog(layer, layer.Extent, layer.PixelSizeX, 2, stack)),
                Tuple.Create<string, Form>("WarpDialog_仅复制", new WarpDialog(layer, layer.Extent, layer.PixelSizeX, 4, stack)),
                Tuple.Create<string, Form>("OverviewDialog", new OverviewDialog(layer)),
                Tuple.Create<string, Form>("BandCalcDialog", new BandCalcDialog(layer)),
                Tuple.Create<string, Form>("DemDialog", new DemDialog(layer)),
                Tuple.Create<string, Form>("FilterDialog", new FilterDialog(layer)),
                Tuple.Create<string, Form>("EnhanceDialog", new EnhanceDialog(layer)),
                Tuple.Create<string, Form>("KMeansDialog", new KMeansDialog(layer)),
                Tuple.Create<string, Form>("FusionDialog", new FusionDialog(layer, stack)),
                Tuple.Create<string, Form>("CaMarkovDialog", new CaMarkovDialog(layer, stack)),
                Tuple.Create<string, Form>("CaMarkovResultDialog",
                    new CaMarkovResultDialog(CaMarkovResultDialog.Sample(), "结果图表")),
                Tuple.Create<string, Form>("ProgressForm", new ProgressForm("进度框布局检查"))
            };
        }

        // ---------------- 对话框 → 算子 接线 ----------------

        /// <summary>展开每个顶级菜单，检查下拉是否超出屏幕，并截屏留证。</summary>
        private static void MenuChecks(TestLog log, MainForm form, string outDir)
        {
            if (form.MainMenuStrip == null) { log.Fail("菜单巡检", "找不到主菜单"); return; }
            int workHeight = Screen.PrimaryScreen.WorkingArea.Height;

            foreach (ToolStripItem item in form.MainMenuStrip.Items)
            {
                var top = item as ToolStripMenuItem;
                if (top == null) continue;
                try
                {
                    top.ShowDropDown();
                    Pump(1);
                    var dd = top.DropDown;

                    int totalHeight = 0, widest = 0;
                    foreach (ToolStripItem child in dd.Items)
                    {
                        totalHeight += child.Height;
                        widest = Math.Max(widest, child.GetPreferredSize(Size.Empty).Width);
                    }

                    bool fits = dd.Height <= workHeight - 40 && widest <= dd.Width;
                    log.Check(fits, "菜单下拉 " + top.Text.Trim('&'),
                        string.Format("{0} 项 · 高 {1}px · 最宽项 {2}px / 下拉宽 {3}px",
                            dd.Items.Count, dd.Height, widest, dd.Width));

                    // 整窗截屏（展开的菜单是独立窗口，DrawToBitmap 抓不到，只能抓屏）
                    string shot = Path.Combine(outDir, "audit", "menu_" + top.Text.Trim('&') + ".png");
                    try
                    {
                        var screenRect = form.RectangleToScreen(form.ClientRectangle);
                        using (var bmp = new Bitmap(screenRect.Width, screenRect.Height))
                        {
                            using (var g = Graphics.FromImage(bmp))
                                g.CopyFromScreen(screenRect.Location, Point.Empty, screenRect.Size);
                            bmp.Save(shot, ImageFormat.Png);
                        }
                    }
                    catch { }

                    top.HideDropDown();
                    Pump(1);
                }
                catch (Exception ex)
                {
                    log.Fail("菜单下拉 " + top.Text, ex.Message);
                }
            }
        }

        /// <summary>图层右键菜单：项数、启用矩阵、勾选状态，以及右键不误切可见性。</summary>
        private static void LayerMenuChecks(TestLog log, MainForm form, string dataDir)
        {
            var panel = form.LayerPanel;
            var menu = panel.ContextMenuForTest;
            if (menu == null) { log.Fail("右键菜单存在", "null"); return; }

            int items = 0, separators = 0;
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripSeparator) separators++;
                else if (item is ToolStripMenuItem) items++;
            }
            log.Check(items == 13 && separators == 4, "右键菜单 13 项 + 4 分隔线",
                items + " 项 / " + separators + " 分隔线");

            // 启用矩阵需要两层，这里临时补一层（结束时移除）
            RasterLayer temp = null;
            if (form.Stack.Count < 2)
            {
                string extra = Path.Combine(dataDir, "multi.tif");
                if (File.Exists(extra)) temp = form.OpenRaster(extra, false);
            }

            var layers = form.Stack.ToArray();
            if (layers.Length < 2)
            {
                log.Fail("右键菜单启用矩阵", "图层不足 2 个");
                if (temp != null) form.Stack.Remove(temp);
                return;
            }

            var sync = typeof(LayerPanelControl).GetMethod("SyncMenuState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (sync == null) { log.Fail("右键菜单启用矩阵", "找不到 SyncMenuState"); return; }

            // 底层图层：应可上移、不可下移
            sync.Invoke(panel, new object[] { layers[0] });
            bool bottomOk = IsEnabled(menu, "上移") && !IsEnabled(menu, "下移");
            log.Check(bottomOk, "底层图层：上移可用、下移禁用",
                "上移=" + IsEnabled(menu, "上移") + " 下移=" + IsEnabled(menu, "下移"));

            // 顶层图层：应可下移、不可上移
            sync.Invoke(panel, new object[] { layers[layers.Length - 1] });
            bool topOk = !IsEnabled(menu, "上移") && IsEnabled(menu, "下移");
            log.Check(topOk, "顶层图层：下移可用、上移禁用",
                "上移=" + IsEnabled(menu, "上移") + " 下移=" + IsEnabled(menu, "下移"));

            // 勾选状态跟随可见性
            var probe = layers[0];
            bool visible = probe.Visible;
            probe.Visible = false;
            sync.Invoke(panel, new object[] { probe });
            bool uncheckedOk = !IsChecked(menu, "显示图层") && !IsChecked(menu, "隐藏图层");
            probe.Visible = true;
            sync.Invoke(panel, new object[] { probe });
            bool checkedOk = IsChecked(menu, "隐藏图层");
            probe.Visible = visible;
            sync.Invoke(panel, new object[] { probe });
            log.Check(uncheckedOk && checkedOk, "显示/隐藏项勾选状态跟随图层",
                "隐藏时未勾选=" + uncheckedOk + " 显示时勾选=" + checkedOk);

            // 无图层时全部禁用
            sync.Invoke(panel, new object[] { null });
            bool allDisabled = true;
            foreach (ToolStripItem item in menu.Items)
            {
                var mi = item as ToolStripMenuItem;
                if (mi != null && mi.Enabled) allDisabled = false;
            }
            log.Check(allDisabled, "无图层时菜单项全部禁用", allDisabled ? "ok" : "仍有可用项");

            // 右键点勾选区不得切换可见性
            var list = panel.ListForTest;
            if (list.Items.Count == 0) { log.Fail("右键不误切可见性", "列表为空"); return; }
            var row = list.Items[0].Tag;
            var rowLayer = row.GetType().GetField("Layer").GetValue(row) as RasterLayer;
            bool before = rowLayer.Visible;
            var bounds = list.Items[0].Bounds;
            var mouseDown = typeof(LayerPanelControl).GetMethod("List_MouseDown",
                BindingFlags.Instance | BindingFlags.NonPublic);
            mouseDown.Invoke(panel, new object[]
            {
                list, new MouseEventArgs(MouseButtons.Right, 1, bounds.Left + 12, bounds.Top + 20, 0)
            });
            log.Check(rowLayer.Visible == before, "右键点击勾选区不改变可见性",
                before == rowLayer.Visible ? "可见性未变" : "被误切换");
            sync.Invoke(panel, new object[] { form.Stack.TopVisible() });

            // 弹一次菜单并截图（菜单是独立窗口，只能抓屏；用整个窗体区域避免 DPI 缩放带来的偏移）
            try
            {
                sync.Invoke(panel, new object[] { form.Stack.TopVisible() });
                var itemBounds = list.Items[0].Bounds;
                Point screen = list.PointToScreen(new Point(itemBounds.Left + 60, itemBounds.Top + 12));
                menu.Show(screen);
                // 先记下弹出状态：后续 Pump 可能因为焦点变化让菜单自动关闭
                bool shown = menu.Visible;
                Pump(2);

                var screenRect = form.RectangleToScreen(form.ClientRectangle);
                using (var bmp = new Bitmap(screenRect.Width, screenRect.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(screenRect.Location, Point.Empty, screenRect.Size);
                    bmp.Save(Path.Combine(Path.GetDirectoryName(log.LogPath), "menu_layer_context.png"),
                        ImageFormat.Png);
                }
                log.Check(shown && menu.Width > 100, "右键菜单可弹出", menu.Width + "×" + menu.Height);
            }
            catch (Exception ex)
            {
                log.Fail("右键菜单截图", ex.Message);
            }
            finally
            {
                menu.Close();
            }

            if (temp != null) form.Stack.Remove(temp);
        }

        private static bool IsEnabled(ContextMenuStrip menu, string text)
        {
            foreach (ToolStripItem item in menu.Items)
            {
                var mi = item as ToolStripMenuItem;
                if (mi != null && mi.Text == text) return mi.Enabled;
            }
            return false;
        }

        private static bool IsChecked(ContextMenuStrip menu, string text)
        {
            foreach (ToolStripItem item in menu.Items)
            {
                var mi = item as ToolStripMenuItem;
                if (mi != null && mi.Text == text) return mi.Checked;
            }
            return false;
        }

        private static void DialogFunctionalChecks(TestLog log, MainForm form, RasterLayer layer,
            string multiPath, string panPath, string outDir)
        {
            log.Info("--- 对话框参数接线 ---");
            string funcDir = Path.Combine(outDir, "audit", "func");
            Directory.CreateDirectory(funcDir);

            // 1) 导出
            var export = new ExportDialog(layer);
            CheckRun(log, export, Path.Combine(funcDir, "d_export.tif"), "对话框：格式导出", 8);

            // 2) 重投影（模式 0，目标 EPSG:4326）
            var warp = new WarpDialog(layer, layer.Extent, layer.PixelSizeX, 0, form.Stack);
            SetText(warp, "_dstSrs", "EPSG:4326");
            CheckRun(log, warp, Path.Combine(funcDir, "d_warp4326.tif"), "对话框：重投影", -1);

            // 3) 裁剪（模式 2）
            var crop = new WarpDialog(layer, layer.Extent, layer.PixelSizeX, 2, form.Stack);
            CheckRun(log, crop, Path.Combine(funcDir, "d_crop.tif"), "对话框：按范围裁剪", -1);

            // 4) 按行列数（-ts）
            var resize = new WarpDialog(layer, layer.Extent, layer.PixelSizeX, 1, form.Stack);
            SetCheck(resize, "_useSize", true);
            SetNum(resize, "_cols", 640);
            SetNum(resize, "_rows", 480);
            CheckRun(log, resize, Path.Combine(funcDir, "d_size640x480.tif"), "对话框：按行列数", -1, 640, 480);

            // 5) 波段子集（-b）
            var subset = new WarpDialog(layer, layer.Extent, layer.PixelSizeX, 1, form.Stack);
            SetText(subset, "_bands", "3");
            CheckRun(log, subset, Path.Combine(funcDir, "d_band3.tif"), "对话框：波段子集", 1);

            // 6) 金字塔
            CheckRun(log, new OverviewDialog(layer), Path.Combine(funcDir, "d_overview.tif"), "对话框：金字塔", 8);

            // 7) 波段计算器
            var calc = new BandCalcDialog(layer);
            SetText(calc, "_expr", "normalizedDifference(b4, b3)");
            CheckRun(log, calc, Path.Combine(funcDir, "d_bandcalc.tif"), "对话框：波段计算", 1);

            // 8) DEM 山体阴影
            CheckRun(log, new DemDialog(layer), Path.Combine(funcDir, "d_dem.tif"), "对话框：DEM 山体阴影", 1);

            // 9) 滤波均值
            CheckRun(log, new FilterDialog(layer), Path.Combine(funcDir, "d_filter.tif"), "对话框：滤波", 1);

            // 10) 增强
            CheckRun(log, new EnhanceDialog(layer), Path.Combine(funcDir, "d_enhance.tif"), "对话框：增强", -1);

            // 11) KMeans
            var km = new KMeansDialog(layer);
            SetNum(km, "_k", 4);
            CheckRun(log, km, Path.Combine(funcDir, "d_kmeans.tif"), "对话框：KMeans 分类", 1);

            // 12) 融合（需要两个图层）
            if (File.Exists(multiPath) && File.Exists(panPath))
            {
                using (var ms = DatasetService.Open(multiPath))
                using (var pn = DatasetService.Open(panPath))
                {
                    var stack = new LayerStack();
                    stack.Add(ms);
                    stack.Add(pn);
                    var fusion = new FusionDialog(ms, stack);
                    SetCombo(fusion, "_msLayer", 0);
                    SetCombo(fusion, "_panLayer", 1);
                    SetCombo(fusion, "_panBand", 0);
                    string outPath = Path.Combine(funcDir, "d_fusion.tif");
                    string rt = InvokeRunCore(fusion, outPath);
                    bool ok = File.Exists(outPath) && rt == outPath;
                    if (ok)
                    {
                        using (var r = DatasetService.Open(outPath))
                            log.Check(r.BandCount == 3 && r.Width == pn.Width, "对话框：影像融合",
                                r.Width + "×" + r.Height + " · " + r.BandCount + " 波段");
                    }
                    else log.Fail("对话框：影像融合", "未产出文件");
                    fusion.Dispose();
                }
            }
        }

        private static void CheckRun(TestLog log, ProcessingDialogBase dlg, string outPath, string name,
            int expectBands, int expectW = 0, int expectH = 0)
        {
            try
            {
                string result = InvokeRunCore(dlg, outPath);
                if (!File.Exists(outPath)) { log.Fail(name, "未生成输出文件"); return; }

                using (var r = DatasetService.Open(outPath))
                {
                    bool sizeOk = expectW <= 0 || (r.Width == expectW && r.Height == expectH);
                    bool bandOk = expectBands < 0 || r.BandCount == expectBands || r.DisplayBandCount == expectBands;
                    log.Check(result == outPath && bandOk && sizeOk, name,
                        string.Format("{0}×{1} · {2} 波段（有效 {3}）", r.Width, r.Height, r.BandCount, r.DisplayBandCount));
                }
            }
            catch (Exception ex)
            {
                log.Fail(name, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { dlg.Dispose(); } catch { }
            }
        }

        /// <summary>把处理产物重新加载为图层并渲染，验证“处理完自动加载”这条链路。</summary>
        private static void ResultRoundTripChecks(TestLog log, MainForm form, string outDir)
        {
            string[] candidates =
            {
                Path.Combine(outDir, "audit", "func", "d_kmeans.tif"),
                Path.Combine(outDir, "audit", "func", "d_bandcalc.tif"),
                Path.Combine(outDir, "audit", "func", "d_dem.tif")
            };
            string[] names = { "分类结果（Byte+调色板）", "波段计算（Float32 单波段）", "DEM（Byte 单波段）" };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (!File.Exists(candidates[i])) { log.Fail("结果回灌：" + names[i], "缺少产物"); continue; }
                var loaded = form.OpenRaster(candidates[i], true);
                if (loaded == null) { log.Fail("结果回灌：" + names[i], "加载失败"); continue; }
                Pump(1);
                form.Canvas.RenderNow(Rendering.RenderQuality.Full);
                using (var bmp = form.Canvas.RenderToBitmap(500, 400))
                {
                    double sd = RenderTests.StdDev(bmp);
                    log.Check(sd > 1.0, "结果回灌并渲染：" + names[i],
                        string.Format("{0}×{1} · {2} 波段 · stdev={3:F1}", loaded.Width, loaded.Height,
                            loaded.DisplayBandCount, sd));
                }
                form.Stack.Remove(loaded);
                Pump(1);
            }
        }

        /// <summary>
        /// 走真实按钮路径：构造导出对话框、设置输出、调用私有的 OnExecute。
        /// 这条链路包含参数校验、进度框、后台线程与结果回填，是用户实际点击的路径。
        /// 用 400×400 的 multi.tif 保证速度。
        /// </summary>
        private static void ExecuteButtonChecks(TestLog log, string dataDir, string outDir)
        {
            string src = Path.Combine(dataDir, "multi.tif");
            string dst = Path.Combine(outDir, "audit", "func2", "onexecute_export.tif");
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            ProcessingUtil.DeleteOutputSet(dst);
            if (!File.Exists(src)) { log.Fail("真实执行路径：导出", "缺少 multi.tif"); return; }

            ExportDialog dlg = null;
            try
            {
                using (var layer = DatasetService.Open(src))
                {
                    dlg = new ExportDialog(layer);
                    dlg.StartPosition = FormStartPosition.Manual;
                    dlg.Location = new Point(60, 60);
                    dlg.Show();
                    Pump(2);
                    SetText(dlg, "_output", dst);

                    var method = typeof(ProcessingDialogBase).GetMethod("OnExecute",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (method == null) { log.Fail("真实执行路径：导出", "找不到 OnExecute"); return; }

                    // 前置哨兵：先在后台线程跑一次 RunCore。若算子偷偷访问控件会在这里就失败，
                    // 避免随后的 OnExecute 弹出模态错误框把自动化卡死。
                    try { InvokeRunCore(dlg, dst); }
                    catch (Exception canary)
                    {
                        log.Fail("真实执行路径：导出（线程安全前置检查）", canary.Message);
                        return;
                    }
                    ProcessingUtil.DeleteOutputSet(dst);

                    var started = DateTime.UtcNow;
                    method.Invoke(dlg, null);
                    double ms = (DateTime.UtcNow - started).TotalMilliseconds;

                    bool produced = File.Exists(dst);
                    log.Check(produced, "真实执行路径：导出（OnExecute → 后台线程）",
                        produced ? string.Format("{0:F0}ms · {1}", ms, Path.GetFileName(dst)) : "未产出文件");
                }
            }
            catch (Exception ex)
            {
                var inner = ex is System.Reflection.TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException : ex;
                log.Fail("真实执行路径：导出", inner.GetType().Name + ": " + inner.Message);
            }
            finally
            {
                if (dlg != null) { try { dlg.Hide(); dlg.Dispose(); } catch { } }
            }
        }

        // ---------------- 反射工具 ----------------

        /// <summary>
        /// 按真实执行模型调用 RunCore：先在调用线程（UI）执行 CaptureParameters，
        /// 再放到后台线程执行 RunCore，并打开跨线程检查。这样只要算子偷偷读了控件就会抛出
        /// “线程间操作无效”，把这类只在调试运行时才暴露的 bug 变成自检失败。
        /// </summary>
        private static string InvokeRunCore(ProcessingDialogBase dlg, string outPath)
        {
            var runCore = dlg.GetType().GetMethod("RunCore",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (runCore == null) throw new InvalidOperationException("找不到 RunCore");

            var capture = dlg.GetType().GetMethod("CaptureParameters",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (capture == null) throw new InvalidOperationException("找不到 CaptureParameters");

            Control.CheckForIllegalCrossThreadCalls = true;
            if (!dlg.IsHandleCreated) { var handle = dlg.Handle; }   // 让 InvokeRequired 能生效

            capture.Invoke(dlg, null);                                // UI 线程采集参数

            string result = null;
            Exception failure = null;
            var reporter = new ProgressReporter((p, m) => { }, () => false, 0.0);
            var thread = new System.Threading.Thread(() =>
            {
                try { result = (string)runCore.Invoke(dlg, new object[] { reporter, outPath }); }
                catch (System.Reflection.TargetInvocationException ex) { failure = ex.InnerException ?? ex; }
                catch (Exception ex) { failure = ex; }
            });
            thread.IsBackground = true;
            thread.Start();
            if (!thread.Join(TimeSpan.FromMinutes(5)))
                throw new TimeoutException("后台执行超时");

            if (failure != null)
            {
                string message = failure.Message ?? "";
                if (message.Contains("线程间操作无效") || message.Contains("cross-thread"))
                    throw new InvalidOperationException("算子在线程安全上犯规（后台线程访问了控件）：" + message, failure);
                throw failure;
            }
            return result;
        }

        private static void SetText(object target, string field, string value)
        {
            var c = GetField<TextBox>(target, field);
            if (c != null) c.Text = value;
        }

        private static void SetCheck(object target, string field, bool value)
        {
            var c = GetField<CheckBox>(target, field);
            if (c != null) c.Checked = value;
        }

        private static void SetNum(object target, string field, int value)
        {
            var c = GetField<NumericUpDown>(target, field);
            if (c != null) c.Value = Math.Max(c.Minimum, Math.Min(c.Maximum, value));
        }

        private static void SetCombo(object target, string field, int index)
        {
            var c = GetField<ComboBox>(target, field);
            if (c != null && index >= 0 && index < c.Items.Count) c.SelectedIndex = index;
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            // 私有字段可能声明在基类（如 ProcessingDialogBase._output），必须逐级向上找
            var type = target.GetType();
            FieldInfo f = null;
            while (type != null && f == null)
            {
                f = type.GetField(name, flags);
                type = type.BaseType;
            }
            if (f == null) return null;
            return f.GetValue(target) as T;
        }

        // ---------------- JobRunner 端到端 ----------------

        private static void JobRunnerCheck(TestLog log, MainForm form, RasterLayer layer, string outDir)
        {
            string path = Path.Combine(outDir, "audit", "jobrunner_export.tif");
            var job = JobRunner.Run(form, "自检：导出（进度框）", reporter =>
            {
                reporter.Report(0.1, "准备…");
                return RasterExportService.Run(layer, new ExportOptions { OutputPath = path }, reporter);
            });

            log.Check(job.Ok && File.Exists(path), "后台任务 + 进度框端到端",
                job.Outcome + " → " + Path.GetFileName(path));
        }

        /// <summary>抽样计算两张位图的平均绝对色差（0~255）。</summary>
        private static double MeanAbsDiff(Bitmap a, Bitmap b)
        {
            int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
            int stepX = Math.Max(1, w / 80), stepY = Math.Max(1, h / 80);
            double sum = 0; int n = 0;
            for (int y = 0; y < h; y += stepY)
                for (int x = 0; x < w; x += stepX)
                {
                    var ca = a.GetPixel(x, y);
                    var cb = b.GetPixel(x, y);
                    sum += (Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B)) / 3.0;
                    n++;
                }
            return n == 0 ? 0 : sum / n;
        }

        private static void Pump(int rounds)
        {
            for (int i = 0; i < rounds; i++)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(90);
            }
        }
    }
}
