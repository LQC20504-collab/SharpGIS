using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OSGeo.GDAL;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
        /// <summary>DEM 派生参数。</summary>
    public sealed class DemParams
    {
        /// <summary>参与计算的波段号（1 起）。</summary>
        public int Band = 1;
        /// <summary>算法名：hillshade | slope | aspect | color-relief（本工具只封装这四种）。</summary>
        public string Algorithm = "hillshade";
        /// <summary>光源方位角（度，0=北，顺时针）。</summary>
        public double Azimuth = 315;
        /// <summary>光源高度角（度）。</summary>
        public double Altitude = 45;
        /// <summary>高程夸张系数。</summary>
        public double ZFactor = 1;
        /// <summary>垂直单位换算系数（度制 DEM 转米时需要）。</summary>
        public double Scale = 1;
        /// <summary>是否计算边缘，避免输出一圈 NoData。</summary>
        public bool ComputeEdges = true;
        /// <summary>坡度是否以百分比输出（默认度）。</summary>
        public bool SlopePercent;
        /// <summary>彩色渲染使用的色带。</summary>
        public ColorRamp Ramp = ColorRamp.Terrain;
        /// <summary>色带映射下限。</summary>
        public double RampMin = 0;
        /// <summary>色带映射上限。</summary>
        public double RampMax = 255;
    }

    /// <summary>DEM 派生：山体阴影 / 坡度 / 坡向 / 彩色渲染，底层调用 Gdal.wrapper_GDALDEMProcessing。</summary>
    public static class DemService
    {
        /// <summary>
        /// 算法下拉的可选项。用属性而非静态字段：语言在运行时才确定，
        /// 静态字段会在类初始化时把中文固化，切换语言后就取不到英文了。
        /// </summary>
        public static string[] AlgorithmNames
        {
            get { return new[] { Lang.T("山体阴影 hillshade"), Lang.T("坡度 slope"), Lang.T("坡向 aspect"), Lang.T("彩色渲染 color-relief") }; }
        }

        /// <summary>把界面显示名转换为 GDAL 算法标识。</summary>
        /// <param name="display">界面选项文本。</param>
        /// <returns>hillshade/slope/aspect/color-relief 之一。</returns>
        public static string AlgorithmKey(string display)
        {
            if (string.IsNullOrEmpty(display)) return "hillshade";
            string s = display.ToLowerInvariant();
            if (s.Contains("slope") || s.Contains(Lang.T("坡度"))) return "slope";
            if (s.Contains("aspect") || s.Contains(Lang.T("坡向"))) return "aspect";
            if (s.Contains("relief") || s.Contains(Lang.T("彩色"))) return "color-relief";
            return "hillshade";
        }

        /// <summary>执行 DEM 派生（山体阴影/坡度/坡向/彩色渲染）。</summary>
        /// <param name="layer">源图层。</param>
        /// <param name="p">派生参数。</param>
        /// <param name="outPath">输出文件路径。</param>
        /// <param name="progress">进度与取消上报器。</param>
        /// <returns>输出文件路径。</returns>
        public static string Run(RasterLayer layer, DemParams p, string outPath, ProgressReporter progress)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            ProcessingUtil.ValidateOutputPath(outPath, layer.SourcePath, layer.Path);
            if (progress != null) progress.ThrowIfCancellationRequested();
            if (p == null) p = new DemParams();
            if (p.Band < 1 || p.Band > layer.BandCount) throw new ArgumentOutOfRangeException("Band", Lang.T("波段号越界"));

            var o = new OptionBuilder();
            o.AddPair("-of", "GTiff");
            o.Add("-b").Add(p.Band.ToString());
            if (p.ComputeEdges) o.Add("-compute_edges");

            string colorFile = null;
            try
            {
                switch (p.Algorithm)
                {
                    case "hillshade":
                        o.Add("-az").Add(p.Azimuth.ToString("G17", CultureInfo.InvariantCulture));
                        o.Add("-alt").Add(p.Altitude.ToString("G17", CultureInfo.InvariantCulture));
                        o.Add("-z").Add(p.ZFactor.ToString("G17", CultureInfo.InvariantCulture));
                        if (Math.Abs(p.Scale - 1.0) > 1e-12)
                            o.Add("-s").Add(p.Scale.ToString("G17", CultureInfo.InvariantCulture));
                        break;
                    case "slope":
                        if (p.SlopePercent) o.Add("-p");
                        o.Add("-s").Add(p.Scale.ToString("G17", CultureInfo.InvariantCulture));
                        break;
                    case "aspect":
                        break;
                    case "color-relief":
                        // 色表通过独立参数传入（不是 -colorFilename 选项）
                        colorFile = WriteColorFile(p.Ramp, p.RampMin, p.RampMax);
                        break;
                }
                o.CreationOption("TILED=YES");
                o.CreationOption("COMPRESS=DEFLATE");

                Dataset dst = null;
                try
                {
                    var opts = new GDALDEMProcessingOptions(o.ToArray());
                    dst = Gdal.wrapper_GDALDEMProcessing(outPath, layer.Ds, p.Algorithm, colorFile ?? string.Empty,
                        opts, progress != null ? progress.Delegate : null, null);
                    if (dst == null)
                    {
                        if (progress != null && progress.WasCancelled) throw new OperationCanceledException(Lang.T("操作已取消"));
                        throw new InvalidOperationException(Lang.T("DEM 处理失败：") + ProcessingUtil.DescribeGdalError());
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
            finally
            {
                if (colorFile != null) ProcessingUtil.DeleteQuietly(colorFile);
            }
        }

        /// <summary>生成 gdal 色表文件（纯 ASCII 路径，避免中文路径在这条通道上出问题）。</summary>
        private static string WriteColorFile(ColorRamp ramp, double min, double max)
        {
            string dir = Path.Combine(Path.GetTempPath(), "SharpGIS");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "dem_color_" + Guid.NewGuid().ToString("N") + ".txt");

            var lines = new List<string>();
            int steps = 32;
            for (int i = 0; i < steps; i++)
            {
                double t = i / (double)(steps - 1);
                double v = min + (max - min) * t;
                var c = (ramp ?? ColorRamp.Terrain).Sample(t);
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0:G17} {1} {2} {3}", v, c.R, c.G, c.B));
            }
            lines.Add("nv 0 0 0 0");
            File.WriteAllLines(path, lines);
            return path;
        }
    }
}
