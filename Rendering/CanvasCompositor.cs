using System;
using SharpGIS.Models;

namespace SharpGIS.Rendering
{
    /// <summary>
    /// 把 tile 合成到画布缓冲（32bppPArgb，内存序 B,G,R,A）。
    /// alpha 在合成阶段显式计算（opacity × tile alpha），因此 NoData 与半透明叠层都是精确的。
    /// </summary>
    public static class CanvasCompositor
    {
        /// <summary>用背景色填满画布缓冲。</summary>
        /// <param name="canvas">画布首地址（32bppPArgb）。</param>
        /// <param name="width">画布宽。</param>
        /// <param name="height">画布高。</param>
        /// <param name="stride">行字节数。</param>
        /// <param name="b">背景蓝分量。</param>
        /// <param name="g">背景绿分量。</param>
        /// <param name="r">背景红分量。</param>
        public static unsafe void FillBackground(byte* canvas, int width, int height, int stride,
            byte b, byte g, byte r)
        {
            for (int y = 0; y < height; y++)
            {
                byte* p = canvas + y * stride;
                for (int x = 0; x < width; x++)
                {
                    p[0] = b; p[1] = g; p[2] = r; p[3] = 255;
                    p += 4;
                }
            }
        }

        /// <summary>把一个 RGBA tile 按透明度与混合模式合成到画布（支持最近邻放大）。</summary>
        /// <param name="canvas">画布首地址。</param>
        /// <param name="canvasW">画布宽。</param>
        /// <param name="canvasH">画布高。</param>
        /// <param name="stride">画布行字节数。</param>
        /// <param name="tile">tile 像素缓冲（B,G,R,A）。</param>
        /// <param name="tileW">tile 宽。</param>
        /// <param name="tileH">tile 高。</param>
        /// <param name="p">tile 的目标矩形与源窗口。</param>
        /// <param name="opacity">图层不透明度 0~1。</param>
        /// <param name="mode">混合模式。</param>
        public static unsafe void Blend(byte* canvas, int canvasW, int canvasH, int stride,
            byte[] tile, int tileW, int tileH, TilePlacement p, double opacity, BlendMode mode)
        {
            if (tile == null || tileW <= 0 || tileH <= 0) return;
            int alphaScale = (int)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 255);
            if (alphaScale <= 0) return;

            int x0 = Math.Max(0, p.DstX);
            int y0 = Math.Max(0, p.DstY);
            int x1 = Math.Min(canvasW, p.DstX + p.DstW);
            int y1 = Math.Min(canvasH, p.DstY + p.DstH);
            if (x1 <= x0 || y1 <= y0) return;

            fixed (byte* pt = tile)
            {
                for (int y = y0; y < y1; y++)
                {
                    int ty = (int)((long)(y - p.DstY) * tileH / Math.Max(1, p.DstH));
                    if (ty < 0) ty = 0; else if (ty >= tileH) ty = tileH - 1;
                    byte* srcRow = pt + (long)ty * tileW * 4;
                    byte* dstRow = canvas + (long)y * stride;

                    for (int x = x0; x < x1; x++)
                    {
                        int tx = (int)((long)(x - p.DstX) * tileW / Math.Max(1, p.DstW));
                        if (tx < 0) tx = 0; else if (tx >= tileW) tx = tileW - 1;
                        byte* s = srcRow + tx * 4;
                        int a = s[3] * alphaScale / 255;
                        if (a == 0) continue;

                        MixPixel(dstRow + x * 4, s[2], s[1], s[0], a, mode);
                    }
                }
            }
        }

