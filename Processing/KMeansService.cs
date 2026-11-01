using System;
using OSGeo.GDAL;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
        /// <summary>KMeans 分类参数。</summary>
    public sealed class KMeansOptions
    {
        /// <summary>参与分类的波段（1 起）。</summary>
        public int[] Bands;
        /// <summary>类别数。</summary>
        public int K = 6;
        /// <summary>最大迭代次数。</summary>
        public int MaxIterations = 20;
        /// <summary>随机种子（固定后结果可复现）。</summary>
        public int Seed = 12345;
        /// <summary>是否写入类别调色板（关闭后输出为普通 8bit 灰度类别图）。</summary>
        public bool WritePalette = true;
    }

        /// <summary>KMeans 执行结果。</summary>
    public sealed class KMeansResult
    {
        /// <summary>输出文件路径。</summary>
        public string OutputPath;
        /// <summary>各类别在抽样点中的计数。</summary>
        public int[] SampleCounts;
        /// <summary>归一化空间中的类中心。</summary>
        public double[][] Centers;
        /// <summary>换算回原始数值量纲的类中心。</summary>
        public double[][] RawCenters;
    }

    /// <summary>KMeans 非监督分类：抽样训练 + 分块分类，输出带调色板的 Byte 影像。</summary>
    public static class KMeansService
    {
        private const int SampleSize = 256;   // 每边最多采 256 个点
        private const int BlockRows = 256;

        /// <summary>抽样训练 + 分块分类，输出 Byte 类别图（可选调色板）。</summary>
        /// <param name="layer">源图层。</param>
        /// <param name="o">分类参数。</param>
        /// <param name="outPath">输出文件路径。</param>
        /// <param name="progress">进度与取消上报器。</param>
        /// <returns>包含各类样本计数与类中心的结果对象。</returns>
        public static KMeansResult Run(RasterLayer layer, KMeansOptions o, string outPath, ProgressReporter progress)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (o == null) o = new KMeansOptions();
            ProcessingUtil.ValidateOutputPath(outPath, layer.SourcePath, layer.Path);
            if (progress != null) progress.ThrowIfCancellationRequested();

            int k = Math.Max(2, Math.Min(30, o.K));
            int[] bands = o.Bands != null && o.Bands.Length > 0 ? o.Bands : new[] { 1 };
            foreach (var b in bands)
                if (b < 1 || b > layer.BandCount) throw new ArgumentOutOfRangeException("Bands", Lang.T("波段号越界: ") + b);

            int w = layer.Width, h = layer.Height;
            int bandCount = bands.Length;

            // 1) 抽样
            int sw = Math.Min(w, SampleSize);
            int sh = Math.Min(h, SampleSize);
            int sampleCount = sw * sh;
            var samples = new float[bandCount][];
            lock (layer.ReadLock)
            {
                if (layer.IsDisposed) throw new InvalidOperationException(Lang.T("图层已关闭"));
                for (int i = 0; i < bandCount; i++)
                {
                    samples[i] = new float[sampleCount];
                    var band = layer.Ds.GetRasterBand(bands[i]);
                    try { band.ReadRaster(0, 0, w, h, samples[i], sw, sh, 0, 0); }
                    finally { band.Dispose(); }
                }
            }

            // 2) 归一化参数
            var mean = new double[bandCount];
            var std = new double[bandCount];
            for (int i = 0; i < bandCount; i++)
            {
                double sum = 0, sum2 = 0;
                int count = 0;
                for (int p = 0; p < sampleCount; p++)
                {
                    double v = samples[i][p];
                    if (double.IsNaN(v)) continue;
                    sum += v; sum2 += v * v; count++;
                }
                if (count == 0) { mean[i] = 0; std[i] = 1; continue; }
                mean[i] = sum / count;
                double var = sum2 / count - mean[i] * mean[i];
                std[i] = var > 1e-12 ? Math.Sqrt(var) : 1;
            }

            // 3) 训练（在归一化空间里做 KMeans++）
            var data = new double[sampleCount * bandCount];
            for (int p = 0; p < sampleCount; p++)
                for (int i = 0; i < bandCount; i++)
                    data[p * bandCount + i] = (samples[i][p] - mean[i]) / std[i];

            var rnd = new Random(o.Seed);
            var centers = InitCenters(data, sampleCount, bandCount, k, rnd);
            var assign = new int[sampleCount];
            int iterations = Math.Max(1, o.MaxIterations);

            for (int iter = 0; iter < iterations; iter++)
            {
                bool changed = AssignAll(data, sampleCount, bandCount, centers, assign);
                UpdateCenters(data, sampleCount, bandCount, centers, assign, rnd);
                if (progress != null)
                    progress.Report((iter + 1) / (double)(iterations + 1) * 0.5, "");
                if (!changed && iter > 0) break;
            }

            // 4) 分类全图
            var driver = Gdal.GetDriverByName(ProcessingUtil.DriverForExtension(outPath));
            Dataset dst = null;
            try
            {
                dst = driver.Create(outPath, w, h, 1, DataType.GDT_Byte,
                    System.IO.Path.GetExtension(outPath).ToLowerInvariant() == ".tif"
                        ? new[] { "TILED=YES", "COMPRESS=DEFLATE" }
                        : new string[0]);
                if (dst == null) throw new InvalidOperationException(Lang.T("创建输出失败：") + ProcessingUtil.DescribeGdalError());
                dst.SetGeoTransform(layer.GeoTransform);
                if (layer.HasProjection) dst.SetProjection(layer.ProjectionWkt);

                var outBand = dst.GetRasterBand(1);
                if (o.WritePalette)
                {
                    var palette = ColorRamp.ClassPalette(k);
                    var table = new ColorTable(PaletteInterp.GPI_RGB);
                    for (int i = 0; i < 256; i++)
                    {
                        var c = palette[i % palette.Count];
                        var entry = new ColorEntry { c1 = (short)c.R, c2 = (short)c.G, c3 = (short)c.B, c4 = 255 };
                        table.SetColorEntry(i, entry);
                    }
                    outBand.SetRasterColorTable(table);
                    outBand.SetRasterColorInterpretation(ColorInterp.GCI_PaletteIndex);
                }
                else
                {
                    outBand.SetRasterColorInterpretation(ColorInterp.GCI_GrayIndex);
                }
                outBand.FlushCache();

                var buffers = new float[bandCount][];
                for (int i = 0; i < bandCount; i++) buffers[i] = new float[w * BlockRows];
                var outBuf = new byte[w * BlockRows];
                var point = new double[bandCount];

                for (int row0 = 0; row0 < h; row0 += BlockRows)
                {
                    int rows = Math.Min(BlockRows, h - row0);
                    int n = w * rows;
                    for (int i = 0; i < bandCount; i++)
                    {
                        var band = layer.Ds.GetRasterBand(bands[i]);
                        try { band.ReadRaster(0, row0, w, rows, buffers[i], w, rows, 0, 0); }
                        finally { band.Dispose(); }
                    }

                    for (int p = 0; p < n; p++)
                    {
                        for (int i = 0; i < bandCount; i++)
                            point[i] = (buffers[i][p] - mean[i]) / std[i];
                        outBuf[p] = (byte)Nearest(centers, point, k);
                    }

                    outBand.WriteRaster(0, row0, w, rows, outBuf, w, rows, 0, 0);
                    if (progress != null)
                    {
                        double frac = 0.5 + 0.5 * (row0 + rows) / (double)h;
                        progress.Report(frac >= 1 ? 0.999 : frac, "");
                    }
                }

                outBand.Dispose();
                dst.FlushCache();
                dst.Dispose();
                dst = null;
            }
            catch
            {
                if (dst != null) { try { dst.Dispose(); } catch { } }
                ProcessingUtil.DeleteOutputSet(outPath);
                throw;
            }

            // 5) 统计
            var counts = new int[k];
            for (int p = 0; p < sampleCount; p++) counts[assign[p]]++;

            var raw = new double[k][];
            for (int c = 0; c < k; c++)
            {
                raw[c] = new double[bandCount];
                for (int i = 0; i < bandCount; i++) raw[c][i] = centers[c][i] * std[i] + mean[i];
            }

            return new KMeansResult
            {
                OutputPath = outPath,
                SampleCounts = counts,
                Centers = centers,
                RawCenters = raw
            };
        }

        private static double[][] InitCenters(double[] data, int count, int dims, int k, Random rnd)
        {
            var centers = new double[k][];
            for (int i = 0; i < k; i++) centers[i] = new double[dims];

            int first = rnd.Next(count);
            for (int d = 0; d < dims; d++) centers[0][d] = data[first * dims + d];

            var dist2 = new double[count];
            for (int c = 1; c < k; c++)
            {
                double total = 0;
                for (int p = 0; p < count; p++)
                {
                    double best = double.MaxValue;
                    for (int j = 0; j < c; j++)
                    {
                        double d2 = 0;
                        for (int d = 0; d < dims; d++)
                        {
                            double diff = data[p * dims + d] - centers[j][d];
                            d2 += diff * diff;
                        }
                        if (d2 < best) best = d2;
                    }
                    dist2[p] = best;
                    total += best;
                }

                if (total <= 0) { for (int d = 0; d < dims; d++) centers[c][d] = data[rnd.Next(count) * dims + d]; continue; }

                double target = rnd.NextDouble() * total;
                double acc = 0;
                int chosen = count - 1;
                for (int p = 0; p < count; p++)
                {
                    acc += dist2[p];
                    if (acc >= target) { chosen = p; break; }
                }
                for (int d = 0; d < dims; d++) centers[c][d] = data[chosen * dims + d];
            }
            return centers;
        }

        private static bool AssignAll(double[] data, int count, int dims, double[][] centers, int[] assign)
        {
            bool changed = false;
            int k = centers.Length;
            for (int p = 0; p < count; p++)
            {
                double best = double.MaxValue;
                int bestIdx = 0;
                for (int c = 0; c < k; c++)
                {
                    double d2 = 0;
                    for (int d = 0; d < dims; d++)
                    {
                        double diff = data[p * dims + d] - centers[c][d];
                        d2 += diff * diff;
                        if (d2 >= best) break;
                    }
                    if (d2 < best) { best = d2; bestIdx = c; }
                }
                if (assign[p] != bestIdx) { assign[p] = bestIdx; changed = true; }
            }
            return changed;
        }

        private static void UpdateCenters(double[] data, int count, int dims, double[][] centers, int[] assign, Random rnd)
        {
            int k = centers.Length;
            var sums = new double[k][];
            var counts = new int[k];
            for (int c = 0; c < k; c++) sums[c] = new double[dims];

            for (int p = 0; p < count; p++)
            {
                int c = assign[p];
                counts[c]++;
                for (int d = 0; d < dims; d++) sums[c][d] += data[p * dims + d];
            }

            for (int c = 0; c < k; c++)
            {
                if (counts[c] == 0)
                {
                    // 空簇重新播种
                    int pick = rnd.Next(count);
                    for (int d = 0; d < dims; d++) centers[c][d] = data[pick * dims + d];
                    continue;
                }
                for (int d = 0; d < dims; d++) centers[c][d] = sums[c][d] / counts[c];
            }
        }

        private static int Nearest(double[][] centers, double[] point, int k)
        {
            double best = double.MaxValue;
            int bestIdx = 0;
            for (int c = 0; c < k; c++)
            {
                double d2 = 0;
                var center = centers[c];
                for (int d = 0; d < point.Length; d++)
                {
                    double diff = point[d] - center[d];
                    d2 += diff * diff;
                }
                if (d2 < best) { best = d2; bestIdx = c; }
            }
            return bestIdx;
        }
    }
}
