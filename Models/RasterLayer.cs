using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using OSGeo.GDAL;

using SharpGIS.Services;
namespace SharpGIS.Models
{
    /// <summary>
    /// 一个栅格图层：独占持有一个 GDAL Dataset，并提供元数据、统计信息与渲染所需的全部状态。
    /// Dataset 不是线程安全的，因此所有 GDAL 访问必须包在 <see cref="ReadLock"/> 里。
    /// </summary>
    public sealed class RasterLayer : IDisposable
    {
        private static int _nextId = 1;

        public readonly object ReadLock = new object();
        public readonly int Id;

        /// <summary>GDAL 数据集句柄；所有访问都必须在该图层的 ReadLock 内进行。</summary>
        public Dataset Ds { get; private set; }
        /// <summary>打开时使用的路径；旋转正视化情况下仍为原始文件路径，临时 VRT 见 TempFilePath。</summary>
        public string Path { get; private set; }
        /// <summary>图层显示名，默认取文件名，可被用户重命名。</summary>
        public string Name { get; set; }

        /// 原始源文件路径（只读约束的判定依据）。
        /// 经旋转正视化后 Path 仍等于它，临时 VRT 记录在 <see cref="TempFilePath"/>。
        public string SourcePath { get; private set; }
        /// <summary>旋转影像正视化后生成的临时 VRT 路径（无则为 null）。</summary>
        public string TempFilePath { get; internal set; }
        /// <summary>是否经过旋转正视化。</summary>
        public bool IsRectified { get; internal set; }

        /// <summary>影像列数（像素）。</summary>
        public int Width { get; private set; }
        /// <summary>影像行数（像素）。</summary>
        public int Height { get; private set; }
        /// <summary>波段总数，含 alpha 掩膜波段（如有）。</summary>
        public int BandCount { get; private set; }
        /// <summary>各波段的 GDAL 数据类型。</summary>
        public DataType[] BandTypes { get; private set; }
        /// <summary>各波段的色彩解释（Red/Green/Blue/Alpha 等）。</summary>
        public ColorInterp[] ColorInterps { get; private set; }
        /// <summary>各波段的描述文字，可能为空字符串。</summary>
        public string[] BandDescriptions { get; private set; }

        /// <summary>仿射地理变换（6 个系数）；无参考时为单位矩阵，配合 HasGeoTransform 判断。</summary>
        public double[] GeoTransform { get; private set; }
        /// <summary>是否存在真实的地理变换（非单位矩阵）。</summary>
        public bool HasGeoTransform { get; private set; }
        /// <summary>投影 WKT 文本；无投影时为空字符串。</summary>
        public string ProjectionWkt { get; private set; }
        /// <summary>投影的可读描述，如 EPSG:4326 · WGS 84 或“自定义投影（…）”。</summary>
        public string EpsgText { get; private set; }
        /// <summary>是否带投影信息。</summary>
        public bool HasProjection { get { return !string.IsNullOrWhiteSpace(ProjectionWkt); } }

        /// <summary>是否设置了 NoData 值。</summary>
        public bool HasNoData { get; private set; }
        /// <summary>NoData 取值（HasNoData 为 true 时才有意义）。</summary>
        public double NoDataValue { get; private set; }

        /// <summary>alpha 掩膜波段号（1 起），0 表示没有。</summary>
        public int AlphaBandIndex { get; private set; }

        /// <summary>第 1 波段的块宽（像素）。</summary>
        public int BlockX { get; private set; }
        /// <summary>第 1 波段的块高（像素）。</summary>
        public int BlockY { get; private set; }
        /// <summary>压缩方式（取自 IMAGE_STRUCTURE 元数据），无压缩时为 null。</summary>
        public string Compression { get; private set; }
        /// <summary>打开该文件的 GDAL 驱动短名。</summary>
        public string DriverName { get; private set; }
        /// <summary>该数据集关联的文件列表（VRT/World File 等）。</summary>
        public string[] FileList { get; private set; }

        /// <summary>符号化设置；修改后必须调用 Touch() 触发重绘。</summary>
        public RasterStyle Style { get; set; }
        /// <summary>是否参与渲染（对应图层面板的可见性开关）。</summary>
        public bool Visible = true;

        /// <summary>样式版本号，任何符号化变更 +1，用于渲染缓存失效。</summary>
        public int StyleVersion { get; private set; }

        public GeoRect Extent { get; private set; }

