using System;

using SharpGIS.Services;
namespace SharpGIS.Models
{
        /// <summary>渲染方式：单波段灰度、多波段 RGB、调色板。</summary>
    public enum RenderMode { Gray, Rgb, Palette }
        /// <summary>对比度拉伸方式：无 / 最小最大 / 标准差 / 百分比裁剪 / 直方图均衡 / 自定义区间。</summary>
    public enum StretchMode { None, MinMax, StdDev, PercentClip, HistogramEqualize, CustomMinMax }
        /// <summary>图层混合模式：正常、正片叠底、滤色。</summary>
    public enum BlendMode { Normal, Multiply, Screen }
        /// <summary>渲染重采样算法：最近邻、双线性、立方、平均值。</summary>
    public enum ResampleMode { Nearest, Bilinear, Cubic, Average }

        /// <summary>单个波段的拉伸参数。</summary>
    public sealed class StretchConfig
    {
        /// <summary>拉伸方式，默认按 2%~98% 累计裁剪由 <see cref="RasterStyle"/> 初始化。</summary>
        public StretchMode Mode = StretchMode.MinMax;
        /// <summary>自定义区间下限（仅 CustomMinMax 模式生效）。</summary>
        public double CustomMin;
        /// <summary>自定义区间上限（仅 CustomMinMax 模式生效）。</summary>
        public double CustomMax = 255;
        /// <summary>标准差拉伸的倍数 n（区间取 mean ± n·σ）。</summary>
        public int StdDevFactor = 2;
        /// <summary>百分比裁剪的下限百分比。</summary>
        public double PercentLow = 2;
        /// <summary>百分比裁剪的上限百分比。</summary>
        public double PercentHigh = 2;

        /// <summary>根据统计量解析出实际拉伸端点。</summary>
        /// <param name="s">波段统计信息，可为 null。</param>
        /// <param name="lo">解析出的下限。</param>
        /// <param name="hi">解析出的上限。</param>
        public void Resolve(BandStats s, out double lo, out double hi)
        {
            if (s == null || !s.Valid)
            {
                lo = 0; hi = 1;
                return;
            }

            switch (Mode)
            {
                case StretchMode.None:
                    lo = s.QuantileMin; hi = s.QuantileMax;
                    break;
                case StretchMode.StdDev:
                    lo = s.Mean - StdDevFactor * s.StdDev;
                    hi = s.Mean + StdDevFactor * s.StdDev;
                    break;
                case StretchMode.PercentClip:
                    lo = s.Percentile(PercentLow);
                    hi = s.Percentile(100 - PercentHigh);
                    break;
                case StretchMode.CustomMinMax:
                    lo = CustomMin; hi = CustomMax;
                    break;
                case StretchMode.HistogramEqualize:
                case StretchMode.MinMax:
                default:
                    lo = s.Min; hi = s.Max;
                    break;
            }

            if (hi - lo < 1e-12)
            {
                // 常量影像：给一个极小的区间，避免除零
                double pad = Math.Abs(lo) > 1e-9 ? Math.Abs(lo) * 1e-6 : 1e-6;
                lo -= pad; hi += pad;
            }
            if (hi < lo) { double t = lo; lo = hi; hi = t; }
        }


        public override string ToString()
        {
            switch (Mode)
            {
                case StretchMode.None: return Lang.T("无拉伸（线性）");
                case StretchMode.MinMax: return Lang.T("最小-最大值");
                case StretchMode.StdDev: return Lang.T("标准差 ×") + StdDevFactor;
                case StretchMode.PercentClip: return Lang.T("百分比裁剪 ") + PercentLow + "%-" + PercentHigh + "%";
                case StretchMode.HistogramEqualize: return Lang.T("直方图均衡");
                case StretchMode.CustomMinMax: return Lang.T("自定义 ") + CustomMin.ToString("G4") + " ~ " + CustomMax.ToString("G4");
            }
            return Mode.ToString();
        }
    }

        /// <summary>栅格符号化设置。任何字段变更后需调用所属图层的 Touch() 使渲染失效。</summary>
    public sealed class RasterStyle
    {
        /// <summary>渲染方式。</summary>
        public RenderMode Mode = RenderMode.Gray;

        /// <summary>单波段模式使用的波段号（1 起）。</summary>
        public int GrayBand = 1;

        /// <summary>RGB 模式使用的三个波段号（1 起），顺序 R,G,B。</summary>
        public int[] RgbBands = { 1, 2, 3 };

        /// <summary>单波段/伪彩色使用的色带。</summary>
        public ColorRamp Ramp = ColorRamp.Gray;
        /// <summary>是否反相使用色带。</summary>
        public bool InvertRamp;

        // 默认采用 2%~98% 累计裁剪（遥感显示惯例，能压掉极值与无效区）
        public StretchConfig GrayStretch = NewDefaultStretch();
        public StretchConfig RStretch = NewDefaultStretch();
        public StretchConfig GStretch = NewDefaultStretch();
        public StretchConfig BStretch = NewDefaultStretch();

        private static StretchConfig NewDefaultStretch()
        {
            return new StretchConfig { Mode = StretchMode.PercentClip, PercentLow = 2, PercentHigh = 2 };
        }

        /// <summary>Gamma 值（0.1~4，>1 变亮）。</summary>
        public double Gamma = 1.0;
        /// <summary>亮度偏移（-1~1）。</summary>
        public double Brightness;
        /// <summary>对比度倍数（0.2~3）。</summary>
        public double Contrast = 1.0;
        /// <summary>图层不透明度（0~1）。</summary>
        public double Opacity = 1.0;
        /// <summary>与下层图层的混合模式。</summary>
        public BlendMode Blend = BlendMode.Normal;
        /// <summary>读取像素时的重采样算法。</summary>
        public ResampleMode Resample = ResampleMode.Bilinear;

        /// <summary>取指定通道的拉伸配置。</summary>
        /// <param name="channel">0=R，1=G，2=B；单波段模式下忽略。</param>
        /// <returns>对应的拉伸配置。</returns>
        public StretchConfig StretchFor(int channel)
        {
            if (Mode == RenderMode.Rgb)
            {
                if (channel == 0) return RStretch;
                if (channel == 1) return GStretch;
                return BStretch;
            }
            return GrayStretch;
        }



        /// <summary>生成用于图层面板/状态栏的一行符号化摘要。</summary>
        /// <returns>简短描述文本。</returns>
        public string Describe()
        {
            if (Mode == RenderMode.Palette) return Lang.T("调色板");
            if (Mode == RenderMode.Rgb)
                return string.Format("RGB {0}/{1}/{2}", RgbBands[0], RgbBands[1], RgbBands[2]);
            return string.Format(Lang.T("波段 {0} · {1} · {2}"), GrayBand, Ramp.Name, GrayStretch.Mode);
        }
    }
}
