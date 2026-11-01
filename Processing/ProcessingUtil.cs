using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Gdal = OSGeo.GDAL.Gdal;
using OSGeo.GDAL;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
    /// <summary>处理算子公用工具：输出路径校验、格式映射、进度回调与失败清理。</summary>
    public static class ProcessingUtil
    {
        /// <summary>默认输出目录：我的文档\SharpGIS输出。</summary>
        public static string DefaultOutputDirectory
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Lang.T("SharpGIS输出"));
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        /// <summary>在默认输出目录下按“源文件名_后缀.扩展名”生成建议输出路径。</summary>
        /// <param name="sourcePath">源影像路径，用于取基础文件名。</param>
        /// <param name="suffix">追加的处理标记，如 export、warp。</param>
        /// <param name="extension">输出扩展名，默认 .tif。</param>
        /// <returns>建议的输出文件完整路径（不保证文件不存在）。</returns>
        public static string SuggestOutputPath(string sourcePath, string suffix, string extension = ".tif")
        {
            string name = Path.GetFileNameWithoutExtension(sourcePath ?? "output") + "_" + suffix + extension;
            return Path.Combine(DefaultOutputDirectory, name);
        }

        /// <summary>校验输出路径：非空、不与任何源文件同名（源数据只读），并确保目录存在。</summary>
        /// <param name="outPath">用户指定的输出文件路径。</param>
        /// <param name="sourcePaths">参与本次处理的所有源文件路径，用于拦截覆盖。</param>
        public static void ValidateOutputPath(string outPath, params string[] sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(outPath)) throw new ArgumentException(Lang.T("请指定输出文件路径"));
            string full = Path.GetFullPath(outPath);
            foreach (var src in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(src)) continue;
                if (string.Equals(Path.GetFullPath(src), full, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Lang.T("输出路径不能与源文件相同（源数据只读）:\r\n") + full);
            }
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        /// <summary>按输出文件扩展名推断 GDAL 驱动短名，未知扩展名回落 GTiff。</summary>
        /// <param name="path">输出文件路径。</param>
        /// <returns>驱动短名，如 GTiff、HFA、PNG。</returns>
        public static string DriverForExtension(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".tif":
                case ".tiff": return "GTiff";
                case ".img": return "HFA";
                case ".png": return "PNG";
                case ".jpg":
                case ".jpeg": return "JPEG";
                case ".bmp": return "BMP";
                case ".vrt": return "VRT";
                case ".asc": return "AAIGrid";
                case ".xyz": return "XYZ";
                case ".nc": return "netCDF";
                case ".grd": return "GS7BG";
                default: return "GTiff";
            }
        }

        /// <summary>删除单个文件，文件不存在或删除失败时静默忽略。</summary>
        /// <param name="path">要删除的文件路径。</param>
        public static void DeleteQuietly(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>GDAL 可能伴随主文件生成的旁文件后缀。</summary>
        private static readonly string[] SidecarSuffixes = { ".ovr", ".aux.xml", ".msk", ".rrd", ".enp" };

        /// <summary>删除主文件及其 GDAL 旁文件（.ovr/.aux.xml/.msk/.rrd/.enp），用于失败或取消后的清理。</summary>
        /// <param name="path">输出主文件路径。</param>
        public static void DeleteOutputSet(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            DeleteQuietly(path);
            foreach (var suffix in SidecarSuffixes) DeleteQuietly(path + suffix);
        }

        /// <summary>
        /// 目标文件能否被独占写入。文件不存在视为可以；被任何进程（包括本程序已打开的图层）
        /// 持有句柄时返回 false——Windows 下此时删除/覆盖都会得到 Permission denied。
        /// </summary>
        /// <param name="path">目标文件路径。</param>
        /// <returns>可写返回 true。</returns>
        public static bool IsFileWritable(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return true;
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>为一个被占用或已存在的文件生成不冲突的替代名（xxx_1.tif、xxx_2.tif …）。</summary>
        /// <param name="path">原路径。</param>
        /// <returns>可用的新路径；找不到时原样返回。</returns>
        public static string SuggestFreePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(dir, name + "_" + i + ext);
                if (!File.Exists(candidate) && !File.Exists(candidate + ".aux.xml")) return candidate;
            }
            return path;
        }

        /// <summary>读取 GDAL 最近一次错误信息，用于拼接可读的失败提示。</summary>
        /// <returns>错误文本；GDAL 未返回信息时给出占位说明。</returns>
        public static string DescribeGdalError()
        {
            try
            {
                string msg = Gdal.GetLastErrorMsg();
                if (string.IsNullOrEmpty(msg)) return Lang.T("（GDAL 未返回错误信息）");
                if (msg.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Lang.T("目标文件被占用或无写入权限（可能已在图层中打开，或被其他程序锁定）。")
                         + Lang.T("请先关闭占用它的图层，或换一个输出文件名。");
                return msg;
            }
            catch { return Lang.T("（无法读取 GDAL 错误）"); }
        }
    }

    /// <summary>
    /// GDAL 进度回调封装，同时承担取消信号的传递。
    /// 委托必须保存在字段上，否则被 GC 回收后原生层回调会崩溃。
    /// </summary>
    public sealed class ProgressReporter
    {
        private readonly Gdal.GDALProgressFuncDelegate _delegate;
        private readonly Action<double, string> _sink;
        private readonly Func<bool> _isCancelled;
        private double _lastReported = -1;
        private readonly double _minDelta;

        /// <summary>GDAL 原生回调因取消返回 0 时置位；调用方据此把 null 结果判定为“已取消”而非“失败”。</summary>
        public bool WasCancelled { get; private set; }

        /// <summary>创建进度上报器。</summary>
        /// <param name="sink">进度接收回调，参数为 0~1 的完成度与消息。</param>
        /// <param name="isCancelled">取消探测函数，返回 true 表示用户已请求取消。</param>
        /// <param name="minDelta">两次上报之间的最小增量，避免刷屏。</param>
        public ProgressReporter(Action<double, string> sink, Func<bool> isCancelled = null, double minDelta = 0.005)
        {
            _sink = sink;
            _isCancelled = isCancelled;
            _minDelta = minDelta;
            _delegate = Callback;
        }

        /// <summary>供 GDAL 原生接口使用的进度委托。</summary>
        public Gdal.GDALProgressFuncDelegate Delegate
        {
            get
            {
                GC.KeepAlive(this);
                return _delegate;
            }
        }

        /// <summary>用户是否已请求取消。</summary>
        public bool IsCancellationRequested
        {
            get { return _isCancelled != null && _isCancelled(); }
        }

        /// <summary>若已请求取消则抛出 <see cref="OperationCanceledException"/>，供算子在关键步骤前预检。</summary>
        public void ThrowIfCancellationRequested()
        {
            if (IsCancellationRequested) throw new OperationCanceledException(Lang.T("操作已取消"));
        }

        /// <summary>
        /// 手工上报进度（供自写分块循环使用）。
        /// 与 GDAL 回调不同，这里会主动检查取消并抛 <see cref="OperationCanceledException"/>，
        /// 从而让分块算子能够真正中断。
        /// </summary>
        /// <param name="complete">0~1 的完成度。</param>
        /// <param name="message">可选的进度消息。</param>
        public void Report(double complete, string message)
        {
            ThrowIfCancellationRequested();
            if (_sink == null) return;
            try { _sink(complete, message ?? ""); } catch { }
        }

        /// <summary>
        /// 不做取消检查的进度写入。供子进程 stdout 回调线程使用：这些回调运行在 IO 线程池上，
        /// 一旦抛异常会中断读取，取消应由调度方在主循环里判断。
        /// </summary>
        /// <param name="complete">0~1 的完成度。</param>
        /// <param name="message">可选的进度消息。</param>
        public void Publish(double complete, string message)
        {
            if (_sink == null) return;
            double clamped = complete < 0 ? 0 : (complete > 1 ? 1 : complete);
            try { _sink(clamped, message ?? ""); } catch { }
        }

        /// <summary>
        /// 派生一个把 0~1 映射到 [from,to] 区间的子进度器，用于多阶段算子（如先复制再建金字塔）。
        /// 子进度器共享取消探测函数。
        /// </summary>
        /// <param name="from">子阶段起始比例。</param>
        /// <param name="to">子阶段结束比例。</param>
        /// <param name="prefix">该阶段固定的提示文字，为空时沿用 GDAL 消息。</param>
        /// <returns>新的进度上报器。</returns>
        public ProgressReporter Segment(double from, double to, string prefix = null)
        {
            var parent = this;
            return new ProgressReporter(
                (value, message) => parent.MapReport(from, to, prefix, value, message),
                _isCancelled, _minDelta);
        }

        private void MapReport(double from, double to, string prefix, double value, string message)
        {
            if (_sink == null) return;
            double clamped = value < 0 ? 0 : (value > 1 ? 1 : value);
            try
            {
                _sink(from + (to - from) * clamped,
                     string.IsNullOrEmpty(prefix) ? (message ?? "") : prefix);
            }
            catch { }
        }

        private int Callback(double complete, IntPtr messagePtr, IntPtr data)
        {
            if (complete - _lastReported >= _minDelta || complete >= 1.0)
            {
                _lastReported = complete;
                if (_sink != null)
                {
                    try
                    {
                        _sink(complete,
                            messagePtr == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(messagePtr));
                    }
                    catch { }
                }
            }
            if (IsCancellationRequested)
            {
                WasCancelled = true;
                return 0; // 返回 0 表示取消，GDAL 会中止并返回 null
            }
            return 1;
        }
    }

    /// <summary>
    /// GDAL 命令行选项拼装器。数字统一用 InvariantCulture 输出，避免小数点被本地化。
    /// </summary>
    public sealed class OptionBuilder
    {
        private readonly List<string> _items = new List<string>();

        /// <summary>追加一个原样 token（如 "-b"）。</summary>
        /// <param name="value">要追加的选项或参数。</param>
        /// <returns>自身，便于链式调用。</returns>
        public OptionBuilder Add(string value)
        {
            _items.Add(value);
            return this;
        }

        /// <summary>追加一个以 InvariantCulture 格式化的浮点参数。</summary>
        /// <param name="key">选项名，如 "-tr"。</param>
        /// <param name="value">参数值。</param>
        /// <returns>自身，便于链式调用。</returns>
        public OptionBuilder Add(string key, double value)
        {
            _items.Add(key);
            _items.Add(value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>追加“选项 + 单个参数”两个 token。</summary>
        /// <param name="option">选项名，如 "-of"。</param>
        /// <param name="value">参数值。</param>
        /// <returns>自身，便于链式调用。</returns>
        public OptionBuilder AddPair(string option, string value)
        {
            _items.Add(option);
            _items.Add(value);
            return this;
        }

        /// <summary>追加一个创建选项（自动补 "-co" 前缀，值不带前缀）。</summary>
        /// <param name="value">形如 TILED=YES 的创建选项。</param>
        /// <returns>自身，便于链式调用。</returns>
        public OptionBuilder CreationOption(string value)
        {
            _items.Add("-co");
            _items.Add(value);
            return this;
        }

        /// <summary>导出为可直接传给 GDAL 的选项数组。</summary>
        /// <returns>按 argv 顺序排列的选项数组。</returns>
        public string[] ToArray() { return _items.ToArray(); }
    }
}
