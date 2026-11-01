using System;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.Processing;

using SharpGIS.Services;
namespace SharpGIS.UI.Dialogs
{
    /// <summary>格式转换 / 导出。</summary>
    public sealed class ExportDialog : ProcessingDialogBase
    {
        private readonly ComboBox _format, _compression, _bigTiff;
        private readonly CheckBox _tiled, _buildOverviews;
        private readonly NumericUpDown _blockSize, _quality;

        // UI 线程快照，供后台线程构造导出参数
        private string _snapDriver, _snapCompression, _snapBigTiff;
        private bool _snapTiled, _snapOverviews;
        private int _snapBlockSize, _snapQuality;

        public ExportDialog(RasterLayer layer) : base(layer, Lang.T("格式转换 / 导出"), "export")
        {
            _format = Row(Lang.T("输出格式"), Combo("GeoTIFF (*.tif)", "Erdas IMG (*.img)", "PNG (*.png)", "JPEG (*.jpg)", "BMP (*.bmp)", Lang.T("ASCII 网格 (*.asc)")));
            _compression = Row(Lang.T("压缩方式"), Combo("LZW", "DEFLATE", Lang.T("无"), "JPEG", "PACKBITS"));
            _tiled = Row(Lang.T("分块存储"), new CheckBox { Text = Lang.T("启用 TILED（大图推荐）"), Checked = true, ForeColor = Theme.Text, Width = 300 });
            _blockSize = Row(Lang.T("块大小"), Num(64, 2048, 256, 0, 64));
            _bigTiff = Row("BigTIFF", Combo("IF_SAFER", "YES", "NO", Lang.T("不限制")));
            _quality = Row(Lang.T("JPEG 质量"), Num(10, 100, 90));
            _buildOverviews = Row(Lang.T("金字塔"), new CheckBox { Text = Lang.T("同时建立 2/4/8/16 级金字塔"), Checked = false, ForeColor = Theme.Text, Width = 300 });
        }

        protected override void CaptureParameters()
        {
            _snapDriver = _format.SelectedIndex == 1 ? "HFA" : _format.SelectedIndex == 2 ? "PNG"
                : _format.SelectedIndex == 3 ? "JPEG" : _format.SelectedIndex == 4 ? "BMP"
                : _format.SelectedIndex == 5 ? "AAIGrid" : null;   // null 表示按扩展名推断
            _snapCompression = _compression.SelectedItem as string;
            _snapTiled = _tiled.Checked;
            _snapBlockSize = (int)_blockSize.Value;
            _snapBigTiff = _bigTiff.SelectedItem as string;
            _snapQuality = (int)_quality.Value;
            _snapOverviews = _buildOverviews.Checked;
        }

        protected override string RunCore(ProgressReporter reporter, string outputPath)
        {
            var options = new ExportOptions
            {
                OutputPath = outputPath,
                Driver = _snapDriver ?? ProcessingUtil.DriverForExtension(outputPath),
                Compression = _snapCompression,
                Tiled = _snapTiled,
                BlockSize = _snapBlockSize,
                BigTiff = _snapBigTiff,
                JpegQuality = _snapQuality,
                BuildOverviews = _snapOverviews
            };
            return RasterExportService.Run(Layer, options, reporter);
        }
    }

    /// <summary>生成金字塔。</summary>
    public sealed class OverviewDialog : ProcessingDialogBase
    {
        private readonly ComboBox _factors, _resampling;

        private int[] _snapFactors;
        private string _snapResampling;

        public OverviewDialog(RasterLayer layer) : base(layer, Lang.T("生成金字塔"), "overview")
        {
            _factors = Row(Lang.T("层级"), Combo("2, 4, 8", "2, 4, 8, 16", "2, 4, 8, 16, 32", "3, 9, 27"));
            _resampling = Row(Lang.T("重采样"), Combo("AVERAGE", "NEAREST", "GAUSS", "CUBIC", "MODE"));
            Note(Lang.T("源影像只读：会先把数据复制到输出文件，再在副本上建立内置金字塔（不写 .ovr 到源目录）。"));
        }

        protected override void CaptureParameters()
        {
            switch (_factors.SelectedIndex)
            {
                case 1: _snapFactors = new[] { 2, 4, 8, 16 }; break;
                case 2: _snapFactors = new[] { 2, 4, 8, 16, 32 }; break;
                case 3: _snapFactors = new[] { 3, 9, 27 }; break;
                default: _snapFactors = new[] { 2, 4, 8 }; break;
            }
            _snapResampling = _resampling.SelectedItem as string;
        }