        /// <summary>
        /// 把一个源像素按透明度与混合模式混到画布像素上（Blend 与 BlendAffine 共用，避免两份颜色算法漂移）。
        /// </summary>
        /// <param name="d">目标像素（B,G,R,A）。</param>
        /// <param name="sr">源红。</param>
        /// <param name="sg">源绿。</param>
        /// <param name="sb">源蓝。</param>
        /// <param name="a">实际应用的 alpha（0~255，已乘图层不透明度）。</param>
        /// <param name="mode">混合模式。</param>
        private static unsafe void MixPixel(byte* d, int sr, int sg, int sb, int a, BlendMode mode)
        {
            int dr = d[2], dg = d[1], db = d[0];

            if (mode == BlendMode.Multiply)
            {
                sr = sr * dr / 255;
                sg = sg * dg / 255;
                sb = sb * db / 255;
            }
            else if (mode == BlendMode.Screen)
            {
                sr = 255 - (255 - sr) * (255 - dr) / 255;
                sg = 255 - (255 - sg) * (255 - dg) / 255;
                sb = 255 - (255 - sb) * (255 - db) / 255;
            }

            if (a >= 255)
            {
                d[0] = (byte)sb; d[1] = (byte)sg; d[2] = (byte)sr; d[3] = 255;
            }
            else
            {
                d[0] = (byte)((sb * a + db * (255 - a)) / 255);
                d[1] = (byte)((sg * a + dg * (255 - a)) / 255);
                d[2] = (byte)((sr * a + dr * (255 - a)) / 255);
                d[3] = 255;
            }
        }

        /// <summary>
        /// 旋转合成：tile 在“未旋转屏幕空间”中对齐 <see cref="TilePlacement"/> 的 Q 矩形，
        /// 这里把画布每个像素反旋回该空间再双线性采样，从而整体施加视图旋转。
        /// 只有落在 Q 矩形内的像素会被写入，因此画布四角自然保留背景色。
        /// </summary>
        /// <param name="canvas">画布首地址。</param>
        /// <param name="canvasW">画布宽。</param>
        /// <param name="canvasH">画布高。</param>
        /// <param name="stride">画布行字节数。</param>
        /// <param name="tile">tile 像素缓冲（B,G,R,A）。</param>
        /// <param name="tileW">tile 宽。</param>
        /// <param name="tileH">tile 高。</param>
        /// <param name="p">放置信息（含未旋转目标矩形与旋转系数）。</param>
        /// <param name="opacity">图层不透明度 0~1。</param>
        /// <param name="mode">混合模式。</param>
        public static unsafe void BlendAffine(byte* canvas, int canvasW, int canvasH, int stride,
            byte[] tile, int tileW, int tileH, TilePlacement p, double opacity, BlendMode mode)
        {
            if (tile == null || tileW <= 0 || tileH <= 0) return;
            int alphaScale = (int)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 255);
            if (alphaScale <= 0) return;

            double c = p.QC, s = p.QS;
            double qw = p.QX1 - p.QX0, qh = p.QY1 - p.QY0;
            if (qw <= 0 || qh <= 0) return;

            double scaleX = tileW / qw;
            double scaleY = tileH / qh;
            double cx = canvasW * 0.5, cy = canvasH * 0.5;

            // 旋转后四边形在屏幕上的外接矩形，限定循环范围（小图层叠在大画布上时省掉大量空转）
            double qcx = (p.QX0 + p.QX1) * 0.5, qcy = (p.QY0 + p.QY1) * 0.5;
            double hw = qw * 0.5, hh = qh * 0.5;
            double ex = Math.Abs(c) * hw + Math.Abs(s) * hh;
            double ey = Math.Abs(s) * hw + Math.Abs(c) * hh;
            double scx = cx + c * (qcx - cx) - s * (qcy - cy);
            double scy = cy + s * (qcx - cx) + c * (qcy - cy);

            int x0 = Math.Max(0, (int)Math.Floor(scx - ex));
            int x1 = Math.Min(canvasW, (int)Math.Ceiling(scx + ex) + 1);
            int y0 = Math.Max(0, (int)Math.Floor(scy - ey));
            int y1 = Math.Min(canvasH, (int)Math.Ceiling(scy + ey) + 1);
            if (x1 <= x0 || y1 <= y0) return;

