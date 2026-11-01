using System;
using OSGeo.GDAL;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
        /// <summary>融合方法：Brovey 变换、IHS 变换、HPF 高通滤波。</summary>
    public enum FusionMethod { Brovey, IHS, Hpf }

    /// <summary>
    /// 影像融合（全色 + 多光谱）：以全色影像的网格为输出网格，
    /// 多光谱按地理坐标重采样到该网格后做 Brovey / IHS / HPF 变换。
    /// </summary>
    public static class FusionService
    {
        private const int BlockRows = 128;

        /// <summary>融合方法的可选项（按当前语言惰性求值，理由同 DemService.AlgorithmNames）。</summary>
        public static string[] MethodNames
        {
            get { return new[] { Lang.T("Brovey 变换"), Lang.T("IHS 变换"), Lang.T("HPF 高通滤波") }; }
        }

        /// <summary>把界面显示名转为融合方法。</summary>
        /// <param name="name">界面选项文本。</param>
        /// <returns>对应的融合方法。</returns>
        public static FusionMethod MethodFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return FusionMethod.Brovey;
            string s = name.ToLowerInvariant();
            if (s.Contains("ihs")) return FusionMethod.IHS;
            if (s.Contains("hpf") || s.Contains(Lang.T("高通"))) return FusionMethod.Hpf;
            return FusionMethod.Brovey;
        }

        /// <summary>以全色网格为输出网格，融合多光谱与全色影像。</summary>
        /// <param name="ms">多光谱图层。</param>
        /// <param name="msBands">参与融合的三个多光谱波段（R/G/B，1 起）。</param>
        /// <param name="pan">全色图层。</param>
        /// <param name="panBand">全色波段号。</param>
        /// <param name="method">融合方法。</param>
        /// <param name="outPath">输出文件路径。</param>
        /// <param name="progress">进度与取消上报器。</param>
        /// <returns>输出文件路径。</returns>
        public static string Run(RasterLayer ms, int[] msBands, RasterLayer pan, int panBand,
            FusionMethod method, string outPath, ProgressReporter progress)
        {
            if (ms == null || pan == null) throw new ArgumentNullException();
            if (msBands == null || msBands.Length != 3)
                throw new ArgumentException(Lang.T("融合需要选择 3 个多光谱波段（R/G/B）"));
            ProcessingUtil.ValidateOutputPath(outPath, ms.SourcePath, pan.SourcePath, ms.Path, pan.Path);
            if (panBand < 1 || panBand > pan.BandCount) throw new ArgumentOutOfRangeException("panBand");
            if (progress != null) progress.ThrowIfCancellationRequested();

            int w = pan.Width, h = pan.Height;
            var gt = pan.GeoTransform;
            double pixW = Math.Abs(gt[1]), pixH = Math.Abs(gt[5]);

            var driver = Gdal.GetDriverByName(ProcessingUtil.DriverForExtension(outPath));
            Dataset dst = null;
            try
            {
                dst = driver.Create(outPath, w, h, 3, DataType.GDT_Byte,
                    System.IO.Path.GetExtension(outPath).ToLowerInvariant() == ".tif"
                        ? new[] { "TILED=YES", "COMPRESS=DEFLATE", "PHOTOMETRIC=RGB" }
                        : new string[0]);
                if (dst == null) throw new InvalidOperationException(Lang.T("创建输出失败：") + ProcessingUtil.DescribeGdalError());
                dst.SetGeoTransform(gt);
                if (pan.HasProjection) dst.SetProjection(pan.ProjectionWkt);

                var panBuf = new float[w * BlockRows];
                var b1 = new float[w * BlockRows];
                var b2 = new float[w * BlockRows];
                var b3 = new float[w * BlockRows];
                var out1 = new byte[w * BlockRows];
                var out2 = new byte[w * BlockRows];
                var out3 = new byte[w * BlockRows];

                for (int row0 = 0; row0 < h; row0 += BlockRows)
                {
                    int rows = Math.Min(BlockRows, h - row0);
                    int n = w * rows;

                    var pb = pan.Ds.GetRasterBand(panBand);
                    try { pb.ReadRaster(0, row0, w, rows, panBuf, w, rows, 0, 0); }
                    finally { pb.Dispose(); }

                    ReadAligned(ms, msBands[0], gt, pixW, pixH, row0, rows, w, b1);
                    ReadAligned(ms, msBands[1], gt, pixW, pixH, row0, rows, w, b2);
                    ReadAligned(ms, msBands[2], gt, pixW, pixH, row0, rows, w, b3);

                    for (int p = 0; p < n; p++)
                    {
                        double panV = panBuf[p];
                        double r = b1[p], g = b2[p], b = b3[p];
                        double o1, o2, o3;

                        switch (method)
                        {
                            case FusionMethod.Brovey:
                                {
                                    double sum = r + g + b;
                                    if (sum < 1e-6) { o1 = r; o2 = g; o3 = b; }
                                    else
                                    {
                                        double k = panV / sum;
                                        o1 = r * k; o2 = g * k; o3 = b * k;
                                    }
                                    break;
                                }
                            case FusionMethod.IHS:
                                {
                                    double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0;
                                    double intensity = (rr + gg + bb) / 3.0;
                                    double delta = panV / 255.0 - intensity;
                                    o1 = (rr + delta) * 255.0;
                                    o2 = (gg + delta) * 255.0;
                                    o3 = (bb + delta) * 255.0;
                                    break;
                                }
                            default:
                                {
                                    double hp = panV - LowPass(panBuf, w, rows, p % w, p / w);
                                    o1 = r + hp; o2 = g + hp; o3 = b + hp;
                                    break;
                                }
                        }

                        out1[p] = Clamp(o1);
                        out2[p] = Clamp(o2);
                        out3[p] = Clamp(o3);
                    }

                    var ob1 = dst.GetRasterBand(1);
                    var ob2 = dst.GetRasterBand(2);
                    var ob3 = dst.GetRasterBand(3);
                    try
                    {
                        ob1.WriteRaster(0, row0, w, rows, out1, w, rows, 0, 0);
                        ob2.WriteRaster(0, row0, w, rows, out2, w, rows, 0, 0);
                        ob3.WriteRaster(0, row0, w, rows, out3, w, rows, 0, 0);
                    }
                    finally { ob1.Dispose(); ob2.Dispose(); ob3.Dispose(); }

                    if (progress != null)
                    {
                        double frac = (row0 + rows) / (double)h;
                        progress.Report(frac >= 1 ? 0.999 : frac, "");
                    }
                }

                for (int i = 1; i <= 3; i++)
                {
                    var b = dst.GetRasterBand(i);
                    try
                    {
                        b.SetColorInterpretation(i == 1 ? ColorInterp.GCI_RedBand
                            : i == 2 ? ColorInterp.GCI_GreenBand : ColorInterp.GCI_BlueBand);
                    }
                    finally { b.Dispose(); }
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

        /// <summary>按输出网格（gt/pix 来自全色）的地理位置，把多光谱波段重采样读入同样尺寸的块。</summary>
        private static void ReadAligned(RasterLayer layer, int bandNo, double[] outGt,
            double pixW, double pixH, int row0, int rows, int outW, float[] buffer)
        {
            double gx0 = outGt[0];
            double gx1 = outGt[0] + outW * outGt[1];
            double gy1 = outGt[3] - row0 * pixH;
            double gy0 = gy1 - rows * pixH;

            double c0, r0, c1, r1;
            layer.GeoToPixel(gx0, gy1, out c0, out r0);
            layer.GeoToPixel(gx1, gy0, out c1, out r1);

            int srcCol = (int)Math.Floor(Math.Min(c0, c1));
            int srcRow = (int)Math.Floor(Math.Min(r0, r1));
            int srcW = (int)Math.Ceiling(Math.Abs(c1 - c0));
            int srcH = (int)Math.Ceiling(Math.Abs(r1 - r0));

            srcCol = Math.Max(0, Math.Min(layer.Width - 1, srcCol));
            srcRow = Math.Max(0, Math.Min(layer.Height - 1, srcRow));
            srcW = Math.Max(1, Math.Min(srcW, layer.Width - srcCol));
            srcH = Math.Max(1, Math.Min(srcH, layer.Height - srcRow));

            var band = layer.Ds.GetRasterBand(bandNo);
            try { band.ReadRaster(srcCol, srcRow, srcW, srcH, buffer, outW, rows, 0, 0); }
            finally { band.Dispose(); }
        }

        private static double LowPass(float[] buf, int w, int rows, int x, int y)
        {
            double sum = 0, wsum = 0;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= rows) continue;
                    double weight = Math.Exp(-(dx * dx + dy * dy) / 4.0);
                    sum += buf[yy * w + xx] * weight;
                    wsum += weight;
                }
            return wsum > 0 ? sum / wsum : buf[y * w + x];
        }

        private static byte Clamp(double v)
        {
            if (double.IsNaN(v)) return 0;
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)Math.Round(v);
        }
    }
}
