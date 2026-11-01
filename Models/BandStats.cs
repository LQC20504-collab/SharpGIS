using System;
using OSGeo.GDAL;

namespace SharpGIS.Models
{
        /// <summary>单波段统计信息（抽样计算并缓存）。</summary>
    public sealed class BandStats
    {
        /// <summary>直方图分箱数。</summary>
        public const int BinCount = 256;

        /// <summary>统计是否可用；false 时其余字段为兜底值。</summary>
        public bool Valid;
        /// <summary>参与统计的有效样本数。</summary>
        public long SampleCount;
        /// <summary>被剔除的无效样本数（alpha=0 或 NaN）。</summary>
        public long NoDataCount;

        /// <summary>样本最小值。</summary>
        public double Min;
        /// <summary>样本最大值。</summary>
        public double Max;
        /// <summary>样本均值。</summary>
        public double Mean;
        /// <summary>样本标准差。</summary>
        public double StdDev;

        /// <summary>1% 分位数。</summary>
        public double QuantileMin;
        /// <summary>99% 分位数。</summary>
        public double QuantileMax;

        /// <summary>数据类型的名义下限（“无拉伸”模式使用）。</summary>
        public double TypeMin;
        /// <summary>数据类型的名义上限（“无拉伸”模式使用）。</summary>
        public double TypeMax = 255;

        /// <summary>直方图覆盖的下限。</summary>
        public double HistMin;
        /// <summary>直方图覆盖的上限。</summary>
        public double HistMax;
        /// <summary>256 箱直方图计数。</summary>
        public int[] Histogram = new int[BinCount];
        /// <summary>归一化累积分布，供直方图均衡使用。</summary>
        public double[] Cdf = new double[BinCount];
        /// <summary>直方图计数总和。</summary>
        public long HistogramTotal;

        /// <summary>按数据类型创建统计对象并填入名义量程。</summary>
        /// <param name="type">GDAL 数据类型。</param>
        /// <returns>尚未计算统计量的实例。</returns>
        public static BandStats Create(DataType type)
        {
            var s = new BandStats();
            switch (type)
            {
                case DataType.GDT_Byte: s.TypeMin = 0; s.TypeMax = 255; break;
                case DataType.GDT_UInt16: s.TypeMin = 0; s.TypeMax = 65535; break;
                case DataType.GDT_Int16: s.TypeMin = -32768; s.TypeMax = 32767; break;
                case DataType.GDT_UInt32: s.TypeMin = 0; s.TypeMax = 4294967295.0; break;
                case DataType.GDT_Int32: s.TypeMin = -2147483648.0; s.TypeMax = 2147483647.0; break;
                default: s.TypeMin = 0; s.TypeMax = 1; break; // 浮点类型等由实际统计决定
            }
            return s;
        }

        /// <summary>抽样边长上限：无论影像多大，统计最多读取 640×640 个样本。</summary>
        private const int MaxSamples = 640;

