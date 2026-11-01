using System;
using OSGeo.GDAL;
using SharpGIS.Models;

namespace SharpGIS.Rendering
{
        /// <summary>一个图层在当前视图中的源窗口与目标屏幕矩形。</summary>
    public struct TilePlacement
    {
        /// <summary>读窗起始列（对齐到源像素）。</summary>
        public int Col0;
        /// <summary>读窗起始行（对齐到源像素）。</summary>
        public int Row0;
        /// <summary>读窗列数。</summary>
        public int NCols;
        /// <summary>读窗行数。</summary>
        public int NRows;
        /// <summary>tile 缓冲宽（快速档小于目标宽）。</summary>
        public int OutW;
        /// <summary>tile 缓冲高。</summary>
        public int OutH;
        /// <summary>目标矩形左上角 X。</summary>
        public int DstX;
        /// <summary>目标矩形左上角 Y。</summary>
        public int DstY;
        /// <summary>目标矩形宽。</summary>
        public int DstW;
        /// <summary>目标矩形高。</summary>
        public int DstH;

        // ---- 以下为旋转渲染所需：tile 在“未旋转屏幕空间”中的目标矩形 + 旋转系数 ----

        /// <summary>未旋转屏幕空间中的目标左边界（可含小数）。</summary>
        public double QX0;
        /// <summary>未旋转屏幕空间中的目标上边界。</summary>
        public double QY0;
        /// <summary>未旋转屏幕空间中的目标右边界。</summary>
        public double QX1;
        /// <summary>未旋转屏幕空间中的目标下边界。</summary>
        public double QY1;
        /// <summary>视图旋转角余弦（合成时使用）。</summary>
        public double QC;
        /// <summary>视图旋转角正弦（合成时使用）。</summary>
        public double QS;
        /// <summary>是否需要走旋转合成路径。</summary>
        public bool Rotated;
    }

    /// <summary>
    /// 把单个图层渲染成屏幕空间的 RGBA tile。
    /// 读窗按源像素网格对齐，屏幕落点由对齐后的地理范围反推，因此平移不会累积漂移。
    /// </summary>
    public static class RasterTileRenderer
    {
        /// <summary>单块读窗的像素上限：超过时按块切分，避免旋转导致 AABB 放大后整层消失。</summary>
        private const long MaxWindowPixels = 400_000_000L;

        /// <summary>计算图层在当前视图中的读窗与屏幕落点（单块）。</summary>
        /// <param name="layer">目标图层。</param>
        /// <param name="view">视口。</param>
        /// <param name="qualityScale">质量系数，1=全分辨率，0.5=半分辨率。</param>
        /// <param name="p">输出的放置信息。</param>
        /// <returns>false 表示图层不可见、变换非法或窗口过大。</returns>
        public static bool TryComputePlacement(RasterLayer layer, Viewport view, double qualityScale, out TilePlacement p)
        {
            p = new TilePlacement();
            int col0, row0, col1, row1;
            if (!ComputeWindow(layer, view, out col0, out row0, out col1, out row1)) return false;
            if ((long)(col1 - col0) * (row1 - row0) > MaxWindowPixels) return false;
            return BuildPlacement(layer, view, qualityScale, col0, row0, col1, row1, out p);
        }

