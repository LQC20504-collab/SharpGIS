using System;
using OSGeo.GDAL;
using SharpGIS.Models;

namespace SharpGIS.Rendering
{
    /// <summary>
    /// 单通道查找表：灰阶/RGB 通道为 1024 级（<see cref="StretchEngine.LutSize"/>）；
    /// 调色板模式由 <see cref="StretchEngine.BuildFromColorTable"/> 生成 256 级索引表。
    /// </summary>
    public sealed class ChannelLut
    {
        public byte[] R;
        public byte[] G;
        public byte[] B;
        public double Lo;
        public double Hi;
        public double Scale;   // (LutSize-1)/(Hi-Lo)
        public int Size;

    }

    /// <summary>把统计数据与符号化设置编译成查找表。</summary>
    public static class StretchEngine
    {
        /// <summary>灰阶/RGB 通道查找表的级数。</summary>
        public const int LutSize = 1024;

        /// <summary>把统计量与符号化设置编译成查找表。</summary>
        /// <param name="stats">波段统计，可为 null。</param>
        /// <param name="cfg">拉伸配置。</param>
        /// <param name="ramp">色带。</param>
        /// <param name="invert">是否反相。</param>
        /// <param name="gamma">Gamma 值。</param>
        /// <param name="brightness">亮度偏移。</param>
        /// <param name="contrast">对比度倍数。</param>
        /// <returns>可直接按值索引的查找表。</returns>
        public static ChannelLut Build(BandStats stats, StretchConfig cfg, ColorRamp ramp,
            bool invert, double gamma, double brightness, double contrast)
        {
            var lut = new ChannelLut
            {
                R = new byte[LutSize],
                G = new byte[LutSize],
                B = new byte[LutSize],
                Size = LutSize
            };

            double lo = 0, hi = 1;
            if (cfg != null) cfg.Resolve(stats, out lo, out hi);
            else if (stats != null) { lo = stats.TypeMin; hi = stats.TypeMax; }

            if (stats != null && cfg != null && cfg.Mode == StretchMode.None)
            {
                // 无拉伸：按数据类型满量程直接映射（Byte 即 0-255）
                lo = stats.TypeMin;
                hi = stats.TypeMax;
            }

            lut.Lo = lo;
            lut.Hi = hi;
            double span = hi - lo;
            if (Math.Abs(span) < 1e-12) span = 1e-12;
            lut.Scale = (LutSize - 1) / span;

            // 直方图均衡：先构造 256 级均衡表
            byte[] eqTable = null;
            if (cfg != null && cfg.Mode == StretchMode.HistogramEqualize && stats != null)
                eqTable = stats.BuildEqualizationTable();

            double g = gamma <= 0 ? 1 : gamma;
            double invG = 1.0 / g;
            double c = contrast <= 0 ? 1 : contrast;

            for (int i = 0; i < LutSize; i++)
            {
                double t = i / (double)(LutSize - 1);

                if (eqTable != null)
                {
                    int bin = (int)(t * 255.0 + 0.5);
                    if (bin < 0) bin = 0; if (bin > 255) bin = 255;
                    t = eqTable[bin] / 255.0;
                }

                if (Math.Abs(g - 1.0) > 1e-6) t = Math.Pow(t, invG);
                if (Math.Abs(c - 1.0) > 1e-6) t = (t - 0.5) * c + 0.5;
                if (Math.Abs(brightness) > 1e-9) t += brightness;
                if (invert) t = 1.0 - t;

                if (t < 0) t = 0; else if (t > 1) t = 1;

                var col = ramp != null ? ramp.Sample(t) : System.Drawing.Color.FromArgb((int)(t * 255), (int)(t * 255), (int)(t * 255));
                lut.R[i] = col.R;
                lut.G[i] = col.G;
                lut.B[i] = col.B;
            }
            return lut;
        }

        /// <summary>由 GDAL 调色板直接生成 256 项索引表。</summary>
        /// <param name="ct">调色板，可为 null（退化为灰度）。</param>
        /// <returns>索引型查找表。</returns>
        public static ChannelLut BuildFromColorTable(ColorTable ct)
        {
            var lut = new ChannelLut
            {
                R = new byte[256],
                G = new byte[256],
                B = new byte[256],
                Size = 256,
                Lo = 0,
                Hi = 255,
                Scale = 1.0
            };
            int count = ct != null ? ct.GetCount() : 0;
            for (int i = 0; i < 256; i++)
            {
                if (i < count)
                {
                    try
                    {
                        var e = ct.GetColorEntry(i);
                        lut.R[i] = ToByte(e.c1);
                        lut.G[i] = ToByte(e.c2);
                        lut.B[i] = ToByte(e.c3);
                        continue;
                    }
                    catch { }
                }
                lut.R[i] = lut.G[i] = lut.B[i] = (byte)i;
            }
            return lut;
        }

        private static byte ToByte(short v)
        {
            if (v <= 0) return 0;
            if (v >= 255) return 255;
            return (byte)v;
        }

    }
}
