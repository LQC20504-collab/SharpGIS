using System;
using System.Collections.Generic;
using System.IO;
using SharpGIS.Models;
using SharpGIS.Processing;
using SharpGIS.Services;

namespace SharpGIS.SelfTest
{
    /// <summary>
    /// 质量整改专项自检：取消语义、旁文件清理、两阶段进度、以及本轮新增的三个算子。
    /// 全部使用确定性手段（不依赖 GDAL 回调频率）。
    /// </summary>
    internal static class QualityTests
    {
        public static void Run(TestLog log, string dataDir, string outRoot)
        {
            log.Info("--- 取消与新增算子 ---");
            string tm = Path.Combine(dataDir, "TMtest.tif");
            string multi = Path.Combine(dataDir, "multi.tif");
            string pan = Path.Combine(dataDir, "pan.tif");

            using (var layer = DatasetService.Open(tm))
            {
                CheckCancelMidLoop(log, layer, outRoot);
                CheckCancelPrecheck(log, layer, outRoot);
                CheckDeleteOutputSet(log, outRoot);
                CheckTwoPhaseProgress(log, layer, outRoot);
                CheckBandSubset(log, layer, outRoot);
                CheckSizeOverride(log, layer, outRoot);
            }

            CheckOutputLock(log, dataDir, outRoot);

            if (File.Exists(multi) && File.Exists(pan))
            {
                using (var ms = DatasetService.Open(multi))
                using (var pn = DatasetService.Open(pan))
                {
                    CheckReferenceAlignment(log, ms, pn, outRoot);
                }

                using (var ms = DatasetService.Open(multi))
                {
                    CheckPaletteSwitch(log, ms, outRoot);
                }
            }
        }

        /// <summary>分块循环中途取消：第 3 次进度上报时请求取消，必须抛 OCE 且不留文件。</summary>
        private static void CheckCancelMidLoop(TestLog log, RasterLayer layer, string outRoot)
        {
            string path = Path.Combine(outRoot, "cancel_filter.tif");
            int calls = 0;
            var reporter = new ProgressReporter((p, m) => { }, () => ++calls > 2, 0.0);
            bool threw = false;
            try
            {
                FilterService.Run(layer, 1, FilterKind.Mean, 3, 0, path, reporter);
            }
            catch (OperationCanceledException) { threw = true; }
            catch (Exception ex) { log.Fail("滤波中途取消", "抛出了 " + ex.GetType().Name + " 而非取消异常"); }

            log.Check(threw && !File.Exists(path) && !File.Exists(path + ".ovr"),
                "滤波分块循环可被取消", threw ? "已取消且无残留" : "未抛出取消异常");

            string path2 = Path.Combine(outRoot, "cancel_bandcalc.tif");
            calls = 0;
            var reporter2 = new ProgressReporter((p, m) => { }, () => ++calls > 2, 0.0);
            threw = false;
            try { BandCalculator.Run(layer, "b1 * 2", path2, reporter2); }
            catch (OperationCanceledException) { threw = true; }
            catch (Exception ex) { log.Fail("波段计算出中途取消", ex.GetType().Name); }

            log.Check(threw && !File.Exists(path2), "波段计算可被取消", threw ? "已取消且无残留" : "未抛出取消异常");
        }

        /// <summary>预检取消：一进入算子就抛 OCE，不应创建任何输出。</summary>
        private static void CheckCancelPrecheck(TestLog log, RasterLayer layer, string outRoot)
        {
            var reporter = new ProgressReporter((p, m) => { }, () => true, 0.0);

            string p1 = Path.Combine(outRoot, "cancel_warp.tif");
            log.Check(ThrowsCancel(() => WarpService.Run(layer, new WarpRequest { OutputPath = p1 }, reporter)) && !File.Exists(p1),
                "Warp 预检取消", "OCE 且无输出");

            string p2 = Path.Combine(outRoot, "cancel_export.tif");
            log.Check(ThrowsCancel(() => RasterExportService.Run(layer,
                    new ExportOptions { OutputPath = p2 }, reporter)) && !File.Exists(p2),
                "导出预检取消", "OCE 且无输出");

            string p3 = Path.Combine(outRoot, "cancel_overview.tif");
            log.Check(ThrowsCancel(() => OverviewService.BuildFromLayer(layer, p3, new[] { 2 }, "AVERAGE", reporter))
                      && !File.Exists(p3),
                "金字塔预检取消", "OCE 且无输出");
        }

