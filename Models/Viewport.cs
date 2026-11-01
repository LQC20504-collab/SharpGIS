using System;
using System.Collections.Generic;

namespace SharpGIS.Models
{
    /// <summary>
    /// 二维视口：视图状态由中心地理坐标、分辨率（每屏幕像素的地图单位）与旋转角决定，
    /// 因此可直接作为渲染缓存键，也便于做视图历史。
    /// 旋转只是视图状态，不写任何文件，也不改变数据。
    /// </summary>
    public sealed class Viewport
    {
        /// <summary>视图中心 X（地图单位）。</summary>
        public double CenterX;
        /// <summary>视图中心 Y（地图单位）。</summary>
        public double CenterY;
        /// <summary>地图单位 / 屏幕像素。</summary>
        public double Mupp = 1.0;
        /// <summary>
        /// 视图旋转角（度）。屏幕顺时针为正，0 表示北向上。
        /// 旋转以视图中心为枢轴；要让某个屏幕点下的地物保持不动，用 <see cref="RotateTo(double,double,double,double,double)"/>。
        /// </summary>
        public double Angle;
        /// <summary>视口像素宽。</summary>
        public int Width = 1;
        /// <summary>视口像素高。</summary>
        public int Height = 1;

        /// <summary>允许的最小分辨率（放大上限）。</summary>
        public double MinMupp = 1e-9;
        /// <summary>允许的最大分辨率（缩小上限）。</summary>
        public double MaxMupp = 1e9;

        /// <summary>复制当前视口。</summary>
        /// <returns>新的视口实例。</returns>
        public Viewport Clone()
        {
            return (Viewport)MemberwiseClone();
        }

        /// <summary>从另一视口复制全部状态。</summary>
        /// <param name="other">源视口。</param>
        public void CopyFrom(Viewport other)
        {
            CenterX = other.CenterX;
            CenterY = other.CenterY;
            Mupp = other.Mupp;
            Width = other.Width;
            Height = other.Height;
            MinMupp = other.MinMupp;
            MaxMupp = other.MaxMupp;
            // 漏掉角度会让“前进/后退”后画面回正、坐标换算却仍带旋转，务必保留
            Angle = other.Angle;
        }

        /// <summary>地理坐标 → 屏幕坐标。</summary>
        /// <param name="gx">地理 X。</param>
        /// <param name="gy">地理 Y。</param>
        /// <returns>屏幕坐标点。</returns>
        public PointD ToScreen(double gx, double gy)
        {
            // u/v 是“未旋转屏幕空间”里相对视图中心的像素偏移（v 已含 Y 翻转）
            double u = (gx - CenterX) / Mupp;
            double v = (CenterY - gy) / Mupp;
            double c, s;
            CosSin(out c, out s);
            return new PointD(Width * 0.5 + c * u - s * v,
                              Height * 0.5 + s * u + c * v);
        }

        /// <summary>
        /// 地理坐标 → “未旋转屏幕空间”坐标：即旋转前 tile 的落点（相对视图中心，Y 仍向下）。
        /// 旋转渲染时先用它算出轴对齐的 tile 目标矩形，再在合成阶段整体旋转；
        /// 未旋转时与 <see cref="ToScreen"/> 完全一致。
        /// </summary>
        /// <param name="gx">地理 X。</param>
        /// <param name="gy">地理 Y。</param>
        /// <returns>未旋转屏幕坐标。</returns>
        public PointD ToUnrotatedScreen(double gx, double gy)
        {
            return new PointD((gx - CenterX) / Mupp + Width * 0.5,
                              (CenterY - gy) / Mupp + Height * 0.5);
        }

        /// <summary>屏幕坐标 → 地理坐标。</summary>
        /// <param name="sx">屏幕 X。</param>
        /// <param name="sy">屏幕 Y。</param>
        /// <param name="gx">输出地理 X。</param>
        /// <param name="gy">输出地理 Y。</param>
        public void ToGeo(double sx, double sy, out double gx, out double gy)
        {
            double dx = sx - Width * 0.5;
            double dy = sy - Height * 0.5;
            double c, s;
            CosSin(out c, out s);
            // 逆旋转（R 是正交阵，转置即逆）
            double u = c * dx + s * dy;
            double v = -s * dx + c * dy;
            gx = CenterX + u * Mupp;
            gy = CenterY - v * Mupp;
        }

