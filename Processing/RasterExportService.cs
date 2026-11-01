using System;
using System.Collections.Generic;
using OSGeo.GDAL;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
    /// <summary>格式转换/导出的参数。</summary>
    public sealed class ExportOptions
    {
        /// <summary>输出文件路径。</summary>
        public string OutputPath;
        /// <summary>GDAL 输出驱动短名。</summary>
        public string Driver = "GTiff";
        /// <summary>压缩方式：LZW/DEFLATE/无/JPEG/PACKBITS。</summary>
        public string Compression = "LZW";
        /// <summary>是否按块存储（仅 GTiff 生效，大图推荐）。</summary>
        public bool Tiled = true;
        /// <summary>块大小（仅 TILED 时生效）。</summary>
        public int BlockSize = 256;
        /// <summary>BigTIFF 策略：YES/NO/IF_NEEDED/IF_SAFER。</summary>
        public string BigTiff = "IF_SAFER";
        /// <summary>JPEG 质量（1~100）。</summary>
        public int JpegQuality = 90;
        /// <summary>导出后是否顺带建立金字塔。</summary>
        public bool BuildOverviews;
        /// <summary>金字塔降采样因子。</summary>
        public int[] OverviewFactors = { 2, 4, 8, 16 };
    }

    /// <summary>格式转换 / 导出：CreateCopy + CreationOption，必要时把 alpha 展平为 NoData。</summary>
    public static class RasterExportService
    {
        /// <summary>
        /// 能真正保存 alpha 通道的格式。其余格式（Erdas IMG、JPEG、BMP 等）会把 alpha 当成
        /// 第 N+1 个普通波段，重新打开后无效区既参与统计又参与渲染，外观与 TIFF 不一致。
        /// </summary>
        private static readonly HashSet<string> AlphaCapableDrivers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GTiff", "PNG", "VRT" };

        /// <summary>按参数把图层导出为指定格式，失败或取消时清理输出与旁文件。</summary>
        /// <param name="layer">源图层。</param>
        /// <param name="o">导出参数。</param>
        /// <param name="progress">进度与取消上报器。</param>
        /// <returns>输出文件路径。</returns>
        public static string Run(RasterLayer layer, ExportOptions o, ProgressReporter progress)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (o == null || string.IsNullOrWhiteSpace(o.OutputPath)) throw new ArgumentException(Lang.T("请指定输出文件"));
            ProcessingUtil.ValidateOutputPath(o.OutputPath, layer.SourcePath, layer.Path);
            if (progress != null) progress.ThrowIfCancellationRequested();

            var driver = Gdal.GetDriverByName(o.Driver);
            if (driver == null) throw new InvalidOperationException(Lang.T("GDAL 不支持该输出格式: ") + o.Driver);

            var co = BuildCreationOptions(layer, o);

            // 目标格式不支持 alpha 时，先尝试“去 alpha + NoData 展平”，让结果与显示一致
            if (layer.AlphaBandIndex > 0 && !AlphaCapableDrivers.Contains(o.Driver))
            {
                if (TryExportFlattened(layer, o, driver, co, progress))
                {
                    BuildOverviewsIfNeeded(o, progress);
                    return o.OutputPath;
                }
            }

            Dataset dst = null;
            try
            {
                dst = driver.CreateCopy(o.OutputPath, layer.Ds, 0, co.ToArray(),
                    progress != null ? progress.Delegate : null, null);
                if (dst == null)
                {
                    if (progress != null && progress.WasCancelled) throw new OperationCanceledException(Lang.T("操作已取消"));
                    throw new InvalidOperationException(Lang.T("导出失败：") + ProcessingUtil.DescribeGdalError());
                }
                CopyColorInterpretation(layer, dst);
                dst.FlushCache();
                dst.Dispose();
                dst = null;
            }
            catch
            {
                if (dst != null) { try { dst.Dispose(); } catch { } }
                ProcessingUtil.DeleteOutputSet(o.OutputPath); // 避免留下半成品（含 .ovr/.aux.xml）
                throw;
            }

            BuildOverviewsIfNeeded(o, progress);
            return o.OutputPath;
        }

        /// <summary>按目标驱动与用户选择拼装 CreateCopy 的创建选项。</summary>
        /// <param name="layer">源图层，用于判断数据类型。</param>
        /// <param name="o">导出参数。</param>
        /// <returns>可直接传给 GDAL 的选项列表。</returns>
        private static List<string> BuildCreationOptions(RasterLayer layer, ExportOptions o)
        {
            var co = new List<string>();
            if (string.Equals(o.Driver, "GTiff", StringComparison.OrdinalIgnoreCase))
            {
                bool isByte = layer.BandTypes[0] == DataType.GDT_Byte;
                bool isFloat = layer.BandTypes[0] == DataType.GDT_Float32 || layer.BandTypes[0] == DataType.GDT_Float64;

                if (o.Tiled)
                {
                    co.Add("TILED=YES");
                    co.Add("BLOCKXSIZE=" + o.BlockSize);
                    co.Add("BLOCKYSIZE=" + o.BlockSize);
                }
                if (!string.IsNullOrEmpty(o.Compression) && o.Compression != Lang.T("无"))
                {
                    co.Add("COMPRESS=" + o.Compression);
                    if (o.Compression == "JPEG")
                    {
                        if (!isByte) throw new InvalidOperationException(Lang.T("JPEG 压缩仅支持 8 位整型数据"));
                        if (layer.BandCount != 1 && layer.BandCount != 3)
                            throw new InvalidOperationException(Lang.T("JPEG 压缩仅支持 1 或 3 个波段"));
                        co.Add("JPEG_QUALITY=" + o.JpegQuality);
                    }
                    else if (o.Compression == "LZW" || o.Compression == "DEFLATE")
                    {
                        co.Add("PREDICTOR=" + (isFloat ? 3 : 2));
                    }
                }
                if (!string.IsNullOrEmpty(o.BigTiff) && o.BigTiff != Lang.T("不限制"))
                    co.Add("BIGTIFF=" + o.BigTiff);
            }
            else if (string.Equals(o.Driver, "JPEG", StringComparison.OrdinalIgnoreCase))
            {
                co.Add("QUALITY=" + o.JpegQuality);
            }
            else if (string.Equals(o.Driver, "PNG", StringComparison.OrdinalIgnoreCase))
            {
                co.Add("ZLEVEL=6");
            }
            return co;
        }

        /// <summary>
        /// 把带 alpha 的影像展平为不带 alpha 的输出：丢掉 alpha 波段，
        /// 把 alpha 覆盖不到的像素写成 NoData，并保留色彩解释提示。
        /// 仅在目标驱动支持直接 Create 时可用；不支持时返回 false，由调用方回退到 CreateCopy。
        /// </summary>
        /// <param name="layer">源图层。</param>
        /// <param name="o">导出参数。</param>
        /// <param name="driver">目标驱动。</param>
        /// <param name="co">已构造的创建选项（仅 GTiff 会实际使用）。</param>
        /// <param name="progress">进度与取消上报器。</param>
        /// <returns>是否已成功写出。</returns>
        private static bool TryExportFlattened(RasterLayer layer, ExportOptions o, Driver driver,
            List<string> co, ProgressReporter progress)
        {
            int alpha = layer.AlphaBandIndex;
            var keep = new List<int>();
            for (int i = 1; i <= layer.BandCount; i++)
                if (i != alpha) keep.Add(i);
            if (keep.Count == 0) return false;

            // 非 GTiff 不接受 TILED/COMPRESS 之类的选项，Create 时一律不传
            string[] createOptions = string.Equals(o.Driver, "GTiff", StringComparison.OrdinalIgnoreCase)
                ? co.ToArray() : null;

            Dataset dst = null;
            Band alphaBand = null;
            try
            {
                dst = driver.Create(o.OutputPath, layer.Width, layer.Height, keep.Count,
                    layer.BandTypes[0], createOptions);
                if (dst == null) return false;

                double nd = layer.HasNoData ? layer.NoDataValue : 0;
                alphaBand = layer.Ds.GetRasterBand(alpha);

                int w = layer.Width;
                int blockRows = Math.Max(1, Math.Min(layer.Height, 512));
                var buffer = new double[w * blockRows];
                var mask = new byte[w * blockRows];

                for (int j = 0; j < keep.Count; j++)
                {
                    if (progress != null) progress.ThrowIfCancellationRequested();
                    Band src = null, target = null;
                    try
                    {
                        src = layer.Ds.GetRasterBand(keep[j]);
                        target = dst.GetRasterBand(j + 1);

                        for (int row = 0; row < layer.Height; row += blockRows)
                        {
                            int rows = Math.Min(blockRows, layer.Height - row);
                            int n = w * rows;
                            src.ReadRaster(0, row, w, rows, buffer, w, rows, 0, 0);
                            alphaBand.ReadRaster(0, row, w, rows, mask, w, rows, 0, 0);
                            for (int i = 0; i < n; i++)
                                if (mask[i] < 128) buffer[i] = nd;
                            target.WriteRaster(0, row, w, rows, buffer, w, rows, 0, 0);

                            if (progress != null)
                                progress.Report((j + (row + rows) / (double)layer.Height) / keep.Count, Lang.T("导出中…"));
                        }

                        target.SetNoDataValue(nd);
                        ApplyColorInterpHint(target, layer.ColorInterps[keep[j] - 1]);
                        target.FlushCache();
                    }
                    finally
                    {
                        if (target != null) try { target.Dispose(); } catch { }
                        if (src != null) try { src.Dispose(); } catch { }
                    }
                }

                dst.SetGeoTransform(layer.GeoTransform);
                dst.SetProjection(layer.ProjectionWkt);
                dst.FlushCache();
                dst.Dispose();
                dst = null;
                return true;
            }
            catch (OperationCanceledException)
            {
                if (dst != null) { try { dst.Dispose(); } catch { } }
                ProcessingUtil.DeleteOutputSet(o.OutputPath);
                throw;
            }
            catch
            {
                // 该驱动不支持 Create（如 AAIGrid）：清理后交给 CreateCopy 回退
                if (dst != null) { try { dst.Dispose(); } catch { } }
                ProcessingUtil.DeleteOutputSet(o.OutputPath);
                return false;
            }
            finally
            {
                if (alphaBand != null) try { alphaBand.Dispose(); } catch { }
            }
        }

        /// <summary>把源波段的色彩解释复制到目标波段，并写一条元数据提示供不支持该属性的格式回读。</summary>
        /// <param name="layer">源图层。</param>
        /// <param name="dst">目标数据集。</param>
        private static void CopyColorInterpretation(RasterLayer layer, Dataset dst)
        {
            int count = Math.Min(layer.BandCount, dst.RasterCount);
            for (int i = 1; i <= count; i++)
            {
                Band db = null;
                try
                {
                    db = dst.GetRasterBand(i);
                    ApplyColorInterpHint(db, layer.ColorInterps[i - 1]);
                    db.FlushCache();
                }
                catch { /* 个别驱动不支持写入，忽略 */ }
                finally
                {
                    if (db != null) try { db.Dispose(); } catch { }
                }
            }
        }

        /// <summary>写入色彩解释：先设置官方属性，再落一条 <c>COLOR_INTERP</c> 元数据作为兜底。</summary>
        /// <param name="band">目标波段。</param>
        /// <param name="interp">源波段的色彩解释。</param>
        private static void ApplyColorInterpHint(Band band, ColorInterp interp)
        {
            if (interp == ColorInterp.GCI_Undefined) return;
            try { band.SetRasterColorInterpretation(interp); } catch { }
            try
            {
                string name = RasterLayer.ColorInterpName(interp);
                if (name.Length > 0) band.SetMetadataItem("COLOR_INTERP", name, "");
            }
            catch { }
        }

        /// <summary>按需在导出产物上建立金字塔；失败不影响主结果，但取消要继续抛出。</summary>
        /// <param name="o">导出参数。</param>
        /// <param name="progress">进度与取消上报器。</param>
        private static void BuildOverviewsIfNeeded(ExportOptions o, ProgressReporter progress)
        {
            if (!o.BuildOverviews) return;
            try
            {
                OverviewService.Build(o.OutputPath, o.OverviewFactors,
                    progress != null ? progress.Segment(0.85, 1.0, Lang.T("建立金字塔…")) : null);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 金字塔失败不影响主结果 */ }
        }
    }
}
