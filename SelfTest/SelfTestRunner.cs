using System;
using System.IO;
using SharpGIS.Models;
using SharpGIS.Services;

namespace SharpGIS.SelfTest
{
    /// <summary>无交互自检入口。以退出码作为判定依据：0=全部通过，1=有失败，2=GDAL 不可用。</summary>
    internal static class SelfTestRunner
    {
        /// <summary>
        /// 解析测试数据目录：命令行 --data 优先，其次环境变量 SHARPGIS_TESTDATA，
        /// 最后是当前目录下的 testdata。仓库不附带测试影像，需自备。
        /// </summary>
        private static string ResolveDataDir(CommandLine cli)
        {
            string fromCli = cli.Get("data");
            if (!string.IsNullOrWhiteSpace(fromCli)) return fromCli;

            string fromEnv = Environment.GetEnvironmentVariable("SHARPGIS_TESTDATA");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

            return Path.Combine(Environment.CurrentDirectory, "testdata");
        }

        public static int Run(CommandLine cli)
        {
            if (!GdalBootstrap.Usable)
            {
                ConsoleBridge.Write("GDAL 不可用：" + GdalBootstrap.LastError);
                return 2;
            }

            string step = (cli.Get("step", "all") ?? "all").ToLowerInvariant();
            string outDir = cli.Get("out");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(AppContext.BaseDirectory, "artifacts", "selftest",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss"));

            string dataDir = ResolveDataDir(cli);
            var log = new TestLog(outDir);
            log.Info("SharpGIS 自检  step=" + step);
            log.Info("GDAL=" + GdalBootstrap.GdalVersion + "  输出=" + outDir + "  数据=" + dataDir);

            bool runAll = step == "all";

            try
            {
                if (runAll || step == "open") TestOpen(log, dataDir);
                if (runAll || step == "render") RenderTests.Run(log, dataDir, outDir);
                if (runAll || step == "process") ProcessingTests.Run(log, dataDir, outDir);
                if (runAll || step == "camarkov") CaMarkovTests.Run(log, outDir);
                if (runAll || step == "ui") UiTests.Run(log, dataDir, outDir);
                if (runAll || step == "audit") AuditTests.Run(log, dataDir, outDir);
                if (runAll || step == "lang") LangTests.Run(log, dataDir, outDir);
            }
            catch (Exception ex)
            {
                log.Fail("自检异常", ex.GetType().Name + ": " + ex.Message);
                log.Info(ex.ToString());
            }

            log.Save();
            return log.Failures == 0 ? 0 : 1;
        }

        // ---------------- step=open ----------------

        private static void TestOpen(TestLog log, string dataDir)
        {
            log.Info("--- step=open ---");
            string tm = Path.Combine(dataDir, "TMtest.tif");
            if (!File.Exists(tm))
            {
                log.Fail("TMtest.tif 存在", tm);
                return;
            }

            string auxPath = tm + ".aux.xml";
            bool auxExisted = File.Exists(auxPath);
            var t0 = DateTime.Now;

            RasterLayer layer;
            try
            {
                layer = DatasetService.Open(tm);
            }
            catch (Exception ex)
            {
                log.Fail("打开 TMtest.tif", ex.Message);
                return;
            }

            using (layer)
            {
                double ms = (DateTime.Now - t0).TotalMilliseconds;
                log.Info(DatasetService.DescribeMetadata(layer));
                log.Info("打开耗时: " + ms.ToString("F0") + " ms");

                log.Check(layer.Width >= 2048 && layer.Height >= 2048 &&
                          Math.Abs(layer.Width - layer.Height) <= 4,
                    "影像尺寸（旋转正视化后为外接正方形）",
                    layer.Width + "×" + layer.Height + (layer.IsRectified ? " [已正视化]" : ""));
                log.Check(layer.DisplayBandCount == 7, "有效波段 7（另含 alpha 掩膜）",
                    layer.DisplayBandCount + (layer.AlphaBandIndex > 0 ? " + alpha" : ""));
                log.Check(layer.BandTypes[0] == OSGeo.GDAL.DataType.GDT_Byte, "数据类型 Byte",
                    layer.BandTypes[0].ToString());
                log.Check(layer.HasGeoTransform && layer.PixelSizeX > 20 && layer.PixelSizeX < 40,
                    "像元尺寸约 30m", layer.PixelSizeX.ToString("G8"));
                log.Check(layer.Extent.IsValid, "地理范围有效", layer.Extent.ToString());
                log.Check(layer.Extent.Width > 55000 && layer.Extent.Width < 80000,
                    "范围宽度合理（旋转影像含外扩）", layer.Extent.Width.ToString("F1"));
                log.Info("是否旋转正视化: " + layer.IsRectified + "；投影: " + layer.EpsgText);
                log.Info("WKT 摘要: " + ProjectionUtil.ShortWkt(layer.ProjectionWkt, 400));

                // 抽样统计与直方图（走 ReadRaster 抽样，不调用 GDAL GetStatistics，因此不会产生 PAM 旁文件）
                var stats = layer.Stats(1);
                log.Check(stats != null && stats.Max >= stats.Min, "波段 1 统计",
                    string.Format("min={0:G6} max={1:G6} mean={2:G6} std={3:G6}",
                        stats.Min, stats.Max, stats.Mean, stats.StdDev));
                log.Check(stats.HistogramTotal > 0, "波段 1 直方图非空", stats.HistogramTotal.ToString());

                var px = layer.ReadPixel(1024, 1024);
                bool sane = px.Length == layer.BandCount;
                for (int i = 0; i < px.Length; i++)
                    if (double.IsNaN(px[i]) || px[i] < 0 || px[i] > 255) sane = false;
                log.Check(sane, "像素探针 (1024,1024) 7 波段取值合理",
                    string.Join(",", Array.ConvertAll(px, v => v.ToString("F0"))));

                var thumb = layer.BuildThumbnail(96, 72);
                string thumbPath = Path.Combine(Path.GetDirectoryName(log.LogPath), "thumb_tmtest.png");
                thumb.Save(thumbPath, System.Drawing.Imaging.ImageFormat.Png);
                thumb.Dispose();
                log.Check(File.Exists(thumbPath), "缩略图生成", thumbPath);
            }

            bool auxNow = File.Exists(auxPath);
            log.Check(!(auxNow && !auxExisted), "源目录未被写入 .aux.xml", auxPath);

            // 单波段与多光谱小图
            string multi = Path.Combine(dataDir, "multi.tif");
            if (File.Exists(multi))
            {
                using (var l2 = DatasetService.Open(multi))
                {
                    log.Check(l2.Width == 400 && l2.BandCount == 3, "multi.tif 400×400 / 3 波段",
                        l2.Width + "×" + l2.Height + " / " + l2.BandCount);
                }
            }
            string pan = Path.Combine(dataDir, "pan.tif");
            if (File.Exists(pan))
            {
                using (var l3 = DatasetService.Open(pan))
                {
                    log.Check(l3.BandCount == 1, "pan.tif 单波段", l3.BandCount.ToString());
                }
            }
        }
    }

}
