using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SharpGIS.Models;
using SharpGIS.Rendering;
using SharpGIS.Services;

namespace SharpGIS.SelfTest
{
    /// <summary>渲染管线自检：产出多组 PNG 供目视核对，并做自动化断言。</summary>
    internal static class RenderTests
    {
        public static void Run(TestLog log, string dataDir, string outDir)
        {
            log.Info("--- step=render ---");
            string tm = Path.Combine(dataDir, "TMtest.tif");
            if (!File.Exists(tm)) { log.Fail("TMtest.tif 存在", tm); return; }

            using (var layer = DatasetService.Open(tm))
            {
                var vp = new Viewport();
                vp.Resize(900, 720);
                vp.ZoomToExtent(layer.Extent, 0.02);

                // 坐标往返
                double gx, gy;
                vp.ToGeo(321, 123, out gx, out gy);
                var sp = vp.ToScreen(gx, gy);
                double err = Math.Max(Math.Abs(sp.X - 321), Math.Abs(sp.Y - 123));
                log.Check(err < 1e-6, "屏幕↔地理坐标往返误差 < 1e-6", err.ToString("E2"));

                // 1) 灰度 + 最小最大值
                layer.Style.Mode = RenderMode.Gray;
                layer.Style.GrayBand = 1;
                layer.Style.Ramp = ColorRamp.Gray;
                layer.Style.GrayStretch.Mode = StretchMode.MinMax;
                layer.Touch();
                RenderAndCheck(log, layer, vp, Path.Combine(outDir, "render_01_gray_minmax.png"), "灰度 min-max");

                // 2) 伪彩色 + 百分比裁剪
                layer.Style.Ramp = ColorRamp.Jet;
                layer.Style.GrayStretch.Mode = StretchMode.PercentClip;
                layer.Touch();
                RenderAndCheck(log, layer, vp, Path.Combine(outDir, "render_02_jet_percentclip.png"), "伪彩 Jet 百分比裁剪");

                // 3) RGB 波段合成
                layer.Style.Mode = RenderMode.Rgb;
                layer.Style.RgbBands = new[] { 3, 2, 1 };
                layer.Style.RStretch.Mode = StretchMode.PercentClip;
                layer.Style.GStretch.Mode = StretchMode.PercentClip;
                layer.Style.BStretch.Mode = StretchMode.PercentClip;
                layer.Touch();
                RenderAndCheck(log, layer, vp, Path.Combine(outDir, "render_03_rgb321.png"), "RGB 3/2/1 合成");

                // 4) 直方图均衡
                layer.Style.Mode = RenderMode.Gray;
                layer.Style.Ramp = ColorRamp.Gray;
                layer.Style.GrayStretch.Mode = StretchMode.HistogramEqualize;
                layer.Touch();
                RenderAndCheck(log, layer, vp, Path.Combine(outDir, "render_04_equalize.png"), "直方图均衡");

                // 5) 多图层叠加（同一影像两层：底层灰度 + 顶层 RGB 55% 正片叠底）
                layer.Style.Mode = RenderMode.Gray;
                layer.Style.GrayStretch.Mode = StretchMode.MinMax;
                layer.Style.Opacity = 1.0;
                layer.Style.Blend = BlendMode.Normal;
                layer.Touch();

                using (var top = DatasetService.Open(tm))
                {
                    top.Style.Mode = RenderMode.Rgb;
                    top.Style.RgbBands = new[] { 3, 2, 1 };
                    top.Style.Opacity = 0.55;
                    top.Style.Blend = BlendMode.Multiply;
                    top.Touch();

                    var layers = new[] { layer, top };
                    RenderStats st;
                    string path = Path.Combine(outDir, "render_05_stack_multiply.png");
                    using (var bmp = RenderPipeline.Render(vp, layers, RenderQuality.Full, out st))
                    {
                        bmp.Save(path, ImageFormat.Png);
                        double sd = StdDev(bmp);
                        log.Check(sd > 3, "多图层叠加非纯色", "stdev=" + sd.ToString("F2"));
                        log.Check(st.LayersDrawn == 2, "两层均参与合成", st.LayersDrawn.ToString());
                        log.Info("叠加渲染耗时: " + st.ElapsedMs.ToString("F0") + " ms");
                    }
                }

                // 6) 快速质量 + 缩放性能
                layer.Style.Opacity = 1.0;
                layer.Style.Blend = BlendMode.Normal;
                layer.Touch();
                RenderStats fastStats;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var bmp = RenderPipeline.Render(vp, new[] { layer }, RenderQuality.Fast, out fastStats))
                {
                    sw.Stop();
                    bmp.Save(Path.Combine(outDir, "render_06_fast.png"), ImageFormat.Png);
                    log.Check(sw.ElapsedMilliseconds < 1500, "快速模式渲染 < 1500ms", sw.ElapsedMilliseconds + " ms");
                }

                // 7) 缩放到 1:1 与放大
                var zoomed = vp.Clone();
                zoomed.SetPixelSize(layer.PixelSizeX);
                using (var bmp = RenderPipeline.Render(zoomed, new[] { layer }, RenderQuality.Full, out fastStats))
                {
                    bmp.Save(Path.Combine(outDir, "render_07_1to1_crop.png"), ImageFormat.Png);
                    log.Check(StdDev(bmp) > 3, "1:1 视图有内容", "stdev=" + StdDev(bmp).ToString("F2"));
                }

                // 8) 缩放锚点：光标下地物不移动
                var z2 = vp.Clone();
                double ax, ay; z2.ToGeo(400, 300, out ax, out ay);
                z2.ZoomAt(400, 300, 2.0);
                double bx, by; z2.ToGeo(400, 300, out bx, out by);
                log.Check(Math.Abs(ax - bx) < 1e-6 && Math.Abs(ay - by) < 1e-6, "缩放锚点保持不动",
                    string.Format("Δ=({0:E1},{1:E1})", bx - ax, by - ay));

                // 9) 旋转数学
                RotationMathChecks(log, vp);

                // 9.4) 合成器单元测试
                BlendAffineUnitChecks(log);

                // 9.5) 旋转渲染
                RotatedRenderChecks(log, layer, vp, outDir);

                // 10) 探针值落在统计区间内
                var stats1 = layer.Stats(1);
                var px = layer.ReadPixel(layer.Width / 2, layer.Height / 2);
                log.Check(px[0] >= stats1.Min - 1 && px[0] <= stats1.Max + 1, "探针值在统计区间内",
                    px[0].ToString("F0"));
            }
        }

