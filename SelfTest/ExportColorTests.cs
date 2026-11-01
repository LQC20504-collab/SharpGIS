using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SharpGIS.Models;
using SharpGIS.Processing;
using SharpGIS.Rendering;
using SharpGIS.Services;

namespace SharpGIS.SelfTest
{
    /// <summary>
    /// 导出格式一致性：同一图层导出为 GeoTIFF 与 Erdas IMG 后，
    /// 有效波段、色彩解释、统计量与渲染结果必须一致。
    /// 覆盖两个历史缺陷：HFA 不保存 ColorInterp、目标格式不支持 alpha 时无效区参与渲染。
    /// </summary>
    internal static class ExportColorTests
    {
        public static void Run(TestLog log, string dataDir, string outDir)
        {
            log.Info("--- 导出格式一致性 ---");
            string src = Path.Combine(dataDir, "TMtest.tif");
            if (!File.Exists(src)) { log.Fail("导出对比数据", src); return; }

            string dir = Path.Combine(outDir, "exportcolor");
            Directory.CreateDirectory(dir);
            string tif = Path.Combine(dir, "same.tif");
            string img = Path.Combine(dir, "same.img");
            ProcessingUtil.DeleteOutputSet(tif);
            ProcessingUtil.DeleteOutputSet(img);

            using (var layer = DatasetService.Open(src))
            {
                if (layer.AlphaBandIndex == 0)
                {
                    log.Fail("导出对比前提：源带 alpha", "AlphaBandIndex=0");
                    return;
                }

                var reporter = new ProgressReporter((p, m) => { }, () => false, 0.0);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                RasterExportService.Run(layer, new ExportOptions
                {
                    OutputPath = tif, Driver = "GTiff", Compression = "LZW", Tiled = true
                }, reporter);
                long tifMs = sw.ElapsedMilliseconds;

                sw.Restart();
                RasterExportService.Run(layer, new ExportOptions
                {
                    OutputPath = img, Driver = "HFA", Compression = "LZW", Tiled = true
                }, reporter);
                long imgMs = sw.ElapsedMilliseconds;

                log.Check(File.Exists(tif) && File.Exists(img), "导出 TIFF 与 IMG",
                    string.Format("TIFF {0}ms / IMG {1}ms", tifMs, imgMs));
                log.Check(!File.Exists(img + ".aux.xml") && !File.Exists(img + ".ovr"),
                    "IMG 导出未产生旁文件", Path.GetDirectoryName(img));

                using (var a = DatasetService.Open(tif))
                using (var b = DatasetService.Open(img))
                {
                    log.Check(a.AlphaBandIndex == 8 && a.DisplayBandCount == 7,
                        "TIFF 保留 alpha（8 波段，有效 7）",
                        a.BandCount + " 波段，alpha=" + a.AlphaBandIndex);

                    log.Check(b.BandCount == 7 && b.AlphaBandIndex == 0,
                        "IMG 丢弃 alpha（7 波段）", b.BandCount + " 波段");
                    log.Check(b.HasNoData && Math.Abs(b.NoDataValue) < 1e-6,
                        "IMG 把无效区写成 NoData=0", b.HasNoData ? b.NoDataValue.ToString("G6") : "未设置");

                    bool interpOk = b.ColorInterps[0] == OSGeo.GDAL.ColorInterp.GCI_RedBand
                                 && b.ColorInterps[1] == OSGeo.GDAL.ColorInterp.GCI_GreenBand
                                 && b.ColorInterps[2] == OSGeo.GDAL.ColorInterp.GCI_BlueBand;
                    log.Check(interpOk, "IMG 还原色彩解释 R/G/B",
                        b.ColorInterps[0] + "/" + b.ColorInterps[1] + "/" + b.ColorInterps[2]);

                    double worstDelta = 0;
                    for (int i = 1; i <= 3; i++)
                    {
                        var sa = a.Stats(i);
                        var sb = b.Stats(i);
                        worstDelta = Math.Max(worstDelta, Math.Abs(sa.Mean - sb.Mean));
                    }
                    log.Check(worstDelta < 0.6, "IMG 与 TIFF 波段均值一致",
                        "最大偏差 " + worstDelta.ToString("F2") + " DN");

                    using (var ra = RenderPipeline.RenderExtent(a.Extent, 320, 320, new[] { a }, out RenderStats _))
                    using (var rb = RenderPipeline.RenderExtent(b.Extent, 320, 320, new[] { b }, out RenderStats _))
                    {
                        ra.Save(Path.Combine(dir, "render_tif.png"), ImageFormat.Png);
                        rb.Save(Path.Combine(dir, "render_img.png"), ImageFormat.Png);
                        double diff = MeanAbsDiff(ra, rb);
                        log.Check(diff < 4.0, "IMG 与 TIFF 渲染外观一致",
                            "平均色差 " + diff.ToString("F2") + " / 255");
                    }
                }
            }
        }

        private static double MeanAbsDiff(Bitmap a, Bitmap b)
        {
            int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
            int sx = Math.Max(1, w / 100), sy = Math.Max(1, h / 100);
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
    }
}