        private static bool ThrowsCancel(Action action)
        {
            try { action(); return false; }
            catch (OperationCanceledException) { return true; }
            catch { return false; }
        }

        /// <summary>DeleteOutputSet 必须连同 .ovr/.aux.xml/.msk 一起删掉。</summary>
        private static void CheckDeleteOutputSet(TestLog log, string outRoot)
        {
            string path = Path.Combine(outRoot, "sidecar_demo.tif");
            var extras = new[] { "", ".ovr", ".aux.xml", ".msk" };
            foreach (var ext in extras) File.WriteAllText(path + ext, "x");

            ProcessingUtil.DeleteOutputSet(path);

            bool allGone = true;
            foreach (var ext in extras) if (File.Exists(path + ext)) allGone = false;
            log.Check(allGone, "失败清理会删除旁文件", "主文件与 3 个旁文件均已删除");
        }

        /// <summary>金字塔两阶段进度：先 0~0.4 复制，再 0.4~1.0 建塔，整体单调不减。</summary>
        private static void CheckTwoPhaseProgress(TestLog log, RasterLayer layer, string outRoot)
        {
            string path = Path.Combine(outRoot, "overview_progress.tif");
            var samples = new List<double>();
            var reporter = new ProgressReporter((p, m) => samples.Add(p), () => false, 0.0);
            try
            {
                OverviewService.BuildFromLayer(layer, path, new[] { 2, 4 }, "AVERAGE", reporter);
            }
            catch (Exception ex)
            {
                log.Fail("金字塔两阶段进度", ex.Message);
                return;
            }

            bool hasCopy = false, hasBuild = false, monotonic = true;
            double last = -1;
            foreach (var v in samples)
            {
                if (v <= 0.40001) hasCopy = true;
                if (v > 0.40001) hasBuild = true;
                if (v + 1e-6 < last) monotonic = false;
                last = v;
            }
            log.Check(hasCopy && hasBuild && monotonic, "金字塔两阶段进度",
                string.Format("采样 {0} 条，复制段={1} 建塔段={2} 单调={3}", samples.Count, hasCopy, hasBuild, monotonic));
        }

        /// <summary>波段子集导出：Bands={2} 应输出单波段。</summary>
        private static void CheckBandSubset(TestLog log, RasterLayer layer, string outRoot)
        {
            string path = Path.Combine(outRoot, "subset_band2.tif");
            try
            {
                WarpService.Run(layer, new WarpRequest
                {
                    OutputPath = path,
                    Bands = new[] { 2 },
                    PixelSize = layer.PixelSizeX
                }, null);
                using (var r = DatasetService.Open(path))
                {
                    // gdalwarp 会把源 alpha 掩膜一并带出，因此按“有效波段数”判定
                    bool ok = r.DisplayBandCount == 1;
                    log.Check(ok, "波段子集导出（-b）",
                        string.Format("有效 {0} 波段（总 {1}，alpha={2}）", r.DisplayBandCount, r.BandCount, r.AlphaBandIndex));
                }
            }
            catch (Exception ex) { log.Fail("波段子集导出（-b）", ex.Message); }
        }

        /// <summary>按行列数重采样：-ts 必须拆成 3 个 token，输出尺寸应精确匹配。</summary>
        private static void CheckSizeOverride(TestLog log, RasterLayer layer, string outRoot)
        {
            string path = Path.Combine(outRoot, "size_512x384.tif");
            try
            {
                WarpService.Run(layer, new WarpRequest
                {
                    OutputPath = path,
                    Width = 512,
                    Height = 384
                }, null);
                using (var r = DatasetService.Open(path))
                {
                    log.Check(r.Width == 512 && r.Height == 384, "按行列数重采样（-ts）",
                        r.Width + "×" + r.Height);
                }
            }
            catch (Exception ex) { log.Fail("按行列数重采样（-ts）", ex.Message); }
        }

