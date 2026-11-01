using System;
using OSGeo.GDAL;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
        /// <summary>空域滤波类型：均值、中值、高斯、拉普拉斯锐化、Sobel 边缘。</summary>
    public enum FilterKind { Mean, Median, Gauss, Sharpen, Sobel }

    /// <summary>空域滤波：分块 + 光晕（halo）读取，避免整幅载入内存。</summary>
    public static class FilterService
    {
        private const int BlockRows = 256;

        /// <summary>滤波类型的可选项（按当前语言惰性求值，理由同 DemService.AlgorithmNames）。</summary>
        public static string[] KindNames
        {
            get { return new[] { Lang.T("均值平滑"), Lang.T("中值去噪"), Lang.T("高斯模糊"), Lang.T("拉普拉斯锐化"), Lang.T("Sobel 边缘") }; }
        }

        /// <summary>把界面显示名转为滤波类型。</summary>
        /// <param name="name">界面选项文本。</param>
        /// <returns>对应的滤波类型。</returns>
        public static FilterKind KindFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return FilterKind.Mean;
            string s = name.ToLowerInvariant();
            if (s.Contains(Lang.T("中值")) || s.Contains("median")) return FilterKind.Median;
            if (s.Contains(Lang.T("高斯")) || s.Contains("gauss")) return FilterKind.Gauss;
            if (s.Contains(Lang.T("锐化")) || s.Contains("sharpen") || s.Contains("laplac")) return FilterKind.Sharpen;
            if (s.Contains("sobel") || s.Contains(Lang.T("边缘"))) return FilterKind.Sobel;
            return FilterKind.Mean;
        }

        /// <summary>按类型执行分块卷积滤波，Byte 输入（Sobel 除外）保持 Byte 输出。</summary>
        /// <param name="layer">源图层。</param>
        /// <param name="bandNo">波段号（1 起）。</param>
        /// <param name="kind">滤波类型。</param>
        /// <param name="size">窗口边长（奇数，≥3）。</param>
        /// <param name="sigma">高斯 σ（仅高斯滤波使用）。</param>
        /// <param name="outPath">输出文件路径。</param>
        /// <param name="progress">进度与取消上报器。</param>
        /// <returns>输出文件路径。</returns>
        public static string Run(RasterLayer layer, int bandNo, FilterKind kind, int size, double sigma,
            string outPath, ProgressReporter progress)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            ProcessingUtil.ValidateOutputPath(outPath, layer.SourcePath, layer.Path);
            if (bandNo < 1 || bandNo > layer.BandCount) throw new ArgumentOutOfRangeException("bandNo");
            if (progress != null) progress.ThrowIfCancellationRequested();
            if (size < 3) size = 3;
            if (size % 2 == 0) size++;

            int w = layer.Width, h = layer.Height;
            int r = size / 2;
            bool inputIsByte = layer.BandTypes[bandNo - 1] == DataType.GDT_Byte;
            bool keepByte = inputIsByte && kind != FilterKind.Sobel;
            DataType outType = keepByte ? DataType.GDT_Byte : DataType.GDT_Float32;

            var kernel = BuildKernel(kind, size, sigma);

            var driver = Gdal.GetDriverByName(ProcessingUtil.DriverForExtension(outPath));
            Dataset dst = null;
            try
            {
                dst = driver.Create(outPath, w, h, 1, outType,
                    System.IO.Path.GetExtension(outPath).ToLowerInvariant() == ".tif"
                        ? new[] { "TILED=YES", "COMPRESS=DEFLATE" }
                        : new string[0]);
                if (dst == null) throw new InvalidOperationException(Lang.T("创建输出失败：") + ProcessingUtil.DescribeGdalError());
                dst.SetGeoTransform(layer.GeoTransform);
                if (layer.HasProjection) dst.SetProjection(layer.ProjectionWkt);
                var outBand = dst.GetRasterBand(1);
                outBand.FlushCache();

                var band = layer.Ds.GetRasterBand(bandNo);
                try
                {
                    int paddedRows = BlockRows + 2 * r;
                    var buf = new float[w * paddedRows];
                    var outF = new float[w * BlockRows];
                    var outB = new byte[w * BlockRows];
                    var window = new double[size * size];

                    for (int row0 = 0; row0 < h; row0 += BlockRows)
                    {
                        int rows = Math.Min(BlockRows, h - row0);
                        int y0 = Math.Max(0, row0 - r);
                        int y1 = Math.Min(h, row0 + rows + r);
                        int readRows = y1 - y0;

                        band.ReadRaster(0, y0, w, readRows, buf, w, readRows, 0, 0);

                        for (int j = 0; j < rows; j++)
                        {
                            int globalRow = row0 + j;
                            for (int x = 0; x < w; x++)
                            {
                                double value;
                                if (kind == FilterKind.Median)
                                {
                                    value = MedianFilter(buf, w, readRows, x, globalRow - y0, r, window);
                                }
                                else
                                {
                                    value = Convolve(buf, w, readRows, x, globalRow - y0, r, kernel, size, kind);
                                }

                                int dstIndex = j * w + x;
                                if (keepByte)
                                {
                                    if (double.IsNaN(value)) value = 0;
                                    outB[dstIndex] = (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
                                }
                                else
                                {
                                    outF[dstIndex] = (float)value;
                                }
                            }
                        }

                        if (keepByte) outBand.WriteRaster(0, row0, w, rows, outB, w, rows, 0, 0);
                        else outBand.WriteRaster(0, row0, w, rows, outF, w, rows, 0, 0);

                        if (progress != null)
                        {
                            double frac = (row0 + rows) / (double)h;
                            progress.Report(frac >= 1 ? 0.999 : frac, "");
                        }
                    }
                }
                finally { band.Dispose(); }

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
            return outPath;
        }

        private static double[] BuildKernel(FilterKind kind, int size, double sigma)
        {
            var k = new double[size * size];
            int r = size / 2;

            switch (kind)
            {
                case FilterKind.Mean:
                    for (int i = 0; i < k.Length; i++) k[i] = 1.0 / (size * size);
                    break;
                case FilterKind.Gauss:
                    {
                        if (sigma <= 0) sigma = size / 3.0;
                        double sum = 0;
                        for (int dy = -r; dy <= r; dy++)
                            for (int dx = -r; dx <= r; dx++)
                            {
                                double v = Math.Exp(-(dx * dx + dy * dy) / (2 * sigma * sigma));
                                k[(dy + r) * size + (dx + r)] = v;
                                sum += v;
                            }
                        for (int i = 0; i < k.Length; i++) k[i] /= sum;
                        break;
                    }
                case FilterKind.Sharpen:
                    {
                        // 拉普拉斯锐化：中心加强，邻域取负
                        for (int i = 0; i < k.Length; i++) k[i] = -1.0;
                        k[(r) * size + r] = size * size;
                        break;
                    }
                default:
                    for (int i = 0; i < k.Length; i++) k[i] = 0;
                    break;
            }
            return k;
        }

        private static double Convolve(float[] buf, int w, int readRows, int x, int y, int r, double[] kernel, int size, FilterKind kind)
        {
            if (kind == FilterKind.Sobel)
            {
                double gx = 0, gy = 0;
                int[] kx = { -1, 0, 1, -2, 0, 2, -1, 0, 1 };
                int[] ky = { -1, -2, -1, 0, 0, 0, 1, 2, 1 };
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        double v = Sample(buf, w, readRows, x + dx, y + dy);
                        int idx = (dy + 1) * 3 + (dx + 1);
                        gx += v * kx[idx];
                        gy += v * ky[idx];
                    }
                return Math.Sqrt(gx * gx + gy * gy);
            }

            double sum = 0;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    double v = Sample(buf, w, readRows, x + dx, y + dy);
                    if (double.IsNaN(v)) continue;
                    sum += v * kernel[(dy + r) * size + (dx + r)];
                }
            return sum;
        }

        private static double MedianFilter(float[] buf, int w, int readRows, int x, int y, int r, double[] window)
        {
            int n = 0;
            int size = 2 * r + 1;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    double v = Sample(buf, w, readRows, x + dx, y + dy);
                    if (double.IsNaN(v)) continue;
                    window[n++] = v;
                }
            if (n == 0) return 0;
            // 插入排序（窗口很小，避免 Array.Sort 的分配）
            for (int i = 1; i < n; i++)
            {
                double key = window[i];
                int j = i - 1;
                while (j >= 0 && window[j] > key) { window[j + 1] = window[j]; j--; }
                window[j + 1] = key;
            }
            return window[n / 2];
        }

        private static double Sample(float[] buf, int w, int readRows, int x, int y)
        {
            if (y < 0) y = 0; else if (y >= readRows) y = readRows - 1;
            if (x < 0) x = 0; else if (x >= w) x = w - 1;
            return buf[y * w + x];
        }
    }
}
