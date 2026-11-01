using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.UI;

namespace SharpGIS.SelfTest
{
    /// <summary>界面自检：真实创建主窗体、加载影像、截图，用于目视核对 UI 与渲染效果。</summary>
    internal static class UiTests
    {
        public static void Run(TestLog log, string dataDir, string outDir)
        {
            log.Info("--- step=ui ---");
            string tm = Path.Combine(dataDir, "TMtest.tif");
            if (!File.Exists(tm)) { log.Fail("TMtest.tif 存在", tm); return; }

            MainForm form = null;
            try
            {
                form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(0, 0);
                form.AutoScaleMode = AutoScaleMode.None;
                form.Size = new Size(1440, 900);
                form.AutoAlignCrs = true;
                form.Show();
                Pump(3);

                var layer = form.OpenRaster(tm, true);
                log.Check(layer != null, "界面中打开影像", layer != null ? layer.Name : "null");
                if (layer == null) return;

                Pump(3);
                form.Canvas.RenderNow(Rendering.RenderQuality.Full);
                Pump(2);

                foreach (Control c in form.Controls)
                    log.Info(string.Format("   控件 {0}: Dock={1} Bounds={2}", c.GetType().Name, c.Dock, c.Bounds));
                if (form.MainMenuStrip != null)
                    log.Info("   MainMenuStrip Bounds=" + form.MainMenuStrip.Bounds + " items=" + form.MainMenuStrip.Items.Count);

                string formShot = Path.Combine(outDir, "ui_mainform.png");
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(formShot, ImageFormat.Png);
                    double sd = RenderTests.StdDev(bmp);
                    log.Check(sd > 5, "主窗体截图非空白", "stdev=" + sd.ToString("F2") + " → " + Path.GetFileName(formShot));
                }

                string canvasShot = Path.Combine(outDir, "ui_canvas.png");
                using (var bmp = form.Canvas.RenderToBitmap(1000, 700))
                {
                    bmp.Save(canvasShot, ImageFormat.Png);
                    log.Check(RenderTests.StdDev(bmp) > 5, "画布渲染非空白", Path.GetFileName(canvasShot));
                }

                // 叠加第二层（multi.tif）+ 调整符号化，检查面板联动不崩
                string multi = Path.Combine(dataDir, "multi.tif");
                if (File.Exists(multi))
                {
                    var l2 = form.OpenRaster(multi, false);
                    log.Check(l2 != null, "叠加第二个图层", l2 != null ? l2.Name : "null");
                    Pump(2);

                    // 图层行高：自绘内容 46px，行高不足会让下一行被上一行覆盖
                    var list = form.LayerPanel.ListForTest;
                    int minHeight = int.MaxValue;
                    int overlaps = 0;
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        var bounds = list.Items[i].Bounds;
                        minHeight = Math.Min(minHeight, bounds.Height);
                        if (i > 0 && bounds.Top < list.Items[i - 1].Bounds.Bottom) overlaps++;
                    }
                    log.Check(list.Items.Count >= 2 && minHeight >= 44 && overlaps == 0,
                        "图层行高足够且相邻行不重叠",
                        string.Format("{0} 行 · 最小行高 {1}px · 重叠 {2} 处", list.Items.Count, minHeight, overlaps));
                }

                if (layer != null) form.Canvas.ZoomToLayer(layer);
                form.Canvas.RenderNow(Rendering.RenderQuality.Full);
                Pump(2);

                string stackShot = Path.Combine(outDir, "ui_stack_two_layers.png");
                form.DrawToBitmapSafe(stackShot);
                log.Check(File.Exists(stackShot), "双层叠加截图", Path.GetFileName(stackShot));

                // 切换右侧面板并截图（直方图 / 元数据 / 识别）
                foreach (var pair in new[] { new[] { "1", "histogram" }, new[] { "2", "metadata" } })
                {
                    int idx;
                    if (!int.TryParse(pair[0], out idx)) continue;
                    form.SelectTab(idx);
                    Pump(2);
                    form.DrawToBitmapSafe(Path.Combine(outDir, "ui_tab_" + pair[1] + ".png"));
                }