        protected override string RunCore(ProgressReporter reporter, string outputPath)
        {
            return OverviewService.BuildFromLayer(Layer, outputPath, _snapFactors, _snapResampling, reporter);
        }
    }

    /// <summary>某个处理方式下各控件的可用性规则（纯逻辑，供界面与自检共用）。</summary>
    internal readonly struct WarpModeEnable
    {
        public readonly bool Srs;
        public readonly bool Resample;
        public readonly bool PixelSize;
        public readonly bool Extent;
        public readonly bool Bands;
        public readonly bool SizeOverride;
        public readonly bool Reference;

        public WarpModeEnable(bool srs, bool resample, bool pixelSize, bool extent, bool bands, bool sizeOverride, bool reference)
        {
            Srs = srs; Resample = resample; PixelSize = pixelSize;
            Extent = extent; Bands = bands; SizeOverride = sizeOverride; Reference = reference;
        }
    }

    /// <summary>处理方式与控件启用关系。索引：0 重投影 / 1 重采样 / 2 裁剪 / 3 当前视图 / 4 仅复制。</summary>
    internal static class WarpModeRules
    {
        /// <summary>取指定处理方式的控件启用矩阵，越界索引按“重投影”处理。</summary>
        /// <param name="mode">处理方式索引。</param>
        /// <returns>各控件的启用状态。</returns>
        internal static WarpModeEnable For(int mode)
        {
            switch (mode)
            {
                case 1: return new WarpModeEnable(false, true, true, false, true, true, false);
                case 2: return new WarpModeEnable(false, true, true, true, true, true, true);
                case 3: return new WarpModeEnable(false, true, true, false, true, true, false);
                case 4: return new WarpModeEnable(false, false, false, false, true, false, false);
                default: return new WarpModeEnable(true, true, false, false, true, false, false);
            }
        }
    }

    /// <summary>重投影 / 重采样 / 按范围裁剪 / 按当前视图导出 / 按参考图层对齐。</summary>
    public sealed class WarpDialog : ProcessingDialogBase
    {
        private readonly ComboBox _mode, _resample, _srsPreset, _refLayer;
        private readonly TextBox _dstSrs, _srcSrs, _bands;
        private readonly NumericUpDown _pixelSize, _pixelSizeY, _xmin, _ymin, _xmax, _ymax, _cols, _rows;
        private readonly CheckBox _useSize;

        private readonly GeoRect _viewExtent;
        private readonly LayerStack _stack;

        // UI 线程快照：完整请求参数，后台线程只读取它
        private WarpRequest _snapRequest;

