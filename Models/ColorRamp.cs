using System;
using System.Collections.Generic;
using System.Drawing;

using SharpGIS.Services;
namespace SharpGIS.Models
{
        /// <summary>色带：由若干控制点线性插值，采样为 0~1 归一化色值。</summary>
    public sealed class ColorRamp
    {
        /// <summary>
        /// 显示名。随界面语言变化——构造时只保存中文键，
        /// 显示时再翻译，否则语言切换后拿到的是固化的中文。
        /// </summary>
        public string Name { get { return Lang.T(Key); } }

        /// <summary>中文名（同时是反查用的稳定标识）。</summary>
        public string Key { get; private set; }

        public Color[] Stops { get; private set; }
        public float[] Positions { get; private set; }

        private ColorRamp(string key, Color[] stops, float[] positions)
        {
            Key = key;
            Stops = stops;
            Positions = positions;
        }

        public override string ToString() { return Name; }

        /// <summary>按归一化位置取色。</summary>
        /// <param name="t">0~1 的位置。</param>
        /// <returns>插值后的颜色。</returns>
        public Color Sample(double t)
        {
            if (t <= 0) return Stops[0];
            if (t >= 1) return Stops[Stops.Length - 1];
            for (int i = 1; i < Stops.Length; i++)
            {
                if (t <= Positions[i])
                {
                    float span = Positions[i] - Positions[i - 1];
                    float f = span <= 0 ? 0 : (float)((t - Positions[i - 1]) / span);
                    Color a = Stops[i - 1], b = Stops[i];
                    return Color.FromArgb(
                        Lerp(a.R, b.R, f), Lerp(a.G, b.G, f), Lerp(a.B, b.B, f));
                }
            }
            return Stops[Stops.Length - 1];
        }

        private static int Lerp(int a, int b, float f)
        {
            return (int)Math.Round(a + (b - a) * f);
        }

        // ---------------- 内置色带 ----------------

        public static readonly ColorRamp Gray = new ColorRamp("灰阶",
            new[] { Color.Black, Color.White }, new[] { 0f, 1f });

        public static readonly ColorRamp InverseGray = new ColorRamp("反灰阶",
            new[] { Color.White, Color.Black }, new[] { 0f, 1f });

        public static readonly ColorRamp Jet = new ColorRamp("Jet（彩虹）", new[]
        {
            Color.FromArgb(0, 0, 128), Color.Blue, Color.Cyan, Color.Lime, Color.Yellow,
            Color.Orange, Color.Red, Color.FromArgb(128, 0, 0)
        }, new[] { 0f, 0.125f, 0.375f, 0.625f, 0.75f, 0.875f, 0.95f, 1f });

        public static readonly ColorRamp Turbo = new ColorRamp("Turbo", new[]
        {
            Color.FromArgb(48, 18, 59), Color.FromArgb(70, 107, 227), Color.FromArgb(62, 188, 190),
            Color.FromArgb(70, 200, 68), Color.FromArgb(194, 209, 38), Color.FromArgb(237, 150, 36),
            Color.FromArgb(213, 58, 40), Color.FromArgb(128, 5, 18)
        }, new[] { 0f, 0.15f, 0.33f, 0.52f, 0.68f, 0.82f, 0.93f, 1f });

        public static readonly ColorRamp Terrain = new ColorRamp("地形", new[]
        {
            Color.FromArgb(48, 86, 156), Color.FromArgb(120, 170, 210), Color.FromArgb(60, 140, 60),
            Color.FromArgb(200, 200, 120), Color.FromArgb(150, 110, 70), Color.FromArgb(240, 240, 240)
        }, new[] { 0f, 0.16f, 0.35f, 0.6f, 0.8f, 1f });

        public static readonly ColorRamp Heat = new ColorRamp("热力", new[]
        {
            Color.Black, Color.FromArgb(120, 0, 0), Color.Red, Color.Yellow, Color.White
        }, new[] { 0f, 0.3f, 0.6f, 0.85f, 1f });

        public static readonly ColorRamp Viridis = new ColorRamp("Viridis", new[]
        {
            Color.FromArgb(68, 1, 84), Color.FromArgb(59, 82, 139), Color.FromArgb(33, 145, 140),
            Color.FromArgb(94, 201, 98), Color.FromArgb(253, 231, 37)
        }, new[] { 0f, 0.25f, 0.5f, 0.75f, 1f });

        public static readonly ColorRamp BlueToRed = new ColorRamp("蓝-红", new[]
        {
            Color.FromArgb(20, 60, 160), Color.FromArgb(220, 220, 220), Color.FromArgb(180, 30, 30)
        }, new[] { 0f, 0.5f, 1f });

        public static readonly ColorRamp Ndvi = new ColorRamp("植被（NDVI）", new[]
        {
            Color.FromArgb(140, 90, 40), Color.FromArgb(230, 220, 160), Color.FromArgb(150, 200, 80),
            Color.FromArgb(20, 120, 40)
        }, new[] { 0f, 0.42f, 0.62f, 1f });

        /// <summary>全部内置色带（供 UI 下拉）。</summary>
        public static readonly ColorRamp[] BuiltIn =
        {
            Gray, InverseGray, Jet, Turbo, Terrain, Heat, Viridis, BlueToRed, Ndvi
        };

        /// <summary>按名称取内置色带，找不到时返回灰阶。</summary>
        /// <param name="name">色带名称。</param>
        /// <returns>对应的色带对象。</returns>
        public static ColorRamp ByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return Gray;
            foreach (var r in BuiltIn)
            {
                // 显示名（可能已翻译）优先，中文键作为稳定后备——
                // 这样同一份样式在切换语言后仍能找回原来的色带。
                if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) return r;
                if (string.Equals(r.Key, name, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return Gray;
        }

        /// <summary>生成用于分类结果图例的离散调色板。</summary>
        /// <param name="count">类别数。</param>
        /// <returns>长度为 count 的颜色列表。</returns>
        public static List<Color> ClassPalette(int count)
        {
            var baseColors = new[]
            {
                Color.FromArgb(228, 26, 28), Color.FromArgb(55, 126, 184), Color.FromArgb(77, 175, 74),
                Color.FromArgb(152, 78, 163), Color.FromArgb(255, 127, 0), Color.FromArgb(255, 255, 51),
                Color.FromArgb(166, 86, 40), Color.FromArgb(247, 129, 191), Color.FromArgb(153, 153, 153),
                Color.FromArgb(102, 194, 165), Color.FromArgb(252, 141, 98), Color.FromArgb(141, 160, 203)
            };
            var list = new List<Color>();
            for (int i = 0; i < count; i++) list.Add(baseColors[i % baseColors.Length]);
            return list;
        }
    }
}
