using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OSGeo.GDAL;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Services
{
    /// <summary>数据集打开与元数据抽取。</summary>
    public static class DatasetService
    {
        /// <summary>打开栅格文件并抽取元数据；带旋转的影像会自动正视化。</summary>
        /// <param name="path">影像文件路径。</param>
        /// <returns>可渲染的图层对象，调用方负责 Dispose。</returns>
        public static RasterLayer Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException(Lang.T("文件不存在: ") + path);

            GdalBootstrap.Ensure();
            if (!GdalBootstrap.Usable)
                throw new InvalidOperationException(GdalBootstrap.LastError);

            Dataset ds = null;
            try
            {
                ds = Gdal.Open(path, Access.GA_ReadOnly);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(Lang.T("打开影像失败: ") + path + "\r\n" + ex.Message, ex);
            }

            if (ds == null)
                throw new InvalidOperationException(Lang.T("GDAL 无法识别该文件: ") + path + "\r\n" + LastError());

            var layer = new RasterLayer(ds, path);
            if (layer.Width <= 0 || layer.Height <= 0 || layer.BandCount <= 0)
            {
                layer.Dispose();
                throw new InvalidOperationException(Lang.T("该文件不含栅格波段: ") + path);
            }

            // 带旋转的仿射变换无法用“行对齐”的快速渲染路径表达，转成正视 VRT（源文件只读，不会修改）
            if (layer.IsRotated)
            {
                var rectified = RectifyRotated(layer);
                if (rectified != null) return rectified;
            }
            return layer;
        }

        /// <summary>把带旋转的影像包装为北向上的临时 VRT。失败时返回 null（退回原样打开）。</summary>
        private static RasterLayer RectifyRotated(RasterLayer layer)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SharpGIS");
            string vrtPath = null;
            try
            {
                Directory.CreateDirectory(tempDir);
                vrtPath = Path.Combine(tempDir, "rectify_" + Guid.NewGuid().ToString("N") + ".vrt");
                var opts = new GDALWarpAppOptions(new[] { "-of", "VRT", "-r", "near", "-dstalpha" });
                Dataset vrt = Gdal.Warp(vrtPath, new[] { layer.Ds }, opts, null, null);
                if (vrt == null || !File.Exists(vrtPath))
                {
                    if (vrt != null) vrt.Dispose();
                    return null;
                }
                vrt.Dispose();

                var newDs = Gdal.Open(vrtPath, Access.GA_ReadOnly);
                if (newDs == null) return null;

                string sourcePath = layer.SourcePath;
                string name = layer.Name;
                var rectified = new RasterLayer(newDs, sourcePath)
                {
                    Name = name,
                    IsRectified = true,
                    TempFilePath = vrtPath
                };
                if (rectified.IsRotated || rectified.Width <= 0)
                {
                    // 正视化未生效，别用坏数据
                    newDs.Dispose();
                    try { File.Delete(vrtPath); } catch { }
                    return null;
                }
                return rectified;
            }
            catch
            {
                try { if (vrtPath != null && File.Exists(vrtPath)) File.Delete(vrtPath); } catch { }
                return null;
            }
        }

        /// <summary>读取 GDAL 最近一次错误信息。</summary>
        /// <returns>错误文本。</returns>
        public static string LastError()
        {
            try
            {
                string msg = Gdal.GetLastErrorMsg();
                return string.IsNullOrEmpty(msg) ? Lang.T("（无更多信息）") : msg;
            }
            catch { return Lang.T("（无法读取 GDAL 错误信息）"); }
        }

        /// <summary>拼装 OpenFileDialog 的过滤器（含所有可读扩展名）。</summary>
        /// <returns>过滤器字符串。</returns>
        public static string BuildDialogFilter()
        {
            var exts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var e in GdalBootstrap.RasterDriverExtensions()) exts.Add(e);
            }
            catch { }

            var sb = new StringBuilder();
            sb.Append(Lang.T("所有支持的栅格 ("));
            int i = 0;
            foreach (var e in exts)
            {
                if (i > 0) sb.Append(';');
                sb.Append(e);
                i++;
            }
            sb.Append(")|");
            i = 0;
            foreach (var e in exts)
            {
                if (i > 0) sb.Append(';');
                sb.Append(e);
                i++;
            }
            sb.Append(Lang.T("|常用影像 (*.tif;*.tiff;*.img;*.dem;*.bmp;*.jpg;*.png)|*.tif;*.tiff;*.img;*.dem;*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*"));
            return sb.ToString();
        }

        /// <summary>把图层元数据格式化为多行文本（元数据面板与自检共用）。</summary>
        /// <param name="layer">目标图层。</param>
        /// <returns>可直接显示的文本。</returns>
        public static string DescribeMetadata(RasterLayer layer)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Lang.T("名称: ") + layer.Name);
            sb.AppendLine(Lang.T("路径: ") + layer.Path);
            sb.AppendLine(Lang.T("驱动: ") + layer.DriverName);
            sb.AppendLine(Lang.T("尺寸: ") + layer.Width + " × " + layer.Height + Lang.T(" 像素"));
            sb.AppendLine(Lang.T("波段数: ") + layer.BandCount);
            for (int i = 0; i < layer.BandCount; i++)
            {
                string desc = string.IsNullOrEmpty(layer.BandDescriptions[i]) ? "" : " (" + layer.BandDescriptions[i] + ")";
                sb.AppendLine(string.Format(Lang.T("  波段 {0}: {1} / {2}{3}"), i + 1,
                    layer.BandTypes[i].ToString().Replace("GDT_", ""),
                    layer.ColorInterps[i].ToString().Replace("GCI_", ""), desc));
            }
            sb.AppendLine(Lang.T("数据类型: ") + layer.TypeText);
            sb.AppendLine(Lang.T("地理变换: ") + (layer.HasGeoTransform
                ? string.Format("({0:G8}, {1:G8}, {2:G8}, {3:G8}, {4:G8}, {5:G8})",
                    layer.GeoTransform[0], layer.GeoTransform[1], layer.GeoTransform[2],
                    layer.GeoTransform[3], layer.GeoTransform[4], layer.GeoTransform[5])
                : Lang.T("（无）")));
            sb.AppendLine(Lang.T("分辨率: ") + layer.PixelSizeX.ToString("G6") + " × " + layer.PixelSizeY.ToString("G6"));
            sb.AppendLine(Lang.T("范围: ") + layer.Extent);
            sb.AppendLine(Lang.T("投影: ") + layer.EpsgText);
            sb.AppendLine("NoData: " + (layer.HasNoData ? layer.NoDataValue.ToString("G6") : Lang.T("（未设置）")));
            sb.AppendLine(Lang.T("块大小: ") + layer.BlockX + " × " + layer.BlockY);
            sb.AppendLine(Lang.T("压缩: ") + (string.IsNullOrEmpty(layer.Compression) ? Lang.T("无") : layer.Compression));
            if (layer.FileList != null && layer.FileList.Length > 1)
                sb.AppendLine(Lang.T("关联文件: ") + string.Join("; ", layer.FileList));
            return sb.ToString();
        }
    }
}