            fixed (byte* pt = tile)
            {
                for (int y = y0; y < y1; y++)
                {
                    double dy = y + 0.5 - cy;
                    double dx0 = x0 + 0.5 - cx;
                    // 反旋转到“未旋转屏幕空间”，之后每像素只做两次加法
                    double ux = cx + c * dx0 + s * dy;
                    double uy = cy - s * dx0 + c * dy;
                    byte* dstRow = canvas + (long)y * stride;

                    for (int x = x0; x < x1; x++, ux += c, uy -= s)
                    {
                        if (ux < p.QX0 || ux >= p.QX1 || uy < p.QY0 || uy >= p.QY1) continue;

                        double fx = (ux - p.QX0) * scaleX - 0.5;
                        double fy = (uy - p.QY0) * scaleY - 0.5;

                        int r, g, b, a;
                        BilinearPremul(pt, tileW, tileH, fx, fy, out r, out g, out b, out a);
                        if (a <= 0) continue;

                        int alpha = a * alphaScale / 255;
                        if (alpha <= 0) continue;
                        MixPixel(dstRow + x * 4, r, g, b, alpha, mode);
                    }
                }
            }
        }

        /// <summary>
        /// 双线性采样并按 alpha 加权（预乘），使 NoData/透明边缘不会渗出黑边。
        /// </summary>
        /// <param name="pt">tile 首地址。</param>
        /// <param name="tileW">tile 宽。</param>
        /// <param name="tileH">tile 高。</param>
        /// <param name="fx">采样点 X（像素中心为整数 + 0.5，这里传入的是“像素索引 - 0.5”）。</param>
        /// <param name="fy">采样点 Y。</param>
        /// <param name="r">输出红。</param>
        /// <param name="g">输出绿。</param>
        /// <param name="b">输出蓝。</param>
        /// <param name="a">输出 alpha（0~255）。</param>
        private static unsafe void BilinearPremul(byte* pt, int tileW, int tileH, double fx, double fy,
            out int r, out int g, out int b, out int a)
        {
            r = 0; g = 0; b = 0; a = 0;
            if (fx <= -1 || fy <= -1 || fx >= tileW || fy >= tileH) return;

            int x0 = (int)Math.Floor(fx);
            int y0 = (int)Math.Floor(fy);
            double wx = fx - x0, wy = fy - y0;

            double sumW = 0, sumR = 0, sumG = 0, sumB = 0;
            for (int j = 0; j <= 1; j++)
            {
                double wj = j == 0 ? 1 - wy : wy;
                if (wj <= 0) continue;
                int y = y0 + j;
                if (y < 0) y = 0; else if (y >= tileH) y = tileH - 1;

                for (int i = 0; i <= 1; i++)
                {
                    double wi = i == 0 ? 1 - wx : wx;
                    if (wi <= 0) continue;
                    int x = x0 + i;
                    if (x < 0) x = 0; else if (x >= tileW) x = tileW - 1;

                    byte* s = pt + ((long)y * tileW + x) * 4;
                    double w = wi * wj * s[3];
                    if (w <= 0) continue;
                    sumW += w;
                    sumB += w * s[0];
                    sumG += w * s[1];
                    sumR += w * s[2];
                }
            }

            if (sumW < 0.5) return;          // 四个抽头全透明 → 不写
            // sumW 是 Σ(权重×alpha)，sumB/G/R 是 Σ(权重×alpha×颜色)，因此直接相除即为按 alpha 加权的颜色；
            // 注意不能再乘 255（alpha 因子已经包含在分子分母里）。
            double inv = 1.0 / sumW;
            b = (int)(sumB * inv + 0.5);
            g = (int)(sumG * inv + 0.5);
            r = (int)(sumR * inv + 0.5);
            a = (int)(sumW > 255 ? 255 : sumW + 0.5);
        }
    }
}