        /// 是否带旋转/斜切分量（GeoTransform[2] 或 [4] 非零）。
        /// 带旋转的影像在打开时会被包装成北向上的临时 VRT。
        public bool IsRotated
        {
            get
            {
                if (!HasGeoTransform) return false;
                return Math.Abs(GeoTransform[2]) > 1e-12 || Math.Abs(GeoTransform[4]) > 1e-12;
            }
        }

        /// <summary>像元宽度（地图单位）。</summary>
        public double PixelSizeX { get { return HasGeoTransform ? Math.Abs(GeoTransform[1]) : 1.0; } }
        /// <summary>像元高度（地图单位，已取绝对值）。</summary>
        public double PixelSizeY { get { return HasGeoTransform ? Math.Abs(GeoTransform[5]) : 1.0; } }

        private readonly BandStats[] _stats;
        private int _leases;
        private volatile bool _disposeRequested;

        internal RasterLayer(Dataset ds, string path)
        {
            Id = Interlocked.Increment(ref _nextId);
            Ds = ds;
            Path = path;
            SourcePath = path;
            Name = System.IO.Path.GetFileName(path);

            Width = ds.RasterXSize;
            Height = ds.RasterYSize;
            BandCount = ds.RasterCount;

            BandTypes = new DataType[BandCount];
            ColorInterps = new ColorInterp[BandCount];
            BandDescriptions = new string[BandCount];
            _stats = new BandStats[BandCount];

            for (int i = 0; i < BandCount; i++)
            {
                var b = ds.GetRasterBand(i + 1);
                BandTypes[i] = b.DataType;
                try { ColorInterps[i] = b.GetRasterColorInterpretation(); } catch { ColorInterps[i] = ColorInterp.GCI_Undefined; }
                // HFA(.img) 等格式不保存 ColorInterp，导出时会写一条波段元数据作为提示，这里还原
                if (ColorInterps[i] == ColorInterp.GCI_Undefined)
                    ColorInterps[i] = ReadColorInterpHint(b);
                try { BandDescriptions[i] = b.GetDescription(); } catch { }
                b.Dispose();
                if (ColorInterps[i] == ColorInterp.GCI_AlphaBand && AlphaBandIndex == 0)
                    AlphaBandIndex = i + 1;
            }

            var gt = new double[6];
            ds.GetGeoTransform(gt);
            // GDAL 无地理变换时返回单位矩阵 (0,1,0,0,0,1)
            bool identity = Math.Abs(gt[0]) < 1e-12 && Math.Abs(gt[1] - 1) < 1e-12 &&
                            Math.Abs(gt[2]) < 1e-12 && Math.Abs(gt[3]) < 1e-12 &&
                            Math.Abs(gt[4]) < 1e-12 && Math.Abs(gt[5] - 1) < 1e-12;
            HasGeoTransform = !identity;
            GeoTransform = HasGeoTransform ? gt : new double[] { 0, 1, 0, Height, 0, -1 };
            Extent = GeoRect.FromGeoTransform(Width, Height, GeoTransform);

            try { ProjectionWkt = ds.GetProjection() ?? string.Empty; } catch { ProjectionWkt = string.Empty; }
            EpsgText = ProjectionUtil.Describe(ProjectionWkt);

            var b1 = ds.GetRasterBand(1);
            try
            {
                double nd; int has;
                b1.GetNoDataValue(out nd, out has);
                HasNoData = has != 0;
                NoDataValue = nd;
            }
            catch { HasNoData = false; }
            int bx = 0, by = 0;
            try { b1.GetBlockSize(out bx, out by); } catch { }
            BlockX = bx; BlockY = by;
            try { Compression = b1.GetMetadataItem("COMPRESSION", "IMAGE_STRUCTURE"); } catch { }
            b1.Dispose();

            try { DriverName = ds.GetDriver() != null ? ds.GetDriver().ShortName : Lang.T("未知"); } catch { DriverName = Lang.T("未知"); }
            try { FileList = ds.GetFileList(); } catch { FileList = new[] { path }; }

            Style = CreateDefaultStyle();
        }

        /// <summary>读取导出时写入的色彩解释提示（元数据键 <c>COLOR_INTERP</c>）。</summary>
        /// <param name="band">目标波段。</param>
        /// <returns>识别出的色彩解释；没有提示或无法识别时返回 Undefined。</returns>
        private static ColorInterp ReadColorInterpHint(Band band)
        {
            try
            {
                string text = band.GetMetadataItem("COLOR_INTERP", "");
                return ParseColorInterp(text);
            }
            catch { return ColorInterp.GCI_Undefined; }
        }

