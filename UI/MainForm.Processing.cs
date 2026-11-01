using System;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.UI.Dialogs;

using SharpGIS.Services;
namespace SharpGIS.UI
{
    /// <summary>处理工具箱菜单入口：打开参数对话框，执行后按需加载结果。</summary>
    public partial class MainForm
    {
        private bool RequireLayer(out RasterLayer layer)
        {
            layer = CurrentLayer;
            if (layer == null)
            {
                Warn(Lang.T("请先打开并选中一个栅格图层"));
                return false;
            }
            return true;
        }

        private void AfterProcess(ProcessingDialogBase dialog)
        {
            if (dialog == null) return;
            string path = dialog.OutputPath;
            if (string.IsNullOrEmpty(path)) return;

            if (dialog.ShouldLoad)
            {
                var layer = OpenRaster(path, false);
                if (layer != null)
                {
                    Info(Lang.T("处理完成并已加载：\r\n") + path);
                    return;
                }
            }
            Info(Lang.T("处理完成：\r\n") + path);
        }

        private void ShowDialog(ProcessingDialogBase dialog)
        {
            if (dialog == null) return;
            using (dialog)
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    AfterProcess(dialog);
            }
        }

        private void OnExport()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;
            ShowDialog(new ExportDialog(layer));
        }

        private void OnWarp()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;
            ShowDialog(new WarpDialog(layer, _canvas.View.GetGeoRect(), _canvas.View.Mupp, 0, _stack));
        }

        private void OnCrop()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;
            // 直接进入“按范围裁剪”模式，避免用户被要求填目标坐标系
            ShowDialog(new WarpDialog(layer, _canvas.View.GetGeoRect(), _canvas.View.Mupp, 2, _stack));
        }

        private void OnOverviews()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;
            ShowDialog(new OverviewDialog(layer));
        }

        private void OnBandCalc()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;
            ShowDialog(new BandCalcDialog(layer, _stack));
        }

        private void OnDem()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;
            ShowDialog(new DemDialog(layer));
        }

        private void OnFilter()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;
            ShowDialog(new FilterDialog(layer));
        }

        private void OnEnhance()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;
            ShowDialog(new EnhanceDialog(layer));
        }

        private void OnFusion()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;
            if (_stack.Count < 2)
            {
                Warn(Lang.T("影像融合需要至少两个图层（多光谱 + 全色）"));
                return;
            }
            ShowDialog(new FusionDialog(layer, _stack));
        }

        private void OnClassify()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;
            ShowDialog(new KMeansDialog(layer));
        }

        /// <summary>土地利用变化预测：参数对话框 → 处理 → 可选打开结果图表窗口。</summary>
        private void OnCaMarkov()
        {
            RasterLayer layer;
            if (!RequireLayer(out layer)) return;

            using (var dialog = new CaMarkovDialog(layer, _stack))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                AfterProcess(dialog);
                if (dialog.ShowResultAfter && dialog.LastResult != null)
                {
                    using (var viewer = new CaMarkovResultDialog(dialog.LastResult, Lang.T("土地利用变化预测结果")))
                        viewer.ShowDialog(this);
                }
            }
        }
    }
}
