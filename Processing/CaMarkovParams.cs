using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
    /// <summary>引擎运行模式。</summary>
    public enum CaPredictionMode
    {
        /// <summary>只统计转移矩阵与面积，用于界面上的「先分析」。</summary>
        Analyze,
        /// <summary>训练转移潜力随机森林并保存模型。</summary>
        Train,
        /// <summary>完整预测链路。</summary>
        Predict
    }

    /// <summary>适宜性图集来源。</summary>
    public enum CaSuitabilityMode
    {
        /// <summary>随机森林转移潜力（与参考实现一致）。</summary>
        RandomForest,
        /// <summary>多因子加权（MCE 式）。</summary>
        FactorWeighted,
        /// <summary>用户提供的外部适宜性图集。</summary>
        ExternalAtlas,
        /// <summary>不使用适宜性，只靠邻域聚集。</summary>
        None
    }

    /// <summary>转移矩阵取哪些区间。</summary>
    public enum CaTransitionSource
    {
        /// <summary>最近一个区间（参考实现的 hybrid）。</summary>
        LastInterval,
        /// <summary>最近两个区间计数相加后行归一化（参考实现的 multi，默认）。</summary>
        LastTwoAverage,
        /// <summary>全部区间累计。</summary>
        AllPeriods
    }

    /// <summary>因子标准化方式。</summary>
    public enum CaNormalizeMethod
    {
        /// <summary>分位数拉伸（默认 2%~98%）。</summary>
        Quantile,
        /// <summary>最小-最大拉伸。</summary>
        MinMax
    }

    /// <summary>外部适宜性图集的波段与类别对应方式。</summary>
    public enum CaBandMode
    {
        /// <summary>波段序号 ↔ 按类别值升序的第 k 个有效类。</summary>
        Order,
        /// <summary>波段号等于类别值。</summary>
        ClassValue
    }

    /// <summary>一期土地利用数据。</summary>
    public sealed class CaPeriod
    {
        /// <summary>栅格路径。</summary>
        public string Path;
        /// <summary>波段号（1 基）。</summary>
        public int Band = 1;
        /// <summary>年份标注，仅用于界面显示与日志。</summary>
        public int Year;

        /// <summary>列表显示文本。</summary>
        public override string ToString()
        {
            string name = string.IsNullOrEmpty(Path) ? Lang.T("（未选择）") : System.IO.Path.GetFileName(Path);
            return Year > 0 ? string.Format("{0} · {1}", Year, name) : name;
        }
    }

    /// <summary>一个驱动因子（多因子加权模式）。</summary>
    public sealed class CaFactor
    {
        /// <summary>栅格路径。</summary>
        public string Path;
        /// <summary>波段号（1 基）。</summary>
        public int Band = 1;
        /// <summary>权重，最终会归一化。</summary>
        public double Weight = 1.0;
        /// <summary>是否为负向因子（值越大越不适宜）。</summary>
        public bool Negative;

        /// <summary>列表显示文本。</summary>
        public override string ToString()
        {
            string name = string.IsNullOrEmpty(Path) ? Lang.T("（未选择）") : System.IO.Path.GetFileName(Path);
            return string.Format(Lang.T("{0} · 权重 {1:0.##} · {2}"), name, Weight, Negative ? Lang.T("负向") : Lang.T("正向"));
        }
    }

    /// <summary>随机森林的一个特征（驱动因子）。顺序即模型特征顺序。</summary>
    public sealed class CaDriver
    {
        /// <summary>特征名，写入模型并用于校验顺序。</summary>
        public string Name;
        /// <summary>栅格路径。</summary>
        public string Path;
        /// <summary>波段号（1 基）。</summary>
        public int Band = 1;

        /// <summary>列表显示文本。</summary>
        public override string ToString()
        {
            string name = string.IsNullOrEmpty(Path) ? Lang.T("（未选择）") : System.IO.Path.GetFileName(Path);
            return string.IsNullOrEmpty(Name) ? name : Name + " · " + name;
        }
    }

    /// <summary>CA-Markov 的全部参数。所有字段都由对话框显式暴露给用户。</summary>
    public sealed class CaMarkovParams
    {
        /// <summary>运行模式。</summary>
        public CaPredictionMode Mode = CaPredictionMode.Predict;
        /// <summary>时期列表（旧 → 新，至少两期）。</summary>
        public List<CaPeriod> Periods = new List<CaPeriod>();
        /// <summary>无效值。</summary>
        public double NoData = 0;
        /// <summary>类别数；0 表示由引擎自动推断。</summary>
        public int ClassCount;
        /// <summary>类别名称（索引即类别值）。</summary>
        public string[] ClassNames = new string[0];

        /// <summary>转移矩阵来源。</summary>
        public CaTransitionSource TransitionSource = CaTransitionSource.LastTwoAverage;
        /// <summary>预测步数（每步 = 一个训练区间）。</summary>
        public int Steps = 1;
        /// <summary>邻域窗口边长（3/5/7）。</summary>
        public int NeighborhoodSize = 5;
        /// <summary>每步是否重算邻域。</summary>
        public bool RecomputeNeighborhood = true;

        /// <summary>适宜性来源。</summary>
        public CaSuitabilityMode Suitability = CaSuitabilityMode.RandomForest;
        /// <summary>因子加权模式的因子列表。</summary>
        public List<CaFactor> Factors = new List<CaFactor>();
        /// <summary>因子标准化方式。</summary>
        public CaNormalizeMethod NormalizeMethod = CaNormalizeMethod.Quantile;
        /// <summary>分位数下界（%）。</summary>
        public double PercentileLow = 2;
        /// <summary>分位数上界（%）。</summary>
        public double PercentileHigh = 98;

        /// <summary>限制性条件图路径；空表示无限制。</summary>
        public string ConstraintPath;
        /// <summary>限制图波段号。</summary>
        public int ConstraintBand = 1;

        /// <summary>外部适宜性图集路径。</summary>
        public string ExternalPath;
        /// <summary>图集起始波段。</summary>
        public int ExternalBand = 1;
        /// <summary>图集波段与类别的对应方式。</summary>
        public CaBandMode ExternalBandMode = CaBandMode.Order;

        /// <summary>随机森林模型文件（.npz）；为空或不存在时当场训练。</summary>
        public string ModelPath;
        /// <summary>即使模型存在也强制重训。</summary>
        public bool ForceTrain;
        /// <summary>训练用驱动因子。</summary>
        public List<CaDriver> TrainingDrivers = new List<CaDriver>();
        /// <summary>预测用驱动因子（可为空，表示与训练相同）。</summary>
        public List<CaDriver> PredictionDrivers = new List<CaDriver>();
        /// <summary>训练区间（时期下标对）；为空表示沿用转移矩阵的区间。</summary>
        public List<int[]> TrainingIntervals;
        /// <summary>树数。</summary>
        public int Trees = 200;
        /// <summary>最大深度（0 表示不限）。</summary>
        public int MaxDepth = 20;
        /// <summary>不变像元相对变化像元的采样倍数。</summary>
        public double Imbalance = 2.0;
        /// <summary>变化样本少于此数时不做平衡采样。</summary>
        public int MinSamples = 200;
        /// <summary>随机种子（保证可复现）。</summary>
        public int Seed = 42;
        /// <summary>训练后另存模型的路径（可空）。</summary>
        public string SaveModelPath;

        /// <summary>适宜性权重。</summary>
        public double SuitabilityWeight = 0.6;
        /// <summary>邻域权重。</summary>
        public double NeighborhoodWeight = 0.4;
        /// <summary>是否用转移概率对共享适宜性图做调制。</summary>
        public bool ModulateByProbability = true;

        /// <summary>是否做精度评价。</summary>
        public bool Validate = true;
        /// <summary>精度评价是否使用同期 in-sample（会偏乐观）。</summary>
        public bool ValidationSamePeriod;

        /// <summary>是否输出报告。</summary>
        public bool WriteReport = true;
        /// <summary>是否输出三联对比图。</summary>
        public bool WriteCompareImage = true;
        /// <summary>是否输出变化热点图。</summary>
        public bool WriteHotspotImage = true;

        /// <summary>预测栅格输出路径。</summary>
        public string OutputRaster;
        /// <summary>报告输出路径。</summary>
        public string OutputReport;

        /// <summary>随机森林分块推理的像元数上限。</summary>
        public int Chunk = 200000;

        /// <summary>复制一份参数（用于「先分析」时切换模式而不影响界面状态）。</summary>
        public CaMarkovParams Clone()
        {
            var copy = (CaMarkovParams)MemberwiseClone();
            copy.Periods = new List<CaPeriod>();
            foreach (var p in Periods) copy.Periods.Add(new CaPeriod { Path = p.Path, Band = p.Band, Year = p.Year });
            copy.Factors = new List<CaFactor>();
            foreach (var f in Factors)
                copy.Factors.Add(new CaFactor { Path = f.Path, Band = f.Band, Weight = f.Weight, Negative = f.Negative });
            copy.TrainingDrivers = new List<CaDriver>();
            foreach (var d in TrainingDrivers) copy.TrainingDrivers.Add(new CaDriver { Name = d.Name, Path = d.Path, Band = d.Band });
            copy.PredictionDrivers = new List<CaDriver>();
            foreach (var d in PredictionDrivers) copy.PredictionDrivers.Add(new CaDriver { Name = d.Name, Path = d.Path, Band = d.Band });
            copy.ClassNames = (string[])ClassNames.Clone();
            return copy;
        }

        /// <summary>构造发给引擎的 job 字典。</summary>
        /// <param name="outputs">输出路径集合。</param>
        public Dictionary<string, object> ToJob(Dictionary<string, object> outputs)
        {
            var job = new Dictionary<string, object>
            {
                { "mode", ModeText(Mode) },
                { "periods", PeriodsJson() },
                { "nodata", NoData },
                { "class_count", ClassCount },
                { "class_names", ClassNames ?? new string[0] },
                { "transition_source", TransitionSourceText(TransitionSource) },
                { "steps", Steps },
                { "neighborhood_size", NeighborhoodSize },
                { "recompute_neighborhood", RecomputeNeighborhood },
                { "suitability_mode", SuitabilityText(Suitability) },
                { "normalize", new Dictionary<string, object>
                    {
                        { "method", NormalizeMethod == CaNormalizeMethod.MinMax ? "minmax" : "quantile" },
                        { "low", PercentileLow },
                        { "high", PercentileHigh }
                    }
                },
                { "factors", FactorsJson() },
                { "weights", new Dictionary<string, object>
                    {
                        { "suitability", SuitabilityWeight },
                        { "neighborhood", NeighborhoodWeight }
                    }
                },
                { "modulate_by_probability", ModulateByProbability },
                { "validation", new Dictionary<string, object>
                    {
                        { "enabled", Validate },
                        { "same_period", ValidationSamePeriod }
                    }
                },
                { "outputs", outputs },
                { "write_compare_image", WriteCompareImage },
                { "write_hotspot_image", WriteHotspotImage },
                { "seed", Seed },
                { "chunk", Chunk },
                // 引擎据此决定报告与图表用中文还是英文
                { "lang", SharpGIS.Services.Lang.Current }
            };

            var rf = new Dictionary<string, object>
            {
                { "model", string.IsNullOrWhiteSpace(ModelPath) ? null : ModelPath },
                { "force_train", ForceTrain },
                { "train", new Dictionary<string, object>
                    {
                        { "drivers", DriversJson(TrainingDrivers) },
                        { "predict_drivers", PredictionDrivers.Count > 0 ? DriversJson(PredictionDrivers) : null },
                        { "n_estimators", Trees },
                        { "max_depth", MaxDepth },
                        { "imbalance", Imbalance },
                        { "min_samples", MinSamples },
                        { "seed", Seed },
                        { "save_model", string.IsNullOrWhiteSpace(SaveModelPath) ? null : SaveModelPath },
                        { "intervals", IntervalsJson() }
                    }
                }
            };
            job["rf"] = rf;

            if (!string.IsNullOrWhiteSpace(ConstraintPath))
                job["constraint"] = new Dictionary<string, object>
                {
                    { "path", ConstraintPath },
                    { "band", ConstraintBand }
                };
            if (!string.IsNullOrWhiteSpace(ExternalPath))
                job["external"] = new Dictionary<string, object>
                {
                    { "path", ExternalPath },
                    { "band", ExternalBand },
                    { "band_mode", ExternalBandMode == CaBandMode.ClassValue ? "class_value" : "order" }
                };
            return job;
        }

        private object PeriodsJson()
        {
            var list = new List<object>();
            foreach (var p in Periods)
                list.Add(new Dictionary<string, object>
                {
                    { "path", p.Path }, { "band", p.Band }, { "year", p.Year }
                });
            return list;
        }

        private object FactorsJson()
        {
            var list = new List<object>();
            foreach (var f in Factors)
                list.Add(new Dictionary<string, object>
                {
                    { "path", f.Path }, { "band", f.Band },
                    { "weight", f.Weight }, { "direction", f.Negative ? "-" : "+" }
                });
            return list;
        }

        private static object DriversJson(List<CaDriver> drivers)
        {
            var list = new List<object>();
            foreach (var d in drivers)
                list.Add(new Dictionary<string, object>
                {
                    { "name", d.Name }, { "path", d.Path }, { "band", d.Band }
                });
            return list;
        }

        private object IntervalsJson()
        {
            if (TrainingIntervals == null || TrainingIntervals.Count == 0) return null;
            var list = new List<object>();
            foreach (var pair in TrainingIntervals)
                if (pair != null && pair.Length >= 2)
                    list.Add(new List<object> { pair[0], pair[1] });
            return list;
        }

        /// <summary>序列化为 JSON 文本（UTF-8 安全，中文按 \uXXXX 转义）。</summary>
        public static string SerializeJson(Dictionary<string, object> job)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return serializer.Serialize(job);
        }

        /// <summary>模式枚举 → 引擎字符串。</summary>
        public static string ModeText(CaPredictionMode mode)
        {
            switch (mode)
            {
                case CaPredictionMode.Analyze: return "analyze";
                case CaPredictionMode.Train: return "train";
                default: return "predict";
            }
        }

        /// <summary>适宜性模式 → 引擎字符串。</summary>
        public static string SuitabilityText(CaSuitabilityMode mode)
        {
            switch (mode)
            {
                case CaSuitabilityMode.FactorWeighted: return "factor";
                case CaSuitabilityMode.ExternalAtlas: return "external";
                case CaSuitabilityMode.None: return "none";
                default: return "rf";
            }
        }

        /// <summary>转移矩阵来源 → 引擎字符串。</summary>
        public static string TransitionSourceText(CaTransitionSource source)
        {
            switch (source)
            {
                case CaTransitionSource.LastInterval: return "last_interval";
                case CaTransitionSource.AllPeriods: return "all_periods";
                default: return "last_two_average";
            }
        }

        /// <summary>
        /// 把所有路径转成绝对路径。子进程的工作目录是 Engine 目录，相对路径会解析错位。
        /// </summary>
        public void MakePathsAbsolute()
        {
            foreach (var p in Periods) p.Path = Full(p.Path);
            foreach (var f in Factors) f.Path = Full(f.Path);
            foreach (var d in TrainingDrivers) d.Path = Full(d.Path);
            foreach (var d in PredictionDrivers) d.Path = Full(d.Path);
            ConstraintPath = Full(ConstraintPath);
            ExternalPath = Full(ExternalPath);
            ModelPath = Full(ModelPath);
            SaveModelPath = Full(SaveModelPath);
            OutputRaster = Full(OutputRaster);
            OutputReport = Full(OutputReport);
        }

        private static string Full(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            try { return System.IO.Path.GetFullPath(path); }
            catch { return path; }
        }
    }
}