        /// <summary>把 <see cref="ColorInterp"/> 转成短名（写入元数据用），Undefined 返回空串。</summary>
        /// <param name="interp">色彩解释。</param>
        /// <returns>Red/Green/Blue/Alpha/Gray/Palette，或空串。</returns>
        public static string ColorInterpName(ColorInterp interp)
        {
            switch (interp)
            {
                case ColorInterp.GCI_RedBand: return "Red";
                case ColorInterp.GCI_GreenBand: return "Green";
                case ColorInterp.GCI_BlueBand: return "Blue";
                case ColorInterp.GCI_AlphaBand: return "Alpha";
                case ColorInterp.GCI_GrayIndex: return "Gray";
                case ColorInterp.GCI_PaletteIndex: return "Palette";
                default: return string.Empty;
            }
        }

        /// <summary>把短名解析为 <see cref="ColorInterp"/>，无法识别时返回 Undefined。</summary>
        /// <param name="name">Red/Green/Blue/Alpha/Gray/Palette。</param>
        /// <returns>对应的色彩解释。</returns>
        public static ColorInterp ParseColorInterp(string name)
        {
            if (string.IsNullOrEmpty(name)) return ColorInterp.GCI_Undefined;
            switch (name.Trim().ToLowerInvariant())
            {
                case "red": return ColorInterp.GCI_RedBand;
                case "green": return ColorInterp.GCI_GreenBand;
                case "blue": return ColorInterp.GCI_BlueBand;
                case "alpha": return ColorInterp.GCI_AlphaBand;
                case "gray": return ColorInterp.GCI_GrayIndex;
                case "palette": return ColorInterp.GCI_PaletteIndex;
                default: return ColorInterp.GCI_Undefined;
            }
        }

        /// <summary>根据波段数与色彩解释给出合理的默认符号化（可重复调用以“恢复默认”）。</summary>
        public RasterStyle CreateDefaultStyle()
        {
            var st = new RasterStyle();
            if (BandCount >= 3)
            {
                var rgb = ProjectionUtil.GuessRgbBands(ColorInterps, BandCount);
                if (rgb != null)
                {
                    st.Mode = RenderMode.Rgb;
                    st.RgbBands = rgb;
                }
                else
                {
                    st.Mode = RenderMode.Gray;
                    st.GrayBand = 1;
                }
            }
            else
            {
                st.Mode = RenderMode.Gray;
                st.GrayBand = 1;
            }

            // 若第 1 波段带调色板，直接用调色板显示
            if (BandCount >= 1)
            {
                try
                {
                    using (var b = Ds.GetRasterBand(1))
                    {
                        var ct = b.GetRasterColorTable();
                        if (ct != null) { st.Mode = RenderMode.Palette; ct.Dispose(); }
                    }
                }
                catch { }
            }
            return st;
        }

        /// <summary>把显示用的源路径改回原始文件（重投影/正视化后仍显示原始文件）。</summary>
        public void SourcePathOverride(string sourcePath)
        {
            if (!string.IsNullOrEmpty(sourcePath)) SourcePath = sourcePath;
        }

        /// <summary>标记符号化已变更，使渲染缓存失效。</summary>
        public void Touch()
        {
            StyleVersion++;
        }

        /// <summary>取指定波段的统计信息（首次调用时抽样计算并缓存）。</summary>
        public BandStats Stats(int bandIndex)
        {
            if (bandIndex < 1 || bandIndex > BandCount) return null;
            var cached = _stats[bandIndex - 1];
            if (cached != null) return cached;

            lock (ReadLock)
            {
                cached = _stats[bandIndex - 1];
                if (cached != null) return cached;
                var band = Ds.GetRasterBand(bandIndex);
                Band alpha = null;
                try
                {
                    if (AlphaBandIndex > 0 && AlphaBandIndex != bandIndex)
                        alpha = Ds.GetRasterBand(AlphaBandIndex);
                    cached = BandStats.Compute(band, alpha, band.DataType, HasNoData, NoDataValue);
                }
                finally
                {
                    if (alpha != null) alpha.Dispose();
                    band.Dispose();
                }
                _stats[bandIndex - 1] = cached;
                return cached;
            }
        }

        /// <summary>取指定波段的调色板；没有时返回 null。</summary>
        public ColorTable GetColorTable(int bandIndex)
        {
            lock (ReadLock)
            {
                try
                {
                    var b = Ds.GetRasterBand(bandIndex);
                    try { return b.GetRasterColorTable(); }
                    finally { b.Dispose(); }
                }
                catch { return null; }
            }
        }

