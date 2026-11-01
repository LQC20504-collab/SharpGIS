using System;

namespace SharpGIS.Models
{
        /// <summary>地理范围矩形（地图单位）。</summary>
    public struct GeoRect
    {
        /// <summary>西边界。</summary>
        public double XMin;
        /// <summary>南边界。</summary>
        public double YMin;
        /// <summary>东边界。</summary>
        public double XMax;
        /// <summary>北边界。</summary>
        public double YMax;

        public GeoRect(double xmin, double ymin, double xmax, double ymax)
        {
            XMin = Math.Min(xmin, xmax);
            XMax = Math.Max(xmin, xmax);
            YMin = Math.Min(ymin, ymax);
            YMax = Math.Max(ymin, ymax);
        }

        /// <summary>范围是否有效（宽高为正且非 NaN）。</summary>
        public bool IsValid
        {
            get { return XMax > XMin && YMax > YMin && !double.IsNaN(XMax) && !double.IsInfinity(XMax); }
        }

        /// <summary>东西跨度。</summary>
        public double Width { get { return XMax - XMin; } }
        /// <summary>南北跨度。</summary>
        public double Height { get { return YMax - YMin; } }
        /// <summary>中心经度/东坐标。</summary>
        public double CenterX { get { return (XMin + XMax) * 0.5; } }
        /// <summary>中心纬度/北坐标。</summary>
        public double CenterY { get { return (YMin + YMax) * 0.5; } }

        /// <summary>由尺寸与仿射变换求包围盒（适用于带旋转的分幅）。</summary>
        /// <param name="width">列数。</param>
        /// <param name="height">行数。</param>
        /// <param name="gt">6 系数仿射变换。</param>
        /// <returns>覆盖四角的地理范围。</returns>
        public static GeoRect FromGeoTransform(int width, int height, double[] gt)
        {
            double x0 = gt[0], y0 = gt[3];
            double x1 = gt[0] + width * gt[1];
            double y1 = gt[3] + width * gt[4];
            double x2 = gt[0] + height * gt[2];
            double y2 = gt[3] + height * gt[5];
            double x3 = gt[0] + width * gt[1] + height * gt[2];
            double y3 = gt[3] + width * gt[4] + height * gt[5];
            double xmin = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
            double xmax = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
            double ymin = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
            double ymax = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
            return new GeoRect(xmin, ymin, xmax, ymax);
        }

        /// <summary>判断与另一范围是否相交。</summary>
        /// <param name="o">另一范围。</param>
        /// <returns>相交返回 true。</returns>
        public bool Intersects(GeoRect o)
        {
            return o.XMax > XMin && o.XMin < XMax && o.YMax > YMin && o.YMin < YMax;
        }

        /// <summary>求交集。</summary>
        /// <param name="o">另一范围。</param>
        /// <returns>交集范围，可能无效。</returns>
        public GeoRect Intersect(GeoRect o)
        {
            return new GeoRect(
                Math.Max(XMin, o.XMin), Math.Max(YMin, o.YMin),
                Math.Min(XMax, o.XMax), Math.Min(YMax, o.YMax));
        }

        /// <summary>求并集（自动跳过无效范围）。</summary>
        /// <param name="o">另一范围。</param>
        /// <returns>并集范围。</returns>
        public GeoRect Union(GeoRect o)
        {
            if (!IsValid) return o;
            if (!o.IsValid) return this;
            return new GeoRect(
                Math.Min(XMin, o.XMin), Math.Min(YMin, o.YMin),
                Math.Max(XMax, o.XMax), Math.Max(YMax, o.YMax));
        }

        /// <summary>按比例向四周外扩。</summary>
        /// <param name="ratio">外扩比例（相对宽高）。</param>
        /// <returns>外扩后的范围。</returns>
        public GeoRect Expanded(double ratio)
        {
            double dx = Width * ratio;
            double dy = Height * ratio;
            return new GeoRect(XMin - dx, YMin - dy, XMax + dx, YMax + dy);
        }

        public override string ToString()
        {
            return string.Format("[{0:F3}, {1:F3} - {2:F3}, {3:F3}]", XMin, YMin, XMax, YMax);
        }
    }

        /// <summary>双精度屏幕坐标点，用于高精度坐标互转。</summary>
    public struct PointD
    {
        public double X;
        public double Y;

        public PointD(double x, double y) { X = x; Y = y; }

    }
}
