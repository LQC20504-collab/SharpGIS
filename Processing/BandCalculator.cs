using System;
using System.Collections.Generic;
using OSGeo.GDAL;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
    /// <summary>
    /// 波段计算器：按块读取参与波段 → 逐像素求值 → 写出 Float32 影像（保留参考图层的地理参考）。
    /// 表达式里的 b1/b2 是占位符，实际读哪个文件的哪个波段由 <see cref="BandMapping"/> 决定。
    /// </summary>
    public static class BandCalculator
    {
        private const int BlockRows = 256;

        /// <summary>按表达式逐像素计算并输出 Float32 单波段影像（b_i 默认等于参考图层第 i 波段）。</summary>
        /// <param name="layer">参考图层。</param>
        /// <param name="expression">波段表达式，如 normalizedDifference(b4,b3)。</param>
        /// <param name="outPath">输出文件路径。</param>
        /// <param name="progress">进度与取消上报器。</param>
        /// <param name="compression">GeoTIFF 压缩方式。</param>
        /// <returns>输出文件路径。</returns>
        public static string Run(RasterLayer layer, string expression, string outPath,
            ProgressReporter progress, string compression = "DEFLATE")
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            return Run(layer, expression, BandMapping.Identity(layer), outPath, progress, compression);
        }

        /// <summary>按表达式与波段映射逐像素计算，输出 Float32 单波段影像。</summary>
        /// <param name="layer">参考图层，决定输出的尺寸与地理参考。</param>
        /// <param name="expression">波段表达式。</param>
        /// <param name="mapping">占位符到实际（图层, 波段）的映射表。</param>
        /// <param name="outPath">输出文件路径。</param>
        /// <param name="progress">进度与取消上报器。</param>
        /// <param name="compression">GeoTIFF 压缩方式。</param>
        /// <returns>输出文件路径。</returns>
        public static string Run(RasterLayer layer, string expression, BandMapping mapping,
            string outPath, ProgressReporter progress, string compression = "DEFLATE")
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));
            ProcessingUtil.ValidateOutputPath(outPath, layer.SourcePath, layer.Path);
            if (progress != null) progress.ThrowIfCancellationRequested();

            var compiled = ExpressionParser.Compile(expression, mapping.Count);
            if (compiled.UsedSlots.Length == 0)
                throw new FormatException(Lang.T("表达式中没有引用任何波段（请使用 b1、b2 … 表示波段）"));

            string coverage = mapping.CheckCoverage(compiled.UsedSlots);
            if (coverage != null) throw new FormatException(coverage);

            string gridError = mapping.CheckGrid(layer);
            if (gridError != null) throw new InvalidOperationException(gridError);

            // 按（图层, 波段）去重：同一波段被多个占位符引用时只读一次
            var reads = new List<BandSource>();
            var slotToRead = new int[compiled.UsedSlots.Length];
            for (int i = 0; i < compiled.UsedSlots.Length; i++)
            {
                var src = mapping.Resolve(compiled.UsedSlots[i]);
                int index = -1;
                for (int j = 0; j < reads.Count; j++)
                {
                    if (ReferenceEquals(reads[j].Layer, src.Layer) && reads[j].Band == src.Band)
                    {
                        index = j;
                        break;
                    }
                }
                if (index < 0)
                {
                    reads.Add(src);
                    index = reads.Count - 1;
                }
                slotToRead[i] = index;
            }

            var driver = Gdal.GetDriverByName(ProcessingUtil.DriverForExtension(outPath));
            if (driver == null) throw new InvalidOperationException(Lang.T("不支持该输出格式"));

            var co = new List<string>();
            if (System.IO.Path.GetExtension(outPath).ToLowerInvariant() == ".tif")
            {
                co.Add("TILED=YES");
                co.Add("COMPRESS=" + (string.IsNullOrEmpty(compression) ? "DEFLATE" : compression));
                co.Add("PREDICTOR=3");
            }

            int w = layer.Width, h = layer.Height;
            var usedLayers = mapping.DistinctLayers();
            foreach (var l in usedLayers) l.Acquire();

            Dataset dst = null;
            try
            {
                dst = driver.Create(outPath, w, h, 1, DataType.GDT_Float32, co.ToArray());
                if (dst == null) throw new InvalidOperationException(Lang.T("创建输出文件失败：") + ProcessingUtil.DescribeGdalError());

                dst.SetGeoTransform(layer.GeoTransform);
                if (layer.HasProjection) dst.SetProjection(layer.ProjectionWkt);

                var outBand = dst.GetRasterBand(1);
                outBand.SetNoDataValue(double.NaN);
                outBand.FlushCache();

                var buffers = new float[reads.Count][];
                for (int i = 0; i < reads.Count; i++)
                    buffers[i] = new float[w * BlockRows];

                // env 按“槽位”索引（0 基对应 b1），与具体文件无关
                var env = new double[mapping.Count];
                var outBuf = new float[w * BlockRows];

                for (int row0 = 0; row0 < h; row0 += BlockRows)
                {
                    int rows = Math.Min(BlockRows, h - row0);
                    int n = w * rows;

                    // 同一图层一次加锁；不同图层之间互不阻塞
                    foreach (var srcLayer in usedLayers)
                    {
                        lock (srcLayer.ReadLock)
                        {
                            if (srcLayer.IsDisposed) throw new InvalidOperationException(Lang.T("图层 ") + srcLayer.Name + Lang.T(" 已关闭"));

                            for (int i = 0; i < reads.Count; i++)
                            {
                                if (!ReferenceEquals(reads[i].Layer, srcLayer)) continue;
                                using (var band = srcLayer.Ds.GetRasterBand(reads[i].Band))
                                    band.ReadRaster(0, row0, w, rows, buffers[i], w, rows, 0, 0);
                            }
                        }
                    }

                    for (int p = 0; p < n; p++)
                    {
                        for (int i = 0; i < slotToRead.Length; i++)
                            env[compiled.UsedSlots[i] - 1] = buffers[slotToRead[i]][p];

                        double v;
                        try { v = compiled.Evaluate(env); }
                        catch { v = double.NaN; }

                        if (double.IsNaN(v) || double.IsInfinity(v)) v = double.NaN;
                        outBuf[p] = (float)v;
                    }

                    outBand.WriteRaster(0, row0, w, rows, outBuf, w, rows, 0, 0);
                    if (progress != null)
                    {
                        double frac = (row0 + rows) / (double)h;
                        if (frac >= 1.0) frac = 0.999;
                        progress.Report(frac, "");
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
            finally
            {
                foreach (var l in usedLayers) { try { l.Release(); } catch { } }
            }
            return outPath;
        }

        /// <summary>只做语法校验，不执行计算（b_i 视为第 i 波段）。</summary>
        /// <param name="expression">待校验表达式。</param>
        /// <param name="bandCount">可用槽位数。</param>
        /// <returns>错误信息；表达式可用时返回 null。</returns>
        public static string Validate(string expression, int bandCount)
        {
            try
            {
                var c = ExpressionParser.Compile(expression, bandCount);
                return c.UsedSlots.Length == 0 ? Lang.T("表达式未引用任何波段") : null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>校验语法、占位符映射覆盖率与网格一致性。</summary>
        /// <param name="expression">待校验表达式。</param>
        /// <param name="mapping">占位符映射表。</param>
        /// <param name="reference">参考图层，用于网格判定。</param>
        /// <returns>首条错误；全部可用时返回 null。</returns>
        public static string Validate(string expression, BandMapping mapping, RasterLayer reference)
        {
            if (mapping == null) return Lang.T("缺少波段映射");
            try
            {
                var c = ExpressionParser.Compile(expression, mapping.Count);
                if (c.UsedSlots.Length == 0) return Lang.T("表达式未引用任何波段");

                string err = mapping.CheckCoverage(c.UsedSlots);
                if (err != null) return err;

                err = mapping.CheckGrid(reference);
                if (err != null) return err;

                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