        /// <summary>
        /// 计算一个图层在当前视图下需要的全部放置块。常规情况下只有一块，
        /// 读窗超过 <see cref="MaxWindowPixels"/> 时按 2×2（必要时更多）切分，保证极端缩放下图层仍然可见。
        /// </summary>
        /// <param name="layer">目标图层。</param>
        /// <param name="view">视口。</param>
        /// <param name="qualityScale">质量系数。</param>
        /// <param name="buffer">放置结果输出缓冲，建议长度 4。</param>
        /// <returns>块数；0 表示该图层不可见。</returns>
        public static int ComputePlacements(RasterLayer layer, Viewport view, double qualityScale, TilePlacement[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return 0;

            TilePlacement single;
            if (TryComputePlacement(layer, view, qualityScale, out single))
            {
                buffer[0] = single;
                return 1;
            }

            // 只有“窗口过大”这一种失败才值得切分；其它原因（不可见、变换非法）直接返回
            int col0, row0, col1, row1;
            if (!ComputeWindow(layer, view, out col0, out row0, out col1, out row1)) return 0;

            int cols = col1 - col0, rows = row1 - row0;
            if (cols <= 0 || rows <= 0) return 0;
            if ((long)cols * rows <= MaxWindowPixels) return 0;

            int k = 2;
            while (k < 4 && (long)Math.Ceiling(cols / (double)k) * Math.Ceiling(rows / (double)k) > MaxWindowPixels) k++;
            if (k > buffer.Length) k = buffer.Length;

            int n = 0;
            for (int by = 0; by < k && n < buffer.Length; by++)
            {
                int r0 = row0 + (int)((long)rows * by / k);
                int r1 = row0 + (int)((long)rows * (by + 1) / k);
                for (int bx = 0; bx < k && n < buffer.Length; bx++)
                {
                    int c0 = col0 + (int)((long)cols * bx / k);
                    int c1 = col0 + (int)((long)cols * (bx + 1) / k);
                    if (c1 <= c0 || r1 <= r0) continue;

                    TilePlacement part;
                    if (BuildPlacement(layer, view, qualityScale, c0, r0, c1, r1, out part))
                        buffer[n++] = part;
                }
            }
            return n;
        }

        /// <summary>计算读窗（对齐到源像素网格）。</summary>
        /// <param name="layer">目标图层。</param>
        /// <param name="view">视口。</param>
        /// <param name="col0">起始列。</param>
        /// <param name="row0">起始行。</param>
        /// <param name="col1">结束列（不含）。</param>
        /// <param name="row1">结束行（不含）。</param>
        /// <returns>窗口是否有效。</returns>
        private static bool ComputeWindow(RasterLayer layer, Viewport view,
            out int col0, out int row0, out int col1, out int row1)
        {
            col0 = row0 = col1 = row1 = 0;
            if (layer == null || view == null) return false;
            if (!view.GetGeoRect().Intersects(layer.Extent)) return false;

            var gt = layer.GeoTransform;
            if (Math.Abs(gt[1]) <= 0 || Math.Abs(gt[5]) <= 0) return false;

            var geo = view.GetGeoRect().Intersect(layer.Extent);
            if (!geo.IsValid) return false;

            double col0d = (geo.XMin - gt[0]) / gt[1];
            double col1d = (geo.XMax - gt[0]) / gt[1];
            double row0d = (geo.YMax - gt[3]) / gt[5];
            double row1d = (geo.YMin - gt[3]) / gt[5];
            if (col0d > col1d) { double t = col0d; col0d = col1d; col1d = t; }
            if (row0d > row1d) { double t = row0d; row0d = row1d; row1d = t; }

            col0 = Math.Max(0, Math.Min(layer.Width, (int)Math.Floor(col0d)));
            col1 = Math.Max(0, Math.Min(layer.Width, (int)Math.Ceiling(col1d)));
            row0 = Math.Max(0, Math.Min(layer.Height, (int)Math.Floor(row0d)));
            row1 = Math.Max(0, Math.Min(layer.Height, (int)Math.Ceiling(row1d)));

            return col1 > col0 && row1 > row0;
        }

        /// <summary>由已对齐的读窗构造放置信息（含未旋转目标矩形与旋转系数）。</summary>
        /// <param name="layer">目标图层。</param>
        /// <param name="view">视口。</param>
        /// <param name="qualityScale">质量系数。</param>
        /// <param name="col0">起始列。</param>
        /// <param name="row0">起始行。</param>
        /// <param name="col1">结束列（不含）。</param>
        /// <param name="row1">结束行（不含）。</param>
        /// <param name="p">输出的放置信息。</param>
        /// <returns>是否成功。</returns>
        private static bool BuildPlacement(RasterLayer layer, Viewport view, double qualityScale,
            int col0, int row0, int col1, int row1, out TilePlacement p)
        {
            p = new TilePlacement();
            var gt = layer.GeoTransform;
            double pixW = Math.Abs(gt[1]);
            double pixH = Math.Abs(gt[5]);
            if (pixW <= 0 || pixH <= 0) return false;
            if (col1 <= col0 || row1 <= row0) return false;

            p.Col0 = col0;
            p.Row0 = row0;
            p.NCols = col1 - col0;
            p.NRows = row1 - row0;

            double gx0 = gt[0] + p.Col0 * gt[1];
            double gx1 = gt[0] + (p.Col0 + p.NCols) * gt[1];
            double gy0 = gt[3] + p.Row0 * gt[5];
            double gy1 = gt[3] + (p.Row0 + p.NRows) * gt[5];

            // 用视口自己的“未旋转”变换算落点；旋转在合成阶段统一施加
            var topLeft = view.ToUnrotatedScreen(gx0, gy1);
            var bottomRight = view.ToUnrotatedScreen(gx1, gy0);
            p.QX0 = Math.Min(topLeft.X, bottomRight.X);
            p.QY0 = Math.Min(topLeft.Y, bottomRight.Y);
            p.QX1 = Math.Max(topLeft.X, bottomRight.X);
            p.QY1 = Math.Max(topLeft.Y, bottomRight.Y);
            view.CosSin(out p.QC, out p.QS);
            p.Rotated = view.IsRotated;

            p.DstX = (int)Math.Round(p.QX0);
            p.DstY = (int)Math.Round(p.QY0);
            p.DstW = Math.Max(1, (int)Math.Round(p.QX1 - p.QX0));
            p.DstH = Math.Max(1, (int)Math.Round(p.QY1 - p.QY0));

            double effMupp = view.Mupp / Math.Max(0.05, qualityScale);
            p.OutW = Math.Max(1, (int)Math.Round(p.NCols * pixW / effMupp));
            p.OutH = Math.Max(1, (int)Math.Round(p.NRows * pixH / effMupp));
            p.OutW = Math.Min(p.OutW, 8192);
            p.OutH = Math.Min(p.OutH, 8192);
            return true;
        }

        /// <summary>把一个图层渲染成屏幕空间的 RGBA 缓冲。</summary>
        /// <param name="layer">目标图层。</param>
        /// <param name="p">读窗与目标尺寸。</param>
        /// <param name="resample">重采样算法。</param>
        /// <returns>行优先的 B,G,R,A 缓冲，长度 = OutW×OutH×4。</returns>
        public static byte[] RenderTile(RasterLayer layer, ref TilePlacement p, ResampleMode resample)
        {
            int w = p.OutW, h = p.OutH;
            var rgba = BufferPool.RentByte(w * h * 4);
            Array.Clear(rgba, 0, w * h * 4);

            bool useRgb = layer.Style.Mode == RenderMode.Rgb;
            bool usePalette = layer.Style.Mode == RenderMode.Palette;
            var style = layer.Style;

            float[] buf0 = null, buf1 = null, buf2 = null;
            byte[] mask = null;

            lock (layer.ReadLock)
            {
                if (layer.IsDisposed) return rgba;

                try
                {
                    var extra = new RasterIOExtraArg();
                    // 调色板是索引值，插值会产生不存在的颜色，统一用最近邻
                    extra.eResampleAlg = ToRio(usePalette ? ResampleMode.Nearest : resample);

                    if (usePalette)
                    {
                        buf0 = BufferPool.RentFloat(w * h);
                        ReadBand(layer, style.GrayBand, ref p, buf0, extra);
                        var ct = layer.GetColorTable(style.GrayBand);
                        var paletteLut = StretchEngine.BuildFromColorTable(ct);
                        if (ct != null) ct.Dispose();
                        ShadeIndexed(buf0, rgba, w, h, paletteLut);

                        if (layer.HasNoData)
                            ApplyNoDataAlpha(buf0, rgba, w, h, layer.NoDataValue);
                        ApplyAlphaBand(layer, ref p, rgba, w, h, ref mask);
                        return rgba;
                    }

                    if (useRgb)
                    {
                        buf0 = BufferPool.RentFloat(w * h);
                        buf1 = BufferPool.RentFloat(w * h);
                        buf2 = BufferPool.RentFloat(w * h);

                        int b0 = ClampBand(style.RgbBands[0], layer.BandCount);
                        int b1 = ClampBand(style.RgbBands[1], layer.BandCount);
                        int b2 = ClampBand(style.RgbBands[2], layer.BandCount);
                        ReadBand(layer, b0, ref p, buf0, extra);
                        ReadBand(layer, b1, ref p, buf1, extra);
                        ReadBand(layer, b2, ref p, buf2, extra);

                        var lutR = StretchEngine.Build(layer.Stats(b0), style.RStretch, ColorRamp.Gray, false, style.Gamma, style.Brightness, style.Contrast);
                        var lutG = StretchEngine.Build(layer.Stats(b1), style.GStretch, ColorRamp.Gray, false, style.Gamma, style.Brightness, style.Contrast);
                        var lutB = StretchEngine.Build(layer.Stats(b2), style.BStretch, ColorRamp.Gray, false, style.Gamma, style.Brightness, style.Contrast);

                        ShadeRgb(buf0, buf1, buf2, rgba, w, h, lutR, lutG, lutB);

                        if (layer.HasNoData)
                        {
                            ApplyNoDataAlpha(buf0, rgba, w, h, layer.NoDataValue);
                            ApplyNoDataAlpha(buf1, rgba, w, h, layer.NoDataValue);
                            ApplyNoDataAlpha(buf2, rgba, w, h, layer.NoDataValue);
                        }
                        ApplyAlphaBand(layer, ref p, rgba, w, h, ref mask);
                        return rgba;
                    }

                    // 单波段 + 色带
                    buf0 = BufferPool.RentFloat(w * h);
                    int bandNo = ClampBand(style.GrayBand, layer.BandCount);
                    ReadBand(layer, bandNo, ref p, buf0, extra);
                    var stats = layer.Stats(bandNo);
                    var grayLut = StretchEngine.Build(stats, style.GrayStretch, style.Ramp, style.InvertRamp,
                        style.Gamma, style.Brightness, style.Contrast);
                    ShadeGray(buf0, rgba, w, h, grayLut);

                    if (layer.HasNoData)
                        ApplyNoDataAlpha(buf0, rgba, w, h, layer.NoDataValue);
                    ApplyAlphaBand(layer, ref p, rgba, w, h, ref mask);
                    return rgba;
                }
                catch
                {
                    return rgba;
                }
                finally
                {
                    BufferPool.Return(buf0);
                    BufferPool.Return(buf1);
                    BufferPool.Return(buf2);
                    if (mask != null) BufferPool.Return(mask);
                }
            }
        }

        private static void ReadBand(RasterLayer layer, int bandNo, ref TilePlacement p, float[] buf, RasterIOExtraArg extra)
        {
            var band = layer.Ds.GetRasterBand(bandNo);
            try
            {
                band.ReadRaster(p.Col0, p.Row0, p.NCols, p.NRows, buf, p.OutW, p.OutH, 0, 0, extra);
            }
            finally { band.Dispose(); }
        }

        private static void ApplyAlphaBand(RasterLayer layer, ref TilePlacement p, byte[] rgba, int w, int h, ref byte[] mask)
        {
            int alphaBand = layer.AlphaBandIndex;
            if (alphaBand <= 0) return;
            lock (layer.ReadLock)
            {
                if (layer.IsDisposed) return;
                mask = BufferPool.RentByte(w * h);
                var band = layer.Ds.GetRasterBand(alphaBand);
                try
                {
                    var extra = new RasterIOExtraArg();
                    extra.eResampleAlg = RIOResampleAlg.GRIORA_Bilinear;
                    band.ReadRaster(p.Col0, p.Row0, p.NCols, p.NRows, mask, w, h, 0, 0, extra);
                }
                finally { band.Dispose(); }
            }
            for (int i = 0, j = 3; i < w * h; i++, j += 4)
                rgba[j] = (byte)(rgba[j] * mask[i] / 255);
        }

        private static unsafe void ShadeGray(float[] src, byte[] dst, int w, int h, ChannelLut lut)
        {
            int n = w * h;
            fixed (float* ps = src)
            fixed (byte* pd = dst)
            {
                for (int i = 0; i < n; i++)
                {
                    float v = ps[i];
                    if (float.IsNaN(v)) { pd[i * 4 + 3] = 0; continue; }
                    int idx = (int)((v - lut.Lo) * lut.Scale);
                    if (idx < 0) idx = 0; else if (idx >= lut.Size) idx = lut.Size - 1;
                    pd[i * 4] = lut.B[idx];
                    pd[i * 4 + 1] = lut.G[idx];
                    pd[i * 4 + 2] = lut.R[idx];
                    pd[i * 4 + 3] = 255;
                }
            }
        }

        private static unsafe void ShadeIndexed(float[] src, byte[] dst, int w, int h, ChannelLut lut)
        {
            int n = w * h;
            fixed (float* ps = src)
            fixed (byte* pd = dst)
            {
                for (int i = 0; i < n; i++)
                {
                    float v = ps[i];
                    if (float.IsNaN(v)) { pd[i * 4 + 3] = 0; continue; }
                    int idx = (int)v;
                    if (idx < 0) idx = 0; else if (idx > 255) idx = 255;
                    pd[i * 4] = lut.B[idx];
                    pd[i * 4 + 1] = lut.G[idx];
                    pd[i * 4 + 2] = lut.R[idx];
                    pd[i * 4 + 3] = 255;
                }
            }
        }

        private static unsafe void ShadeRgb(float[] r, float[] g, float[] b, byte[] dst, int w, int h,
            ChannelLut lr, ChannelLut lg, ChannelLut lb)
        {
            int n = w * h;
            fixed (float* pr = r)
            fixed (float* pg = g)
            fixed (float* pb = b)
            fixed (byte* pd = dst)
            {
                for (int i = 0; i < n; i++)
                {
                    int ir = (int)((pr[i] - lr.Lo) * lr.Scale);
                    if (ir < 0) ir = 0; else if (ir >= lr.Size) ir = lr.Size - 1;
                    int ig = (int)((pg[i] - lg.Lo) * lg.Scale);
                    if (ig < 0) ig = 0; else if (ig >= lg.Size) ig = lg.Size - 1;
                    int ib = (int)((pb[i] - lb.Lo) * lb.Scale);
                    if (ib < 0) ib = 0; else if (ib >= lb.Size) ib = lb.Size - 1;

                    pd[i * 4] = lb.B[ib];
                    pd[i * 4 + 1] = lg.G[ig];
                    pd[i * 4 + 2] = lr.R[ir];
                    pd[i * 4 + 3] = 255;
                }
            }
        }

        private static unsafe void ApplyNoDataAlpha(float[] src, byte[] rgba, int w, int h, double noData)
        {
            double tol = Math.Max(1e-6, Math.Abs(noData) * 1e-6);
            int n = w * h;
            fixed (float* ps = src)
            fixed (byte* pd = rgba)
            {
                for (int i = 0; i < n; i++)
                {
                    double v = ps[i];
                    if (Math.Abs(v - noData) <= tol) pd[i * 4 + 3] = 0;
                }
            }
        }

        private static int ClampBand(int bandNo, int bandCount)
        {
            if (bandNo < 1) return 1;
            if (bandNo > bandCount) return bandCount;
            return bandNo;
        }

        /// <summary>把界面重采样枚举转换为 GDAL 的 RasterIO 枚举。</summary>
        /// <param name="mode">界面重采样方式。</param>
        /// <returns>GDAL 的 RasterIO 重采样枚举。</returns>
        public static RIOResampleAlg ToRio(ResampleMode mode)
        {
            switch (mode)
            {
                case ResampleMode.Nearest: return RIOResampleAlg.GRIORA_NearestNeighbour;
                case ResampleMode.Cubic: return RIOResampleAlg.GRIORA_Cubic;
                case ResampleMode.Average: return RIOResampleAlg.GRIORA_Average;
                default: return RIOResampleAlg.GRIORA_Bilinear;
            }
        }
    }
}