        /// <summary>当前视口覆盖的地理范围。</summary>
        /// <returns>视口范围。</returns>
        public GeoRect GetGeoRect()
        {
            // 旋转后视口在地图上是一个旋转矩形，必须用四角求外接 AABB；θ=0 时与两点式结果相同
            double x0, y0, x1, y1, x, y;
            ToGeo(0, 0, out x0, out y0);
            ToGeo(Width, Height, out x1, out y1);
            double xmin = x0, xmax = x0, ymin = y0, ymax = y0;

            ToGeo(Width, 0, out x, out y);
            xmin = Math.Min(xmin, x); xmax = Math.Max(xmax, x);
            ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y);

            ToGeo(0, Height, out x, out y);
            xmin = Math.Min(xmin, x); xmax = Math.Max(xmax, x);
            ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y);

            xmin = Math.Min(xmin, x1); xmax = Math.Max(xmax, x1);
            ymin = Math.Min(ymin, y1); ymax = Math.Max(ymax, y1);
            return new GeoRect(xmin, ymin, xmax, ymax);
        }

        /// <summary>以屏幕点为锚点缩放，光标下的地物保持不动。</summary>
        /// <param name="sx">锚点屏幕 X。</param>
        /// <param name="sy">锚点屏幕 Y。</param>
        /// <param name="factor">缩放倍数，>1 放大。</param>
        public void ZoomAt(double sx, double sy, double factor)
        {
            double gx, gy;
            ToGeo(sx, sy, out gx, out gy);

            double c, s;
            CosSin(out c, out s);
            double dx = sx - Width * 0.5;
            double dy = sy - Height * 0.5;
            double u = c * dx + s * dy;
            double v = -s * dx + c * dy;

            double m = Mupp / factor;
            Mupp = Math.Max(MinMupp, Math.Min(MaxMupp, m));
            CenterX = gx - u * Mupp;
            CenterY = gy + v * Mupp;
        }

        /// <summary>按屏幕像素平移视图。</summary>
        /// <param name="dx">水平位移（像素）。</param>
        /// <param name="dy">垂直位移（像素）。</param>
        public void PanByPixels(int dx, int dy)
        {
            double c, s;
            CosSin(out c, out s);
            CenterX -= Mupp * (c * dx + s * dy);
            CenterY += Mupp * (c * dy - s * dx);
        }

        /// <summary>缩放到指定范围。</summary>
        /// <param name="r">目标范围。</param>
        /// <param name="marginRatio">四周留白比例。</param>
        public void ZoomToExtent(GeoRect r, double marginRatio = 0.02)
        {
            if (!r.IsValid) return;
            var e = marginRatio > 0 ? r.Expanded(marginRatio) : r;
            double muppX = e.Width / Math.Max(1, Width);
            double muppY = e.Height / Math.Max(1, Height);
            Mupp = Math.Max(MinMupp, Math.Min(MaxMupp, Math.Max(muppX, muppY)));
            CenterX = e.CenterX;
            CenterY = e.CenterY;
        }

        /// <summary>视图是否带旋转。</summary>
        public bool IsRotated { get { return Math.Abs(Angle) > 1e-9; } }

        /// <summary>取当前角度的余弦与正弦。</summary>
        /// <param name="c">cos(角度)。</param>
        /// <param name="s">sin(角度)。</param>
        public void CosSin(out double c, out double s)
        {
            double rad = Angle * Math.PI / 180.0;
            c = Math.Cos(rad);
            s = Math.Sin(rad);
        }

        /// <summary>把角度归一化到 (-180, 180]，避免长时间累加后数值失真。</summary>
        /// <param name="deg">任意角度（度）。</param>
        /// <returns>归一化后的角度。</returns>
        public static double NormalizeAngle(double deg)
        {
            if (double.IsNaN(deg) || double.IsInfinity(deg)) return 0;
            double d = deg % 360.0;
            if (d > 180.0) d -= 360.0;
            else if (d <= -180.0) d += 360.0;
            return d;
        }

        /// <summary>
        /// 绕指定屏幕点旋转到给定角度，并保持该点下的地物不动。
        /// 拖动交互应使用这个重载：锚点地物由调用方在按下时捕获一次，
        /// 每帧只传绝对角度，因此来回拖动可逆、不累积误差。
        /// </summary>
        /// <param name="anchorGx">枢轴点的地理 X（按下时捕获）。</param>
        /// <param name="anchorGy">枢轴点的地理 Y（按下时捕获）。</param>
        /// <param name="anchorScreenX">枢轴的屏幕 X。</param>
        /// <param name="anchorScreenY">枢轴的屏幕 Y。</param>
        /// <param name="angleDeg">目标角度（度，顺时针为正）。</param>
        public void RotateTo(double anchorGx, double anchorGy, double anchorScreenX, double anchorScreenY,
            double angleDeg)
        {
            Angle = NormalizeAngle(angleDeg);

            double c, s;
            CosSin(out c, out s);
            double dx = anchorScreenX - Width * 0.5;
            double dy = anchorScreenY - Height * 0.5;
            double u = c * dx + s * dy;
            double v = -s * dx + c * dy;

            CenterX = anchorGx - u * Mupp;
            CenterY = anchorGy + v * Mupp;
        }

        /// <summary>绕指定屏幕点旋转到给定角度（锚点地物取当前视图下的值）。</summary>
        /// <param name="centerScreenX">枢轴屏幕 X。</param>
        /// <param name="centerScreenY">枢轴屏幕 Y。</param>
        /// <param name="angleDeg">目标角度（度）。</param>
        public void RotateTo(double centerScreenX, double centerScreenY, double angleDeg)
        {
            double gx, gy;
            ToGeo(centerScreenX, centerScreenY, out gx, out gy);
            RotateTo(gx, gy, centerScreenX, centerScreenY, angleDeg);
        }

        /// <summary>直接设定角度（绕视图中心转），中心与分辨率不变。</summary>
        /// <param name="angleDeg">目标角度（度）。</param>
        public void SetAngle(double angleDeg)
        {
            Angle = NormalizeAngle(angleDeg);
        }

        /// <summary>直接设定分辨率（1:1 显示用），可选同时设定中心。</summary>
        /// <param name="mupp">地图单位/像素。</param>
        /// <param name="anchorX">新的中心 X，NaN 表示不变。</param>
        /// <param name="anchorY">新的中心 Y，NaN 表示不变。</param>
        public void SetPixelSize(double mupp, double anchorX = double.NaN, double anchorY = double.NaN)
        {
            double m = Math.Max(MinMupp, Math.Min(MaxMupp, mupp));
            if (!double.IsNaN(anchorX)) CenterX = anchorX;
            if (!double.IsNaN(anchorY)) CenterY = anchorY;
            Mupp = m;
        }

        /// <summary>更新视口像素尺寸。</summary>
        /// <param name="width">像素宽。</param>
        /// <param name="height">像素高。</param>
        public void Resize(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
        }

        public override string ToString()
        {
            return string.Format("C=({0:G6},{1:G6}) mupp={2:G6} size={3}x{4} ang={5:0.###}",
                CenterX, CenterY, Mupp, Width, Height, Angle);
        }
    }

    /// <summary>视图历史（前进/后退）。</summary>
    public sealed class ViewHistory
    {
        private readonly List<Viewport> _stack = new List<Viewport>();
        private int _index = -1;
        private const int MaxDepth = 40;

        public bool CanBack { get { return _index > 0; } }
        public bool CanForward { get { return _index >= 0 && _index < _stack.Count - 1; } }

        public void Push(Viewport vp)
        {
            if (_index >= 0 && SameAs(_stack[_index], vp)) return;
            // 丢弃前进分支
            while (_stack.Count > _index + 1) _stack.RemoveAt(_stack.Count - 1);
            _stack.Add(vp.Clone());
            if (_stack.Count > MaxDepth) _stack.RemoveAt(0);
            _index = _stack.Count - 1;
        }

        public Viewport Back()
        {
            if (!CanBack) return null;
            _index--;
            return _stack[_index].Clone();
        }

        public Viewport Forward()
        {
            if (!CanForward) return null;
            _index++;
            return _stack[_index].Clone();
        }

        public void Clear()
        {
            _stack.Clear();
            _index = -1;
        }

        private static bool SameAs(Viewport a, Viewport b)
        {
            return Math.Abs(a.CenterX - b.CenterX) < 1e-9 &&
                   Math.Abs(a.CenterY - b.CenterY) < 1e-9 &&
                   Math.Abs(a.Mupp - b.Mupp) < 1e-12 &&
                   a.Width == b.Width && a.Height == b.Height &&
                   AngleDiff(a.Angle, b.Angle) < 1e-6;
        }

        /// <summary>两个角度的环形差值（度），处理 ±180 环绕。</summary>
        /// <param name="a">角度 A。</param>
        /// <param name="b">角度 B。</param>
        /// <returns>0~180 的最小夹角。</returns>
        internal static double AngleDiff(double a, double b)
        {
            double d = (a - b) % 360.0;
            if (d > 180.0) d -= 360.0;
            else if (d <= -180.0) d += 360.0;
            return Math.Abs(d);
        }
    }
}