        /// <summary>像素坐标 → 地理坐标。</summary>
        public void PixelToGeo(double col, double row, out double x, out double y)
        {
            x = GeoTransform[0] + col * GeoTransform[1] + row * GeoTransform[2];
            y = GeoTransform[3] + col * GeoTransform[4] + row * GeoTransform[5];
        }

        /// 地理坐标 → 像素坐标。
        /// 对含旋转的仿射变换做精确 2×2 求逆；行列式接近 0 时退化为按轴除法。
        public void GeoToPixel(double x, double y, out double col, out double row)
        {
            double dx = x - GeoTransform[0];
            double dy = y - GeoTransform[3];
            double det = GeoTransform[1] * GeoTransform[5] - GeoTransform[2] * GeoTransform[4];
            if (Math.Abs(det) < 1e-20)
            {
                col = dx / (GeoTransform[1] == 0 ? 1 : GeoTransform[1]);
                row = dy / (GeoTransform[5] == 0 ? -1 : GeoTransform[5]);
                return;
            }
            col = (dx * GeoTransform[5] - dy * GeoTransform[2]) / det;
            row = (dy * GeoTransform[1] - dx * GeoTransform[4]) / det;
        }

        /// <summary>读取单个像素的全部波段值（越界返回全 0）。</summary>
        public double[] ReadPixel(int col, int row)
        {
            var values = new double[BandCount];
            if (col < 0 || row < 0 || col >= Width || row >= Height) return values;
            lock (ReadLock)
            {
                var buf = new double[1];
                for (int i = 0; i < BandCount; i++)
                {
                    var b = Ds.GetRasterBand(i + 1);
                    try
                    {
                        b.ReadRaster(col, row, 1, 1, buf, 1, 1, 0, 0);
                        values[i] = buf[0];
                    }
                    catch { values[i] = double.NaN; }
                    finally { b.Dispose(); }
                }
            }
            return values;
        }

        /// <summary>生成图层缩略图（按像素宽高比缩放到 maxW×maxH 以内）。</summary>
        public Bitmap BuildThumbnail(int maxW, int maxH)
        {
            double ratio = Width / (double)Math.Max(1, Height);
            int w = maxW, h = (int)Math.Round(maxW / ratio);
            if (h > maxH) { h = maxH; w = (int)Math.Round(maxH * ratio); }
            w = Math.Max(1, w); h = Math.Max(1, h);

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            lock (ReadLock)
            {
                int bandNo = Style.Mode == RenderMode.Rgb ? Style.RgbBands[0] : Style.GrayBand;
                if (bandNo < 1 || bandNo > BandCount) bandNo = 1;
                var band = Ds.GetRasterBand(bandNo);
                try
                {
                    var buf = new float[w * h];
                    band.ReadRaster(0, 0, Width, Height, buf, w, h, 0, 0);
                    var stats = Stats(bandNo);
                    double lo, hi;
                    (stats != null ? Style.StretchFor(0) : new StretchConfig()).Resolve(stats, out lo, out hi);
                    double span = hi - lo;
                    if (Math.Abs(span) < 1e-12) span = 1;

                    var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        unsafe
                        {
                            for (int y = 0; y < h; y++)
                            {
                                byte* p = (byte*)data.Scan0 + y * data.Stride;
                                for (int x = 0; x < w; x++)
                                {
                                    float v = buf[y * w + x];
                                    int g = (int)((v - lo) / span * 255.0);
                                    if (g < 0) g = 0; if (g > 255) g = 255;
                                    p[x * 4 + 0] = (byte)g;
                                    p[x * 4 + 1] = (byte)g;
                                    p[x * 4 + 2] = (byte)g;
                                    p[x * 4 + 3] = 255;
                                }
                            }
                        }
                    }
                    finally { bmp.UnlockBits(data); }
                }
                finally { band.Dispose(); }
            }
            return bmp;
        }

        // ---------------- 生命周期 ----------------

        internal int Acquire()
        {
            return Interlocked.Increment(ref _leases);
        }

        internal void Release()
        {
            if (Interlocked.Decrement(ref _leases) <= 0 && _disposeRequested)
                DisposeNow();
        }

        /// <summary>请求释放；若仍有渲染租约则延后到租约归零。</summary>
        public void Dispose()
        {
            _disposeRequested = true;
            if (Volatile.Read(ref _leases) <= 0) DisposeNow();
        }

        private int _disposed;