        /// <summary>抽样计算统计量，并按 alpha 掩膜剔除无效像素。</summary>
        /// <param name="band">目标波段。</param>
        /// <param name="alphaBand">alpha 掩膜波段，可为 null。</param>
        /// <param name="type">波段数据类型。</param>
        /// <returns>统计结果。</returns>
        /// <param name="hasNoData">是否声明了 NoData 值。</param>
        /// <param name="noData">NoData 取值；<paramref name="hasNoData"/> 为 false 时忽略。</param>
        public static BandStats Compute(Band band, Band alphaBand, DataType type, bool hasNoData, double noData)
        {
            var s = Create(type);
            if (band == null) return s;

            int w = band.XSize, h = band.YSize;
            if (w <= 0 || h <= 0) return s;

            int sw = Math.Min(w, MaxSamples);
            int sh = Math.Min(h, MaxSamples);
            int n = sw * sh;

            float[] values = null;
            byte[] mask = null;
            try
            {
                values = new float[n];
                band.ReadRaster(0, 0, w, h, values, sw, sh, 0, 0);

                if (alphaBand != null)
                {
                    mask = new byte[n];
                    alphaBand.ReadRaster(0, 0, w, h, mask, sw, sh, 0, 0);
                }

                double min = double.MaxValue, max = double.MinValue;
                double sum = 0, sum2 = 0;
                long count = 0;

                for (int i = 0; i < n; i++)
                {
                    if (mask != null && mask[i] < 8) continue;
                    float v = values[i];
                    if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                    if (hasNoData && Math.Abs(v - noData) < 1e-6) continue;
                    if (v < min) min = v;
                    if (v > max) max = v;
                    sum += v;
                    sum2 += (double)v * v;
                    count++;
                }

                if (count == 0)
                {
                    s.Valid = false;
                    s.Min = s.TypeMin; s.Max = s.TypeMax;
                    return s;
                }

                s.Min = min;
                s.Max = max;
                s.Mean = sum / count;
                double var = sum2 / count - s.Mean * s.Mean;
                s.StdDev = var > 0 ? Math.Sqrt(var) : 0;
                s.SampleCount = count;
                s.NoDataCount = n - count;
                s.Valid = true;

                // 直方图（基于同一批样本，保证百分比裁剪与显示一致）
                double lo = s.Min, hi = s.Max;
                if (!(hi > lo)) hi = lo + (Math.Abs(lo) > 1e-9 ? Math.Abs(lo) * 1e-6 : 1e-6);
                var hist = new int[BinCount];
                double binScale = BinCount / (hi - lo);
                for (int i = 0; i < n; i++)
                {
                    if (mask != null && mask[i] < 8) continue;
                    float v = values[i];
                    if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                    if (hasNoData && Math.Abs(v - noData) < 1e-6) continue;
                    int bin = (int)((v - lo) * binScale);
                    if (bin < 0) bin = 0; else if (bin >= BinCount) bin = BinCount - 1;
                    hist[bin]++;
                }

                s.Histogram = hist;
                s.HistMin = lo;
                s.HistMax = hi;
                s.HistogramTotal = count;

                var cdf = new double[BinCount];
                long acc = 0;
                for (int i = 0; i < BinCount; i++)
                {
                    acc += hist[i];
                    cdf[i] = acc / (double)count;
                }
                s.Cdf = cdf;

                s.QuantileMin = s.Percentile(1.0);
                s.QuantileMax = s.Percentile(99.0);
                if (!(s.QuantileMax > s.QuantileMin)) { s.QuantileMin = s.Min; s.QuantileMax = s.Max; }
                return s;
            }
            catch
            {
                s.Min = s.TypeMin; s.Max = s.TypeMax; s.Valid = false;
                return s;
            }
        }

        /// <summary>按直方图估算百分位。</summary>
        /// <param name="p">0~100 的百分位。</param>
        /// <returns>对应的像素值。</returns>
        public double Percentile(double p)
        {
            if (HistogramTotal <= 0 || Histogram == null)
            {
                return Min + (Max - Min) * (p / 100.0);
            }
            if (p <= 0) return HistMin;
            if (p >= 100) return HistMax;

            double target = HistogramTotal * p / 100.0;
            long acc = 0;
            for (int i = 0; i < BinCount; i++)
            {
                acc += Histogram[i];
                if (acc >= target)
                {
                    double binWidth = (HistMax - HistMin) / BinCount;
                    return HistMin + (i + 0.5) * binWidth;
                }
            }
            return HistMax;
        }

        /// <summary>构造直方图均衡用的 256 级映射表。</summary>
        /// <returns>长度为 256 的映射表。</returns>
        public byte[] BuildEqualizationTable()
        {
            var table = new byte[BinCount];
            if (Cdf == null || Cdf.Length < BinCount) return null;
            for (int i = 0; i < BinCount; i++)
            {
                double v = Cdf[i] * 255.0;
                table[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));
            }
            return table;
        }
    }
}
