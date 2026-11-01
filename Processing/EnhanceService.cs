using System;
using OSGeo.GDAL;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
        /// <summary>增强方式：线性拉伸到 8bit、直方图均衡到 8bit。</summary>
    public enum EnhanceKind { StretchToByte, EqualizeToByte }

    /// <summary>影像增强：线性拉伸到 8bit、直方图均衡到 8bit。</summary>
    public static class EnhanceService
    {
        private const int BlockRows = 512;

        /// <summary>增强方式的可选项（按当前语言惰性求值，理由同 DemService.AlgorithmNames）。</summary>
        public static string[] KindNames
        {
            get { return new[] { Lang.T("线性拉伸到 8bit"), Lang.T("直方图均衡到 8bit") }; }
        }

        /// <summary>把影像拉伸/均衡为 8bit 输出，便于后续分类或导出。</summary>
        /// <param name="layer">源图层。</param>
        /// <param name="kind">增强方式。</param>
        /// <param name="allBands">是否处理全部波段。</param>
        /// <param name="bandNo">单波段模式下的波段号。</param>
        /// <param name="percentLow">累计裁剪下限百分比。</param>
        /// <param name="percentHigh">累计裁剪上限百分比。</param>
        /// <param name="outPath">输出文件路径。</param>
        /// <param name="progress">进度与取消上报器。</param>
        /// <returns>输出文件路径。</returns>
        public static string Run(RasterLayer layer, EnhanceKind kind, bool allBands, int bandNo,
            double percentLow, double percentHigh, string outPath, ProgressReporter progress)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            ProcessingUtil.ValidateOutputPath(outPath, layer.SourcePath, layer.Path);
            if (progress != null) progress.ThrowIfCancellationRequested();

            int w = layer.Width, h = layer.Height;
            int firstBand = allBands ? 1 : Math.Max(1, Math.Min(layer.BandCount, bandNo));
            int bandCount = allBands ? layer.BandCount : 1;

            var driver = Gdal.GetDriverByName(ProcessingUtil.DriverForExtension(outPath));
            Dataset dst = null;
            try
            {
                dst = driver.Create(outPath, w, h, bandCount, DataType.GDT_Byte,
                    System.IO.Path.GetExtension(outPath).ToLowerInvariant() == ".tif"
                        ? new[] { "TILED=YES", "COMPRESS=DEFLATE", "PHOTOMETRIC=" + (bandCount >= 3 ? "RGB" : "MINISBLACK") }
                        : new string[0]);
                if (dst == null) throw new InvalidOperationException(Lang.T("创建输出失败：") + ProcessingUtil.DescribeGdalError());

                dst.SetGeoTransform(layer.GeoTransform);
                if (layer.HasProjection) dst.SetProjection(layer.ProjectionWkt);

                // 预先为每个输出波段准备映射表
                var tables = new byte[bandCount][];
                var los = new double[bandCount];
                var his = new double[bandCount];
                for (int i = 0; i < bandCount; i++)
                {
                    int srcBand = firstBand + i;
                    var stats = layer.Stats(srcBand);
                    if (stats == null || !stats.Valid) throw new InvalidOperationException(Lang.T("波段 ") + srcBand + Lang.T(" 统计不可用"));

                    double lo, hi;
                    if (percentLow > 0 || percentHigh > 0)
                    {
                        lo = stats.Percentile(Math.Max(0, percentLow));
                        hi = stats.Percentile(Math.Min(100, 100 - Math.Max(0, percentHigh)));
                    }
                    else { lo = stats.Min; hi = stats.Max; }
                    if (!(hi > lo)) { lo = stats.Min; hi = stats.Max; }

                    los[i] = lo; his[i] = hi;
                    tables[i] = kind == EnhanceKind.EqualizeToByte
                        ? BuildEqualizeTable(stats)
                        : null;
                }

                var buf = new float[w * BlockRows];
                var outBuf = new byte[w * BlockRows];

                for (int row0 = 0; row0 < h; row0 += BlockRows)
                {
                    int rows = Math.Min(BlockRows, h - row0);
                    int n = w * rows;

                    for (int i = 0; i < bandCount; i++)
                    {
                        int srcBand = firstBand + i;
                        var band = layer.Ds.GetRasterBand(srcBand);
                        try { band.ReadRaster(0, row0, w, rows, buf, w, rows, 0, 0); }
                        finally { band.Dispose(); }

                        double lo = los[i], span = his[i] - los[i];
                        if (Math.Abs(span) < 1e-12) span = 1;
                        var table = tables[i];

                        if (table != null)
                        {
                            for (int p = 0; p < n; p++)
                            {
                                int bin = (int)((buf[p] - lo) / span * 255.0);
                                if (bin < 0) bin = 0; else if (bin > 255) bin = 255;
                                outBuf[p] = table[bin];
                            }
                        }
                        else
                        {
                            double scale = 255.0 / span;
                            for (int p = 0; p < n; p++)
                            {
                                double v = (buf[p] - lo) * scale;
                                if (double.IsNaN(v)) v = 0;
                                outBuf[p] = (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
                            }
                        }

                        var outBand = dst.GetRasterBand(i + 1);
                        outBand.WriteRaster(0, row0, w, rows, outBuf, w, rows, 0, 0);
                        if (i + 1 == bandCount) outBand.FlushCache();
                        outBand.Dispose();
                    }

                    if (progress != null)
                    {
                        double frac = (row0 + rows) / (double)h;
                        progress.Report(frac >= 1 ? 0.999 : frac, "");
                    }
                }

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

        private static byte[] BuildEqualizeTable(BandStats stats)
        {
            var table = new byte[256];
            if (stats.Cdf == null || stats.Cdf.Length < 256)
            {
                for (int i = 0; i < 256; i++) table[i] = (byte)i;
                return table;
            }
            for (int i = 0; i < 256; i++)
            {
                double v = stats.Cdf[i] * 255.0;
                table[i] = (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
            }
            return table;
        }
    }
}