                if (layer != null)
                {
                    form.Canvas.DoIdentifyForTest(layer, layer.Width / 2, layer.Height / 2);
                    form.SelectTab(3);
                    Pump(2);
                    form.DrawToBitmapSafe(Path.Combine(outDir, "ui_tab_identify.png"));
                    log.Check(true, "识别面板截图", "ui_tab_identify.png");
                }

                // 旋转工具与指北针
                RotateUiChecks(log, form, outDir);

                // 波段计算器的函数面板与波段映射
                BandCalcUiChecks(log, form, layer, outDir);

                // 处理对话框截图（不执行，只验证布局与主题）
                CaptureDialogs(log, form, outDir);

                // 静态规则与文案校验（无 UI 依赖）
                StaticChecks(log, form, layer);
            }
            catch (Exception ex)
            {
                log.Fail("界面自检异常", ex.GetType().Name + ": " + ex.Message);
                log.Info(ex.ToString());
            }
            finally
            {
                if (form != null)
                {
                    form.Hide();
                    form.Dispose();
                }
            }
        }

        /// <summary>纯逻辑与文案校验：模式启用矩阵、取消结果归类、菜单文案。</summary>
        private static void StaticChecks(TestLog log, MainForm form, SharpGIS.Models.RasterLayer layer)
        {
            // 1) WarpModeRules 矩阵
            var m0 = SharpGIS.UI.Dialogs.WarpModeRules.For(0);
            var m2 = SharpGIS.UI.Dialogs.WarpModeRules.For(2);
            var m4 = SharpGIS.UI.Dialogs.WarpModeRules.For(4);
            bool matrix = m0.Srs && !m0.Extent && !m0.Reference
                       && !m2.Srs && m2.Extent && m2.Reference && m2.SizeOverride
                       && !m4.Resample && !m4.PixelSize && m4.Bands;
            log.Check(matrix, "Warp 模式启用矩阵", "0=坐标系 / 2=范围+参考 / 4=仅波段");

            // 2) 取消与失败的归类不靠字符串
            var okRes = SharpGIS.Services.JobRunner.Classify("out.tif", null, false, false);
            var failRes = SharpGIS.Services.JobRunner.Classify(null, new Exception("boom"), false, false);
            var cancelRes = SharpGIS.Services.JobRunner.Classify(null, null, true, true);
            log.Check(okRes.Ok && failRes.Outcome == SharpGIS.Services.JobOutcome.Failed
                      && cancelRes.IsCancelled, "任务结果归类（成功/失败/取消）",
                      okRes.Outcome + "/" + failRes.Outcome + "/" + cancelRes.Outcome);

            // 3) 菜单文案必须与实际算法一致
            string filterText = null;
            if (form.MainMenuStrip != null)
            {
                foreach (ToolStripMenuItem top in form.MainMenuStrip.Items)
                {
                    foreach (ToolStripItem item in top.DropDownItems)
                        if (item.Text != null && item.Text.Contains("滤波")) filterText = item.Text;
                }
            }
            bool textOk = filterText != null && filterText.Contains("拉普拉斯") && !filterText.Contains("高斯锐化");
            log.Check(textOk, "滤波菜单与算法一致", filterText ?? "未找到菜单项");

            // 4) 裁剪模式对话框应以“按范围裁剪”打开
            using (var crop = new SharpGIS.UI.Dialogs.WarpDialog(layer, layer.Extent, layer.PixelSizeX, 2, form.Stack))
            {
                crop.StartPosition = FormStartPosition.Manual;
                crop.Location = new Point(60, 60);
                crop.Show();
                Pump(2);
                log.Check(crop.ClientSize.Height <= 560, "裁剪模式对话框高度可控",
                    crop.ClientSize.Width + "×" + crop.ClientSize.Height);
                using (var bmp = new Bitmap(crop.Width, crop.Height))
                {
                    crop.DrawToBitmap(bmp, new Rectangle(0, 0, crop.Width, crop.Height));
                    bmp.Save(Path.Combine(Path.GetDirectoryName(log.LogPath), "ui_dlg_WarpDialog_mode2.png"),
                        ImageFormat.Png);
                }
                crop.Hide();
            }
        }

        /// <summary>波段计算器：函数按钮数量、悬浮提示、点击插入、选中包裹。</summary>
        private static void BandCalcUiChecks(TestLog log, MainForm form, RasterLayer layer, string outDir)
        {
            if (layer == null) return;
            using (var dlg = new SharpGIS.UI.Dialogs.BandCalcDialog(layer))
            {
                dlg.StartPosition = FormStartPosition.Manual;
                dlg.Location = new Point(40, 40);
                dlg.Show();
                Pump(1);

                var buttons = dlg.FunctionButtonsForTest;
                log.Check(buttons.Length == 18, "函数面板按钮齐全", buttons.Length + " 个");

                // 每个按钮都要有“用法 + 说明 + 示例”的悬浮提示
                int withTip = 0;
                foreach (var b in buttons)
                {
                    string tip = dlg.ToolTipForTest(b);
                    if (!string.IsNullOrEmpty(tip) && tip.Contains("用法") && tip.Contains("示例")) withTip++;
                }
                log.Check(withTip == buttons.Length, "每个函数按钮都有用法提示", withTip + "/" + buttons.Length);

                // 点击插入：空表达式里点 sqrt 应得到 "sqrt(" 且光标在括号内
                var expr = dlg.ExpressionForTest;
                expr.Text = "";
                var sqrt = FindButton(buttons, "sqrt");
                sqrt.PerformClick();
                bool inserted = expr.Text == "sqrt(" && expr.SelectionStart == 5;
                log.Check(inserted, "点击函数按钮插入到光标处",
                    string.Format("文本=“{0}” 光标={1}", expr.Text, expr.SelectionStart));

                // 选中文字后再点函数：应把选中内容包进括号
                expr.Text = "b1 + b2";
                expr.SelectionStart = 0;
                expr.SelectionLength = expr.TextLength;      // 选中整段（7 个字符）
                FindButton(buttons, "abs").PerformClick();
                log.Check(expr.Text == "abs(b1 + b2)", "选中文字时函数会包裹选区", expr.Text);

                // 多参数函数插入占位逗号
                expr.Text = "";
                FindButton(buttons, "clamp").PerformClick();
                log.Check(expr.Text == "clamp(, , )", "多参数函数带占位逗号", expr.Text);

                // 映射行跟随表达式
                expr.Text = "";
                Pump(1);
                bool empty = dlg.MappingRowSlotsForTest.Length == 0;

                expr.Text = "b1";
                Pump(1);
                bool one = SameSlots(dlg.MappingRowSlotsForTest, new[] { 1 });

                expr.Text = "b1 + b2 + b3";
                Pump(1);
                bool three = SameSlots(dlg.MappingRowSlotsForTest, new[] { 1, 2, 3 });

                expr.Text = "b4";
                Pump(1);
                bool four = SameSlots(dlg.MappingRowSlotsForTest, new[] { 4 });

                log.Check(empty && one && three && four, "映射行跟随表达式增减",
                    string.Format("空={0} b1={1} b1+b2+b3={2} b4={3}", empty, one, three, four));

                // 改表达式后再改回来，槽位 1 的映射应保留
                expr.Text = "b1";
                Pump(1);
                int targetBand = layer.BandCount >= 4 ? 4 : layer.BandCount;
                dlg.SetMappingForTest(1, layer, targetBand);
                expr.Text = "b1 + b2";
                Pump(1);
                dlg.SetMappingForTest(2, layer, 1);
                expr.Text = "b1";
                Pump(1);
                var kept = dlg.MappingForTest(1);
                log.Check(kept != null && kept.Band == targetBand, "改表达式后已有映射被保留",
                    kept == null ? "丢失" : "b1 → 波段 " + kept.Band);

                // 映射应真正影响输出：b1 → 第 5 波段，结果应与直接算 b5 一致
                if (layer.BandCount >= 5)
                {
                    string uiPath = Path.Combine(outDir, "bandcalc_ui_map.tif");
                    string refPath = Path.Combine(outDir, "bandcalc_ui_ref.tif");
                    SharpGIS.Processing.ProcessingUtil.DeleteOutputSet(uiPath);
                    SharpGIS.Processing.ProcessingUtil.DeleteOutputSet(refPath);

                    expr.Text = "b1";
                    Pump(1);
                    dlg.SetMappingForTest(1, layer, 5);

                    var capture = typeof(SharpGIS.UI.Dialogs.ProcessingDialogBase)
                        .GetMethod("CaptureParameters", BindingFlags.Instance | BindingFlags.NonPublic);
                    var runCore = typeof(SharpGIS.UI.Dialogs.ProcessingDialogBase)
                        .GetMethod("RunCore", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (capture != null && runCore != null)
                    {
                        var reporter = new SharpGIS.Processing.ProgressReporter((p, m) => { }, () => false, 0.0);
                        capture.Invoke(dlg, null);
                        runCore.Invoke(dlg, new object[] { reporter, uiPath });
                        SharpGIS.Processing.BandCalculator.Run(layer, "b5", refPath, reporter);

                        double meanMapped = 0, meanDirect = 0;
                        using (var a = SharpGIS.Services.DatasetService.Open(uiPath)) meanMapped = a.Stats(1).Mean;
                        using (var b = SharpGIS.Services.DatasetService.Open(refPath)) meanDirect = b.Stats(1).Mean;
                        log.Check(Math.Abs(meanMapped - meanDirect) < 1e-6,
                            "对话框映射生效（b1→波段 5 等于直接算 b5）",
                            string.Format("均值 {0:F4} vs {1:F4}", meanMapped, meanDirect));
                    }
                }

                // 单参构造：树只有当前图层一个根，子节点数 = 波段数
                var tree = dlg.BandTreeForTest;
                log.Check(tree.Nodes.Count == 1 && tree.Nodes[0].Nodes.Count == layer.BandCount,
                    "树列出当前图层的全部波段",
                    tree.Nodes.Count + " 个根 / " + (tree.Nodes.Count > 0 ? tree.Nodes[0].Nodes.Count : 0) + " 个波段");

                dlg.Hide();
            }

            // 带图层栈：应列出所有已打开文件，网格不一致的置灰并注明原因
            if (form != null && form.Stack != null && form.Stack.Count >= 2)
            {
                using (var dlg2 = new SharpGIS.UI.Dialogs.BandCalcDialog(layer, form.Stack))
                {
                    dlg2.Show();
                    Pump(1);
                    var tree2 = dlg2.BandTreeForTest;
                    bool twoRoots = tree2.Nodes.Count >= 2;

                    bool greyFound = false;
                    for (int i = 0; i < tree2.Nodes.Count; i++)
                    {
                        var node = tree2.Nodes[i];
                        if (node.ForeColor == Theme.TextDim && node.Text.Contains("网格不一致")) greyFound = true;
                    }
                    log.Check(twoRoots && greyFound, "树列出所有文件，网格不一致的置灰并注明原因",
                        tree2.Nodes.Count + " 个文件，置灰=" + greyFound);

                    // 展开当前图层后截图，便于目视核对树与映射行
                    if (tree2.Nodes.Count > 0) tree2.Nodes[0].Expand();
                    Pump(1);
                    using (var bmp = new Bitmap(dlg2.Width, dlg2.Height))
                    {
                        dlg2.DrawToBitmap(bmp, new Rectangle(0, 0, dlg2.Width, dlg2.Height));
                        bmp.Save(Path.Combine(outDir, "ui_dlg_BandCalc_multi.png"), ImageFormat.Png);
                    }
                    dlg2.Hide();
                }
            }
        }

        private static bool SameSlots(int[] a, int[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static Button FindButton(Button[] buttons, string name)
        {
            foreach (var b in buttons) if (b.Text == name) return b;
            return buttons[0];
        }

        /// <summary>旋转工具状态、角度计算与指北针绘制。</summary>
        private static void RotateUiChecks(TestLog log, MainForm form, string outDir)
        {
            var canvas = form.Canvas;

            // 1) 方向：从正右划到正下应为 +90°（顺时针为正）
            double d = SharpGIS.UI.MapCanvas.RotationDeltaDeg(new Point(100, 100),
                new Point(200, 100), new Point(100, 200));
            log.Check(Math.Abs(d - 90) < 1e-6, "旋转角度差顺时针为正", d.ToString("F2") + "°");

            // 2) 指北针针尖方向
            var up = SharpGIS.UI.MapCanvas.NorthTip(new PointF(0, 0), 0, 10);
            var right = SharpGIS.UI.MapCanvas.NorthTip(new PointF(0, 0), 90, 10);
            var down = SharpGIS.UI.MapCanvas.NorthTip(new PointF(0, 0), 180, 10);
            bool dirOk = Math.Abs(up.Y + 10) < 0.01 && Math.Abs(up.X) < 0.01
                      && Math.Abs(right.X - 10) < 0.01 && Math.Abs(right.Y) < 0.01
                      && Math.Abs(down.Y - 10) < 0.01;
            log.Check(dirOk, "指北针方向随角度旋转",
                string.Format("0°→({0:F0},{1:F0}) 90°→({2:F0},{3:F0})", up.X, up.Y, right.X, right.Y));

            // 3) 指北针在画布内
            var bounds = canvas.CompassBounds;
            bool inside = bounds.Width > 0 &&
                          canvas.ClientRectangle.Contains(bounds);
            log.Check(inside, "指北针完整落在画布内", bounds.ToString());

            // 4) 指北针确实被绘制（区域内存在与背景差异明显的像素）
            using (var bmp = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(bmp, new Rectangle(0, 0, canvas.Width, canvas.Height));
                int diff = 0;
                for (int y = bounds.Top; y < bounds.Bottom; y += 2)
                    for (int x = bounds.Left; x < bounds.Right; x += 2)
                    {
                        var c = bmp.GetPixel(x, y);
                        if (Math.Abs(c.R - Theme.Background.R) > 20 || Math.Abs(c.G - Theme.Background.G) > 20)
                            diff++;
                    }
                log.Check(diff > 20, "指北针已绘制", diff + " 个非背景像素");
                bmp.Save(Path.Combine(outDir, "ui_compass.png"), ImageFormat.Png);
            }

            // 5) 旋转工具状态：刚切入时等待第一次单击
            canvas.Tool = MapTool.Rotate;
            log.Check(canvas.RotateStateForTest == 0, "旋转工具初始为等待中心", "state=" + canvas.RotateStateForTest);
            log.Check(canvas.CancelRotate(true) == false, "未定中心时取消直接返回 false", "ok");
            canvas.Tool = MapTool.Pan;

            // 5.5) 真实鼠标路径：单击定中心 → 按下并拖动 90° → 松手提交
            var md = typeof(MapCanvas).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
            var mm = typeof(MapCanvas).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
            var mu = typeof(MapCanvas).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);
            if (md != null && mm != null && mu != null)
            {
                canvas.Tool = MapTool.Rotate;
                double angleBefore = canvas.View.Angle;
                var pivot = new Point(canvas.Width / 2, canvas.Height / 2);
                double gx0, gy0;
                canvas.View.ToGeo(pivot.X, pivot.Y, out gx0, out gy0);

                // 第 1 次单击：定中心
                md.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, pivot.X, pivot.Y, 0) });
                bool armed = canvas.RotateStateForTest == 1;

                // 第 2 次单击：定手柄
                var handle = new Point(pivot.X + 120, pivot.Y);
                md.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, handle.X, handle.Y, 0) });
                bool following = canvas.RotateStateForTest == 2;

                // 分多小步移动：角度必须累计（曾经的 bug 是每步只算增量，慢慢移动永远不超过 1°）
                var target = new Point(pivot.X, pivot.Y + 120);   // 从右侧转到下方 = 顺时针 90°
                for (int i = 1; i <= 10; i++)
                {
                    var p2 = new Point(
                        handle.X + (target.X - handle.X) * i / 10,
                        handle.Y + (target.Y - handle.Y) * i / 10);
                    mm.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, p2.X, p2.Y, 0) });
                }
                double previewAngle = canvas.View.Angle + 0;      // 预览尚未提交，视口角度仍为 0
                form.DrawToBitmapSafe(Path.Combine(outDir, "ui_rotate_following.png"));

                // 第 3 次单击提交
                md.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, target.X, target.Y, 0) });

                double angleAfter = canvas.View.Angle;
                var sp = canvas.View.ToScreen(gx0, gy0);
                bool pivotFixed = Math.Abs(sp.X - pivot.X) < 0.01 && Math.Abs(sp.Y - pivot.Y) < 0.01;
                log.Check(armed && following, "旋转工具状态机（中心 → 手柄 → 提交）",
                    string.Format("armed={0} following={1}", armed, following));
                log.Check(Math.Abs(previewAngle - angleBefore) < 1e-9, "预览阶段不改动视口（提交才生效）",
                    string.Format("预览时视口角度 {0:F2}°", previewAngle));
                log.Check(Math.Abs(angleAfter - angleBefore - 90) < 0.5, "三次单击转到 90° 角度正确",
                    string.Format("{0:F2}° → {1:F2}°", angleBefore, angleAfter));
                log.Check(pivotFixed, "旋转提交后枢轴地物未移动",
                    string.Format("Δ=({0:F3},{1:F3})", sp.X - pivot.X, sp.Y - pivot.Y));

                canvas.CancelRotate(true);
                canvas.Tool = MapTool.Pan;
                canvas.View.SetAngle(0);
                canvas.RenderNow(Rendering.RenderQuality.Full);
            }

            // 6) 旋转后状态栏角度与画面
            var before = canvas.View.Clone();
            canvas.View.SetAngle(25);
            canvas.RenderNow(Rendering.RenderQuality.Full);
            Pump(1);
            string shot = Path.Combine(outDir, "ui_rotated_25.png");
            form.DrawToBitmapSafe(shot);
            log.Check(File.Exists(shot), "旋转 25° 界面截图", Path.GetFileName(shot));
            canvas.View.CopyFrom(before);
            canvas.RenderNow(Rendering.RenderQuality.Full);
        }

        private static void CaptureDialogs(TestLog log, MainForm form, string outDir)
        {
            var layer = form.Stack.TopVisible();
            if (layer == null) return;

            CaptureCaMarkov(log, form, outDir);

            var dialogs = new System.Collections.Generic.List<Form>
            {
                new SharpGIS.UI.Dialogs.ExportDialog(layer),
                new SharpGIS.UI.Dialogs.WarpDialog(layer, layer.Extent, layer.PixelSizeX, 0, form.Stack),
                new SharpGIS.UI.Dialogs.OverviewDialog(layer),
                new SharpGIS.UI.Dialogs.BandCalcDialog(layer),
                new SharpGIS.UI.Dialogs.DemDialog(layer),
                new SharpGIS.UI.Dialogs.FilterDialog(layer),
                new SharpGIS.UI.Dialogs.EnhanceDialog(layer),
                new SharpGIS.UI.Dialogs.KMeansDialog(layer)
            };
            if (form.Stack.Count >= 2)
                dialogs.Add(new SharpGIS.UI.Dialogs.FusionDialog(layer, form.Stack));

            foreach (var dlg in dialogs)
            {
                try
                {
                    dlg.StartPosition = FormStartPosition.Manual;
                    dlg.Location = new Point(40, 40);
                    dlg.Show();
                    Pump(2);
                    string name = "ui_dlg_" + dlg.GetType().Name + ".png";
                    using (var bmp = new Bitmap(dlg.Width, dlg.Height))
                    {
                        dlg.DrawToBitmap(bmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
                        bmp.Save(Path.Combine(outDir, name), ImageFormat.Png);
                    }
                    log.Check(dlg.ClientSize.Height > 100, "对话框布局 " + dlg.GetType().Name,
                        dlg.ClientSize.Width + "×" + dlg.ClientSize.Height);
                }
                catch (Exception ex)
                {
                    log.Fail("对话框截图 " + dlg.GetType().Name, ex.Message);
                }
                finally
                {
                    dlg.Hide();
                    dlg.Dispose();
                }
            }
        }

        private static void Pump(int rounds)
        {
            for (int i = 0; i < rounds; i++)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(120);
            }
        }

        /// <summary>逐页截图 CA-Markov 参数对话框与结果看图窗口（共 10 页）。</summary>
        private static void CaptureCaMarkov(TestLog log, MainForm form, string outDir)
        {
            var layer = form.Stack.TopVisible();
            if (layer == null) return;

            var dialog = new SharpGIS.UI.Dialogs.CaMarkovDialog(layer, form.Stack);
            try
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(30, 20);
                dialog.Show();
                Pump(1);

                // 灌两期数据并统计一次类别，截图才看得出表格是干什么的
                var field = dialog.GetType().GetField("_periods",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var periods = field == null ? null : field.GetValue(dialog) as System.Collections.IList;
                if (periods != null && periods.Count == 0)
                {
                    periods.Add(new SharpGIS.Processing.CaPeriod { Path = layer.SourcePath, Band = 1, Year = 0 });
                    periods.Add(new SharpGIS.Processing.CaPeriod { Path = layer.SourcePath, Band = 1, Year = 0 });
                    var refresh = dialog.GetType().GetMethod("RefreshPeriodList",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (refresh != null) refresh.Invoke(dialog, null);
                    var count = dialog.GetType().GetMethod("CountClassesIntoGrid",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (count != null) count.Invoke(dialog, null);
                    Pump(1);
                }

                var tabs = dialog.TabsForTest;
                log.Check(tabs.TabPages.Count == 5, "CA-Markov 对话框 5 个标签页",
                    tabs.TabPages.Count.ToString());
                for (int i = 0; i < tabs.TabPages.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    Pump(1);
                    string shot = Path.Combine(outDir, "ui_camarkov_" + i + ".png");
                    using (var bmp = new Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(bmp, new Rectangle(0, 0, dialog.Width, dialog.Height));
                        bmp.Save(shot, ImageFormat.Png);
                    }
                    log.Check(File.Exists(shot), "CA-Markov 对话框页 " + (i + 1),
                        tabs.TabPages[i].Text);
                }
            }
            catch (Exception ex)
            {
                log.Fail("CA-Markov 对话框截图", ex.Message);
            }
            finally
            {
                try { dialog.Hide(); dialog.Dispose(); } catch { }
            }

            var viewer = new SharpGIS.UI.Dialogs.CaMarkovResultDialog(
                SharpGIS.UI.Dialogs.CaMarkovResultDialog.Sample(), "结果图表（自检）");
            try
            {
                viewer.StartPosition = FormStartPosition.Manual;
                viewer.Location = new Point(20, 10);
                viewer.Show();
                Pump(1);

                var tabs = viewer.TabsForTest;
                log.Check(tabs.TabPages.Count == 6, "结果窗口 6 个标签页", tabs.TabPages.Count.ToString());
                for (int i = 0; i < tabs.TabPages.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    Pump(1);
                    string shot = Path.Combine(outDir, "ui_camarkov_result_" + i + ".png");
                    using (var bmp = new Bitmap(viewer.Width, viewer.Height))
                    {
                        viewer.DrawToBitmap(bmp, new Rectangle(0, 0, viewer.Width, viewer.Height));
                        bmp.Save(shot, ImageFormat.Png);
                    }
                    log.Check(File.Exists(shot), "结果窗口页 " + (i + 1), tabs.TabPages[i].Text);
                }
            }
            catch (Exception ex)
            {
                log.Fail("结果窗口截图", ex.Message);
            }
            finally
            {
                try { viewer.Hide(); viewer.Dispose(); } catch { }
            }
        }
    }
}
