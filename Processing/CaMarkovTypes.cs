using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
    /// <summary>一个类别的面积统计行。</summary>
    public sealed class CaAreaRow
    {
        /// <summary>类别值。</summary>
        public int Value;
        /// <summary>类别名称。</summary>
        public string Name;
        /// <summary>基期面积（km² 或像元数）。</summary>
        public double BaseArea;
        /// <summary>末期实测面积。</summary>
        public double EndArea;
        /// <summary>预测面积。</summary>
        public double PredArea;
        /// <summary>预测相对基期的变化率（%）。</summary>
        public double ChangePercent;
        /// <summary>基期像元数。</summary>
        public int BasePixels;
        /// <summary>末期像元数。</summary>
        public int EndPixels;
        /// <summary>预测像元数。</summary>
        public int PredPixels;
    }

    /// <summary>精度评价结果。</summary>
    public sealed class CaAccuracy
    {
        /// <summary>总体精度。</summary>
        public double Oa;
        /// <summary>Cohen Kappa。</summary>
        public double Kappa;
        /// <summary>各类生产者精度。</summary>
        public double[] Pa = new double[0];
        /// <summary>各类用户精度。</summary>
        public double[] Ua = new double[0];
        /// <summary>各类 F1。</summary>
        public double[] F1 = new double[0];
        /// <summary>混淆矩阵（行=实测，列=预测）。</summary>
        public long[][] Confusion;
        /// <summary>参与评价的有效像元数。</summary>
        public int Total;
    }

    /// <summary>随机森林特征重要性。</summary>
    public sealed class CaFeatureImportance
    {
        /// <summary>特征（驱动因子）名。</summary>
        public string Name;
        /// <summary>重要性（0~1，和为 1）。</summary>
        public double Importance;
    }

    /// <summary>一次 CA-Markov 运行的完整结果（由引擎的 metrics.json 解析而来）。</summary>
    public sealed class CaMarkovResult
    {
        /// <summary>预测栅格路径（分析/训练模式为空）。</summary>
        public string RasterPath;
        /// <summary>报告文本路径。</summary>
        public string ReportPath;
        /// <summary>指标 JSON 路径。</summary>
        public string MetricsPath;
        /// <summary>模型文件路径（如生成）。</summary>
        public string ModelPath;
        /// <summary>引擎模式。</summary>
        public string Mode;

        /// <summary>各类面积统计。</summary>
        public List<CaAreaRow> Classes = new List<CaAreaRow>();
        /// <summary>转移计数矩阵。</summary>
        public double[][] Counts;
        /// <summary>转移概率矩阵。</summary>
        public double[][] Probability;
        /// <summary>转移预算矩阵。</summary>
        public double[][] Budget;

        /// <summary>精度评价；未做时为 null。</summary>
        public CaAccuracy Accuracy;
        /// <summary>未变化基线 OA（把基期直接当预测）。</summary>
        public double? BaselineOa;
        /// <summary>精度评价方案说明。</summary>
        public string ValidationNote;
        /// <summary>随机森林训练摘要。</summary>
        public string RfSummary;
        /// <summary>特征重要性（按重要性降序）。</summary>
        public List<CaFeatureImportance> FeatureImportance = new List<CaFeatureImportance>();
        /// <summary>警告列表。</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>图表路径：键为 transition/area/confusion/importance/compare/hotspot。</summary>
        public Dictionary<string, string> Figures = new Dictionary<string, string>();
        /// <summary>预测步数。</summary>
        public int Steps;
        /// <summary>邻域窗口边长。</summary>
        public int NeighborhoodSize;
        /// <summary>像元面积（km²）；地理坐标系时为 0。</summary>
        public double PixelAreaKm2;
        /// <summary>引擎耗时（秒）。</summary>
        public double ElapsedSeconds;

        /// <summary>类别数。</summary>
        public int ClassCount { get { return Classes.Count; } }

        /// <summary>类别名称数组（索引即类别值）。</summary>
        public string[] Names
        {
            get
            {
                var names = new string[Classes.Count];
                for (int i = 0; i < Classes.Count; i++)
                    names[i] = i < Classes.Count ? Classes[i].Name : Lang.T("类别 ") + i;
                return names;
            }
        }

        /// <summary>指定键的图表路径；不存在时返回 null。</summary>
        public string Figure(string key)
        {
            string path;
            return Figures.TryGetValue(key, out path) ? path : null;
        }

        /// <summary>报告里没有精度时给出可读说明。</summary>
        public string AccuracyText
        {
            get
            {
                if (Accuracy == null) return Lang.T("未做精度评价");
                string text = string.Format("OA {0:P2} · Kappa {1:F4}", Accuracy.Oa, Accuracy.Kappa);
                if (BaselineOa.HasValue) text += string.Format(Lang.T(" · 未变化基线 OA {0:P2}"), BaselineOa.Value);
                return text;
            }
        }

        // ---------------------------------------------------------------- 解析

        /// <summary>从 metrics.json 文本解析结果。</summary>
        public static CaMarkovResult Parse(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.Deserialize<Dictionary<string, object>>(json);
            var r = new CaMarkovResult();
            if (root == null) return r;

            r.Mode = Str(root, "mode");
            r.ValidationNote = Str(root, "validation_note");
            r.RfSummary = Str(root, "rf_summary");
            r.Steps = Int(root, "steps");
            r.NeighborhoodSize = Int(root, "neighborhood_size");
            r.PixelAreaKm2 = Dbl(root, "pixel_area_km2");

            foreach (var item in Arr(root, "classes"))
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                r.Classes.Add(new CaAreaRow
                {
                    Value = Int(d, "value"),
                    Name = Str(d, "name"),
                    BaseArea = Dbl(d, "base"),
                    EndArea = Dbl(d, "end"),
                    PredArea = Dbl(d, "pred"),
                    ChangePercent = Dbl(d, "change_pct"),
                    BasePixels = Int(d, "base_px"),
                    EndPixels = Int(d, "end_px"),
                    PredPixels = Int(d, "pred_px"),
                });
            }

            r.Counts = Matrix(root, "matrix_counts");
            r.Probability = Matrix(root, "matrix_probability");
            r.Budget = Matrix(root, "matrix_budget");

            var acc = Get(root, "accuracy") as Dictionary<string, object>;
            if (acc != null)
            {
                r.Accuracy = new CaAccuracy
                {
                    Oa = Dbl(acc, "oa"),
                    Kappa = Dbl(acc, "kappa"),
                    Pa = Vector(acc, "pa"),
                    Ua = Vector(acc, "ua"),
                    F1 = Vector(acc, "f1"),
                    Confusion = LongMatrix(acc, "confusion"),
                    Total = Int(acc, "total"),
                };
            }
            if (root.ContainsKey("baseline_oa") && root["baseline_oa"] != null)
                r.BaselineOa = Dbl(root, "baseline_oa");

            foreach (var item in Arr(root, "feature_importance"))
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                r.FeatureImportance.Add(new CaFeatureImportance
                {
                    Name = Str(d, "name"),
                    Importance = Dbl(d, "importance"),
                });
            }

            foreach (var w in StrList(root, "warnings")) r.Warnings.Add(w);

            var figs = Get(root, "figures") as Dictionary<string, object>;
            if (figs != null)
            {
                foreach (var kv in figs)
                {
                    string path = kv.Value as string;
                    if (!string.IsNullOrEmpty(path)) r.Figures[kv.Key] = path;
                }
            }

            var outs = Get(root, "outputs") as Dictionary<string, object>;
            if (outs != null)
            {
                r.RasterPath = Str(outs, "raster");
                r.ReportPath = Str(outs, "report");
                r.ModelPath = Str(outs, "model");
            }

            var timing = Get(root, "timing") as Dictionary<string, object>;
            if (timing != null) r.ElapsedSeconds = Dbl(timing, "total_s");

            return r;
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static double Dbl(Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            if (v == null) return 0;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static int Int(Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            if (v == null) return 0;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static IEnumerable<object> Arr(Dictionary<string, object> d, string key)
        {
            return AsList(Get(d, key));
        }

        /// <summary>
        /// 把 JavaScriptSerializer 反序列化出来的“数组”统一成对象列表。
        /// 不同路径下它可能给出 object[] 或 ArrayList，这里都接受（字符串按空列表处理）。
        /// </summary>
        internal static List<object> AsList(object value)
        {
            var list = new List<object>();
            if (value == null || value is string) return list;
            var items = value as System.Collections.IEnumerable;
            if (items == null) return list;
            foreach (var item in items) list.Add(item);
            return list;
        }

        private static IEnumerable<string> StrList(Dictionary<string, object> d, string key)
        {
            foreach (var item in Arr(d, key))
                if (item != null) yield return Convert.ToString(item, CultureInfo.InvariantCulture);
        }

        private static double[] Vector(Dictionary<string, object> d, string key)
        {
            var arr = Arr(d, key);
            var list = new List<double>();
            foreach (var item in arr)
            {
                try { list.Add(Convert.ToDouble(item, CultureInfo.InvariantCulture)); }
                catch { list.Add(0); }
            }
            return list.ToArray();
        }

        private static double[][] Matrix(Dictionary<string, object> d, string key)
        {
            var rows = new List<double[]>();
            foreach (var row in Arr(d, key))
            {
                var inner = AsList(row);
                if (inner.Count == 0) continue;
                var cells = new double[inner.Count];
                for (int i = 0; i < inner.Count; i++)
                {
                    try { cells[i] = Convert.ToDouble(inner[i], CultureInfo.InvariantCulture); }
                    catch { cells[i] = 0; }
                }
                rows.Add(cells);
            }
            return rows.ToArray();
        }

        private static long[][] LongMatrix(Dictionary<string, object> d, string key)
        {
            var rows = new List<long[]>();
            foreach (var row in Arr(d, key))
            {
                var inner = AsList(row);
                if (inner.Count == 0) continue;
                var cells = new long[inner.Count];
                for (int i = 0; i < inner.Count; i++)
                {
                    try { cells[i] = Convert.ToInt64(inner[i], CultureInfo.InvariantCulture); }
                    catch { cells[i] = 0; }
                }
                rows.Add(cells);
            }
            return rows.ToArray();
        }
    }
}
