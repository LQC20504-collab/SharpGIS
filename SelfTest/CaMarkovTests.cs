using System;
using System.Collections.Generic;
using System.IO;
using OSGeo.GDAL;
using OSGeo.OSR;
using SharpGIS.Models;
using SharpGIS.Processing;
using SharpGIS.Services;

namespace SharpGIS.SelfTest
{
    /// <summary>
    /// CA-Markov 功能自检：合成一张确定的土地利用序列，跑通 analyze / predict / 模型复用，
    /// 并断言“各类面积 == 转移预算列和”“NoData 不变”“取消不留文件”等硬性不变量。
    /// 没有可用 Python 环境时只校验定位逻辑并跳过端到端部分。
    /// </summary>
    internal static class CaMarkovTests
    {
        private const int W = 64;
        private const int H = 64;

        public static void Run(TestLog log, string outDir)
        {
            log.Info("--- CA-Markov 引擎 ---");

            var env = PythonLocator.Find(true);
            log.Check(!string.IsNullOrEmpty(env.ExePath), "Python 解释器定位", env.Describe());
            log.Check(CaMarkovEngine.ScriptAvailable, "引擎脚本随程序输出", CaMarkovEngine.ScriptPath);
            if (!env.Usable)
            {
                log.Info("跳过 CA-Markov 端到端用例：当前 Python 环境不可用（"
                    + env.Describe().Replace("\r\n", " ") + "）");
                return;
            }

            string dir = Path.Combine(outDir, "camarkov");
            Directory.CreateDirectory(dir);

            try
            {
                RunEndToEnd(log, dir);
            }
            catch (Exception ex)
            {
                log.Fail("CA-Markov 端到端", ex.GetType().Name + ": " + ex.Message);
                log.Info(ex.ToString());
            }
        }

        private static void RunEndToEnd(TestLog log, string dir)
        {
            string wkt = Epsg32650();
            double[] gt = { 500000, 30, 0, 3200000, 0, -30 };

            // ---- 合成三期土地利用：左侧耕地、右上森林、底部水体，顶部 4 行是 NoData
            var p1 = new byte[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    byte v = 1;
                    if (y < 4) v = 0;
                    else if (y < 32 && x >= 32) v = 2;
                    else if (y >= 52) v = 3;
                    p1[y * W + x] = v;
                }
            }

            var rnd = new Random(11);
            var p2 = (byte[])p1.Clone();
            for (int y = 4; y < H; y++)
                for (int x = 0; x < 12; x++)
                    if (p1[y * W + x] == 1 && rnd.NextDouble() < 0.85) p2[y * W + x] = 3;

            var p3 = (byte[])p2.Clone();
            for (int y = 4; y < H; y++)
                for (int x = 0; x < 20; x++)
                    if (p2[y * W + x] == 1 && rnd.NextDouble() < 0.85) p3[y * W + x] = 3;

            string f1 = Path.Combine(dir, "lulc_2010.tif");
            string f2 = Path.Combine(dir, "lulc_2015.tif");
            string f3 = Path.Combine(dir, "lulc_2020.tif");
            WriteByte(f1, p1, gt, wkt);
            WriteByte(f2, p2, gt, wkt);
            WriteByte(f3, p3, gt, wkt);

            var dem = new float[W * H];
            var slope = new float[W * H];
            var rand = new Random(7);
            for (int i = 0; i < dem.Length; i++)
            {
                dem[i] = (float)(rand.NextDouble() * 500);
                slope[i] = (float)(rand.NextDouble() * 20);
            }
            string fdem = Path.Combine(dir, "dem.tif");
            string fslope = Path.Combine(dir, "slope.tif");
            WriteFloat(fdem, dem, gt, wkt);
            WriteFloat(fslope, slope, gt, wkt);

            var p = BuildParams(f1, f2, f3, fdem, fslope);
            p.SaveModelPath = Path.Combine(dir, "model.npz");

            // ---- 1) 分析模式
            var analyze = CaMarkovEngine.Analyze(p, Path.Combine(dir, "figs_analyze"), Quiet());
            log.Check(analyze.Counts != null && analyze.Counts.Length == 4,
                "分析：返回 4×4 转移矩阵", RowShape(analyze.Counts));
            double rowSum = 0;
            for (int j = 0; j < analyze.Probability[1].Length; j++) rowSum += analyze.Probability[1][j];
            log.Check(Math.Abs(rowSum - 1.0) < 1e-9, "分析：概率行和 = 1", rowSum.ToString("F6"));
            log.Check(analyze.Figure("transition") != null && File.Exists(analyze.Figure("transition")),
                "分析：生成转移矩阵图");

