using System;
using System.Collections.Generic;
using System.IO;
using OSGeo.GDAL;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
    /// <summary>一次重投影/重采样/裁剪请求的全部参数。</summary>
    public sealed class WarpRequest
    {
        /// <summary>输出文件路径（-of VRT 时以 .vrt 结尾）。</summary>
        public string OutputPath;
        /// <summary>输出格式短名，默认 GTiff。</summary>
        public string OutputFormat = "GTiff";
        /// <summary>目标坐标系（EPSG:xxxx / WKT / PROJ 串），null 表示不改变。</summary>
        public string TargetSrs;
        /// <summary>源坐标系覆盖（-s_srs）：源影像无投影时，重投影必须提供。</summary>
        public string SourceSrsOverride;
        /// <summary>目标像元宽（地图单位，正数）；与 <see cref="Width"/>/<see cref="Height"/> 互斥。</summary>
        public double? PixelSize;
        /// <summary>
        /// 目标像元高。为空时沿用 <see cref="PixelSize"/>（方形像元）；
        /// 按参考图层对齐时用它保留非方形像元。
        /// </summary>
        public double? PixelSizeY;
        /// <summary>输出列数（-ts），与 <see cref="PixelSize"/> 互斥。</summary>
        public int? Width;
        /// <summary>输出行数（-ts），与 <see cref="PixelSize"/> 互斥。</summary>
        public int? Height;
        /// <summary>输出范围（-te 语义：目标坐标系下的 xmin/ymin/xmax/ymax）。</summary>
        public GeoRect? Extent;
        /// <summary>重采样算法名（中文或 GDAL 短名均可）。</summary>
        public string Resample = "bilinear";
        /// <summary>输出 NoData 值，null 表示沿用源设置。</summary>
        public double? NoData;
        /// <summary>GTiff 创建选项（不带 -co 前缀），VRT 输出时应传空数组。</summary>
        public string[] CreationOptions = { "TILED=YES", "COMPRESS=LZW" };
        /// <summary>输出波段号（1 起），空或 null 表示全部波段。</summary>
        public int[] Bands;
    }

    /// <summary>重投影 / 重采样 / 按范围裁剪 / 按视图导出，统一走 Gdal.Warp。</summary>
    public static class WarpService
    {
        /// <summary>
        /// 重采样名称 → GDAL 标识。用属性而非静态字段：字典的键是显示名，
        /// 显示名随语言变化，静态初始化会把语言固化，之后反查就会失效。
        /// </summary>
        private static Dictionary<string, string> ResampleNames
        {
            get
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { Lang.T("最近邻"), "near" }, { Lang.T("双线性"), "bilinear" }, { Lang.T("三次卷积"), "cubic" },
                    { Lang.T("三次样条"), "cubicspline" }, { "Lanczos", "lanczos" }, { Lang.T("平均值"), "average" },
                    { Lang.T("众数"), "mode" }, { Lang.T("最大值"), "max" }, { Lang.T("最小值"), "min" }, { Lang.T("中值"), "med" }
                };
            }
        }

        /// <summary>界面下拉可选的重采样算法名。</summary>
        public static string[] ResampleChoices { get { return new[] { Lang.T("最近邻"), Lang.T("双线性"), Lang.T("三次卷积"), Lang.T("三次样条"), "Lanczos", Lang.T("平均值"), Lang.T("众数") }; } }

        /// <summary>按当前视图范围与分辨率构造请求（“导出当前视图”）。</summary>
        /// <param name="layer">源图层。</param>
        /// <param name="view">当前视口，提供范围与地图单位/像素。</param>
        /// <param name="outPath">输出文件路径。</param>
        /// <returns>已填好范围与分辨率的请求对象。</returns>

        public static WarpRequest FromCurrentView(RasterLayer layer, Viewport view, string outPath)
        {
            var rect = view.GetGeoRect().Intersect(layer.Extent);
            return new WarpRequest
            {
                OutputPath = outPath,
                Extent = rect,
                PixelSize = view.Mupp,
                Resample = "bilinear"
            };
        }

        /// <summary>执行重投影 / 重采样 / 裁剪，失败或取消时清理输出。</summary>
        /// <param name="layer">源图层。</param>
        /// <param name="req">请求参数。</param>
        /// <param name="progress">进度与取消上报器。</param>
        /// <returns>输出文件路径。</returns>
        public static string Run(RasterLayer layer, WarpRequest req, ProgressReporter progress)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (req == null || string.IsNullOrWhiteSpace(req.OutputPath)) throw new ArgumentException(Lang.T("请指定输出文件"));
            ProcessingUtil.ValidateOutputPath(req.OutputPath, layer.SourcePath, layer.Path);
            if (progress != null) progress.ThrowIfCancellationRequested();

            var o = new OptionBuilder();
            o.AddPair("-of", req.OutputFormat ?? "GTiff");
            o.AddPair("-r", NormalizeResample(req.Resample));

            bool srcHasSrs = layer.HasProjection;
            string srcSrs = req.SourceSrsOverride;
            if (!srcHasSrs && string.IsNullOrWhiteSpace(srcSrs) && !string.IsNullOrWhiteSpace(req.TargetSrs))
                throw new InvalidOperationException(Lang.T("源影像没有投影信息，请先指定源坐标系（-s_srs）再重投影。"));

            if (!string.IsNullOrWhiteSpace(srcSrs)) o.AddPair("-s_srs", srcSrs);
            if (!string.IsNullOrWhiteSpace(req.TargetSrs)) o.AddPair("-t_srs", req.TargetSrs);

            if (req.Extent.HasValue && req.Extent.Value.IsValid)
            {
                var r = req.Extent.Value;
                // -te 需要 4 个独立参数（C# 选项数组按 argv 逐项传递）
                o.Add("-te");
                o.Add(r.XMin.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
                o.Add(r.YMin.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
                o.Add(r.XMax.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
                o.Add(r.YMax.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (req.PixelSize.HasValue && req.PixelSize.Value > 0)
            {
                double px = req.PixelSize.Value;
                double py = req.PixelSizeY.HasValue && req.PixelSizeY.Value > 0 ? req.PixelSizeY.Value : px;
                o.Add("-tr");
                o.Add(px.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
                o.Add(py.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (req.Width.HasValue && req.Height.HasValue && req.Width.Value > 0 && req.Height.Value > 0)
            {
                // -ts 需要 3 个独立 token（与 -te 同理，不能拼成一个字符串）
                o.Add("-ts");
                o.Add(req.Width.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                o.Add(req.Height.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (req.NoData.HasValue)
                o.AddPair("-dstnodata", req.NoData.Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));

            if (req.Bands != null && req.Bands.Length > 0)
            {
                o.Add("-b");
                foreach (var b in req.Bands) o.Add(b.ToString());
            }

            if (req.CreationOptions != null)
                foreach (var co in req.CreationOptions) o.CreationOption(co);

            Dataset dst = null;
            try
            {
                var opts = new GDALWarpAppOptions(o.ToArray());
                dst = Gdal.Warp(req.OutputPath, new[] { layer.Ds }, opts, progress != null ? progress.Delegate : null, null);
                if (dst == null)
                {
                    if (progress != null && progress.WasCancelled) throw new OperationCanceledException(Lang.T("操作已取消"));
                    throw new InvalidOperationException(Lang.T("重投影失败：") + ProcessingUtil.DescribeGdalError());
                }
                dst.FlushCache();
                dst.Dispose();
                dst = null;
            }
            catch
            {
                if (dst != null) { try { dst.Dispose(); } catch { } }
                ProcessingUtil.DeleteOutputSet(req.OutputPath);
                throw;
            }
            return req.OutputPath;
        }

        /// <summary>把界面显示的重采样名转换为 GDAL 短名。</summary>
        /// <param name="name">中文名或 GDAL 短名。</param>
        /// <returns>GDAL 认可的重采样短名，未知输入按小写原样返回。</returns>
        public static string NormalizeResample(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "bilinear";
            string mapped;
            if (ResampleNames.TryGetValue(name, out mapped)) return mapped;
            return name.ToLowerInvariant();
        }

        /// <summary>把用户输入的坐标系解析并导出为 WKT，非法时抛 <see cref="InvalidOperationException"/>。</summary>
        /// <param name="input">EPSG:xxxx、WKT 或 PROJ 串。</param>
        /// <returns>规范化的 WKT 字符串；输入为空时返回 null。</returns>
        public static string NormalizeSrs(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string s = input.Trim();
            try
            {
                using (var sr = new OSGeo.OSR.SpatialReference(""))
                {
                    if (sr.SetFromUserInput(s) != 0) throw new InvalidOperationException(Lang.T("无法识别的坐标系: ") + input);
                    string wkt;
                    sr.ExportToWkt(out wkt, null);
                    return wkt;
                }
            }
            catch (InvalidOperationException) { throw; }
            catch { throw new InvalidOperationException(Lang.T("无法识别的坐标系: ") + input); }
        }
    }
}
