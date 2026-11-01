using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.Rendering;

using SharpGIS.Services;
namespace SharpGIS.UI
{
        /// <summary>画布交互工具：平移、放大、缩小、框选缩放、识别、旋转。</summary>
    public enum MapTool { Pan, ZoomIn, ZoomOut, ZoomRect, Identify, Rotate }

        /// <summary>光标探针信息（状态栏使用）。</summary>
    public sealed class CursorInfoEventArgs : EventArgs
    {
        public double GeoX, GeoY;
        public int Col, Row = -1;
        public double[] Values;
        public RasterLayer Layer;
        public bool ValidPixel;
    }

        /// <summary>识别请求结果，携带行列号、坐标与各波段值。</summary>
    public sealed class IdentifyEventArgs : EventArgs
    {
        public RasterLayer Layer;
        public int Col, Row;
        public double GeoX, GeoY;
        public double[] Values;
    }

    /// <summary>
    /// 自绘地图画布：平移、滚轮锚点缩放、框选缩放、识别、比例尺与光标探针。
    /// 渲染走 RenderPipeline，交互期间先出低质量帧再补全质量帧。
    /// 比例尺按“地图单位为米”绘制，地理坐标系（度）下仅为近似。
    /// </summary>
    public class MapCanvas : Control
    {
        /// <summary>要渲染的图层栈。</summary>
        public LayerStack Layers { get; set; }
        /// <summary>当前视口。</summary>
        public Viewport View { get; } = new Viewport();
        /// <summary>视图前进/后退历史。</summary>
        public ViewHistory History { get; } = new ViewHistory();
        /// <summary>最近一帧的渲染统计。</summary>
        public RenderStats LastStats { get; private set; }

        /// <summary>视口变化（缩放/平移）后触发。</summary>
        public event EventHandler ViewChanged;
        /// <summary>一帧渲染完成后触发。</summary>
        public event EventHandler RenderCompleted;
        /// <summary>光标移动时的探针信息。</summary>
        public event EventHandler<CursorInfoEventArgs> CursorInfoChanged;
        /// <summary>识别工具点选像素时触发。</summary>
        public event EventHandler<IdentifyEventArgs> IdentifyRequested;

        private MapTool _tool = MapTool.Pan;
        /// <summary>当前交互工具。</summary>
        public MapTool Tool
        {
            get { return _tool; }
            set
            {
                if (value != MapTool.Rotate && _rotatePhase != RotatePhase.Off)
                {
                    // 离开旋转工具：清掉未提交的预览（视图只在单击提交时改变，这里不需要回滚）
                    _rotatePhase = RotatePhase.Off;
                    _rotateLiveDelta = 0;
                }
                _tool = value;
                Cursor = value == MapTool.Rotate || value == MapTool.Identify || value == MapTool.ZoomRect
                    ? Cursors.Cross : Cursors.Hand;
                Invalidate();
            }
        }

        private Bitmap _canvas;
        private readonly Timer _timer;
        private bool _wantFullQuality;
        private const int RenderDelayMs = 120;

        private bool _dragging;
        private Point _dragStart;
        private Point _dragOffset;
        private Rectangle _zoomRect;
        private bool _zoomRectActive;
        private bool _busy;
        private Point _mouse = new Point(-1, -1);
        private DateTime _lastProbe = DateTime.MinValue;

        // ---- 旋转工具状态：Off → 单击定中心(Armed) → 单击定手柄(Following) → 移动旋转、再单击提交 ----
        private enum RotatePhase { Off, Armed, Following }
        private RotatePhase _rotatePhase = RotatePhase.Off;
        private Point _rotateCenter;                 // 枢轴（屏幕像素）
        private double _rotateCenterGx, _rotateCenterGy;  // 枢轴处的地物（定中心时捕获一次）
        private Point _rotateAnchor = new Point(-1, -1);  // 手柄基准（第二次单击位置，保持固定）
        private Point _rotateCursor = new Point(-1, -1);  // 当前鼠标位置（只用于绘制手柄）
        private double _rotateRadius;                // 手柄半径（像素）
        private double _rotateStartAngle;            // 进入 Following 时的视图角度
        private double _rotateLiveDelta;             // 相对 _rotateAnchor 的角度差（未提交）

        public MapCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            Cursor = Cursors.Hand;