        private void DisposeNow()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (ReadLock)
            {
                try { if (Ds != null) Ds.Dispose(); } catch { }
                Ds = null;
            }
            if (!string.IsNullOrEmpty(TempFilePath))
            {
                try { if (System.IO.File.Exists(TempFilePath)) System.IO.File.Delete(TempFilePath); } catch { }
            }
        }

        /// <summary>数据集是否已释放。</summary>
        public bool IsDisposed { get { return Volatile.Read(ref _disposed) != 0; } }

        /// <summary>“宽 × 高”文本。</summary>
        public string SizeText { get { return Width + " × " + Height; } }

        /// <summary>不含 alpha 掩膜波段的波段数（用于界面显示）。</summary>
        public int DisplayBandCount
        {
            get { return AlphaBandIndex > 0 ? BandCount - 1 : BandCount; }
        }

        /// <summary>数据类型文本；多波段类型不一致时返回“混合类型”。</summary>
        public string TypeText
        {
            get
            {
                if (BandCount == 0) return "-";
                string t = BandTypes[0].ToString().Replace("GDT_", "");
                bool same = true;
                for (int i = 1; i < BandCount; i++) if (BandTypes[i] != BandTypes[0]) same = false;
                return same ? t : Lang.T("混合类型");
            }
        }
    }

    /// <summary>投影/波段选择辅助。</summary>
    public static class ProjectionUtil
    {
        public static string Describe(string wkt)
        {
            if (string.IsNullOrWhiteSpace(wkt)) return Lang.T("未知（无投影信息）");
            try
            {
                using (var sr = new OSGeo.OSR.SpatialReference(wkt))
                {
                    try { sr.AutoIdentifyEPSG(); } catch { }
                    string auth = null, code = null;
                    try { auth = sr.GetAuthorityName(null); code = sr.GetAuthorityCode(null); } catch { }
                    string name = null;
                    try { name = sr.GetName(); } catch { }

                    // 9001 是 GDAL 在“无法匹配到 EPSG 条目”时给的占位码，不能当真实权限码用
                    bool bogus = string.IsNullOrEmpty(code) || code == "0" || code == "9001";

                    if (!bogus && !string.IsNullOrEmpty(auth))
                        return string.Format("{0}:{1}{2}", auth, code,
                            string.IsNullOrEmpty(name) || name == "unnamed" ? "" : " · " + name);

                    // 退化：从 WKT 尾部抓 AUTHORITY["EPSG","xxxx"]（通常是地理基准的 4326 等）
                    var m = System.Text.RegularExpressions.Regex.Matches(wkt, "AUTHORITY\\[\"([A-Za-z]+)\",\"(\\d+)\"\\]");
                    string proj = null;
                    try { proj = sr.GetAttrValue("PROJECTION", 0); } catch { }
                    string baseName = string.IsNullOrEmpty(name) || name == "unnamed" ? null : name;
                    if (!string.IsNullOrEmpty(proj))
                        return Lang.T("自定义投影（{0}）",
                            proj.Replace('_', ' ') + (baseName != null ? ", " + baseName : ""));
                    if (m.Count > 0)
                    {
                        var last = m[m.Count - 1];
                        return string.Format(Lang.T("{0}:{1}（地理基准）"), last.Groups[1].Value, last.Groups[2].Value);
                    }
                    if (baseName != null) return baseName;
                    return Lang.T("自定义投影（未识别 EPSG）");
                }
            }
            catch { return Lang.T("未知"); }
        }

        public static string ShortWkt(string wkt, int maxLen = 160)
        {
            if (string.IsNullOrWhiteSpace(wkt)) return Lang.T("（无）");
            string s = wkt.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + " …";
        }

        /// <summary>按 ColorInterp 猜 RGB 波段；猜不出时返回 null。</summary>
        public static int[] GuessRgbBands(ColorInterp[] interps, int bandCount)
        {
            if (bandCount < 3) return null;
            int r = -1, g = -1, b = -1;
            for (int i = 0; i < interps.Length; i++)
            {
                if (interps[i] == ColorInterp.GCI_RedBand && r < 0) r = i + 1;
                else if (interps[i] == ColorInterp.GCI_GreenBand && g < 0) g = i + 1;
                else if (interps[i] == ColorInterp.GCI_BlueBand && b < 0) b = i + 1;
            }
            if (r > 0 && g > 0 && b > 0) return new[] { r, g, b };

            // 退化：Landsat TM 顺序（3/2/1 真彩色）优先，否则 1/2/3
            if (bandCount >= 5) return new[] { 3, 2, 1 };
            return new[] { 1, 2, 3 };
        }
    }
}
