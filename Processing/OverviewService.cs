using System;
using System.IO;
using OSGeo.GDAL;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
    /// <summary>
    /// 金字塔（overview）构建。源数据只读，因此先把数据复制到目标文件，再在副本上建立内置金字塔，
    /// 不会在源目录生成 .ovr。
    /// </summary>
    public static class OverviewService
    {
        /// <summary>把图层复制到目标文件并建立金字塔（两阶段进度：0–0.4 复制、0.4–1.0 建塔）。</summary>
        /// <param name="layer">源图层（只读）。</param>
        /// <param name="outPath">输出文件路径，扩展名决定格式。</param>
        /// <param name="factors">降采样因子，如 2/4/8/16。</param>
        /// <param name="resampling">建塔重采样算法，AVERAGE/NEAREST/GAUSS/CUBIC/MODE。</param>
        /// <param name="progress">进度与取消上报器，可为 null。</param>
        /// <returns>输出文件路径。</returns>
        public static string BuildFromLayer(RasterLayer layer, string outPath, int[] factors,
            string resampling, ProgressReporter progress)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (string.IsNullOrWhiteSpace(outPath)) throw new ArgumentException(Lang.T("请指定输出文件"));
            ProcessingUtil.ValidateOutputPath(outPath, layer.SourcePath, layer.Path);
            if (progress != null) progress.ThrowIfCancellationRequested();

            var copyPhase = progress != null ? progress.Segment(0.0, 0.4, Lang.T("复制数据…")) : null;
            var buildPhase = progress != null ? progress.Segment(0.4, 1.0, Lang.T("建立金字塔…")) : null;

            // 1) 先复制一份（含地理参考），避免在源文件旁生成 .ovr
            var driver = Gdal.GetDriverByName(ProcessingUtil.DriverForExtension(outPath));
            if (driver == null) throw new InvalidOperationException(Lang.T("不支持该输出格式: ") + outPath);

            Dataset copy = null;
            try
            {
                var co = Path.GetExtension(outPath).ToLowerInvariant() == ".tif"
                    ? new[] { "TILED=YES", "COMPRESS=LZW", "COPY_SRC_OVERVIEWS=NO" }
                    : new string[0];
                copy = driver.CreateCopy(outPath, layer.Ds, 0, co,
                    copyPhase != null ? copyPhase.Delegate : null, null);
                if (copy == null)
                {
                    if (copyPhase != null && copyPhase.WasCancelled) throw new OperationCanceledException(Lang.T("操作已取消"));
                    throw new InvalidOperationException(Lang.T("复制数据失败：") + ProcessingUtil.DescribeGdalError());
                }
                copy.FlushCache();
                if (copyPhase != null) copyPhase.Report(1.0, Lang.T("复制完成"));
            }
            catch
            {
                ProcessingUtil.DeleteOutputSet(outPath); // 失败/取消时不留下半成品
                throw;
            }
            finally
            {
                if (copy != null) copy.Dispose();
            }

            // 2) 以可写方式打开并建立金字塔
            try
            {
                Build(outPath, factors, buildPhase, resampling);
                if (buildPhase != null) buildPhase.Report(1.0, Lang.T("金字塔完成"));
            }
            catch
            {
                ProcessingUtil.DeleteOutputSet(outPath);
                throw;
            }
            return outPath;
        }

        /// <summary>在已存在的文件上建立内置金字塔（需要以 GA_Update 打开）。</summary>
        /// <param name="path">目标文件路径，必须可写。</param>
        /// <param name="factors">降采样因子，内部会排序副本，不修改调用方数组。</param>
        /// <param name="progress">进度与取消上报器，可为 null。</param>
        /// <param name="resampling">重采样算法名，默认 AVERAGE。</param>
        public static void Build(string path, int[] factors, ProgressReporter progress, string resampling = "AVERAGE")
        {
            var list = factors != null && factors.Length > 0 ? (int[])factors.Clone() : new[] { 2, 4, 8, 16 };
            Array.Sort(list);

            if (progress != null) progress.ThrowIfCancellationRequested();

            Dataset ds = null;
            try
            {
                ds = Gdal.Open(path, Access.GA_Update);
                if (ds == null)
                    throw new InvalidOperationException(
                        Lang.T("无法以可写方式打开: {0}（{1}）", path, ProcessingUtil.DescribeGdalError()));

                int ret = ds.BuildOverviews(resampling ?? "AVERAGE", list,
                    progress != null ? progress.Delegate : null, null);
                if (ret != 0)
                {
                    if (progress != null && progress.WasCancelled) throw new OperationCanceledException(Lang.T("操作已取消"));
                    throw new InvalidOperationException(Lang.T("建立金字塔失败：") + ProcessingUtil.DescribeGdalError());
                }
                ds.FlushCache();
            }
            finally
            {
                if (ds != null) ds.Dispose();
            }
        }
    }
}
