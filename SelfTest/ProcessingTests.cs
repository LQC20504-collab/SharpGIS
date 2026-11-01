using System;
using System.IO;
using OSGeo.GDAL;
using SharpGIS.Models;
using SharpGIS.Processing;
using SharpGIS.Services;

namespace SharpGIS.SelfTest
{
    /// <summary>处理算子自检：逐个算子跑通并断言产物属性。</summary>
    internal static class ProcessingTests
    {
        public static void Run(TestLog log, string dataDir, string outDir)
        {
            log.Info("--- step=process ---");
            string tm = Path.Combine(dataDir, "TMtest.tif");
            string multi = Path.Combine(dataDir, "multi.tif");
            string pan = Path.Combine(dataDir, "pan.tif");
            if (!File.Exists(tm)) { log.Fail("TMtest.tif 存在", tm); return; }

            string outRoot = Path.Combine(outDir, "out");
            Directory.CreateDirectory(outRoot);

            // 记录数据目录的初始文件集，结束时校验没有任何新增（源数据只读）
            var dataFilesBefore = new System.Collections.Generic.HashSet<string>(
                Directory.GetFiles(dataDir), StringComparer.OrdinalIgnoreCase);

            var progress = new ProgressReporter((p, m) => { }, null, 0.25);

            using (var layer = DatasetService.Open(tm))
            {
                // 1) 导出
                try
                {
                    string p = Path.Combine(outRoot, "export_lzw.tif");
                    RasterExportService.Run(layer, new ExportOptions { OutputPath = p, Compression = "LZW", Tiled = true }, progress);
                    Check(log, "格式导出 GTiff+LZW", p, layer.Width, layer.Height, layer.BandCount);
                }
                catch (Exception ex) { log.Fail("格式导出", ex.Message); }

                // 2) 重投影到 WGS84
                try
                {
                    string p = Path.Combine(outRoot, "warp_epsg4326.tif");
                    WarpService.Run(layer, new WarpRequest
                    {
                        OutputPath = p,
                        TargetSrs = "EPSG:4326",
                        Resample = "双线性"
                    }, progress);
                    using (var w = DatasetService.Open(p))
                    {
                        log.Check(w.HasProjection && w.EpsgText.Contains("4326"), "重投影到 EPSG:4326",
                            w.EpsgText + " " + w.Width + "×" + w.Height);
                        log.Check(w.Width > 10 && w.Height > 10, "重投影结果尺寸合理", w.Width + "×" + w.Height);
                    }
                }
                catch (Exception ex) { log.Fail("重投影", ex.Message); }

                // 3) 按范围裁剪
                try
                {
                    string p = Path.Combine(outRoot, "crop_center.tif");
                    var e = layer.Extent;
                    var crop = new GeoRect(
                        e.CenterX - e.Width * 0.25, e.CenterY - e.Height * 0.25,
                        e.CenterX + e.Width * 0.25, e.CenterY + e.Height * 0.25);
                    WarpService.Run(layer, new WarpRequest { OutputPath = p, Extent = crop, PixelSize = layer.PixelSizeX }, progress);
                    using (var c = DatasetService.Open(p))
                    {
                        log.Check(Math.Abs(c.Width - layer.Width / 2.0) < 6, "按范围裁剪尺寸约一半",
                            c.Width + "×" + c.Height + " (原 " + layer.Width + "×" + layer.Height + ")");
                    }
                }
                catch (Exception ex) { log.Fail("按范围裁剪", ex.Message); }

                // 4) 金字塔
                try
                {
                    string p = Path.Combine(outRoot, "overview.tif");
                    OverviewService.BuildFromLayer(layer, p, new[] { 2, 4, 8 }, "AVERAGE", progress);
                    using (var ds = Gdal.Open(p, Access.GA_ReadOnly))
                    {
                        var band = ds.GetRasterBand(1);
                        int count = band.GetOverviewCount();
                        band.Dispose();
                        ds.Dispose();
                        log.Check(count >= 3, "生成金字塔（3 级）", "overview count=" + count);
                    }
                }
                catch (Exception ex) { log.Fail("生成金字塔", ex.Message); }

                // 5) 波段计算器
                try
                {
                    string p = Path.Combine(outRoot, "bandcalc_nd.tif");
                    BandCalculator.Run(layer, "normalizedDifference(b4, b3)", p, progress);
                    using (var r = DatasetService.Open(p))
                    {
                        bool typeOk = r.BandTypes[0] == DataType.GDT_Float32;
                        var st = r.Stats(1);
                        log.Check(typeOk && st.Max <= 1.001 && st.Min >= -1.001, "波段计算器 ND(b4,b3)",
                            string.Format("type={0} min={1:F3} max={2:F3}", r.BandTypes[0], st.Min, st.Max));
                    }
                }
                catch (Exception ex) { log.Fail("波段计算器", ex.Message); }

                // 6) 表达式错误应被拦截
                string err = BandCalculator.Validate("b1 +", layer.BandCount);
                log.Check(!string.IsNullOrEmpty(err), "非法表达式被拦截", err ?? "未报错");
                string err2 = BandCalculator.Validate("b99 * 2", layer.BandCount);
                log.Check(!string.IsNullOrEmpty(err2), "越界波段被拦截", err2 ?? "未报错");

                // 7) DEM 山体阴影
                try
                {
                    string p = Path.Combine(outRoot, "dem_hillshade.tif");
                    DemService.Run(layer, new DemParams { Band = 1, Algorithm = "hillshade" }, p, progress);
                    Check(log, "DEM 山体阴影", p, layer.Width, layer.Height, 1);
                }
                catch (Exception ex) { log.Fail("DEM 山体阴影", ex.Message); }

                // 8) DEM 彩色渲染
                try
                {
                    string p = Path.Combine(outRoot, "dem_colorrelief.tif");
                    DemService.Run(layer, new DemParams
                    {
                        Band = 1,
                        Algorithm = "color-relief",
                        Ramp = ColorRamp.Terrain,
                        RampMin = 0,
                        RampMax = 255
                    }, p, progress);
                    using (var r = DatasetService.Open(p))
                    {
                        log.Check(r.BandCount >= 3, "DEM 彩色渲染为 3 波段", r.BandCount + " 波段");
                    }
                }
                catch (Exception ex) { log.Fail("DEM 彩色渲染", ex.Message); }

                // 9) 滤波
                foreach (var kind in new[] { FilterKind.Mean, FilterKind.Median, FilterKind.Gauss, FilterKind.Sharpen, FilterKind.Sobel })
                {
                    try
                    {
                        string p = Path.Combine(outRoot, "filter_" + kind + ".tif");
                        FilterService.Run(layer, 1, kind, 3, 0, p, progress);
                        using (var r = DatasetService.Open(p))
                        {
                            var st = r.Stats(1);
                            log.Check(r.Width == layer.Width && st.Max > st.Min, "滤波 " + kind,
                                string.Format("{0}×{1} min={2:F2} max={3:F2}", r.Width, r.Height, st.Min, st.Max));
                        }
                    }
                    catch (Exception ex) { log.Fail("滤波 " + kind, ex.Message); }
                }

                // 10) 增强
                try
                {
                    string p = Path.Combine(outRoot, "enhance_stretch.tif");
                    EnhanceService.Run(layer, EnhanceKind.StretchToByte, false, 1, 2, 2, p, progress);
                    using (var r = DatasetService.Open(p))
                        log.Check(r.BandTypes[0] == DataType.GDT_Byte, "线性拉伸到 8bit", r.TypeText);
                }
                catch (Exception ex) { log.Fail("线性拉伸", ex.Message); }

                try
                {
                    string p = Path.Combine(outRoot, "enhance_equalize.tif");
                    EnhanceService.Run(layer, EnhanceKind.EqualizeToByte, false, 1, 2, 2, p, progress);
                    using (var r = DatasetService.Open(p))
                    {
                        var st = r.Stats(1);
                        log.Check(r.BandTypes[0] == DataType.GDT_Byte && st.Max > 200, "直方图均衡",
                            string.Format("max={0:F0}", st.Max));
                    }
                }
                catch (Exception ex) { log.Fail("直方图均衡", ex.Message); }
            }

            // 11) 融合
            if (File.Exists(multi) && File.Exists(pan))
            {
                using (var ms = DatasetService.Open(multi))
                using (var pn = DatasetService.Open(pan))
                {
                    foreach (var method in new[] { FusionMethod.Brovey, FusionMethod.IHS, FusionMethod.Hpf })
                    {
                        try
                        {
                            string p = Path.Combine(outRoot, "fusion_" + method + ".tif");
                            FusionService.Run(ms, new[] { 1, 2, 3 }, pn, 1, method, p, progress);
                            using (var r = DatasetService.Open(p))
                            {
                                var st = r.Stats(1);
                                log.Check(r.Width == pn.Width && r.BandCount == 3 && st.Max > st.Min, "融合 " + method,
                                    string.Format("{0}×{1} {2} 波段", r.Width, r.Height, r.BandCount));
                            }
                        }
                        catch (Exception ex) { log.Fail("融合 " + method, ex.Message); }
                    }
                }
            }

            // 12) KMeans 分类
            try
            {
                using (var small = DatasetService.Open(File.Exists(multi) ? multi : tm))
                {
                    string p = Path.Combine(outRoot, "kmeans_6.tif");
                    int bands = Math.Min(3, small.BandCount);
                    var result = KMeansService.Run(small, new KMeansOptions
                    {
                        Bands = bands == 3 ? new[] { 1, 2, 3 } : new[] { 1 },
                        K = 6,
                        MaxIterations = 15
                    }, p, progress);
                    using (var r = DatasetService.Open(p))
                    {
                        var st = r.Stats(1);
                        log.Check(r.BandTypes[0] == DataType.GDT_Byte && st.Max <= 5, "KMeans 非监督分类（6 类）",
                            string.Format("类别范围 {0:F0}~{1:F0}，样本占比 {2}", st.Min, st.Max,
                                string.Join("/", Array.ConvertAll(result.SampleCounts, c => c.ToString()))));
                    }
                }
            }
            catch (Exception ex) { log.Fail("KMeans 分类", ex.Message); }

            // 12.4) 取消语义、旁文件清理、两阶段进度与新增算子
            QualityTests.Run(log, dataDir, outRoot);

            // 12.45) 导出格式一致性（TIFF vs IMG 的颜色/统计/波段）
            ExportColorTests.Run(log, dataDir, outRoot);

            // 12.46) 占位符与波段映射
            BandMappingTests.Run(log, dataDir, outRoot);

            // 12.5) 生成若干结果预览图（便于目视核对）
            PreviewToPng(Path.Combine(dataDir, "multi.tif"), outRoot);
            PreviewToPng(Path.Combine(dataDir, "pan.tif"), outRoot);
            foreach (var name in new[] { "dem_hillshade.tif", "bandcalc_nd.tif", "filter_Sobel.tif", "kmeans_6.tif",
                     "fusion_Brovey.tif", "fusion_IHS.tif", "fusion_Hpf.tif" })
                PreviewToPng(Path.Combine(outRoot, name), outRoot);

            // 13) 源数据只读校验
            log.Check(!File.Exists(tm + ".aux.xml") && !File.Exists(tm + ".ovr"), "源数据目录未被写入辅助文件");
            var newFiles = new System.Collections.Generic.List<string>();
            foreach (var f in Directory.GetFiles(dataDir))
                if (!dataFilesBefore.Contains(f)) newFiles.Add(Path.GetFileName(f));
            log.Check(newFiles.Count == 0, "数据目录未被写入任何新文件",
                newFiles.Count == 0 ? dataDir : string.Join(", ", newFiles));
        }

