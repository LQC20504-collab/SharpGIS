using System;
using System.IO;
using OSGeo.GDAL;
using SharpGIS.Models;
using SharpGIS.Processing;
using SharpGIS.Services;

namespace SharpGIS.SelfTest
{
    /// <summary>
    /// 波段映射自检：占位符 b1/b2 与实际（图层, 波段）的绑定、跨文件读取、网格一致性与各类拒绝条件。
    /// </summary>
    internal static class BandMappingTests
    {
        public static void Run(TestLog log, string dataDir, string outRoot)
        {
            log.Info("--- 波段映射 ---");
            string tm = Path.Combine(dataDir, "TMtest.tif");
            string multi = Path.Combine(dataDir, "multi.tif");
            if (!File.Exists(tm)) { log.Fail("波段映射数据", tm); return; }

            string dir = Path.Combine(outRoot, "bandmap");
            Directory.CreateDirectory(dir);
            var reporter = new ProgressReporter((p, m) => { }, () => false, 0.0);

            using (var layer = DatasetService.Open(tm))
            using (var copy = DatasetService.Open(tm))     // 同网格但独立的第二个数据集
            {
                // 1) 默认映射与原行为逐像素一致
                string a = Path.Combine(dir, "id_old.tif");
                string b = Path.Combine(dir, "id_new.tif");
                ProcessingUtil.DeleteOutputSet(a);
                ProcessingUtil.DeleteOutputSet(b);
                BandCalculator.Run(layer, "normalizedDifference(b4, b3)", a, reporter);
                BandCalculator.Run(layer, "normalizedDifference(b4, b3)", BandMapping.Identity(layer), b, reporter);

                int diff = CompareBands(a, b);
                log.Check(diff == 0, "默认 1:1 映射与旧行为逐像素一致", diff + " 个像素不同");

                // 2) 槽位数用 BandCount（含 alpha）：b8 是掩膜波段，仍能取到值
                string alphaOut = Path.Combine(dir, "alpha.tif");
                ProcessingUtil.DeleteOutputSet(alphaOut);
                BandCalculator.Run(layer, "b8", alphaOut, reporter);
                var alphaValues = ReadBand(alphaOut, 1);
                int valid = 0;
                foreach (var v in alphaValues) if (!float.IsNaN(v)) valid++;
                log.Check(valid > 0, "b8 取到 alpha 波段（槽位数用 BandCount）",
                    valid + " / " + alphaValues.Length + " 个有效像素");

                // 3) 跨文件同网格：b1 来自 layer，b2 来自 copy，应等于 b1 + b1
                var map = new BandMapping(layer.BandCount);
                map.Set(1, new BandSource(layer, 1));
                map.Set(2, new BandSource(copy, 1));

                string cross = Path.Combine(dir, "cross.tif");
                string self = Path.Combine(dir, "self.tif");
                ProcessingUtil.DeleteOutputSet(cross);
                ProcessingUtil.DeleteOutputSet(self);
                BandCalculator.Run(layer, "b1 + b2", map, cross, reporter);
                BandCalculator.Run(layer, "b1 + b1", self, reporter);
                log.Check(CompareBands(cross, self) == 0, "跨文件读取结果正确（b1+副本b1 == b1+b1）",
                    Path.GetFileName(cross));

                // 4) 同一源被两个槽位引用：不重复读、不报错，结果仍等于 b1 + b1
                var dup = new BandMapping(layer.BandCount);
                dup.Set(1, new BandSource(copy, 1));
                dup.Set(2, new BandSource(copy, 1));
                string dupOut = Path.Combine(dir, "dup.tif");
                ProcessingUtil.DeleteOutputSet(dupOut);
                BandCalculator.Run(layer, "b1 + b2", dup, dupOut, reporter);
                log.Check(CompareBands(dupOut, self) == 0, "同一波段被两个槽位引用时结果正确",
                    Path.GetFileName(dupOut));

                // 5) 网格不一致被拒
                if (File.Exists(multi))
                {
                    using (var small = DatasetService.Open(multi))
                    {
                        var bad = BandMapping.Identity(layer);
                        bad.Set(1, new BandSource(small, 1));

                        string err = BandCalculator.Validate("b1 + 1", bad, layer);
                        log.Check(err != null && err.Contains("网格不一致"), "网格不一致被拦截",
                            err ?? "未报错");

                        bool threw = false;
                        string outPath = Path.Combine(dir, "should_not_exist.tif");
                        ProcessingUtil.DeleteOutputSet(outPath);
                        try { BandCalculator.Run(layer, "b1 + 1", bad, outPath, reporter); }
                        catch (InvalidOperationException) { threw = true; }
                        log.Check(threw && !File.Exists(outPath), "网格不一致时 Run 抛异常且不产生产物",
                            threw ? "已抛出" : "未抛出");
                    }
                }

                // 6) 占位符未映射
                var partial = new BandMapping(layer.BandCount);
                partial.Set(1, new BandSource(layer, 1));
                string missing = BandCalculator.Validate("b1 + b2", partial, layer);
                log.Check(missing != null && missing.Contains("b2") && missing.Contains("还没有指定"),
                    "未映射的占位符被拦截", missing ?? "未报错");

                // 7) 槽位越界（b9 > 8）
                string overflow = BandCalculator.Validate("b9 * 2", BandMapping.Identity(layer), layer);
                log.Check(overflow != null && overflow.Contains("9"), "越界占位符被拦截", overflow ?? "未报错");

                // 8) 同网格同投影时不产生“投影不同”提示
                var sameProj = new BandMapping(layer.BandCount);
                sameProj.Set(1, new BandSource(copy, 1));
                bool gridSame;
                string reason;
                gridSame = RasterGrid.Same(layer, copy, out reason);
                log.Check(gridSame && RasterGrid.SameProjection(layer, copy) &&
                          sameProj.ProjectionMismatches(layer).Length == 0,
                    "同文件两实例网格/投影一致且无提示", reason ?? "ok");
            }

            // 9) 不同网格判定：multi（400²）与 TMtest（2378²）
            if (File.Exists(multi))
            {
                using (var l1 = DatasetService.Open(tm))
                using (var small = DatasetService.Open(multi))
                {
                    string reason;
                    bool same = RasterGrid.Same(l1, small, out reason);
                    log.Check(!same && !string.IsNullOrEmpty(reason), "不同尺寸判定为网格不一致", reason);
                }
            }
        }

        /// <summary>逐像素比较两个单波段 Float32 文件，返回不同的像素数（NaN 视为相等）。</summary>
        private static int CompareBands(string a, string b)
        {
            var fa = ReadBand(a, 1);
            var fb = ReadBand(b, 1);
            if (fa.Length != fb.Length) return Math.Abs(fa.Length - fb.Length);

            int diff = 0;
            for (int i = 0; i < fa.Length; i++)
            {
                bool na = float.IsNaN(fa[i]), nb = float.IsNaN(fb[i]);
                if (na && nb) continue;
                if (na != nb || fa[i] != fb[i]) diff++;
            }
            return diff;
        }

        private static float[] ReadBand(string path, int band)
        {
            using (var ds = Gdal.Open(path, Access.GA_ReadOnly))
            {
                int w = ds.RasterXSize, h = ds.RasterYSize;
                var buf = new float[w * h];
                var b = ds.GetRasterBand(band);
                try { b.ReadRaster(0, 0, w, h, buf, w, h, 0, 0); }
                finally { b.Dispose(); }
                return buf;
            }
        }
    }
}