            // ---- 2) 完整预测
            string raster = Path.Combine(dir, "pred_2025.tif");
            string report = Path.Combine(dir, "pred_2025_report.txt");
            string figs = Path.Combine(dir, "pred_2025_figs");
            var request = new CaMarkovRequest
            {
                Params = p,
                RasterPath = raster,
                ReportPath = report,
                FigureDirectory = figs,
                ModelToSave = p.SaveModelPath,
                TimeoutMinutes = 10
            };
            var result = CaMarkovEngine.Run(request, Quiet());

            log.Check(File.Exists(raster), "预测：输出栅格存在", raster);
            log.Check(File.Exists(report), "预测：报告存在");
            log.Check(File.Exists(p.SaveModelPath), "预测：模型已保存",
                File.Exists(p.SaveModelPath) ? new FileInfo(p.SaveModelPath).Length / 1024 + " KB" : "");

            int[] counts = CountClasses(raster, 4);
            int[] colSum = ColumnSums(result.Budget);
            bool areasMatch = true;
            string detail = "";
            for (int i = 1; i < 4; i++)
            {
                if (counts[i] != colSum[i]) areasMatch = false;
                detail += string.Format("{0}: {1}/{2}  ", i, counts[i], colSum[i]);
            }
            log.Check(areasMatch, "预测：各类像元数 == 转移预算列和", detail.Trim());

            log.Check(counts[0] == W * 4, "预测：NoData 位置保持不变", counts[0] + " / " + (W * 4));

            log.Check(result.Accuracy != null, "预测：有精度指标",
                result.Accuracy == null ? "" : result.AccuracyText);
            log.Check(result.BaselineOa.HasValue, "预测：有未变化基线 OA",
                result.BaselineOa.HasValue ? result.BaselineOa.Value.ToString("F4") : "");
            log.Check(result.FeatureImportance.Count == 2, "预测：特征重要性 2 项",
                result.FeatureImportance.Count > 0
                    ? string.Format("{0} {1:F3}", result.FeatureImportance[0].Name, result.FeatureImportance[0].Importance)
                    : "");
            log.Check(result.Classes.Count == 4, "预测：类别统计完整", result.Classes.Count + " 类");

            string[] keys = { "transition", "area", "confusion", "importance", "compare", "hotspot" };
            foreach (var key in keys)
            {
                string path = result.Figure(key);
                log.Check(!string.IsNullOrEmpty(path) && File.Exists(path), "预测：图表 " + key);
            }

            using (var layer = DatasetService.Open(raster))
            {
                log.Check(layer.Width == W && layer.Height == H && layer.BandCount == 1,
                    "预测：输出尺寸与波段", layer.SizeText + " · " + layer.BandCount + " 波段");
                log.Check(layer.HasNoData && Math.Abs(layer.NoDataValue) < 1e-9,
                    "预测：NoData 已写入", layer.NoDataValue.ToString("G4"));
                log.Check(layer.HasProjection, "预测：投影已继承", layer.EpsgText);
            }

            // ---- 3) 模型复用 + 多步
            p.ModelPath = p.SaveModelPath;
            p.SaveModelPath = null;
            p.Validate = false;
            p.Steps = 2;
            string raster2 = Path.Combine(dir, "pred_2030.tif");
            var reuse = CaMarkovEngine.Run(new CaMarkovRequest
            {
                Params = p,
                RasterPath = raster2,
                ReportPath = null,
                FigureDirectory = Path.Combine(dir, "pred_2030_figs"),
                TimeoutMinutes = 10
            }, Quiet());
            log.Check(reuse.RfSummary != null && reuse.RfSummary.Contains("载入"),
                "复用：直接从磁盘载入模型", reuse.RfSummary);

            int[] counts2 = CountClasses(raster2, 4);
            int[] colSum2 = ColumnSums(reuse.Budget);
            bool stepOk = true;
            for (int i = 1; i < 4; i++) if (counts2[i] != colSum2[i]) stepOk = false;
            log.Check(stepOk, "复用：第 2 步面积仍等于该步预算列和",
                string.Format("{0} vs {1}", counts2[1], colSum2[1]));

            // ---- 4) 取消：不留任何文件
            string cancelRaster = Path.Combine(dir, "cancelled.tif");
            bool cancelled = false;
            try
            {
                var reporter = new ProgressReporter((v, m) => { }, () => true, 0.01);
                CaMarkovEngine.Run(new CaMarkovRequest
                {
                    Params = p,
                    RasterPath = cancelRaster,
                    TimeoutMinutes = 5
                }, reporter);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            log.Check(cancelled, "取消：抛出 OperationCanceledException");
            log.Check(!File.Exists(cancelRaster), "取消：未留下半成品文件");

            // ---- 5) 参数 JSON 往返
            var job = p.ToJob(new Dictionary<string, object> { { "metrics", "m.json" } });
            string json = CaMarkovParams.SerializeJson(job);
            var back = new System.Web.Script.Serialization.JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(json);
            log.Check(back.ContainsKey("periods") && CaMarkovResult.AsList(back["periods"]).Count == 3,
                "参数：JSON 往返保留三期数据");
            log.Check(json.Contains("\\u8015\\u5730") || json.Contains("耕地"),
                "参数：中文类别名已写入 JSON");
        }

