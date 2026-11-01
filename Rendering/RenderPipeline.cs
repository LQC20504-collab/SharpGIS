using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using SharpGIS.Models;

namespace SharpGIS.Rendering
{
    public enum RenderQuality { Fast, Full }

        /// <summary>单帧渲染的统计信息，用于状态栏显示。</summary>
    public sealed class RenderStats
    {
        /// <summary>渲染耗时（毫秒）。</summary>
        public double ElapsedMs;
        /// <summary>实际参与合成的图层数。</summary>
        public int LayersDrawn;
        /// <summary>渲染质量档位。</summary>
        public RenderQuality Quality;
        public override string ToString()
        {
            return string.Format("{0} 图层 · {1:F0} ms{2}", LayersDrawn, ElapsedMs,
                Quality == RenderQuality.Fast ? " · 快速" : "");
        }
    }

    /// <summary>
    /// 把图层栈合成到一块画布位图。
    /// 除全局背景色 <see cref="BackgroundColor"/> 外不持有可变状态；图层的 GDAL 访问由各自 ReadLock 保护。
    /// </summary>
    public static class RenderPipeline
    {
        /// <summary>画布背景色（全局，导出时可临时改）。</summary>
        public static Color BackgroundColor = Color.FromArgb(38, 40, 44);

        /// <summary>按视口渲染图层栈。</summary>
        /// <param name="view">视口。</param>
        /// <param name="layers">底→顶排列的图层快照。</param>
        /// <param name="quality">质量档位，快速档按半分辨率渲染。</param>
        /// <param name="stats">输出渲染统计。</param>
        /// <param name="background">可选背景色，null 用 <see cref="BackgroundColor"/>。</param>
        /// <returns>画布位图，调用方负责释放。</returns>
        public static Bitmap Render(Viewport view, RasterLayer[] layers, RenderQuality quality,
            out RenderStats stats, Color? background = null)
        {
            var sw = Stopwatch.StartNew();
            stats = new RenderStats { Quality = quality };

            int w = Math.Max(1, view.Width);
            int h = Math.Max(1, view.Height);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var bg = background ?? BackgroundColor;

            double qualityScale = quality == RenderQuality.Fast ? 0.5 : 1.0;
            int drawn = 0;
            // 每个图层最多切成 4 块（仅在极端缩放/旋转时发生），复用同一个数组避免每帧分配
            var placementBuffer = new TilePlacement[4];

            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                unsafe
                {
                    byte* canvas = (byte*)data.Scan0;
                    CanvasCompositor.FillBackground(canvas, w, h, data.Stride, bg.B, bg.G, bg.R);

                    if (layers != null)
                    {
                        for (int i = 0; i < layers.Length; i++)
                        {
                            var layer = layers[i];
                            if (layer == null || layer.IsDisposed || !layer.Visible) continue;

                            int blocks = RasterTileRenderer.ComputePlacements(layer, view, qualityScale, placementBuffer);
                            for (int k = 0; k < blocks; k++)
                            {
                                var p = placementBuffer[k];
                                var tile = RasterTileRenderer.RenderTile(layer, ref p, layer.Style.Resample);
                                try
                                {
                                    if (p.Rotated)
                                    {
                                        CanvasCompositor.BlendAffine(canvas, w, h, data.Stride, tile,
                                            p.OutW, p.OutH, p, layer.Style.Opacity, layer.Style.Blend);
                                    }
                                    else
                                    {
                                        CanvasCompositor.Blend(canvas, w, h, data.Stride, tile,
                                            p.OutW, p.OutH, p, layer.Style.Opacity, layer.Style.Blend);
                                    }
                                    if (k == 0) drawn++;
                                }
                                finally
                                {
                                    BufferPool.Return(tile, true);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            sw.Stop();
            stats.LayersDrawn = drawn;
            stats.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return bmp;
        }

        /// <summary>按指定地理范围与像素尺寸渲染（导出图/预览用）。</summary>
        /// <param name="extent">目标地理范围。</param>
        /// <param name="width">输出宽。</param>
        /// <param name="height">输出高。</param>
        /// <param name="layers">图层快照。</param>
        /// <param name="stats">输出渲染统计。</param>
        /// <param name="background">可选背景色。</param>
        /// <returns>画布位图。</returns>
        public static Bitmap RenderExtent(GeoRect extent, int width, int height, RasterLayer[] layers,
            out RenderStats stats, Color? background = null)
        {
            // 导出与预览一律北向上：这里是“按范围出图”，与当前视口的旋转无关
            var vp = new Viewport
            {
                CenterX = extent.CenterX,
                CenterY = extent.CenterY,
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                Angle = 0
            };
            double muppX = extent.Width / vp.Width;
            double muppY = extent.Height / vp.Height;
            vp.Mupp = Math.Max(muppX, muppY);
            return Render(vp, layers, RenderQuality.Full, out stats, background);
        }
    }
}
