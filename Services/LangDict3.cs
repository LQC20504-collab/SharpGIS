using System.Collections.Generic;

namespace SharpGIS.Services
{
    /// <summary>英文译文（第 3 段：算子对话框、图层面板、主窗口、右侧面板）。</summary>
    public static partial class Lang
    {
        private static void RegisterPart3(Dictionary<string, string> d)
        {
            d["波段计算器"] = "Band calculator";
            d["表达式"] = "Expression";
            d["（选择模板插入…）"] = "(pick a template…)";
            d["常用模板"] = "Common templates";
            d["选中一个模板会把上面的表达式整体替换为该模板，可再手动修改"] =
                "Picking a template replaces the whole expression; you can edit it afterwards";
            d["函数"] = "Functions";
            d["波段映射"] = "Band mapping";
            d["校验表达式"] = "Validate expression";
            d["b1…b{0} 是占位符，默认等于本图层第 1…{0} 波段；可在“波段映射”里改成其它已打开文件的波段。\r\n"] =
                "b1…b{0} are placeholders, by default bands 1…{0} of this layer; Bands in the mapping table may point at other open files.\r\n";
            d["点函数按钮插入到光标处，选中文字会自动包进括号。"] =
                "A function button inserts at the caret; selected text is wrapped in parentheses.";
            d["结果输出为 Float32 单波段 GeoTIFF；地理参考取自当前图层。"] =
                "The result is a single-band Float32 GeoTIFF using this layer's georeferencing.";
            d["（当前图层）"] = " (current layer)";
            d["（网格不一致："] = " (grid mismatch: ";
            d["（投影不同）"] = " (different projection)";
            d["（未指定）"] = "(not set)";
            d["✓ 表达式可用"] = "✓ expression is valid";
            d[" 与当前图层投影不同，结果按当前图层的地理参考写出"] =
                " has a different projection; the result uses this layer's georeferencing";
            d["DEM 分析"] = "DEM analysis";
            d["输入波段"] = "Input band";
            d["算法"] = "Algorithm";
            d["光源方位角"] = "Light azimuth";
            d["光源高度角"] = "Light altitude";
            d["高程夸张系数 Z"] = "Vertical exaggeration Z";
            d["坡度单位"] = "Slope unit";
            d["以百分比表示（默认度）"] = "Percent (default is degrees)";
            d["边缘处理"] = "Edge handling";
            d["计算边缘（避免一圈 NoData）"] = "Compute edges (avoids a NoData rim)";
            d["彩色渲染色带"] = "Colour ramp";
            d["色带最小值"] = "Ramp minimum";
            d["色带最大值"] = "Ramp maximum";
            d["空域滤波"] = "Spatial filter";
            d["滤波类型"] = "Filter type";
            d["窗口大小"] = "Window size";
            d["高斯 σ"] = "Gaussian σ";
            d["Byte 数据（Sobel 除外）输出仍为 Byte；其余类型输出 Float32。分块处理，支持大图。"] =
                "Byte input stays Byte (except Sobel); other types produce Float32. Block-wise, large rasters supported.";
            d["影像增强"] = "Enhancement";
            d["增强方式"] = "Method";
            d["作用范围"] = "Scope";
            d["全部波段"] = "All bands";
            d["单波段模式"] = "Single band";
            d["累计裁剪下限 %"] = "Cumulative clip low %";
            d["累计裁剪上限 %"] = "Cumulative clip high %";
            d["输出 8bit GeoTIFF，便于后续做分类或导入其它软件。"] =
                "Outputs an 8-bit GeoTIFF, handy for classification or other software.";
            d["影像融合（全色锐化）"] = "Pan-sharpening";
            d["多光谱图层"] = "Multispectral layer";
            d["R 波段"] = "Red band";
            d["G 波段"] = "Green band";
            d["B 波段"] = "Blue band";
            d["全色图层"] = "Panchromatic layer";
            d["全色波段"] = "Panchromatic band";
            d["融合方法"] = "Fusion method";
            d["要求全色与多光谱已完成几何配准（范围一致）。输出为 8bit 三波段 GeoTIFF。"] =
                "Pan and multispectral must already be co-registered. Output is an 8-bit three-band GeoTIFF.";
            d["请选择多光谱与全色图层"] = "Select both the multispectral and panchromatic layers";
            d["多光谱与全色图层不能是同一个"] = "Multispectral and panchromatic must be different layers";
            d["多光谱图层至少要有 3 个波段"] = "The multispectral layer needs at least 3 bands";
            d["非监督分类（KMeans）"] = "Unsupervised classification (KMeans)";
            d["参与波段"] = "Bands used";
            d["类别数 K"] = "Clusters K";
            d["最大迭代次数"] = "Max iterations";
            d["随机种子"] = "Random seed";
            d["输出调色板（便于直接显示）"] = "Write a palette (easier to display)";
            d["算法：KMeans++ 初始化 + 抽样训练（每边最多 256 个样本）+ 分块分类，输出 Byte 类别图。"] =
                "KMeans++ init + sampled training (max 256 per axis) + block-wise classification; output is a Byte class map.";
            d["请填写参与分类的波段号，例如 1,2,3"] = "Enter the bands to use, e.g. 1,2,3";
            d["确定"] = "OK";

            d["图层"] = "Layers";
            d["上移图层 (Ctrl+↑)"] = "Move layer up (Ctrl+↑)";
            d["下移图层 (Ctrl+↓)"] = "Move layer down (Ctrl+↓)";
            d["移除图层 (Del)"] = "Remove layer (Del)";
            d["缩放至图层"] = "Zoom to layer";
            d["符号化属性"] = "Symbology";
            d["符号化属性…"] = "Symbology…";
            d["元数据"] = "Metadata";
            d["恢复默认符号化"] = "Reset symbology";
            d["重命名…"] = "Rename…";
            d["显示 / 隐藏"] = "Show / hide";
            d["移除图层"] = "Remove layer";
            d["导出…"] = "Export…";
            d["生成金字塔…"] = "Build overviews…";
            d["重投影…"] = "Reproject…";
            d["复制文件路径"] = "Copy file path";
            d["隐藏图层"] = "Hide layer";
            d["显示图层"] = "Show layer";
            d["{0}\n{1} × {2} · {3} 波段 · {4}\n{5}"] = "{0}\n{1} × {2} · {3} bands · {4}\n{5}";
            d["{0}×{1} · {2}波段 · {3}"] = "{0}×{1} · {2} bands · {3}";

            d["请先打开并选中一个栅格图层"] = "Open and select a raster layer first";
            d["处理完成并已加载：\r\n"] = "Finished and loaded:\r\n";
            d["处理完成：\r\n"] = "Finished:\r\n";
            d["影像融合需要至少两个图层（多光谱 + 全色）"] =
                "Pan-sharpening needs at least two layers (multispectral + panchromatic)";

            d["SharpGIS 1.0 — 栅格遥感影像处理平台"] = "SharpGIS 1.0 — Raster Remote Sensing Platform";
            d["符号化"] = "Symbology";
            d["直方图"] = "Histogram";
            d["识别"] = "Identify";
            d["  图层"] = "  Layers";
            d["文件(&F)"] = "&File";
            d["打开栅格影像…"] = "Open raster…";
            d["关闭当前图层"] = "Close current layer";
            d["最近打开"] = "Recent";
            d["导出当前视图为图片…"] = "Export current view as image…";
            d["退出"] = "Exit";
            d["视图(&V)"] = "&View";
            d["放大"] = "Zoom in";
            d["缩小"] = "Zoom out";
            d["缩放到全图"] = "Zoom to full extent";
            d["1:1 像素"] = "1:1 pixels";
            d["缩放到图层"] = "Zoom to layer";
            d["旋转工具（三次单击：中心 → 手柄 → 提交）"] =
                "Rotate tool (three clicks: centre → handle → commit)";
            d["重置北向"] = "Reset north";
            d["上一视图"] = "Previous view";
            d["下一视图"] = "Next view";
            d["刷新显示"] = "Refresh";
            d["图层(&L)"] = "&Layer";
            d["处理(&P)"] = "&Processing";
            d["帮助(&H)"] = "&Help";
            d["快捷键一览"] = "Keyboard shortcuts";
            d["关于 SharpGIS"] = "About SharpGIS";
            d["打开栅格影像 (Ctrl+O)"] = "Open raster (Ctrl+O)";
            d["平移（拖动地图）"] = "Pan (drag the map)";
            d["放大（点击）"] = "Zoom in (click)";
            d["缩小（点击）"] = "Zoom out (click)";
            d["框选缩放"] = "Zoom to rectangle";
            d["旋转视图：单击定中心 → 单击定手柄 → 移动旋转，再单击提交（R）"] =
                "Rotate: click centre → click handle → move to rotate → click to commit (R)";
            d["缩放到全图 (Ctrl+Shift+F)"] = "Zoom to full extent (Ctrl+Shift+F)";
            d["1:1 像素 (Ctrl+1)"] = "1:1 pixels (Ctrl+1)";
            d["重置北向（Shift+R）"] = "Reset north (Shift+R)";
            d["识别像素值"] = "Identify pixel values";
            d["处理"] = "Processing";
            d["栅格处理工具箱"] = "Raster processing toolbox";
            d["格式转换 / 导出…"] = "Convert / Export…";
            d["重投影 / 重采样…"] = "Reproject / Resample…";
            d["按范围裁剪…"] = "Clip by extent…";
            d["波段计算器…"] = "Band calculator…";
            d["DEM 分析（坡度/坡向/山体阴影）…"] = "DEM analysis (slope/aspect/hillshade)…";
            d["滤波（均值/中值/高斯/拉普拉斯锐化/Sobel）…"] =
                "Filter (mean/median/Gaussian/Laplacian/Sobel)…";
            d["增强（线性拉伸/直方图均衡）…"] = "Enhance (linear stretch/histogram equalise)…";
            d["影像融合（Brovey/IHS/HPF）…"] = "Pan-sharpening (Brovey/IHS/HPF)…";
            d["非监督分类（KMeans）…"] = "Unsupervised classification (KMeans)…";
            d["土地利用变化预测（CA-Markov）…"] = "Land-use change prediction (CA-Markov)…";
            d["快捷键：\r\n"] = "Shortcuts:\r\n";
            d["Ctrl+O  打开栅格影像\r\n"] = "Ctrl+O  Open raster\r\n";
            d["Ctrl+W  关闭当前图层\r\n"] = "Ctrl+W  Close current layer\r\n";
            d["Ctrl+Shift+F  缩放到全图\r\n"] = "Ctrl+Shift+F  Zoom to full extent\r\n";
            d["Ctrl+1  1:1 像素\r\n"] = "Ctrl+1  1:1 pixels\r\n";
            d["Ctrl+ + / -  放大 / 缩小\r\n"] = "Ctrl+ + / -  Zoom in / out\r\n";
            d["Alt+← / →  上一视图 / 下一视图\r\n"] = "Alt+← / →  Previous / next view\r\n";
            d["Delete  移除当前图层\r\n"] = "Delete  Remove current layer\r\n";
            d["F5  刷新显示\r\n"] = "F5  Refresh\r\n";
            d["R  旋转工具（单击定中心 → 单击定手柄 → 移动旋转，再单击提交）\r\n"] =
                "R  Rotate tool (click centre → click handle → move → click to commit)\r\n";
            d["Shift+R  重置北向\r\n"] = "Shift+R  Reset north\r\n";
            d["Esc  取消旋转 / 回到平移工具\r\n"] = "Esc  Cancel rotation / back to pan\r\n";
            d["滚轮  以光标为锚点缩放；拖动  平移；双击图层  打开符号化"] =
                "Wheel  zoom at cursor; drag  pan; double-click a layer  symbology";
            d["SharpGIS 1.0\r\n\r\nC# + GDAL 实现的栅格 GIS（QGIS 栅格功能子集）\r\n"] =
                "SharpGIS 1.0\r\n\r\nA raster GIS built with C# + GDAL (a subset of QGIS raster tools)\r\n";
            d["GDAL 版本："] = "GDAL version: ";
            d["支持多图层叠加、符号化、直方图、识别、重投影、裁剪、波段计算、DEM、滤波、融合、分类等。\r\n"] =
                "Supports multiple layers, symbology, histograms, identify, reprojection, clipping, band maths, DEM, filters, pan-sharpening, classification and more.\r\n";
            d["不包含矢量功能。"] = "Vector data is not supported.";
            d["坐标: -"] = "Coord: -";
            d["行列: -"] = "Row/Col: -";
            d["值: -"] = "Value: -";
            d["投影: -"] = "Projection: -";
            d["缩放: -"] = "Zoom: -";
            d["打开栅格影像（可多选）"] = "Open rasters (multi-select)";
            d["已打开 {0} 个文件"] = "Opened {0} file(s)";
            d["新图层的坐标系与工程不一致：\r\n\r\n工程：{0}\r\n新图层：{1}\r\n\r\n是否重投影到工程坐标系后再加载？\r\n（选择“否”将按原坐标直接叠加，位置可能错位）"] =
                "The new layer uses a different CRS:\r\n\r\nProject: {0}\r\nNew layer: {1}\r\n\r\nReproject it to the project CRS before loading?\r\n(Choosing No overlays it as is; positions may be misaligned)";
            d["坐标系不一致"] = "CRS mismatch";
            d["打开失败"] = "Open failed";
            d["重命名图层"] = "Rename layer";
            d["图层名称"] = "Layer name";
            d["（空）"] = "(empty)";
            d["清空列表"] = "Clear the list";
            d["缩放 1:{0:N0} · {1:0}°"] = "Scale 1:{0:N0} · {1:0}°";
            d["当前视图已旋转 {0:0.0}°（顺时针为正）"] = "View rotated {0:0.0}° (clockwise positive)";
            d["缩放 1:{0:N0}"] = "Scale 1:{0:N0}";
            d["缩放比例"] = "Scale";
            d["坐标: {0:F2}, {1:F2}"] = "Coord: {0:F2}, {1:F2}";
            d["行列: {0}, {1}"] = "Row/Col: {0}, {1}";
            d["值: "] = "Value: ";
            d["请先打开影像"] = "Open a raster first";
            d["PNG 图片|*.png|JPEG 图片|*.jpg"] = "PNG image|*.png|JPEG image|*.jpg";
            d["已导出: "] = "Exported: ";
            d["导出失败: "] = "Export failed: ";
            d["重投影到工程坐标系 "] = "Reprojecting to the project CRS ";
            d["打开重投影结果失败："] = "Cannot open the reprojected result: ";
            d["渲染异常: "] = "Rendering error: ";
            d["拖入栅格文件，或按 Ctrl+O 打开\r\n（支持 GeoTIFF / IMG / DEM / PNG / JPEG 等）"] =
                "Drop a raster here or press Ctrl+O\r\n(GeoTIFF / IMG / DEM / PNG / JPEG …)";
            d["正在渲染…"] = "Rendering…";
            d["第 2 步：单击确定手柄位置"] = "Step 2: click to set the handle";
            d["第 3 步：移动鼠标旋转，单击提交（Esc 取消）"] =
                "Step 3: move to rotate, click to commit (Esc cancels)";
            d["渲染中…"] = "Rendering…";
            d["未选择图层"] = "No layer selected";
            d["拖动端点即时应用为拉伸区间"] = "Drag an end point to set the stretch range";
            d["统计不可用"] = "Statistics unavailable";
            d["最小 {0:G5}   最大 {1:G5}\n均值 {2:G5}   标准差 {3:G5}\n中位数 {4:G5}   P2/P98 {5:G4} / {6:G4}\n当前拉伸 {7:G5} ~ {8:G5}"] =
                "Min {0:G5}   Max {1:G5}\nMean {2:G5}   Std {3:G5}\nMedian {4:G5}   P2/P98 {5:G4} / {6:G4}\nStretch {7:G5} ~ {8:G5}";
            d["无统计数据"] = "No statistics";
            d["备注: 源影像带旋转，已包装为北向上临时 VRT 显示（波段数含 alpha 掩膜）"] =
                "Note: the source is rotated; it is displayed through a north-up temporary VRT (band count includes the alpha mask)";
            d["—— 各波段统计（抽样，已剔除无效像素）——"] =
                "—— Per-band statistics (sampled, NoData excluded) ——";
            d["波段 {0}: alpha 掩膜"] = "Band {0}: alpha mask";
            d["波段 {0}: 统计不可用"] = "Band {0}: statistics unavailable";
            d["波段 {0}: min={1:G6} max={2:G6} mean={3:G6} std={4:G6} P2={5:G6} P98={6:G6}"] =
                "Band {0}: min={1:G6} max={2:G6} mean={3:G6} std={4:G6} P2={5:G6} P98={6:G6}";
            d["在地图上使用识别工具（或双击）点击像素"] = "Use the identify tool (or double-click) on the map";
            d["数值"] = "Value";
            d["说明"] = "Description";
            d["复制结果"] = "Copy result";
            d["{0}\n行列: {1}, {2}    坐标: {3:F3}, {4:F3}"] = "{0}\nRow/Col: {1}, {2}    Coord: {3:F3}, {4:F3}";
            d["{0}  行={1} 列={2}"] = "{0}  row={1} col={2}";
            d["单波段灰度"] = "Single-band grey";
            d["多波段彩色（RGB）"] = "Multi-band colour (RGB)";
            d["反相"] = "Invert";
            d["无拉伸（按类型满量程）"] = "No stretch (full type range)";
            d["标准差"] = "Std dev";
            d["百分比裁剪"] = "Percent clip";
            d["自定义区间"] = "Custom range";
            d["正常"] = "Normal";
            d["正片叠底"] = "Multiply";
            d["滤色"] = "Screen";
            d["立方"] = "Cubic";
            d["符号化方式"] = "Symbology mode";
            d["模式"] = "Mode";
            d["灰度波段"] = "Grey band";
            d["色带与拉伸"] = "Ramp and stretch";
            d["色带"] = "Ramp";
            d["拉伸"] = "Stretch";
            d["标准差倍数"] = "Std-dev factor";
            d["裁剪下限 %"] = "Clip low %";
            d["裁剪上限 %"] = "Clip high %";
            d["自定义最小"] = "Custom min";
            d["自定义最大"] = "Custom max";
            d["色调与合成"] = "Tone and blending";
            d["亮度"] = "Brightness";
            d["对比度 %"] = "Contrast %";
            d["透明度 %"] = "Opacity %";
            d["混合模式"] = "Blend mode";
            d["{0} × {1} · {2} 波段 · {3}\n当前：{4}"] = "{0} × {1} · {2} bands · {3}\nNow: {4}";
            // ---- 语言切换与国际化 ----
            d["语言(&G)"] = "Lan&guage";
            d["界面语言已改为"] = "Interface language changed to";
            d["切换语言需要重启程序才能生效，现在重启吗？（已打开的图层不会保留）"] =
                "A restart is required. Restart now? (Open layers will not be kept)";
            d["切换语言"] = "Change language";
            d["精度评价"] = "Accuracy";
            d["结果图表"] = "Result charts";
            d["进度框布局检查"] = "Progress dialog layout check";
            d["转移分析（不写文件）"] = "Transition analysis (no files written)";
            d["土地利用变化预测结果"] = "Land-use change prediction result";
            d["驱动因子重要性（{0}）"] = "Driver importance ({0})";
            d["参考图层坐标系（{0}）与当前图层（{1}）"] =
                "The reference layer uses CRS ({0}) while the current layer uses ({1})";
            d["自定义投影（{0}）"] = "Custom projection ({0})";
            d["解释器不存在："] = "Interpreter not found: ";
            d["波段 {0}（{1}）"] = "Band {0} ({1})";
            d["（网格不一致：{0}）"] = " (grid mismatch: {0})";
            d["CA-Markov 引擎运行失败（退出码 {0}）"] =
                "The CA-Markov engine failed (exit code {0})";
            d["无法识别的字符 '{0}'（位置 {1}）"] =
                "Unrecognised character '{0}' (position {1})";
            d["表达式缺少操作数（位置 {0}）"] =
                "The expression is missing an operand (position {0})";
            d["无法以可写方式打开: {0}（{1}）"] =
                "Cannot open for writing: {0} ({1})";
        }
    }
}