        /// <summary>旋转相关纯数学断言：往返、方向、锚点、绕点、可逆、历史。</summary>
        private static void RotationMathChecks(TestLog log, Viewport baseVp)
        {
            double[] angles = { 0, 15, 37, 45, 90, 135, 180, -73 };

            // 1) 多角度往返
            double worst = 0;
            foreach (var a in angles)
            {
                var vp = baseVp.Clone();
                vp.SetAngle(a);
                double gx, gy;
                vp.ToGeo(321, 123, out gx, out gy);
                var sp = vp.ToScreen(gx, gy);
                worst = Math.Max(worst, Math.Max(Math.Abs(sp.X - 321), Math.Abs(sp.Y - 123)));
            }
            log.Check(worst < 1e-6, "多角度坐标往返误差 < 1e-6", "worst=" + worst.ToString("E2"));

            // 2) 方向约定：角度为正 = 屏幕顺时针（未旋转时位于中心右侧的地物，转到中心下方）
            //    注意不能用同一个视口做 ToGeo→ToScreen 往返，那样必然回到原点，测不出方向。
            double rgx, rgy;
            baseVp.ToGeo(baseVp.Width * 0.5 + 120, baseVp.Height * 0.5, out rgx, out rgy);
            var vp90 = baseVp.Clone();
            vp90.SetAngle(90);
            var sp90 = vp90.ToScreen(rgx, rgy);
            bool clockwise = sp90.Y > vp90.Height * 0.5 + 119 && Math.Abs(sp90.X - vp90.Width * 0.5) < 1e-6;
            log.Check(clockwise, "角度为正表示顺时针（右侧点转到下方）",
                string.Format("→ ({0:F1},{1:F1})", sp90.X, sp90.Y));

            // 3) 旋转状态下缩放锚点仍不漂移
            double worstAnchor = 0;
            foreach (var a in angles)
            {
                var vp = baseVp.Clone();
                vp.SetAngle(a);
                double ax, ay, bx, by;
                vp.ToGeo(400, 300, out ax, out ay);
                vp.ZoomAt(400, 300, 2.0);
                vp.ToGeo(400, 300, out bx, out by);
                worstAnchor = Math.Max(worstAnchor, Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by)));
            }
            log.Check(worstAnchor < 1e-6, "旋转时缩放锚点不漂移", "worst=" + worstAnchor.ToString("E2"));

            // 4) 绕任意屏幕点旋转，该点下的地物不动
            double worstPivot = 0;
            double agx, agy;
            baseVp.ToGeo(357, 211, out agx, out agy);
            foreach (var a in angles)
            {
                var vp = baseVp.Clone();
                vp.RotateTo(agx, agy, 357, 211, a);
                var sp = vp.ToScreen(agx, agy);
                worstPivot = Math.Max(worstPivot, Math.Max(Math.Abs(sp.X - 357), Math.Abs(sp.Y - 211)));
            }
            log.Check(worstPivot < 1e-9, "绕点旋转时枢轴地物不动", "worst=" + worstPivot.ToString("E2"));

            // 5) 来回拖动可逆（只用绝对量，不累积误差）
            var vp3 = baseVp.Clone();
            double cx0 = vp3.CenterX, cy0 = vp3.CenterY, m0 = vp3.Mupp;
            vp3.RotateTo(agx, agy, 357, 211, 37);
            vp3.RotateTo(agx, agy, 357, 211, -12);
            vp3.RotateTo(agx, agy, 357, 211, 0);
            bool reversible = Math.Abs(vp3.CenterX - cx0) < 1e-9 &&
                              Math.Abs(vp3.CenterY - cy0) < 1e-9 &&
                              Math.Abs(vp3.Mupp - m0) < 1e-12 && Math.Abs(vp3.Angle) < 1e-12;
            log.Check(reversible, "拖动旋转可逆（回到起点无残差）",
                string.Format("ΔC=({0:E1},{1:E1}) Δang={2:E1}", vp3.CenterX - cx0, vp3.CenterY - cy0, vp3.Angle));

            // 6) 角度随视图历史一起恢复
            var history = new ViewHistory();
            history.Push(baseVp);
            var rotated = baseVp.Clone();
            rotated.SetAngle(40);
            history.Push(rotated);
            var back = history.Back();
            var forward = history.Forward();
            log.Check(back != null && Math.Abs(back.Angle) < 1e-9 &&
                      forward != null && Math.Abs(forward.Angle - 40) < 1e-9,
                "前进/后退恢复旋转角",
                string.Format("back={0:F1} forward={1:F1}", back == null ? -999 : back.Angle, forward == null ? -999 : forward.Angle));

            // 7) 角度容差：极小差异不应产生新历史项
            var h2 = new ViewHistory();
            h2.Push(baseVp);
            var jitter = baseVp.Clone();
            jitter.Angle = 1e-9;
            h2.Push(jitter);
            log.Check(!h2.CanBack, "角度微差被判定为同一视图", h2.CanBack ? "多出一条历史" : "ok");
        }

        /// <summary>旋转渲染断言：内容非空、四角留背景、角度确实改变画面。</summary>
        private static void RotatedRenderChecks(TestLog log, RasterLayer layer, Viewport baseVp, string outDir)
        {
            layer.Style.Mode = RenderMode.Rgb;
            layer.Style.RgbBands = new[] { 1, 2, 3 };
            layer.Style.RStretch.Mode = StretchMode.PercentClip;
            layer.Style.GStretch.Mode = StretchMode.PercentClip;
            layer.Style.BStretch.Mode = StretchMode.PercentClip;
            layer.Touch();

            Bitmap normal = null;
            try
            {
                RenderStats st0;
                normal = RenderPipeline.Render(baseVp, new[] { layer }, RenderQuality.Full, out st0);
                normal.Save(Path.Combine(outDir, "render_07b_rot0.png"), ImageFormat.Png);

                foreach (var angle in new[] { 30.0, 45.0, 90.0 })
                {
                    var vp = baseVp.Clone();
                    vp.SetAngle(angle);

                    RenderStats st;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    using (var bmp = RenderPipeline.Render(vp, new[] { layer }, RenderQuality.Full, out st))
                    {
                        sw.Stop();
                        if (Math.Abs(angle - 30) < 1e-9)
                            bmp.Save(Path.Combine(outDir, "render_08_rot30.png"), ImageFormat.Png);
                        if (Math.Abs(angle - 45) < 1e-9)
                            bmp.Save(Path.Combine(outDir, "render_09_rot45.png"), ImageFormat.Png);

                        double sd = StdDev(bmp);
                        var bg = RenderPipeline.BackgroundColor;
                        bool corners = IsBackground(bmp.GetPixel(2, 2), bg) &&
                                       IsBackground(bmp.GetPixel(bmp.Width - 3, 2), bg) &&
                                       IsBackground(bmp.GetPixel(2, bmp.Height - 3), bg) &&
                                       IsBackground(bmp.GetPixel(bmp.Width - 3, bmp.Height - 3), bg);

                        log.Check(st.LayersDrawn == 1 && sd > 3,
                            string.Format("{0:F0}° 旋转渲染有内容", angle),
                            string.Format("stdev={0:F1} 耗时={1:F0}ms", sd, st.ElapsedMs));
                        log.Check(corners, string.Format("{0:F0}° 旋转后画布四角留背景色", angle), "ok");
                    }
                }

                // 90° 与 0° 的画面必须明显不同（否则说明旋转没生效）
                var vp90 = baseVp.Clone();
                vp90.SetAngle(90);
                RenderStats st90;
                using (var bmp90 = RenderPipeline.Render(vp90, new[] { layer }, RenderQuality.Full, out st90))
                {
                    double diff = MeanAbsDiff(normal, bmp90);
                    log.Check(diff > 5, "90° 旋转确实改变了画面", "平均色差 " + diff.ToString("F1"));
                }
            }
            finally
            {
                if (normal != null) normal.Dispose();
            }
        }

        /// <summary>合成器单元测试：1:1、无旋转时 BlendAffine 必须逐像素还原 tile。</summary>
        private static unsafe void BlendAffineUnitChecks(TestLog log)
        {
            const int TW = 8, TH = 8;
            var tile = new byte[TW * TH * 4];
            for (int y = 0; y < TH; y++)
                for (int x = 0; x < TW; x++)
                {
                    int i = (y * TW + x) * 4;
                    tile[i + 0] = (byte)(x * 30);
                    tile[i + 1] = (byte)(y * 30);
                    tile[i + 2] = (byte)((x * 17 + y * 23) & 0xFF);
                    tile[i + 3] = 255;
                }

            using (var bmp = new Bitmap(16, 16, PixelFormat.Format32bppPArgb))
            {
                var data = bmp.LockBits(new Rectangle(0, 0, 16, 16), ImageLockMode.ReadWrite,
                    PixelFormat.Format32bppPArgb);
                try
                {
                    byte* canvas = (byte*)data.Scan0;
                    CanvasCompositor.FillBackground(canvas, 16, 16, data.Stride, 0, 0, 0);
                    var p = new TilePlacement
                    {
                        QX0 = 4, QY0 = 4, QX1 = 12, QY1 = 12,
                        QC = 1, QS = 0, Rotated = true,
                        OutW = TW, OutH = TH
                    };
                    CanvasCompositor.BlendAffine(canvas, 16, 16, data.Stride, tile, TW, TH, p, 1.0, BlendMode.Normal);
                }
                finally { bmp.UnlockBits(data); }

                int bad = 0;
                for (int y = 0; y < TH; y++)
                    for (int x = 0; x < TW; x++)
                    {
                        var c = bmp.GetPixel(4 + x, 4 + y);
                        int i = (y * TW + x) * 4;
                        if (Math.Abs(c.B - tile[i]) > 1 || Math.Abs(c.G - tile[i + 1]) > 1 ||
                            Math.Abs(c.R - tile[i + 2]) > 1) bad++;
                    }
                log.Check(bad == 0, "BlendAffine 1:1 逐像素还原", bad + " 个像素不符");
            }
        }

        private static bool IsBackground(Color c, Color bg)
        {
            return Math.Abs(c.R - bg.R) <= 3 && Math.Abs(c.G - bg.G) <= 3 && Math.Abs(c.B - bg.B) <= 3;
        }

        private static double MeanAbsDiff(Bitmap a, Bitmap b)
        {
            int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
            int sx = Math.Max(1, w / 90), sy = Math.Max(1, h / 90);
            double sum = 0; int n = 0;
            for (int y = 0; y < h; y += sy)
                for (int x = 0; x < w; x += sx)
                {
                    Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
                    sum += (Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B)) / 3.0;
                    n++;
                }
            return n == 0 ? 0 : sum / n;
        }

        private static void RenderAndCheck(TestLog log, RasterLayer layer, Viewport vp, string path, string name)
        {
            RenderStats st;
            using (var bmp = RenderPipeline.Render(vp, new[] { layer }, RenderQuality.Full, out st))
            {
                bmp.Save(path, ImageFormat.Png);
                double sd = StdDev(bmp);
                double mean = Mean(bmp);
                log.Check(sd > 3, name, string.Format("stdev={0:F2} mean={1:F1}, {2:F0}ms, {3}", sd, mean, st.ElapsedMs, Path.GetFileName(path)));
                var st1 = layer.Stats(layer.Style.Mode == RenderMode.Rgb ? layer.Style.RgbBands[0] : layer.Style.GrayBand);
                log.Info(string.Format("   波段统计: min={0:F1} p2={1:F1} p50={2:F1} p98={3:F1} max={4:F1} typeMin={5:F0} typeMax={6:F0}",
                    st1.Min, st1.Percentile(2), st1.Percentile(50), st1.Percentile(98), st1.Max, st1.TypeMin, st1.TypeMax));
            }
        }

        public static double Mean(Bitmap bmp)
        {
            int stepX = Math.Max(1, bmp.Width / 60);
            int stepY = Math.Max(1, bmp.Height / 60);
            double sum = 0; int n = 0;
            for (int y = 0; y < bmp.Height; y += stepY)
                for (int x = 0; x < bmp.Width; x += stepX)
                {
                    Color c = bmp.GetPixel(x, y);
                    sum += (c.R + c.G + c.B) / 3.0; n++;
                }
            return n == 0 ? 0 : sum / n;
        }

        /// <summary>抽样计算灰度标准差，用于判断图是否非纯色。</summary>
        public static double StdDev(Bitmap bmp)
        {
            int stepX = Math.Max(1, bmp.Width / 60);
            int stepY = Math.Max(1, bmp.Height / 60);
            double sum = 0, sum2 = 0;
            int n = 0;
            for (int y = 0; y < bmp.Height; y += stepY)
            {
                for (int x = 0; x < bmp.Width; x += stepX)
                {
                    Color c = bmp.GetPixel(x, y);
                    double v = (c.R + c.G + c.B) / 3.0;
                    sum += v; sum2 += v * v; n++;
                }
            }
            if (n == 0) return 0;
            double mean = sum / n;
            return Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
        }
    }
}