        /// <summary>把影像渲染成 PNG 预览（永远写到输出目录，绝不写回数据目录）。</summary>
        private static void PreviewToPng(string path, string outDir)
        {
            if (!File.Exists(path)) return;
            try
            {
                using (var layer = DatasetService.Open(path))
                {
                    Rendering.RenderStats st;
                    int w = Math.Min(800, Math.Max(200, layer.Width));
                    int h = (int)Math.Round(w * layer.Height / (double)layer.Width);
                    string target = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + "_preview.png");
                    using (var bmp = Rendering.RenderPipeline.RenderExtent(layer.Extent, w, h, new[] { layer }, out st))
                        bmp.Save(target, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
        }

        private static void Check(TestLog log, string name, string path, int w, int h, int bands)
        {
            if (!File.Exists(path)) { log.Fail(name, "输出文件不存在: " + path); return; }
            try
            {
                using (var r = DatasetService.Open(path))
                {
                    var st = r.Stats(1);
                    bool ok = r.Width == w && r.Height == h && r.BandCount == bands && st.Max >= st.Min;
                    log.Check(ok, name, string.Format("{0}×{1} · {2} 波段 · {3} · {4:F0}KB",
                        r.Width, r.Height, r.BandCount, r.TypeText,
                        new FileInfo(path).Length / 1024.0));
                }
            }
            catch (Exception ex)
            {
                log.Fail(name, "打开输出失败: " + ex.Message);
            }
        }
    }
}