        /// <summary>构造重投影/重采样/裁剪对话框。</summary>
        /// <param name="layer">源图层。</param>
        /// <param name="viewExtent">当前视图的地理范围，供“导出当前视图”与范围输入框初始填充。</param>
        /// <param name="viewPixelSize">当前视图的地图单位/像素，作为分辨率默认值。</param>
        /// <param name="initialMode">初始处理方式：0 重投影 / 1 重采样 / 2 裁剪 / 3 当前视图 / 4 仅复制。</param>
        /// <param name="referenceStack">可选的图层栈，用于“对齐参考图层”；为 null 时该能力不可用。</param>
        public WarpDialog(RasterLayer layer, GeoRect viewExtent, double viewPixelSize,
            int initialMode = 0, LayerStack referenceStack = null)
            : base(layer, Lang.T("重投影 / 重采样 / 裁剪"), "warp")
        {
            _viewExtent = viewExtent;
            _stack = referenceStack;

            _mode = Row(Lang.T("处理方式"), Combo(Lang.T("重投影到目标坐标系"), Lang.T("重采样（改变分辨率）"), Lang.T("按范围裁剪"),
                Lang.T("导出当前视图"), Lang.T("仅复制（保持原样）")));
            _srsPreset = Row(Lang.T("常用坐标系"), Combo(Lang.T("（不改变）"), Lang.T("EPSG:4326 WGS84 经纬度"), Lang.T("EPSG:3857 Web 墨卡托"),
                "EPSG:32650 WGS84 / UTM 50N", "EPSG:32651 WGS84 / UTM 51N", Lang.T("EPSG:4490 CGCS2000 经纬度"),
                "EPSG:4547 CGCS2000 / 3-degree Gauss-Kruger CM 114E"));
            _dstSrs = Row(Lang.T("目标坐标系"), Text2(""));
            _srcSrs = Row(Lang.T("源坐标系覆盖"), Text2(""));
            _resample = Row(Lang.T("重采样算法"), Combo(WarpService.ResampleChoices));

            double initialPixel = viewPixelSize > 0 ? viewPixelSize : layer.PixelSizeX;
            _pixelSize = Row(Lang.T("像元宽度"), Num(0, 1e7, initialPixel, 6, 0.5), 145);
            _pixelSizeY = Row(Lang.T("像元高度"), Num(0, 1e7, layer.PixelSizeY, 6, 0.5), 145);

            _refLayer = Row(Lang.T("对齐参考图层"), BuildReferenceCombo(), 300);

            _xmin = Num(-1e9, 1e9, viewExtent.XMin, 3, 10);
            _ymin = Num(-1e9, 1e9, viewExtent.YMin, 3, 10);
            RowInline(Lang.T("范围 X/Y 最小"), new Control[] { _xmin, _ymin }, new[] { 145, 145 });

            _xmax = Num(-1e9, 1e9, viewExtent.XMax, 3, 10);
            _ymax = Num(-1e9, 1e9, viewExtent.YMax, 3, 10);
            RowInline(Lang.T("范围 X/Y 最大"), new Control[] { _xmax, _ymax }, new[] { 145, 145 });

            _bands = Row(Lang.T("输出波段"), Text2(""), 160);

            _useSize = new CheckBox
            {
                Text = Lang.T("按行列数"),
                ForeColor = Theme.Text,
                Width = 100,
                AutoSize = false
            };
            _cols = Num(1, 200000, layer.Width, 0, 64);
            _rows = Num(1, 200000, layer.Height, 0, 64);
            RowInline(Lang.T("输出尺寸"), new Control[] { _useSize, _cols, _rows }, new[] { 100, 84, 84 });

            Note(Lang.T("源影像无投影时，重投影需填“源坐标系覆盖”（如 EPSG:32651）。\r\n")
               + Lang.T("“对齐参考图层”会用其范围与像元尺寸填 -te/-tr，要求同坐标系。\r\n")
               + Lang.T("“输出波段”留空表示全部波段。"), 60);

            _srsPreset.SelectedIndexChanged += (s, e) =>
            {
                switch (_srsPreset.SelectedIndex)
                {
                    case 1: _dstSrs.Text = "EPSG:4326"; break;
                    case 2: _dstSrs.Text = "EPSG:3857"; break;
                    case 3: _dstSrs.Text = "EPSG:32650"; break;
                    case 4: _dstSrs.Text = "EPSG:32651"; break;
                    case 5: _dstSrs.Text = "EPSG:4490"; break;
                    case 6: _dstSrs.Text = "EPSG:4547"; break;
                }
            };
            _refLayer.SelectedIndexChanged += (s, e) => ApplyReferenceLayer();
            _useSize.CheckedChanged += (s, e) => SyncMode();
            _mode.SelectedIndexChanged += (s, e) => SyncMode();

            _mode.SelectedIndex = Math.Max(0, Math.Min(4, initialMode));
            SyncMode();
        }

        private ComboBox BuildReferenceCombo()
        {
            var combo = Combo(Lang.T("（不对齐，手工填范围）"));
            if (_stack != null)
                foreach (var layer in _stack.ToArray())
                    combo.Items.Add(layer.Name);
            return combo;
        }

        /// <summary>当前选中的参考图层；未选择或栈为空时返回 null。</summary>
        private RasterLayer ReferenceLayer
        {
            get
            {
                if (_stack == null || _refLayer.SelectedIndex <= 0) return null;
                var layers = _stack.ToArray();
                int index = _refLayer.SelectedIndex - 1;
                return index >= 0 && index < layers.Length ? layers[index] : null;
            }
        }

        private void ApplyReferenceLayer()
        {
            var reference = ReferenceLayer;
            if (reference == null) return;

            _xmin.Value = Clamp((decimal)reference.Extent.XMin, _xmin);
            _ymin.Value = Clamp((decimal)reference.Extent.YMin, _ymin);
            _xmax.Value = Clamp((decimal)reference.Extent.XMax, _xmax);
            _ymax.Value = Clamp((decimal)reference.Extent.YMax, _ymax);
            _pixelSize.Value = Clamp((decimal)reference.PixelSizeX, _pixelSize);
            _pixelSizeY.Value = Clamp((decimal)reference.PixelSizeY, _pixelSizeY);
            _useSize.Checked = false;
        }

        private static decimal Clamp(decimal value, NumericUpDown box)
        {
            if (value < box.Minimum) return box.Minimum;
            if (value > box.Maximum) return box.Maximum;
            return value;
        }

