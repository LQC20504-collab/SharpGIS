using System;
using System.Text;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
    /// <summary>
    /// 一个波段引用：某个图层的某个物理波段（1 基）。
    /// 波段计算器里的 b1/b2 只是占位符，真正读哪个文件的哪个波段由本类描述。
    /// </summary>
    public sealed class BandSource
    {
        /// <summary>所属图层（不会为 null）。</summary>
        public readonly RasterLayer Layer;
        /// <summary>波段号（1 基，1..Layer.BandCount）。</summary>
        public readonly int Band;

        /// <summary>创建一个波段引用。</summary>
        /// <param name="layer">所属图层。</param>
        /// <param name="band">波段号（1 基）。</param>
        public BandSource(RasterLayer layer, int band)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (band < 1 || band > layer.BandCount)
                throw new ArgumentOutOfRangeException(nameof(band), band,
                    Lang.T("波段号超出范围：图层 ") + layer.Name + Lang.T(" 共 ") + layer.BandCount + Lang.T(" 个波段"));
            Layer = layer;
            Band = band;
        }

        /// <summary>引用是否仍然可用（图层未释放、波段仍在范围内）。</summary>
        public bool IsValid
        {
            get { return Layer != null && !Layer.IsDisposed && Band >= 1 && Band <= Layer.BandCount; }
        }

        /// <summary>波段自身的描述，例如“波段 4 · 红 · Byte”。</summary>
        public string BandLabel { get { return BandText(Layer, Band); } }

        /// <summary>带图层名的完整描述，例如“TMtest.tif · 波段 4 · 红 · Byte”。</summary>
        public string DisplayName
        {
            get { return (Layer != null ? Layer.Name + " · " : string.Empty) + BandLabel; }
        }

        /// <summary>格式化一个波段的简短描述（色彩解释 + 数据类型 + 掩膜标记）。</summary>
        /// <param name="layer">所属图层。</param>
        /// <param name="band">波段号（1 基）。</param>
        /// <returns>如“波段 4 · 红 · Byte”或“波段 8 · alpha · Byte（掩膜）”。</returns>
        public static string BandText(RasterLayer layer, int band)
        {
            if (layer == null) return Lang.T("波段 ") + band;
            var sb = new StringBuilder();
            sb.Append(Lang.T("波段 ")).Append(band);

            if (band >= 1 && band <= layer.ColorInterps.Length)
            {
                string color = ColorName(layer.ColorInterps[band - 1]);
                if (color.Length > 0) sb.Append(" · ").Append(color);
            }

            if (band >= 1 && band <= layer.BandTypes.Length)
                sb.Append(" · ").Append(layer.BandTypes[band - 1].ToString().Replace("GDT_", string.Empty));

            if (band == layer.AlphaBandIndex) sb.Append(Lang.T("（掩膜）"));

            if (band >= 1 && band <= layer.BandDescriptions.Length)
            {
                string desc = layer.BandDescriptions[band - 1];
                if (!string.IsNullOrEmpty(desc))
                {
                    if (desc.Length > 24) desc = desc.Substring(0, 24) + "…";
                    sb.Append(Lang.IsEnglish ? " [" : " 「").Append(desc)
                        .Append(Lang.IsEnglish ? ']' : '」');
                }
            }
            return sb.ToString();
        }

        private static string ColorName(OSGeo.GDAL.ColorInterp interp)
        {
            switch (interp)
            {
                case OSGeo.GDAL.ColorInterp.GCI_RedBand: return Lang.T("红");
                case OSGeo.GDAL.ColorInterp.GCI_GreenBand: return Lang.T("绿");
                case OSGeo.GDAL.ColorInterp.GCI_BlueBand: return Lang.T("蓝");
                case OSGeo.GDAL.ColorInterp.GCI_AlphaBand: return "alpha";
                case OSGeo.GDAL.ColorInterp.GCI_GrayIndex: return Lang.T("灰阶");
                case OSGeo.GDAL.ColorInterp.GCI_PaletteIndex: return Lang.T("调色板");
                default: return string.Empty;
            }
        }

        /// <summary>按（图层引用 + 波段号）比较。</summary>
        /// <param name="obj">要比较的对象。</param>
        /// <returns>指向同一图层的同一波段时返回 true。</returns>
        public override bool Equals(object obj)
        {
            var other = obj as BandSource;
            return other != null && ReferenceEquals(other.Layer, Layer) && other.Band == Band;
        }

        /// <summary>哈希码。</summary>
        /// <returns>由图层与波段号组合而成。</returns>
        public override int GetHashCode()
        {
            return (Layer != null ? Layer.GetHashCode() : 0) * 397 ^ Band;
        }

        /// <summary>文本形式，等同 <see cref="DisplayName"/>。</summary>
        /// <returns>可读描述。</returns>
        public override string ToString()
        {
            return DisplayName;
        }
    }
}