        /// <summary>按参考图层范围与网格对齐：输出尺寸应与参考图层相差不超过 1 像素。</summary>
        private static void CheckReferenceAlignment(TestLog log, RasterLayer ms, RasterLayer pan, string outRoot)
        {
            string path = Path.Combine(outRoot, "align_to_pan.tif");
            try
            {
                WarpService.Run(ms, new WarpRequest
                {
                    OutputPath = path,
                    Extent = pan.Extent,
                    PixelSize = pan.PixelSizeX,
                    PixelSizeY = pan.PixelSizeY
                }, null);
                using (var r = DatasetService.Open(path))
                {
                    bool ok = Math.Abs(r.Width - pan.Width) <= 1 && Math.Abs(r.Height - pan.Height) <= 1;
                    log.Check(ok, "按参考图层对齐范围与网格",
                        string.Format("{0}×{1} vs 参考 {2}×{3}", r.Width, r.Height, pan.Width, pan.Height));
                }
            }
            catch (Exception ex) { log.Fail("按参考图层对齐", ex.Message); }
        }

        /// <summary>KMeans 调色板开关：开则写 ColorTable，关则不写。</summary>
        private static void CheckPaletteSwitch(TestLog log, RasterLayer layer, string outRoot)
        {
            string on = Path.Combine(outRoot, "kmeans_palette_on.tif");
            string off = Path.Combine(outRoot, "kmeans_palette_off.tif");
            try
            {
                KMeansService.Run(layer, new KMeansOptions { Bands = new[] { 1, 2, 3 }, K = 4, MaxIterations = 5, WritePalette = true }, on, null);
                KMeansService.Run(layer, new KMeansOptions { Bands = new[] { 1, 2, 3 }, K = 4, MaxIterations = 5, WritePalette = false }, off, null);

                using (var a = DatasetService.Open(on))
                using (var b = DatasetService.Open(off))
                {
                    var ctOn = a.GetColorTable(1);
                    var ctOff = b.GetColorTable(1);
                    bool onHas = ctOn != null;
                    if (ctOn != null) ctOn.Dispose();
                    log.Check(onHas && ctOff == null, "KMeans 调色板开关生效",
                        string.Format("开={0} 关={1}", onHas, ctOff != null));
                }
            }
            catch (Exception ex) { log.Fail("KMeans 调色板开关", ex.Message); }
        }
        /// <summary>输出文件被占用时的检测与建议名（覆盖“无法覆盖已打开图层文件”的修复）。</summary>
        private static void CheckOutputLock(TestLog log, string dataDir, string outRoot)
        {
            string src = Path.Combine(dataDir, "multi.tif");
            if (!File.Exists(src)) { log.Fail("占用检测数据", src); return; }

            using (var layer = DatasetService.Open(src))
            {
                log.Check(!ProcessingUtil.IsFileWritable(src),
                    "已打开的影像被判定为占用", Path.GetFileName(src));

                string existing = Path.Combine(outRoot, "lock_probe.tif");
                File.WriteAllBytes(existing, new byte[] { 0 });
                string suggestion = ProcessingUtil.SuggestFreePath(existing);
                log.Check(!string.Equals(suggestion, existing, StringComparison.OrdinalIgnoreCase)
                          && !File.Exists(suggestion),
                    "为被占用文件生成可用的新名", Path.GetFileName(suggestion));
                ProcessingUtil.DeleteQuietly(existing);
            }

            string free = Path.Combine(outRoot, "lock_free.tif");
            ProcessingUtil.DeleteQuietly(free);
            log.Check(ProcessingUtil.IsFileWritable(free), "不存在的文件视为可写", "ok");
        }

    }
}