        /// <summary>按当前处理方式启用/禁用控件（规则见 <see cref="WarpModeRules"/>）。</summary>
        private void SyncMode()
        {
            var rules = WarpModeRules.For(_mode.SelectedIndex);

            _dstSrs.Enabled = _srsPreset.Enabled = rules.Srs;
            _resample.Enabled = rules.Resample;
            _refLayer.Enabled = rules.Reference;
            _xmin.Enabled = _ymin.Enabled = _xmax.Enabled = _ymax.Enabled = rules.Extent;
            _bands.Enabled = rules.Bands;
            _useSize.Enabled = rules.SizeOverride;

            bool sized = rules.SizeOverride && _useSize.Checked;
            _cols.Enabled = _rows.Enabled = sized;
            _pixelSize.Enabled = rules.PixelSize && !sized;
            _pixelSizeY.Enabled = rules.PixelSize && !sized;
        }

        /// <summary>参数校验：坐标系必填、范围有效、参考图层同系、波段号可解析。</summary>
        /// <param name="error">校验失败时的中文提示。</param>
        /// <returns>参数是否可用。</returns>
        protected override bool ValidateInputs(out string error)
        {
            error = null;
            int mode = _mode.SelectedIndex;

            if (mode == 0 && string.IsNullOrWhiteSpace(_dstSrs.Text))
            {
                error = Lang.T("请指定目标坐标系，例如 EPSG:4326");
                return false;
            }
            if (!Layer.HasProjection && string.IsNullOrWhiteSpace(_srcSrs.Text) && mode == 0)
            {
                error = Lang.T("源影像没有投影信息，请先填写“源坐标系覆盖”（例如 EPSG:32651）");
                return false;
            }
            if (WarpModeRules.For(mode).Extent && (_xmax.Value <= _xmin.Value || _ymax.Value <= _ymin.Value))
            {
                error = Lang.T("裁剪范围无效：XMax 必须大于 XMin，YMax 必须大于 YMin");
                return false;
            }

            var reference = ReferenceLayer;
            if (reference != null && Layer.HasProjection && reference.HasProjection &&
                !string.Equals(reference.EpsgText, Layer.EpsgText, StringComparison.Ordinal))
            {
                error = Lang.T("参考图层坐标系（{0}）与当前图层（{1}）", reference.EpsgText, Layer.EpsgText)
                      + Lang.T("）不一致，请先重投影到同一坐标系");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_bands.Text) && ParseBands(_bands.Text, Layer.BandCount).Length == 0)
            {
                error = Lang.T("输出波段无效：应填 1~") + Layer.BandCount + Lang.T(" 之间的波段号，逗号分隔，留空表示全部");
                return false;
            }
            return true;
        }

        /// <summary>按界面参数执行 Warp。</summary>
        /// <param name="reporter">进度与取消上报器。</param>
        /// <param name="outputPath">输出文件路径。</param>
        /// <returns>输出文件路径。</returns>
        protected override void CaptureParameters()
        {
            var rules = WarpModeRules.For(_mode.SelectedIndex);
            var req = new WarpRequest
            {
                Resample = _resample.SelectedItem as string
            };

            if (rules.Bands && !string.IsNullOrWhiteSpace(_bands.Text))
                req.Bands = ParseBands(_bands.Text, Layer.BandCount);

            if (rules.SizeOverride && _useSize.Checked)
            {
                req.Width = (int)_cols.Value;
                req.Height = (int)_rows.Value;
            }
            else if (rules.PixelSize)
            {
                req.PixelSize = (double)_pixelSize.Value;
                req.PixelSizeY = (double)_pixelSizeY.Value;
            }

            if (!string.IsNullOrWhiteSpace(_srcSrs.Text))
                req.SourceSrsOverride = WarpService.NormalizeSrs(_srcSrs.Text);

            switch (_mode.SelectedIndex)
            {
                case 0:
                    req.TargetSrs = WarpService.NormalizeSrs(_dstSrs.Text);
                    break;
                case 2:
                    req.Extent = new GeoRect((double)_xmin.Value, (double)_ymin.Value,
                        (double)_xmax.Value, (double)_ymax.Value);
                    break;
                case 3:
                    req.Extent = _viewExtent;
                    if ((req.PixelSize == null || req.PixelSize <= 0) && !req.Width.HasValue)
                        req.PixelSize = Layer.PixelSizeX;
                    break;
                case 4:
                    req.PixelSize = null;
                    req.PixelSizeY = null;
                    req.Width = null;
                    req.Height = null;
                    break;
            }

            _snapRequest = req;
        }

        protected override string RunCore(ProgressReporter reporter, string outputPath)
        {
            // 只改两个与输出地址有关的字段，其余全部来自 UI 线程快照
            _snapRequest.OutputPath = outputPath;
            _snapRequest.OutputFormat = ProcessingUtil.DriverForExtension(outputPath);
            return WarpService.Run(Layer, _snapRequest, reporter);
        }
    }
}
