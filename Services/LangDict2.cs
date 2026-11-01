using System.Collections.Generic;

namespace SharpGIS.Services
{
    /// <summary>英文译文（第 2 段：CA-Markov 对话框、结果窗口、导出/重投影对话框）。</summary>
    public static partial class Lang
    {
        private static void RegisterPart2(Dictionary<string, string> d)
        {
            d["土地利用变化预测（CA-Markov）"] = "Land-use Change Prediction (CA-Markov)";
            d["输入数据"] = "Input data";
            d["时期（旧 → 新，至少两期）"] = "Periods (old → new, at least two)";
            d["添加文件…"] = "Add files…";
            d["从已打开图层"] = "From open layers";
            d["上移"] = "Move up";
            d["下移"] = "Move down";
            d["移除"] = "Remove";
            d["无效值"] = "NoData value";
            d["类别数"] = "Class count";
            d["0 = 自动推断"] = "0 = detect automatically";
            d["转移矩阵来源"] = "Transition matrix source";
            d["最近一期（外推能力最弱）"] = "Last interval (weakest extrapolation)";
            d["最近两期平均（推荐）"] = "Average of last two intervals (recommended)";
            d["全部时期累计"] = "All intervals combined";
            d["波段"] = "Band";
            d["年份"] = "Year";
            d["先分析（只看矩阵，约 10 秒）"] = "Analyse first (matrix only, ~10 s)";
            d["分析不写任何文件，只把转移矩阵与面积统计画出来供调参。"] =
                "Analysis writes nothing; it only plots the transition matrix and area statistics.";
            d["年份只用于显示与推算预测年份；真正决定预测跨度的是「步数 × 区间年数」。"] =
                "The year is only used for display and for estimating the target year; the real span is steps × interval length.";

            d["类别"] = "Classes";
            d["类别值来自栅格本身；点「统计类别」按最后一期逐像元统计，"] =
                "Class values come from the raster itself. Click Count to tally the last period pixel by pixel;";
            d["表格只列出「数量大于 0 且不是无效值」的类别，右侧名称可双击直接改。"] =
                "only classes with a count above zero (and not NoData) are listed. Double-click a name to edit it.";
            d["统计类别"] = "Count classes";
            d["按 CLCD 命名"] = "Use CLCD names";
            d["恢复默认名"] = "Reset names";
            d["无效值不参与建模、也不会出现在上表；名称只影响报告与图例，不影响计算。"] =
                "NoData never takes part in modelling and is not listed; names affect reports and legends only.";

            d["转移与 CA"] = "Transition & CA";
            d["预测步数"] = "Forecast steps";
            d["每步 = 一个训练区间（例如 5 年）；步数大于 1 时会逐步重算预算与邻域。"] =
                "One step equals one training interval (e.g. 5 years); above 1, budgets and neighbourhood are recomputed each step.";
            d["邻域窗口"] = "Neighbourhood size";
            d["5 × 5（推荐）"] = "5 × 5 (recommended)";
            d["窗口越大，新增建设用地越容易连片；窗口越小越贴近转移概率的原始分布。"] =
                "Larger windows grow compact patches; smaller windows stay closer to the raw transition probabilities.";
            d["每个预测步都重算邻域（步数 > 1 时建议开启）"] =
                "Recompute the neighbourhood at every step (recommended when steps > 1)";
            d["适宜性权重"] = "Suitability weight";
            d["邻域权重"] = "Neighbourhood weight";
            d["用转移概率调制共享适宜性图（因子加权 / 外部图集模式下区分目标类）"] =
                "Modulate the shared suitability map by transition probability (separates target classes in MCE/atlas modes)";
            d["限制条件图"] = "Constraint raster";
            d["选择限制性条件图"] = "Select the constraint raster";
            d["值 > 0.5 的像元允许转变，其余像元锁定为原类别（可留空）。"] =
                "Pixels above 0.5 may change; the rest keep their class (may be left empty).";
            d["评分 = 适宜性权重 × 适宜性 + 邻域权重 × 邻域占比，两项都会先归一化；\r\n"] =
                "Score = suitability weight × suitability + neighbourhood weight × neighbourhood fraction (both normalised);\r\n";
            d["邻域占比是目标类在窗口内的像元比例，边界外按 0 计算。"] =
                "the fraction is the share of the target class inside the window; outside the raster counts as 0.";

            d["适宜性"] = "Suitability";
            d["适宜性来源"] = "Suitability source";
            d["随机森林转移潜力（推荐）"] = "Random-forest transition potential (recommended)";
            d["因子加权（MCE）"] = "Weighted factors (MCE)";
            d["外部适宜性图集"] = "External suitability atlas";
            d["不使用适宜性"] = "No suitability";
            d["不使用适宜性时只用邻域聚集，适合快速试算；预测的空间分布会明显比随机森林粗糙。"] =
                "Without suitability only the neighbourhood term acts; useful for quick trials but clearly coarser than random forest.";

            d["评价与输出"] = "Assessment & output";
            d["输出精度评价（OA / Kappa / PA / UA / F1 + 混淆矩阵）"] =
                "Accuracy assessment (OA / Kappa / PA / UA / F1 + confusion matrix)";
            d["改用「同期自检」（in-sample）"] = "Use in-sample self-check instead";
            d["精度评价有两种口径，区别只有在“末期有没有参与建模”："] =
                "There are two ways to assess accuracy; they differ only in whether the last period took part in training:";
            d["· 时间外推（默认，推荐）：把最后一期当成“未来”，只用更早的区间建模去预测它，\r\n"] =
                "· Temporal extrapolation (default): treat the last period as the future, train on earlier intervals only,\r\n";
            d["  再和真实的末期逐像元比对 —— 这样得到的 OA/Kappa 才代表“预测能力”。"] =
                "  predict it, then compare pixel by pixel with the truth — this OA/Kappa reflects prediction skill.";
            d["· 同期自检（勾选上面那个框）：末期自己参与了建模，再拿它去预测它自己，\r\n"] =
                "· In-sample (tick the box above): the last period helped train the model and is then predicted by it,\r\n";
            d["  相当于用答案考自己，OA/Kappa 必然偏高，只能说明拟合得好，不代表能预测未来。"] =
                "  i.e. marking your own homework: OA/Kappa is inflated and only shows fit, not forecast skill.";
            d["只有两期数据时无法做外推，程序会自动退回同期自检，并在报告里注明。"] =
                "With only two periods extrapolation is impossible; the program falls back to in-sample and says so in the report.";
            d["输出文本报告"] = "Write text report";
            d["输出三联对比图"] = "Write three-panel comparison";
            d["输出变化热点图"] = "Write change hotspot map";
            d["完成后打开结果图表窗口"] = "Open the result window when finished";
            d["报告、图表与预测栅格放在同一目录（<输出名>_report.txt、<输出名>_figs\\）。"] =
                "Report, figures and the prediction raster share one folder (<name>_report.txt, <name>_figs\\).";

            d["选择一期土地利用栅格"] = "Select a land-use raster";
            d["当前没有已打开的图层。"] = "No layer is currently open.";
            d["提示"] = "Notice";
            d["年份未识别"] = "year not recognised";
            d["  ·  波段 "] = "  ·  band ";
            d["还需要 "] = "Still need ";
            d[" 期数据。转移矩阵由相邻两期的变化统计得到，"] =
                " more period(s). The transition matrix is counted from adjacent periods,";
            d["顺序必须由旧到新。"] = " so order them from oldest to newest.";
            d["最近一期"] = "last interval";
            d["最近两期平均"] = "average of last two intervals";
            d["最近两期平均（只有 1 个区间，自动退化为最近一期）"] =
                "average of last two intervals (only one interval, so it falls back to the last interval)";
            d["全部 "] = "all ";
            d[" 个区间累计"] = " intervals combined";
            d["；末期 {0} + {1} 年 × {2} 步 ≈ {3} 年"] = "; last period {0} + {1} yr × {2} step(s) ≈ {3}";
            d["；年份未识别时只能按「步」理解跨度（可在上方填写年份）"] =
                "; without a year the span can only be read in steps (fill the year above)";
            d["共 {0} 期 · {1} 个区间 · 转移矩阵使用：{2}。\r\n预测从最后一期出发，向后再推 {3} 步{4}。"] =
                "{0} periods · {1} interval(s) · transition matrix from: {2}.\r\nForecast starts at the last period and runs {3} step(s) ahead{4}.";

            d["模型文件"] = "Model file";
            d["留空表示每次重新训练"] = "Leave empty to retrain every run";
            d["训练驱动因子（顺序即特征顺序，建议 5~8 个）"] =
                "Training drivers (order = feature order, 5–8 recommended)";
            d["添加因子…"] = "Add driver…";
            d["树数"] = "Trees";
            d["深度"] = "Depth";
            d["采样比"] = "Sampling ratio";
            d["种子"] = "Seed";
            d["保存模型"] = "Save model";
            d["选择模型保存位置"] = "Choose where to save the model";
            d["强制重训"] = "Force retrain";
            d["训练并保存模型"] = "Train and save model";
            d["训练只使用指定时期的样本；模型训练好后可直接复用，避免每次预测都重跑。"] =
                "Training uses the selected periods only; once saved, the model can be reused instead of retraining.";
            d["因子列表（每个因子的权重与方向可调）"] = "Factors (weight and direction per factor)";
            d["选中因子权重"] = "Weight of selected factor";
            d["负向因子（值越大越不适宜）"] = "Negative factor (higher = less suitable)";
            d["标准化"] = "Normalisation";
            d["分位数拉伸"] = "Quantile stretch";
            d["最小-最大"] = "Min-Max";
            d["下限 %"] = "Lower %";
            d["上限 %"] = "Upper %";
            d["适宜性 = Σ(权重 × 标准化后的因子值) / Σ权重；NoData 像元用该因子的中位数填充。"] =
                "Suitability = Σ(weight × normalised factor) / Σweight; NoData pixels use that factor's median.";
            d["因子栅格必须与土地利用栅格同网格（宽高与地理变换一致）。"] =
                "Factor rasters must share the land-use grid (same size and geotransform).";
            d["适宜性图集"] = "Suitability atlas";
            d["选择适宜性图集（多波段）"] = "Select the suitability atlas (multi-band)";
            d["起始波段"] = "First band";
            d["波段对应"] = "Band mapping";
            d["按类别值升序对应"] = "Match classes in ascending order";
            d["波段号 = 类别值"] = "Band index = class value";
            d["每个波段是一张“某目标类的适宜度”图（0~1 或任意量纲，程序会按有效范围归一化）。\r\n"] =
                "Each band is a suitability map for one target class (any scale; normalised by its valid range).\r\n";
            d["例如 9 类土地利用 + 8 个有效类别 → 需要 8 个波段（无数据类可跳过）。"] =
                "e.g. 9 land-use classes with 8 valid ones needs 8 bands (classes without data may be skipped).";
            d["两个权重不能同时为 0"] = "Both weights cannot be zero";
            d["归一化后：适宜性 {0:0.000} · 邻域 {1:0.000}"] =
                "Normalised: suitability {0:0.000} · neighbourhood {1:0.000}";
            d["选择驱动因子栅格"] = "Select driver rasters";
            d["选择驱动因子栅格（随机森林特征）"] = "Select driver rasters (random-forest features)";
            d["训练转移潜力模型"] = "Training the transition-potential model";
            d["模型已保存："] = "Model saved: ";
            d["转移矩阵分析"] = "Transition matrix analysis";
            d["转移分析（不写文件）"] = "Transition analysis (nothing written)";
            d["特征重要性："] = "Feature importance: ";
            d["选择随机森林模型（.npz）"] = "Select a random-forest model (.npz)";
            d["SharpGIS CA-Markov 模型|*.npz|所有文件|*.*"] =
                "SharpGIS CA-Markov model|*.npz|All files|*.*";
            d["至少需要两期土地利用数据（当前 "] = "At least two land-use periods are required (currently ";
            d[" 期）。"] = ").";
            d["文件不存在："] = "File not found: ";
            d["随机森林至少需要一个驱动因子（如高程、坡度、距道路距离、人口密度）。"] =
                "Random forest needs at least one driver (elevation, slope, distance to roads, population…).";
            d["驱动因子文件不存在："] = "Driver file not found: ";
            d["因子加权模式至少需要一个驱动因子。"] = "Weighted-factor mode needs at least one factor.";
            d["请指定外部适宜性图集文件（多波段栅格）。"] =
                "Please choose the external suitability atlas (multi-band raster).";
            d["限制性条件图不存在："] = "Constraint raster not found: ";
            d["适宜性权重与邻域权重不能同时为 0。"] = "Suitability and neighbourhood weights cannot both be 0.";
            d["无法打开第 1 期：\r\n"] = "Cannot open the first period:\r\n";
            d["第 {0} 期与第 1 期网格不一致：{1}\r\n"] = "Period {0} does not match the grid of period 1: {1}\r\n";
            d["请先用「重投影 / 重采样」对齐到同一网格。"] =
                "Align them with Reproject / Resample first.";
            d["因子"] = "Factor";
            d["驱动因子"] = "Driver";
            d["{0} {1} 与土地利用栅格网格不一致：{2}"] = "{0} {1} does not match the land-use grid: {2}";
            d["校验网格时出错：\r\n"] = "Grid check failed:\r\n";
            d["请先添加至少一期数据。"] = "Add at least one period first.";
            d["统计类别失败：\r\n"] = "Counting classes failed:\r\n";
            d["类别值"] = "Class value";
            d["像元数（最后一期）"] = "Pixels (last period)";
            d["类别名称（可双击修改）"] = "Class name (double-click to edit)";
            d["尚未统计"] = "not counted yet";
            d["统计自 {0} · 有效像元 {1:#,0} / 总计 {2:#,0}"] =
                "Counted from {0} · valid {1:#,0} / total {2:#,0}";
            d["预计耗时：约 {0}（{1} 棵树、{2} 步{3}）。树数越大越接近参考配置，也越慢。"] =
                "Estimated time: about {0} ({1} trees, {2} step(s){3}). More trees approach the reference setup but run slower.";
            d["，含一次外推精度评价（相当于再预测一遍）"] = ", including an extrapolation check (a second prediction pass)";
            d["预计耗时：约 {0}（{1} 步，不含随机森林训练）。"] =
                "Estimated time: about {0} ({1} step(s), no random-forest training).";
            d["{0:0} 秒"] = "{0:0} s";
            d["{0:0.#} 分钟"] = "{0:0.#} min";

            d["耕地"] = "Cropland";
            d["森林"] = "Forest";
            d["草地"] = "Grassland";
            d["灌木"] = "Shrubland";
            d["水体"] = "Water";
            d["冰雪"] = "Snow/Ice";
            d["裸地"] = "Bare land";
            d["不透水面"] = "Impervious";

            d["土地利用变化预测结果"] = "Land-use Change Prediction Results";
            d["实测 / 预测 对比"] = "Observed vs predicted";
            d["三联对比图由引擎生成：末期实测、预测、逐像元差异。"] =
                "The engine draws three panels: observed last period, prediction, per-pixel difference.";
            d["变化热点"] = "Change hotspots";
            d["按“源类→目标类”着色的变化热点，图例给出最主要的四种转移类型。"] =
                "Hotspots coloured by source→target transition; the legend lists the four largest transitions.";
            d["打开报告"] = "Open report";
            d["打开输出目录"] = "Open output folder";
            d["定位预测栅格"] = "Show prediction file";
            d["关闭"] = "Close";
            d["预测步数 {0} · 邻域 {1}×{1} · 类别 {2} 个 · 耗时 {3:0.0} 秒"] =
                "{0} step(s) · neighbourhood {1}×{1} · {2} classes · {3:0.0} s";
            d["\r\n精度评价："] = "\r\nAccuracy: ";
            d["\r\n提示："] = "\r\nNote: ";
            d["转移矩阵"] = "Transition matrix";
            d["转移概率（行归一化）"] = "Transition probability (row-normalised)";
            d["转移计数（像元）"] = "Transition counts (pixels)";
            d["转移预算（像元）"] = "Transition budget (pixels)";
            d["转移计数矩阵 C[i,j]（像元）"] = "Transition count matrix C[i,j] (pixels)";
            d["转移预算矩阵 B[i,j]（行和 = 当期该类像元数）"] =
                "Transition budget matrix B[i,j] (row sum = pixels of that class)";
            d["Markov 转移概率矩阵 P[i,j]"] = "Markov transition probability matrix P[i,j]";
            d["面积变化"] = "Area change";
            d[" 像元"] = " px";
            d["各类面积：基期 / 末期实测 / 预测"] = "Area by class: base / observed last / predicted";
            d["基期"] = "Base";
            d["末期实测"] = "Observed last";
            d["预测"] = "Predicted";
            d["本次运行没有做精度评价。\r\n勾选「输出精度评价」后重跑即可看到 OA / Kappa / PA / UA / F1 与混淆矩阵。"] =
                "This run did not assess accuracy.\r\nTick \"Accuracy assessment\" and rerun to see OA / Kappa / PA / UA / F1 and the confusion matrix.";
            d["总体精度 OA"] = "Overall accuracy";
            d["验证像元"] = "Validated pixels";
            d["未变化基线 OA"] = "No-change baseline";
            d["混淆矩阵（行=实测，列=预测）"] = "Confusion matrix (rows = observed, columns = predicted)";
            d["实测"] = "Observed";
            d["特征重要性"] = "Feature importance";
            d["本次没有训练随机森林（适宜性来源不是「随机森林」），因此没有特征重要性。"] =
                "No random forest was trained (suitability source is not random forest), so there is no feature importance.";
            d["驱动因子重要性"] = "Driver importance";
            d["没有生成这张图。\r\n"] = "This figure was not generated.\r\n";
            d["时间外推检验（示例数据）"] = "Temporal extrapolation (sample data)";
            d["200 棵树 · 深度 20 · 样本 624000"] = "200 trees · depth 20 · 624000 samples";
            d["距道路"] = "Distance to roads";
            d["人口密度"] = "Population density";
            d["高程"] = "Elevation";
            d["距水系"] = "Distance to water";
            d["距铁路"] = "Distance to railways";
            d["示例数据：仅用于界面自检"] = "Sample data: UI self-test only";

            d["格式转换 / 导出"] = "Convert / Export";
            d["输出格式"] = "Output format";
            d["ASCII 网格 (*.asc)"] = "ASCII grid (*.asc)";
            d["压缩方式"] = "Compression";
            d["分块存储"] = "Tiled storage";
            d["启用 TILED（大图推荐）"] = "Enable TILED (recommended for large rasters)";
            d["块大小"] = "Block size";
            d["JPEG 质量"] = "JPEG quality";
            d["金字塔"] = "Overviews";
            d["同时建立 2/4/8/16 级金字塔"] = "Also build 2/4/8/16 overview levels";
            d["生成金字塔"] = "Build overviews";
            d["层级"] = "Levels";
            d["重采样"] = "Resampling";
            d["源影像只读：会先把数据复制到输出文件，再在副本上建立内置金字塔（不写 .ovr 到源目录）。"] =
                "Source data is read-only: data is copied to the output first, then internal overviews are built on the copy (no .ovr in the source folder).";
            d["重投影 / 重采样 / 裁剪"] = "Reproject / Resample / Clip";
            d["处理方式"] = "Operation";
            d["重投影到目标坐标系"] = "Reproject to target CRS";
            d["重采样（改变分辨率）"] = "Resample (change resolution)";
            d["按范围裁剪"] = "Clip by extent";
            d["导出当前视图"] = "Export current view";
            d["仅复制（保持原样）"] = "Copy only (keep as is)";
            d["常用坐标系"] = "Common CRS";
            d["（不改变）"] = "(unchanged)";
            d["EPSG:4326 WGS84 经纬度"] = "EPSG:4326 WGS84 lon/lat";
            d["EPSG:3857 Web 墨卡托"] = "EPSG:3857 Web Mercator";
            d["EPSG:4490 CGCS2000 经纬度"] = "EPSG:4490 CGCS2000 lon/lat";
            d["目标坐标系"] = "Target CRS";
            d["源坐标系覆盖"] = "Source CRS override";
            d["重采样算法"] = "Resampling method";
            d["像元宽度"] = "Pixel width";
            d["像元高度"] = "Pixel height";
            d["对齐参考图层"] = "Align to reference layer";
            d["范围 X/Y 最小"] = "Extent X/Y min";
            d["范围 X/Y 最大"] = "Extent X/Y max";
            d["输出波段"] = "Output bands";
            d["按行列数"] = "By rows/columns";
            d["输出尺寸"] = "Output size";
            d["源影像无投影时，重投影需填“源坐标系覆盖”（如 EPSG:32651）。\r\n"] =
                "If the source has no projection, fill \"Source CRS override\" (e.g. EPSG:32651).\r\n";
            d["“对齐参考图层”会用其范围与像元尺寸填 -te/-tr，要求同坐标系。\r\n"] =
                "\"Align to reference layer\" fills -te/-tr from that layer and requires the same CRS.\r\n";
            d["“输出波段”留空表示全部波段。"] = "Leave \"Output bands\" empty for all bands.";
            d["（不对齐，手工填范围）"] = "(no alignment, enter the extent manually)";
            d["请指定目标坐标系，例如 EPSG:4326"] = "Please give a target CRS, e.g. EPSG:4326";
            d["源影像没有投影信息，请先填写“源坐标系覆盖”（例如 EPSG:32651）"] =
                "The source has no projection; fill \"Source CRS override\" (e.g. EPSG:32651)";
            d["裁剪范围无效：XMax 必须大于 XMin，YMax 必须大于 YMin"] =
                "Invalid clip extent: XMax must exceed XMin and YMax must exceed YMin";
            d["）与当前图层（"] = ") differs from the current layer (";
            d["）不一致，请先重投影到同一坐标系"] = "); reproject to a common CRS first";
            d["输出波段无效：应填 1~"] = "Invalid output bands: expected 1–";
            d[" 之间的波段号，逗号分隔，留空表示全部"] =
                ", comma separated; leave empty for all bands";
            d["输出文件"] = "Output file";
            d["完成后加载为图层"] = "Load as a layer when finished";
            d["开始执行"] = "Run";
            d["选择输出文件"] = "Choose the output file";
            d["TIFF 影像|*.tif;*.tiff|IMG 影像|*.img|PNG 图片|*.png|JPEG 图片|*.jpg|VRT 虚拟格式|*.vrt|所有文件|*.*"] =
                "TIFF image|*.tif;*.tiff|IMG image|*.img|PNG image|*.png|JPEG image|*.jpg|VRT virtual format|*.vrt|All files|*.*";
            d["目标文件无法写入：\r\n{0}\r\n\r\n"] = "The target file cannot be written:\r\n{0}\r\n\r\n";
            d["常见原因是它已作为图层打开、被其他程序锁定，或文件被设为只读。\r\n\r\n是否改写到：\r\n{1}？"] =
                "It is probably open as a layer, locked by another program, or read-only.\r\n\r\nWrite to this instead?\r\n{1}";
            d["文件被占用"] = "File in use";
            d["输出文件已存在，是否覆盖？"] = "The output file exists. Overwrite it?";
            d["确认"] = "Confirm";
            d["参数有误"] = "Invalid parameters";
            d["处理失败"] = "Processing failed";
        }
    }
}
