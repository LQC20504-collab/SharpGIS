using System.Collections.Generic;

namespace SharpGIS.Services
{
    /// <summary>英文译文（第 1 段：GDAL 引导、模型层、处理算子）。</summary>
    public static partial class Lang
    {
        private static void RegisterPart1(Dictionary<string, string> d)
        {
            d["未找到 GDAL 原生库目录: "] = "GDAL native library folder not found: ";
            d["未找到 GDAL 托管包装原生库: "] = "GDAL managed wrapper native library not found: ";
            d["GDAL 初始化失败，程序无法打开影像。"] = "GDAL failed to initialise; rasters cannot be opened.";
            d["原因: "] = "Reason: ";
            d["类型: "] = "Kind: ";
            d["程序目录: "] = "Application folder: ";
            d["原生库目录: "] = "Native library folder: ";
            d["(未解析)"] = "(unresolved)";
            d["进程位数: "] = "Process bitness: ";
            d["gdal 目录存在: "] = "gdal folder exists: ";
            d["gdal 子项: "] = "gdal entries: ";

            d["灰阶"] = "Grayscale";
            d["反灰阶"] = "Inverse grayscale";
            d["Jet（彩虹）"] = "Jet (rainbow)";
            d["地形"] = "Terrain";
            d["热力"] = "Heat";
            d["蓝-红"] = "Blue-Red";
            d["植被（NDVI）"] = "Vegetation (NDVI)";
            d["未知"] = "Unknown";
            d["混合类型"] = "Mixed types";
            d["未知（无投影信息）"] = "Unknown (no projection)";
            d["{0}:{1}（地理基准）"] = "{0}:{1} (geographic datum)";
            d["自定义投影（未识别 EPSG）"] = "Custom projection (no EPSG code)";
            d["（无）"] = "(none)";
            d["无拉伸（线性）"] = "No stretch (linear)";
            d["最小-最大值"] = "Min-Max";
            d["标准差 ×"] = "Std dev ×";
            d["百分比裁剪 "] = "Percent clip ";
            d["直方图均衡"] = "Histogram equalise";
            d["自定义 "] = "Custom ";
            d["调色板"] = "Palette";
            d["波段 {0} · {1} · {2}"] = "Band {0} · {1} · {2}";

            d["表达式中没有引用任何波段（请使用 b1、b2 … 表示波段）"] =
                "The expression does not use any band (use b1, b2 … for bands).";
            d["不支持该输出格式"] = "Unsupported output format";
            d["创建输出文件失败："] = "Failed to create the output file: ";
            d["图层 "] = "Layer ";
            d[" 已关闭"] = " has been closed";
            d["表达式未引用任何波段"] = "The expression does not use any band";
            d["缺少波段映射"] = "Missing band mapping";
            d["槽位超出映射表范围（共 "] = "Slot out of range (the mapping table has ";
            d[" 个）"] = " slots)";
            d["占位符 b"] = "Placeholder b";
            d[" 还没有指定实际波段，请在右侧“波段映射”里为它选择一个波段"] =
                " has no band yet — pick one on the right under Band mapping";
            d[" 已关闭或波段不足，请重新打开后再计算"] =
                " is closed or has too few bands; reopen it and try again";
            d["缺少参考图层"] = "Missing reference layer";
            d[" 与当前图层网格不一致："] = " does not share the grid of the current layer: ";
            d["，不能参与波段计算"] = ", so it cannot join the band calculation";
            d["图层为空"] = "Layer is null";
            d["图层已关闭"] = "Layer is closed";
            d["尺寸不同（{0} ≠ {1}）"] = "Different size ({0} ≠ {1})";
            d["一侧没有地理变换"] = "One side has no geotransform";
            d["地理变换不一致（第 {0} 个系数 {1:G6} ≠ {2:G6}）"] =
                "Geotransform mismatch (coefficient {0}: {1:G6} ≠ {2:G6})";
            d["波段号超出范围：图层 "] = "Band out of range: layer ";
            d[" 共 "] = " has ";
            d[" 个波段"] = " bands";
            d["波段 "] = "Band ";
            d["（掩膜）"] = "(mask)";
            d["红"] = "Red";
            d["绿"] = "Green";
            d["蓝"] = "Blue";

            d["找不到 CA-Markov 引擎脚本：\r\n"] = "CA-Markov engine script not found:\r\n";
            d["\r\n请确认程序目录下的 Engine 文件夹完整（camarkov_engine.py）。"] =
                "\r\nMake sure the Engine folder next to the program contains camarkov_engine.py.";
            d["无法运行 CA-Markov 引擎：\r\n"] = "Cannot run the CA-Markov engine:\r\n";
            d["CA-Markov 参数或数据有误"] = "CA-Markov: invalid parameters or data";
            d["CA-Markov 引擎运行失败（退出码 "] = "CA-Markov engine failed (exit code ";
            d["\r\n最后阶段："] = "\r\nLast stage: ";
            d["引擎没有生成指标文件，可能被提前中断：\r\n"] =
                "The engine did not produce a metrics file (it may have been interrupted):\r\n";
            d["引擎返回的指标为空或格式不符：\r\n"] =
                "The engine returned empty or malformed metrics:\r\n";
            d["（空文件）"] = "(empty file)";
            d["（未选择）"] = "(not selected)";
            d["{0} · 权重 {1:0.##} · {2}"] = "{0} · weight {1:0.##} · {2}";
            d["负向"] = "negative";
            d["正向"] = "positive";
            d["类别 "] = "Class ";
            d["未做精度评价"] = "No accuracy assessment";
            d[" · 未变化基线 OA {0:P2}"] = " · no-change baseline OA {0:P2}";

            d["山体阴影 hillshade"] = "Hillshade (hillshade)";
            d["坡度 slope"] = "Slope (slope)";
            d["坡向 aspect"] = "Aspect (aspect)";
            d["彩色渲染 color-relief"] = "Colour relief (color-relief)";
            d["坡度"] = "Slope";
            d["坡向"] = "Aspect";
            d["彩色"] = "Colour";
            d["波段号越界"] = "Band out of range";
            d["操作已取消"] = "Operation cancelled";
            d["DEM 处理失败："] = "DEM processing failed: ";
            d["线性拉伸到 8bit"] = "Linear stretch to 8-bit";
            d["直方图均衡到 8bit"] = "Histogram equalise to 8-bit";
            d["创建输出失败："] = "Failed to create output: ";
            d[" 统计不可用"] = ": statistics unavailable";

            d["用法："] = "Usage: ";
            d["\r\n示例："] = "\r\nExample: ";
            d["表达式为空"] = "The expression is empty";
            d["表达式在第 "] = "Unexpected content at character ";
            d[" 个字符处有多余内容: "] = " of the expression: ";
            d["非法数字: "] = "Invalid number: ";
            d["无法识别的字符 '"] = "Unexpected character '";
            d["'（位置 "] = "' (position ";
            d["条件表达式缺少 ':'"] = "Conditional expression is missing ':'";
            d["缺少右括号 ')'"] = "Missing closing ')'";
            d["非法波段变量: "] = "Invalid band variable: ";
            d["占位符 {0} 超出范围：当前映射表共 {1} 个槽位（b1…b{1}）"] =
                "Placeholder {0} is out of range: the mapping table has {1} slots (b1…b{1})";
            d["未知标识符: "] = "Unknown identifier: ";
            d["（函数需要带括号）"] = " (functions need parentheses)";
            d["函数 "] = "Function ";
            d[" 缺少右括号"] = " is missing a closing parenthesis";
            d["表达式缺少操作数（位置 "] = "Missing operand (position ";
            d["函数 {0} 需要 {1} 个参数，实际 {2} 个"] = "Function {0} expects {1} argument(s), got {2}";
            d["min 至少需要 1 个参数"] = "min needs at least 1 argument";
            d["max 至少需要 1 个参数"] = "max needs at least 1 argument";
            d["未知函数: "] = "Unknown function: ";
            d["开平方；x 为负数时结果为 NaN"] = "Square root; NaN for negative x";
            d["取绝对值"] = "Absolute value";
            d["log(x)  等价 ln(x)"] = "log(x)  = ln(x)";
            d["自然对数；x ≤ 0 时结果 NaN"] = "Natural logarithm; NaN for x ≤ 0";
            d["以 10 为底的对数；x ≤ 0 时结果 NaN"] = "Base-10 logarithm; NaN for x ≤ 0";
            d["自然指数 e^x"] = "Exponential e^x";
            d["x 的 y 次幂（y 可为小数）"] = "x raised to y (y may be fractional)";
            d["取最小值，可传多个参数"] = "Minimum of several arguments";
            d["取最大值，可传多个参数"] = "Maximum of several arguments";
            d["把 x 限制在 [lo, hi] 区间内"] = "Clamp x into [lo, hi]";
            d["if(条件, 真值, 假值)"] = "if(condition, then, else)";
            d["条件不为 0 取真值，否则取假值；比较可用 > < =="] =
                "Non-zero condition yields the first value; compare with > < ==";
            d["归一化差值 (a-b)/(a+b)，NDVI/NDWI 都用它"] =
                "Normalised difference (a-b)/(a+b), used by NDVI/NDWI";
            d["向下取整"] = "Round down";
            d["向上取整"] = "Round up";
            d["四舍五入取整"] = "Round to nearest";
            d["符号：正数 1、负数 -1、零 0"] = "Sign: 1 for positive, -1 for negative, 0 for zero";
            d["正弦（x 为弧度）"] = "Sine (x in radians)";
            d["余弦（x 为弧度）"] = "Cosine (x in radians)";
            d["x 为 NaN 时返回 1，否则 0"] = "1 when x is NaN, otherwise 0";
            d["直接把某个波段当结果输出"] = "Return a band unchanged";
            d["NDVI：植被指数，近红外与红波段"] = "NDVI: vegetation index from NIR and red";
            d["简单比值指数，抑制光照差异"] = "Simple ratio index, dampens illumination differences";
            d["多波段加权合成（权重可按需修改）"] = "Weighted multi-band composite (weights editable)";
            d["阈值分段：大于 100 取 255，否则放大两倍"] =
                "Threshold split: 255 above 100, doubled below";
            d["三波段平均值，常用于降噪"] = "Average of three bands, often used to reduce noise";

            d["均值平滑"] = "Mean smoothing";
            d["中值去噪"] = "Median denoise";
            d["高斯模糊"] = "Gaussian blur";
            d["拉普拉斯锐化"] = "Laplacian sharpen";
            d["Sobel 边缘"] = "Sobel edges";
            d["中值"] = "Median";
            d["高斯"] = "Gaussian";
            d["锐化"] = "Sharpen";
            d["边缘"] = "Edges";
            d["Brovey 变换"] = "Brovey transform";
            d["IHS 变换"] = "IHS transform";
            d["HPF 高通滤波"] = "HPF high-pass filter";
            d["高通"] = "High-pass";
            d["融合需要选择 3 个多光谱波段（R/G/B）"] = "Fusion needs three multispectral bands (R/G/B)";
            d["波段号越界: "] = "Band out of range: ";

            d["请指定输出文件"] = "Please choose an output file";
            d["复制数据…"] = "Copying data…";
            d["建立金字塔…"] = "Building overviews…";
            d["不支持该输出格式: "] = "Unsupported output format: ";
            d["复制数据失败："] = "Copying data failed: ";
            d["复制完成"] = "Copy finished";
            d["金字塔完成"] = "Overviews finished";
            d["无法以可写方式打开: "] = "Cannot open for writing: ";
            d["建立金字塔失败："] = "Building overviews failed: ";
            d["SharpGIS输出"] = "SharpGIS Output";
            d["请指定输出文件路径"] = "Please specify an output path";
            d["输出路径不能与源文件相同（源数据只读）:\r\n"] =
                "The output path must differ from the source file (source data is read-only):\r\n";
            d["（GDAL 未返回错误信息）"] = "(GDAL returned no error message)";
            d["目标文件被占用或无写入权限（可能已在图层中打开，或被其他程序锁定）。"] =
                "The target file is locked or not writable (it may be open as a layer or held by another program).";
            d["请先关闭占用它的图层，或换一个输出文件名。"] =
                "Close the layer that holds it, or choose another output name.";
            d["（无法读取 GDAL 错误）"] = "(cannot read the GDAL error)";
            d["GDAL 不支持该输出格式: "] = "GDAL does not support this output format: ";
            d["导出失败："] = "Export failed: ";
            d["无"] = "None";
            d["JPEG 压缩仅支持 8 位整型数据"] = "JPEG compression only supports 8-bit integer data";
            d["JPEG 压缩仅支持 1 或 3 个波段"] = "JPEG compression only supports 1 or 3 bands";
            d["不限制"] = "Unlimited";
            d["导出中…"] = "Exporting…";

            d["最近邻"] = "Nearest neighbour";
            d["双线性"] = "Bilinear";
            d["三次卷积"] = "Cubic convolution";
            d["三次样条"] = "Cubic spline";
            d["平均值"] = "Average";
            d["众数"] = "Mode";
            d["最大值"] = "Maximum";
            d["最小值"] = "Minimum";
            d["源影像没有投影信息，请先指定源坐标系（-s_srs）再重投影。"] =
                "The source raster has no projection; set the source CRS (-s_srs) before reprojecting.";
            d["重投影失败："] = "Reprojection failed: ";
            d["无法识别的坐标系: "] = "Unrecognised CRS: ";

            d["SharpGIS 启动失败"] = "SharpGIS failed to start";
            d["文件不存在: "] = "File not found: ";
            d["打开影像失败: "] = "Failed to open the raster: ";
            d["GDAL 无法识别该文件: "] = "GDAL does not recognise this file: ";
            d["该文件不含栅格波段: "] = "The file contains no raster band: ";
            d["（无更多信息）"] = "(no further details)";
            d["（无法读取 GDAL 错误信息）"] = "(cannot read the GDAL error message)";
            d["所有支持的栅格 ("] = "All supported rasters (";
            d["|常用影像 (*.tif;*.tiff;*.img;*.dem;*.bmp;*.jpg;*.png)|*.tif;*.tiff;*.img;*.dem;*.bmp;*.jpg;*.jpeg;*.png|所有文件 (*.*)|*.*"] =
                "|Common images (*.tif;*.tiff;*.img;*.dem;*.bmp;*.jpg;*.png)|*.tif;*.tiff;*.img;*.dem;*.bmp;*.jpg;*.jpeg;*.png|All files (*.*)|*.*";
            d["名称: "] = "Name: ";
            d["路径: "] = "Path: ";
            d["驱动: "] = "Driver: ";
            d["尺寸: "] = "Size: ";
            d[" 像素"] = " px";
            d["波段数: "] = "Bands: ";
            d["  波段 {0}: {1} / {2}{3}"] = "  Band {0}: {1} / {2}{3}";
            d["数据类型: "] = "Data type: ";
            d["地理变换: "] = "Geotransform: ";
            d["分辨率: "] = "Resolution: ";
            d["范围: "] = "Extent: ";
            d["投影: "] = "Projection: ";
            d["（未设置）"] = "(not set)";
            d["块大小: "] = "Block size: ";
            d["压缩: "] = "Compression: ";
            d["关联文件: "] = "Associated files: ";

            d["算子未返回输出路径"] = "The operator returned no output path";
            d["正在处理…"] = "Working…";
            d["准备中…"] = "Preparing…";
            d["取消"] = "Cancel";
            d["正在取消…"] = "Cancelling…";
            d["\r\n请确认程序目录下的 Engine 文件夹完整。"] =
                "\r\nMake sure the Engine folder next to the program is complete.";
            d["找不到参数文件："] = "Parameter file not found: ";
            d["无法启动 Python 进程：\r\n"] = "Cannot start the Python process:\r\n";
            d["Python 引擎运行超过 "] = "The Python engine ran longer than ";
            d[" 分钟，已强制结束。"] = " minutes and was terminated.";
            d["未找到可用的 Python 解释器"] = "No usable Python interpreter found";
            d["Python {0} 缺少依赖：{1}\r\n可在命令行执行：\r\n{2}"] =
                "Python {0} is missing: {1}\r\nRun this on the command line:\r\n{2}";
            d["未找到 Python 解释器。请安装 Python 3.8+ 并执行：\r\n"] =
                "No Python interpreter found. Install Python 3.8+ and run:\r\n";
            d["解释器探测超时："] = "Interpreter probe timed out: ";
            d["无法启动解释器："] = "Cannot start the interpreter: ";
            d["探测 Python 失败："] = "Python probe failed: ";
            d["源类别"] = "From class";
            d["目标类别"] = "To class";
            d["没有可显示的数据"] = "No data to display";
        }
    }
}