        private static CaMarkovParams BuildParams(string f1, string f2, string f3, string fdem, string fslope)
        {
            var p = new CaMarkovParams
            {
                Mode = CaPredictionMode.Predict,
                NoData = 0,
                ClassCount = 4,
                ClassNames = new[] { "无效值", "耕地", "森林", "水体" },
                TransitionSource = CaTransitionSource.LastTwoAverage,
                Steps = 1,
                NeighborhoodSize = 5,
                RecomputeNeighborhood = true,
                Suitability = CaSuitabilityMode.RandomForest,
                Trees = 8,
                MaxDepth = 6,
                Imbalance = 2.0,
                MinSamples = 50,
                Seed = 42,
                SuitabilityWeight = 0.6,
                NeighborhoodWeight = 0.4,
                ModulateByProbability = true,
                Validate = true,
                WriteReport = true,
                WriteCompareImage = true,
                WriteHotspotImage = true,
            };
            p.Periods.Add(new CaPeriod { Path = f1, Band = 1, Year = 2010 });
            p.Periods.Add(new CaPeriod { Path = f2, Band = 1, Year = 2015 });
            p.Periods.Add(new CaPeriod { Path = f3, Band = 1, Year = 2020 });
            p.TrainingDrivers.Add(new CaDriver { Name = "dem", Path = fdem, Band = 1 });
            p.TrainingDrivers.Add(new CaDriver { Name = "slope", Path = fslope, Band = 1 });
            return p;
        }

        private static ProgressReporter Quiet()
        {
            return new ProgressReporter((v, m) => { }, null, 1.0);
        }

        private static string RowShape(double[][] m)
        {
            if (m == null) return "null";
            return m.Length + "×" + (m.Length > 0 ? m[0].Length : 0);
        }

        private static int[] ColumnSums(double[][] m)
        {
            int n = m.Length;
            var sums = new int[n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    sums[j] += (int)Math.Round(m[i][j]);
            return sums;
        }

        private static int[] CountClasses(string path, int n)
        {
            var counts = new int[Math.Max(4, n)];
            using (var ds = Gdal.Open(path, Access.GA_ReadOnly))
            {
                var band = ds.GetRasterBand(1);
                try
                {
                    var buf = new byte[ds.RasterXSize * ds.RasterYSize];
                    band.ReadRaster(0, 0, ds.RasterXSize, ds.RasterYSize, buf,
                        ds.RasterXSize, ds.RasterYSize, 0, 0);
                    foreach (var v in buf) counts[v]++;
                }
                finally { band.Dispose(); }
            }
            return counts;
        }

        private static string Epsg32650()
        {
            using (var sr = new SpatialReference(""))
            {
                sr.ImportFromEPSG(32650);
                string wkt;
                sr.ExportToWkt(out wkt, null);
                return wkt;
            }
        }

        private static void WriteByte(string path, byte[] data, double[] gt, string wkt)
        {
            var driver = Gdal.GetDriverByName("GTiff");
            using (var ds = driver.Create(path, W, H, 1, DataType.GDT_Byte,
                new[] { "TILED=YES", "COMPRESS=DEFLATE" }))
            {
                ds.SetGeoTransform(gt);
                if (!string.IsNullOrEmpty(wkt)) ds.SetProjection(wkt);
                var band = ds.GetRasterBand(1);
                try
                {
                    band.SetNoDataValue(0);
                    band.WriteRaster(0, 0, W, H, data, W, H, 0, 0);
                    band.FlushCache();
                }
                finally { band.Dispose(); }
                ds.FlushCache();
            }
        }

        private static void WriteFloat(string path, float[] data, double[] gt, string wkt)
        {
            var driver = Gdal.GetDriverByName("GTiff");
            using (var ds = driver.Create(path, W, H, 1, DataType.GDT_Float32,
                new[] { "TILED=YES", "COMPRESS=DEFLATE" }))
            {
                ds.SetGeoTransform(gt);
                if (!string.IsNullOrEmpty(wkt)) ds.SetProjection(wkt);
                var band = ds.GetRasterBand(1);
                try
                {
                    band.SetNoDataValue(-9999);
                    band.WriteRaster(0, 0, W, H, data, W, H, 0, 0);
                    band.FlushCache();
                }
                finally { band.Dispose(); }
                ds.FlushCache();
            }
        }
    }
}