            _timer = new Timer { Interval = RenderDelayMs };
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                RenderNow(_wantFullQuality ? RenderQuality.Full : RenderQuality.Fast);
                if (_wantFullQuality) _wantFullQuality = false;
            };

            Disposed += (s, e) =>
            {
                if (_canvas != null) { _canvas.Dispose(); _canvas = null; }
            };
        }

        // ---------------- 视图操作 ----------------

        /// <summary>绑定图层栈并订阅其变化。</summary>
        /// <param name="stack">图层栈。</param>
        public void SetLayers(LayerStack stack)
        {
            if (Layers != null) Layers.Changed -= OnLayersChanged;
            Layers = stack;
            if (Layers != null) Layers.Changed += OnLayersChanged;
            RequestRender(true);
        }

        private void OnLayersChanged(object sender, EventArgs e)
        {
            RequestRender(true);
        }

        /// <summary>缩放到全部可见图层的并集范围。</summary>
        public void ZoomToFull()
        {
            var extent = Layers != null ? Layers.UnionExtent() : new GeoRect();
            if (!extent.IsValid) return;
            UpdateMuppLimits(extent);
            var before = View.Clone();
            View.ZoomToExtent(extent, 0.02);
            PushHistory(before);
            RequestRender(false);
            RaiseViewChanged();
        }

        /// <summary>缩放到指定图层范围。</summary>
        /// <param name="layer">目标图层。</param>
        public void ZoomToLayer(RasterLayer layer)
        {
            if (layer == null) return;
            UpdateMuppLimits(layer.Extent);
            var before = View.Clone();
            View.ZoomToExtent(layer.Extent, 0.02);
            PushHistory(before);
            RequestRender(false);
            RaiseViewChanged();
        }

        /// <summary>切到 1:1（一个屏幕像素对应一个栅格像素）。</summary>
        public void ZoomToPixelSize()
        {
            var layer = Layers != null ? Layers.TopVisible() : null;
            double px = layer != null ? layer.PixelSizeX : View.Mupp;
            var before = View.Clone();
            View.SetPixelSize(px);
            PushHistory(before);
            RequestRender(false);
            RaiseViewChanged();
        }

        /// <summary>以视图中心缩放。</summary>
        /// <param name="factor">缩放倍数，>1 放大。</param>
        public void ZoomBy(double factor)
        {
            var before = View.Clone();
            View.ZoomAt(View.Width * 0.5, View.Height * 0.5, factor);
            PushHistory(before);
            RequestRender(false);
            RaiseViewChanged();
        }

        /// <summary>回到上一个视图。</summary>
        public void GoBack()
        {
            var vp = History.Back();
            if (vp != null) { View.CopyFrom(vp); RequestRender(false); RaiseViewChanged(); }
        }

        /// <summary>前进到下一个视图。</summary>
        public void GoForward()
        {
            var vp = History.Forward();
            if (vp != null) { View.CopyFrom(vp); RequestRender(false); RaiseViewChanged(); }
        }

        /// <summary>把视图恢复为正北（只改角度，保持中心与缩放）。</summary>
        public void ResetNorth()
        {
            if (Math.Abs(View.Angle) < 1e-9) return;
            var before = View.Clone();
            View.SetAngle(0);
            PushHistory(before);
            RenderNow(RenderQuality.Fast);
            RequestRender(true);
            RaiseViewChanged();
        }

        /// <summary>
        /// 取消当前的旋转操作：跟随预览中则退回“已定中心”，已定中心则清掉中心。
        /// </summary>
        /// <param name="rollback">拖动中是否把视图恢复到按下前。</param>
        /// <returns>是否消费了这次取消（false 表示当前没有可取消的旋转状态）。</returns>
        public bool CancelRotate(bool rollback)
        {
            if (_tool != MapTool.Rotate) return false;

            if (_rotatePhase == RotatePhase.Following)
            {
                // 预览只画在位图上，视口尚未改动，因此“回滚”只是丢掉预览
                _rotatePhase = RotatePhase.Armed;
                _rotateLiveDelta = 0;
                _rotateStartAngle = View.Angle;
                _rotateRadius = 0;
                Invalidate();
                return true;
            }

            if (_rotatePhase == RotatePhase.Armed)
            {
                _rotatePhase = RotatePhase.Off;
                _rotateLiveDelta = 0;
                _rotateRadius = 0;
                _rotateAnchor = new Point(-1, -1);
                _rotateCursor = new Point(-1, -1);
                Invalidate();
                return true;
            }
            return false;
        }

        /// <summary>当前是否处于旋转工具的某个阶段（0=未激活，1=已定中心，2=拖动中），供自检使用。</summary>
        internal int RotateStateForTest { get { return (int)_rotatePhase; } }

        /// <summary>指北针的绘制区域，供自检检查是否越界。</summary>
        internal Rectangle CompassBounds { get; private set; }

        /// <summary>
        /// 鼠标绕枢轴划过的角度差（度，顺时针为正）。
        /// </summary>
        /// <param name="center">枢轴屏幕坐标。</param>
        /// <param name="from">起始手柄位置。</param>
        /// <param name="to">当前手柄位置。</param>
        /// <returns>-180~180 的角度差。</returns>
        internal static double RotationDeltaDeg(Point center, Point from, Point to)
        {
            double a0 = Math.Atan2(from.Y - center.Y, from.X - center.X);
            double a1 = Math.Atan2(to.Y - center.Y, to.X - center.X);
            double d = (a1 - a0) * 180.0 / Math.PI;
            while (d > 180) d -= 360;
            while (d <= -180) d += 360;
            return d;
        }

        /// <summary>指北针箭头的针尖位置（屏幕坐标）。</summary>
        /// <param name="center">圆环中心。</param>
        /// <param name="angleDeg">视图角度（度）。</param>
        /// <param name="radius">箭头长度（像素）。</param>
        /// <returns>针尖坐标。</returns>
        internal static PointF NorthTip(PointF center, double angleDeg, float radius)
        {
            double rad = angleDeg * Math.PI / 180.0;
            return new PointF(center.X + (float)(Math.Sin(rad) * radius),
                              center.Y - (float)(Math.Cos(rad) * radius));
        }

        private void PushHistory(Viewport before)
        {
            History.Push(before);
            History.Push(View);
        }

        private void UpdateMuppLimits(GeoRect extent)
        {
            if (!extent.IsValid) return;
            double diag = Math.Sqrt(extent.Width * extent.Width + extent.Height * extent.Height);
            View.MaxMupp = diag / 30.0;
            var layer = Layers != null ? Layers.TopVisible() : null;
            double px = layer != null ? Math.Min(layer.PixelSizeX, layer.PixelSizeY) : 1;
            View.MinMupp = px / 64.0;
        }

        private void RaiseViewChanged()
        {
            var h = ViewChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ---------------- 渲染 ----------------

        /// <summary>请求重绘（防抖后执行）。</summary>
        /// <param name="fullQuality">是否在防抖后补一帧全质量。</param>
        public void RequestRender(bool fullQuality)
        {
            _wantFullQuality = _wantFullQuality || fullQuality;
            _timer.Stop();
            _timer.Interval = fullQuality ? 200 : RenderDelayMs;
            _timer.Start();
        }

        /// <summary>立即同步渲染一帧。</summary>
        /// <param name="quality">渲染质量。</param>
        public void RenderNow(RenderQuality quality)
        {
            if (Layers == null || IsDisposed) return;
            if (View.Width < 2 || View.Height < 2) return;

            _busy = true;
            var snapshot = Layers.TakeSnapshot();
            Bitmap bmp = null;
            RenderStats stats = null;
            try
            {
                bmp = RenderPipeline.Render(View, snapshot, quality, out stats);
            }
            catch (Exception ex)
            {
                ConsoleBridge.Write(Lang.T("渲染异常: ") + ex.Message);
            }
            finally
            {
                LayerStack.ReleaseAll(snapshot);
            }

            if (bmp != null)
            {
                var old = _canvas;
                _canvas = bmp;
                LastStats = stats;
                if (old != null && !ReferenceEquals(old, bmp)) old.Dispose();
            }
            _busy = false;
            Invalidate();
            var done = RenderCompleted;
            if (done != null) done(this, EventArgs.Empty);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            View.Resize(Width, Height);
            // 尺寸变化会让枢轴与半径失效，直接放弃本次拖动
            if (_rotatePhase == RotatePhase.Following) CancelRotate(true);
            RequestRender(false);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Background);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            DrawContent(e.Graphics);
        }

        /// <summary>按指定像素尺寸导出当前视图（与窗口无关，供导出图与自检使用）。</summary>
        /// <param name="width">输出宽。</param>
        /// <param name="height">输出高。</param>
        /// <returns>渲染结果位图。</returns>
        public Bitmap RenderToBitmap(int width, int height)
        {
            var vp = View.Clone();
            vp.Resize(width, height);
            var snapshot = Layers != null ? Layers.TakeSnapshot() : new RasterLayer[0];
            try
            {
                RenderStats stats;
                return RenderPipeline.Render(vp, snapshot, RenderQuality.Full, out stats);
            }
            finally
            {
                LayerStack.ReleaseAll(snapshot);
            }
        }

        private void DrawContent(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            if (_canvas != null)
            {
                Point offset = _dragging ? _dragOffset : Point.Empty;
                bool rotatePreview = _rotatePhase == RotatePhase.Following && Math.Abs(_rotateLiveDelta) > 0.01;

                if (rotatePreview)
                {
                    // 拖动预览：直接旋转已渲染好的画布（不重跑管线，避免每帧重读 GDAL）
                    var oldInterp = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    using (var m = new Matrix())
                    {
                        m.RotateAt((float)_rotateLiveDelta,
                            new PointF(_rotateCenter.X, _rotateCenter.Y), MatrixOrder.Append);
                        g.Transform = m;
                        g.DrawImageUnscaled(_canvas, 0, 0);
                    }
                    g.ResetTransform();
                    g.InterpolationMode = oldInterp;
                }
                else if (offset.IsEmpty)
                {
                    g.DrawImageUnscaled(_canvas, 0, 0);
                }
                else
                {
                    g.DrawImageUnscaled(_canvas, offset);
                }
            }
            else
            {
                using (var brush = new SolidBrush(Theme.TextDim))
                {
                    var rect = new RectangleF(0, Height / 2f - 30, Width, 60);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(Layers == null || Layers.Count == 0
                            ? Lang.T("拖入栅格文件，或按 Ctrl+O 打开\r\n（支持 GeoTIFF / IMG / DEM / PNG / JPEG 等）")
                            : Lang.T("正在渲染…"),
                        Theme.UIFont, brush, rect, sf);
                }
            }

            if (_zoomRectActive && _zoomRect.Width > 2 && _zoomRect.Height > 2)
            {
                using (var pen = new Pen(Theme.Accent, 1f) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, _zoomRect);
                using (var fill = new SolidBrush(Color.FromArgb(40, Theme.Accent)))
                    g.FillRectangle(fill, _zoomRect);
            }

            DrawRotateOverlay(g);
            DrawScaleBar(g);
            DrawNorthArrow(g);
            if (_busy) DrawBusy(g);
        }

        /// <summary>提交本次旋转：用绝对角度写回视口，并压一条历史。</summary>
        private void CommitRotate()
        {
            double target = _rotateStartAngle + _rotateLiveDelta;
            bool moved = Math.Abs(Viewport.NormalizeAngle(target) - Viewport.NormalizeAngle(_rotateStartAngle)) > 0.05;

            if (!moved)
            {
                // 角度没变：只结束本次预览，中心保留，用户可以重新点手柄
                _rotatePhase = RotatePhase.Armed;
                _rotateLiveDelta = 0;
                _rotateRadius = 0;
                Invalidate();
                return;
            }

            // 预览只画在位图上，视口此刻仍是旋转前的状态，直接作为历史即可
            var before = View.Clone();
            View.RotateTo(_rotateCenterGx, _rotateCenterGy, _rotateCenter.X, _rotateCenter.Y, target);
            PushHistory(before);

            _rotateLiveDelta = 0;
            _rotateStartAngle = View.Angle;
            _rotateRadius = 0;
            _rotatePhase = RotatePhase.Armed;      // 保留枢轴，可直接再点一个手柄继续转

            RenderNow(RenderQuality.Fast);
            RequestRender(true);
            RaiseViewChanged();
            Invalidate();
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>旋转工具的屏幕空间覆盖层：中心十字、半径圆、手柄与角度提示。</summary>
        /// <param name="g">绘图上下文。</param>
        private void DrawRotateOverlay(Graphics g)
        {
            if (_tool != MapTool.Rotate || _rotatePhase == RotatePhase.Off) return;

            var oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            try
            {
                // 中心十字
                using (var pen = new Pen(Theme.Accent, 1.2f))
                {
                    int cx = _rotateCenter.X, cy = _rotateCenter.Y;
                    g.DrawLine(pen, cx - 9, cy, cx + 9, cy);
                    g.DrawLine(pen, cx, cy - 9, cx, cy + 9);
                    g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
                }

                if (_rotatePhase == RotatePhase.Following && _rotateCursor.X >= 0 && _rotateRadius > 4)
                {
                    using (var pen = new Pen(Color.FromArgb(130, Theme.Accent), 1f) { DashStyle = DashStyle.Dash })
                        g.DrawEllipse(pen, (float)(_rotateCenter.X - _rotateRadius),
                            (float)(_rotateCenter.Y - _rotateRadius),
                            (float)(_rotateRadius * 2), (float)(_rotateRadius * 2));

                    // 基准线（从中心到第二次单击的位置）固定不动，手柄线跟手，两者夹角就是旋转角
                    using (var pen = new Pen(Color.FromArgb(90, Theme.TextDim), 1f) { DashStyle = DashStyle.Dot })
                        g.DrawLine(pen, _rotateCenter, _rotateAnchor);
                    using (var pen = new Pen(Color.FromArgb(170, Theme.Accent), 1f))
                        g.DrawLine(pen, _rotateCenter, _rotateCursor);

                    using (var brush = new SolidBrush(Theme.Accent))
                        g.FillEllipse(brush, _rotateCursor.X - 5, _rotateCursor.Y - 5, 10, 10);
                    using (var pen = new Pen(Theme.Text, 1.2f))
                        g.DrawEllipse(pen, _rotateCursor.X - 5, _rotateCursor.Y - 5, 10, 10);

                    double shown = _rotateStartAngle + _rotateLiveDelta;
                    string text = shown.ToString("0.0") + "°";
                    var pos = new PointF(_rotateCursor.X + 14, _rotateCursor.Y - 26);
                    var size = g.MeasureString(text, Theme.UIFont);
                    using (var back = new SolidBrush(Color.FromArgb(190, Theme.Background)))
                        g.FillRectangle(back, pos.X - 4, pos.Y - 2, size.Width + 8, size.Height + 2);
                    using (var brush = new SolidBrush(Theme.Text))
                        g.DrawString(text, Theme.UIFont, brush, pos);
                }

                string hint = _rotatePhase == RotatePhase.Armed
                    ? Lang.T("第 2 步：单击确定手柄位置")
                    : Lang.T("第 3 步：移动鼠标旋转，单击提交（Esc 取消）");
                var hintSize = g.MeasureString(hint, Theme.UIFont);
                float hx = (Width - hintSize.Width) / 2f;
                float hy = Height - 56;
                using (var back = new SolidBrush(Color.FromArgb(200, Theme.Background)))
                    g.FillRectangle(back, hx - 10, hy - 5, hintSize.Width + 20, hintSize.Height + 10);
                using (var pen = new Pen(Theme.Border, 1f))
                    g.DrawRectangle(pen, hx - 10, hy - 5, hintSize.Width + 20, hintSize.Height + 10);
                using (var brush = new SolidBrush(Theme.Text))
                    g.DrawString(hint, Theme.UIFont, brush, hx, hy);
            }
            finally
            {
                g.SmoothingMode = oldSmooth;
            }
        }

        /// <summary>右下角指北针：随视图角度反向旋转，并显示当前角度。</summary>
        /// <param name="g">绘图上下文。</param>
        private void DrawNorthArrow(Graphics g)
        {
            CompassBounds = Rectangle.Empty;
            if (Width < 220 || Height < 220) return;

            var bounds = new Rectangle(Width - 92, Height - 100, 76, 76);
            CompassBounds = bounds;

            var oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (var back = new SolidBrush(Color.FromArgb(150, Theme.Background)))
                    g.FillRectangle(back, bounds);

                var center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
                float radius = 22f;

                using (var pen = new Pen(Theme.Border, 1f))
                    g.DrawEllipse(pen, center.X - radius, center.Y - radius, radius * 2, radius * 2);

                // 预览（未提交）时画面已经转了，指北针要跟着转，否则会自相矛盾
                double shownAngle = View.Angle +
                    (_rotatePhase == RotatePhase.Following ? _rotateLiveDelta : 0);
                var tip = NorthTip(center, shownAngle, radius - 3f);
                var tail = NorthTip(center, shownAngle + 180, radius - 10f);

                using (var pen = new Pen(Theme.Text, 1.8f))
                    g.DrawLine(pen, tail, tip);

                // 箭头（沿指北方向的一个小三角）
                var left = NorthTip(center, shownAngle - 14, radius - 9f);
                var right = NorthTip(center, shownAngle + 14, radius - 9f);
                using (var brush = new SolidBrush(Theme.Accent))
                    g.FillPolygon(brush, new[] { tip, left, right });

                using (var brush = new SolidBrush(Theme.Text))
                {
                    var label = g.MeasureString("N", Theme.UIFont);
                    var nPos = NorthTip(center, shownAngle, radius + 4f);
                    g.DrawString("N", Theme.UIFont, brush, nPos.X - label.Width / 2f, nPos.Y - label.Height / 2f);
                }

                if (Math.Abs(shownAngle) >= 0.05)
                {
                    string deg = shownAngle.ToString("0") + "°";
                    using (var brush = new SolidBrush(Theme.TextDim))
                    {
                        var size = g.MeasureString(deg, Theme.UIFont);
                        g.DrawString(deg, Theme.UIFont, brush,
                            bounds.Left + (bounds.Width - size.Width) / 2f, bounds.Bottom - size.Height - 2);
                    }
                }
            }
            finally
            {
                g.SmoothingMode = oldSmooth;
            }
        }

        private void DrawScaleBar(Graphics g)
        {
            if (_canvas == null) return;
            double targetPx = 110;
            double mapUnits = targetPx * View.Mupp;
            if (mapUnits <= 0 || double.IsNaN(mapUnits)) return;

            // 取 1/2/5 × 10^n 的整齐长度
            double exp = Math.Floor(Math.Log10(mapUnits));
            double pow = Math.Pow(10, exp);
            double nice = pow;
            foreach (var m in new[] { 1.0, 2.0, 5.0, 10.0 })
            {
                if (mapUnits <= m * pow) { nice = m * pow; break; }
                nice = m * pow;
            }
            double px = nice / View.Mupp;
            int x0 = 14, y0 = Height - 22;
            var penColor = Theme.Text;
            using (var pen = new Pen(penColor, 1.4f))
            {
                g.DrawLine(pen, x0, y0, x0 + (float)px, y0);
                g.DrawLine(pen, x0, y0 - 4, x0, y0 + 4);
                g.DrawLine(pen, x0 + (float)px, y0 - 4, x0 + (float)px, y0 + 4);
            }
            string text = nice >= 1000 ? (nice / 1000).ToString("G3") + " km" : nice.ToString("G3") + " m";
            using (var brush = new SolidBrush(penColor))
                g.DrawString(text, Theme.UIFont, brush, x0, y0 - 20);

            if (_tool == MapTool.Identify && _mouse.X >= 0)
            {
                using (var pen = new Pen(Color.FromArgb(180, Theme.Accent)))
                {
                    g.DrawLine(pen, _mouse.X - 40, _mouse.Y, _mouse.X + 40, _mouse.Y);
                    g.DrawLine(pen, _mouse.X, _mouse.Y - 40, _mouse.X, _mouse.Y + 40);
                }
            }
        }

        private void DrawBusy(Graphics g)
        {
            using (var brush = new SolidBrush(Color.FromArgb(160, Theme.Background)))
                g.FillRectangle(brush, Width - 96, 8, 84, 20);
            using (var brush = new SolidBrush(Theme.Text))
                g.DrawString(Lang.T("渲染中…"), Theme.UIFont, brush, Width - 88, 10);
        }

        // ---------------- 鼠标交互 ----------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            switch (_tool)
            {
                case MapTool.Pan:
                    _dragging = true;
                    _dragStart = e.Location;
                    _dragOffset = Point.Empty;
                    Cursor = Cursors.SizeAll;
                    break;
                case MapTool.ZoomRect:
                    _zoomRectActive = true;
                    _dragStart = e.Location;
                    _zoomRect = new Rectangle(e.Location, Size.Empty);
                    break;
                case MapTool.ZoomIn:
                    ZoomAtScreen(e.X, e.Y, 1.6);
                    break;
                case MapTool.ZoomOut:
                    ZoomAtScreen(e.X, e.Y, 1 / 1.6);
                    break;
                case MapTool.Identify:
                    DoIdentify(e.X, e.Y);
                    break;
                case MapTool.Rotate:
                    OnRotateMouseDown(e);
                    break;
            }
        }

        /// <summary>旋转工具的按下处理：第一次定中心，第二次按下开始拖动。</summary>
        /// <param name="e">鼠标事件参数。</param>
        private void OnRotateMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            // 第二次单击后双击 = 放弃本次预览，退回“已定中心”
            if (e.Clicks >= 2 && _rotatePhase == RotatePhase.Following)
            {
                CancelRotate(true);
                return;
            }

            // 第 1 次单击：定旋转中心
            if (_rotatePhase == RotatePhase.Off)
            {
                _rotateCenter = e.Location;
                View.ToGeo(e.X, e.Y, out _rotateCenterGx, out _rotateCenterGy);
                _rotateRadius = 0;
                _rotateStartAngle = View.Angle;
                _rotateLiveDelta = 0;
                _rotatePhase = RotatePhase.Armed;
                Invalidate();
                return;
            }

            // 第 2 次单击：定手柄（半径与角度基准）
            if (_rotatePhase == RotatePhase.Armed)
            {
                double r = Distance(e.Location, _rotateCenter);
                if (r < 8) return;              // 手柄压在中心上，角度会乱跳
                _rotateRadius = r;
                _rotateAnchor = e.Location;
                _rotateCursor = e.Location;
                _rotateStartAngle = View.Angle;
                _rotateLiveDelta = 0;
                _rotatePhase = RotatePhase.Following;
                Invalidate();
                return;
            }

            // 第 3 次单击：提交当前角度
            if (_rotatePhase == RotatePhase.Following)
            {
                CommitRotate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouse = e.Location;

            if (_tool == MapTool.Rotate && _rotatePhase == RotatePhase.Following)
            {
                // 角度差始终相对“第二次单击的位置”计算，因此鼠标小步移动也能累计出正确角度
                _rotateCursor = e.Location;
                _rotateRadius = Distance(e.Location, _rotateCenter);
                _rotateLiveDelta = RotationDeltaDeg(_rotateCenter, _rotateAnchor, e.Location);
                Invalidate();
                return;
            }
            if (_tool == MapTool.Rotate && _rotatePhase == RotatePhase.Armed)
            {
                // 已定中心但还没定手柄：只更新光标，不产生角度
                _rotateCursor = e.Location;
                return;
            }

            if (_dragging)
            {
                _dragOffset = new Point(e.X - _dragStart.X, e.Y - _dragStart.Y);
                Invalidate();
            }
            else if (_zoomRectActive)
            {
                _zoomRect = new Rectangle(
                    Math.Min(_dragStart.X, e.X), Math.Min(_dragStart.Y, e.Y),
                    Math.Abs(e.X - _dragStart.X), Math.Abs(e.Y - _dragStart.Y));
                Invalidate();
            }
            else
            {
                Probe(e.X, e.Y);
                if (_tool == MapTool.Identify) Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_dragging)
            {
                _dragging = false;
                int dx = e.X - _dragStart.X;
                int dy = e.Y - _dragStart.Y;
                _dragOffset = Point.Empty;
                Cursor = Cursors.Hand;
                if (dx != 0 || dy != 0)
                {
                    var before = View.Clone();
                    View.PanByPixels(dx, dy);
                    PushHistory(before);
                    RequestRender(false);
                    RaiseViewChanged();
                }
                Invalidate();
            }
            else if (_zoomRectActive)
            {
                _zoomRectActive = false;
                var rect = _zoomRect;
                _zoomRect = Rectangle.Empty;
                Invalidate();
                if (rect.Width > 6 && rect.Height > 6)
                {
                    // 旋转后不能再拿两个对角点建地理矩形（那是轴对齐的假设），
                    // 改为“屏幕矩形中心 → 地理中心 + 按屏幕尺度算分辨率”，θ=0 时与旧行为等价。
                    double cxg, cyg;
                    View.ToGeo(rect.Left + rect.Width * 0.5, rect.Top + rect.Height * 0.5, out cxg, out cyg);
                    double m = View.Mupp * Math.Max(rect.Width / (double)Math.Max(1, View.Width),
                                                    rect.Height / (double)Math.Max(1, View.Height));
                    var before = View.Clone();
                    View.SetPixelSize(m, cxg, cyg);
                    PushHistory(before);
                    RequestRender(false);
                    RaiseViewChanged();
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouse = new Point(-1, -1);
            if (_tool == MapTool.Identify) Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta == 0) return;
            // 旋转拖动中缩放会破坏半径与回滚语义
            if (_rotatePhase == RotatePhase.Following) return;
            double factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
            ZoomAtScreen(e.X, e.Y, factor);
        }

        private void ZoomAtScreen(int sx, int sy, double factor)
        {
            var before = View.Clone();
            View.ZoomAt(sx, sy, factor);
            PushHistory(before);
            RenderNow(RenderQuality.Fast);
            RequestRender(true);
            RaiseViewChanged();
        }

        /// <summary>自检专用：以像素坐标触发一次识别。</summary>
        internal void DoIdentifyForTest(RasterLayer layer, int col, int row)
        {
            if (layer == null) return;
            var values = layer.ReadPixel(col, row);
            double gx, gy;
            layer.PixelToGeo(col + 0.5, row + 0.5, out gx, out gy);
            var h = IdentifyRequested;
            if (h != null)
                h(this, new IdentifyEventArgs { Layer = layer, Col = col, Row = row, GeoX = gx, GeoY = gy, Values = values });
        }

        private void DoIdentify(int sx, int sy)
        {
            double gx, gy;
            View.ToGeo(sx, sy, out gx, out gy);
            var layer = Layers != null ? HitTestLayer(gx, gy) : null;
            if (layer == null) return;

            double col, row;
            layer.GeoToPixel(gx, gy, out col, out row);
            int c = (int)Math.Floor(col), r = (int)Math.Floor(row);
            if (c < 0 || r < 0 || c >= layer.Width || r >= layer.Height) return;

            var values = layer.ReadPixel(c, r);
            var h = IdentifyRequested;
            if (h != null)
                h(this, new IdentifyEventArgs { Layer = layer, Col = c, Row = r, GeoX = gx, GeoY = gy, Values = values });
        }

        private RasterLayer HitTestLayer(double gx, double gy)
        {
            if (Layers == null) return null;
            var arr = Layers.ToArray();
            for (int i = arr.Length - 1; i >= 0; i--)
            {
                var l = arr[i];
                if (!l.Visible || l.IsDisposed) continue;
                if (gx >= l.Extent.XMin && gx <= l.Extent.XMax && gy >= l.Extent.YMin && gy <= l.Extent.YMax)
                    return l;
            }
            return null;
        }

        private void Probe(int sx, int sy)
        {
            var now = DateTime.Now;
            if ((now - _lastProbe).TotalMilliseconds < 40) return;
            _lastProbe = now;

            double gx, gy;
            View.ToGeo(sx, sy, out gx, out gy);
            var args = new CursorInfoEventArgs { GeoX = gx, GeoY = gy, Col = -1, Row = -1 };

            var layer = Layers != null ? HitTestLayer(gx, gy) : null;
            if (layer != null)
            {
                double col, row;
                layer.GeoToPixel(gx, gy, out col, out row);
                int c = (int)Math.Floor(col), r = (int)Math.Floor(row);
                if (c >= 0 && r >= 0 && c < layer.Width && r < layer.Height)
                {
                    args.Layer = layer;
                    args.Col = c;
                    args.Row = r;
                    args.Values = layer.ReadPixel(c, r);
                    args.ValidPixel = true;
                }
            }
            var h = CursorInfoChanged;
            if (h != null) h(this, args);
        }

        // 自绘画布要支持 WM_PRINTCLIENT，否则 DrawToBitmap 得到空白
        private const int WM_PRINTCLIENT = 0x0318;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PRINTCLIENT)
            {
                using (var g = Graphics.FromHdc(m.WParam))
                    DrawContent(g);
                return;
            }
            base.WndProc(ref m);
        }
    }
}
