using System;
using System.Collections.Generic;
using SharpGIS.Models;

using SharpGIS.Services;
namespace SharpGIS.Processing
{
    /// <summary>
    /// 占位符槽位（b1、b2 …）到实际 (图层, 波段) 的映射表。
    /// 槽位号 1 基，容量固定为当前图层的波段总数；默认按 1:1 映射到当前图层。
    /// </summary>
    public sealed class BandMapping
    {
        private readonly BandSource[] _slots;   // 下标 1..Count 有效，_slots[0] 不用

        /// <summary>创建一个空映射表。</summary>
        /// <param name="slotCount">槽位数量（= 当前图层波段数）。</param>
        public BandMapping(int slotCount)
        {
            if (slotCount < 1) slotCount = 1;
            _slots = new BandSource[slotCount + 1];
        }

        /// <summary>槽位数量。</summary>
        public int Count { get { return _slots.Length - 1; } }

        /// <summary>取某个槽位映射到的波段；未指定或越界返回 null。</summary>
        /// <param name="slot">槽位号（1 基）。</param>
        /// <returns>波段引用或 null。</returns>
        public BandSource Resolve(int slot)
        {
            if (slot < 1 || slot >= _slots.Length) return null;
            return _slots[slot];
        }

        /// <summary>该槽位是否已指定实际波段。</summary>
        /// <param name="slot">槽位号（1 基）。</param>
        /// <returns>已指定返回 true。</returns>
        public bool IsMapped(int slot)
        {
            return Resolve(slot) != null;
        }

        /// <summary>指定槽位对应的波段；传 null 等于清除。</summary>
        /// <param name="slot">槽位号（1 基）。</param>
        /// <param name="source">波段引用。</param>
        public void Set(int slot, BandSource source)
        {
            if (slot < 1 || slot >= _slots.Length)
                throw new ArgumentOutOfRangeException(nameof(slot), slot, Lang.T("槽位超出映射表范围（共 ") + Count + Lang.T(" 个）"));
            _slots[slot] = source;
        }

        /// <summary>已指定的槽位号（升序去重）。</summary>
        /// <returns>槽位号数组。</returns>
        public int[] MappedSlots()
        {
            var list = new List<int>();
            for (int i = 1; i < _slots.Length; i++)
                if (_slots[i] != null) list.Add(i);
            return list.ToArray();
        }

        /// <summary>映射表里涉及到的图层（按首次出现顺序去重）。</summary>
        /// <returns>图层数组。</returns>
        public RasterLayer[] DistinctLayers()
        {
            var list = new List<RasterLayer>();
            for (int i = 1; i < _slots.Length; i++)
            {
                var src = _slots[i];
                if (src == null) continue;
                bool seen = false;
                for (int j = 0; j < list.Count; j++)
                    if (ReferenceEquals(list[j], src.Layer)) { seen = true; break; }
                if (!seen) list.Add(src.Layer);
            }
            return list.ToArray();
        }

        /// <summary>复制一份映射表。</summary>
        /// <returns>新实例（波段引用不可变，可共享）。</returns>
        public BandMapping Clone()
        {
            var copy = new BandMapping(Count);
            Array.Copy(_slots, copy._slots, _slots.Length);
            return copy;
        }

        /// <summary>构造 1:1 映射：b_i → 该图层的第 i 波段。</summary>
        /// <param name="layer">参考图层。</param>
        /// <returns>映射表。</returns>
        public static BandMapping Identity(RasterLayer layer)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            var map = new BandMapping(layer.BandCount);
            for (int i = 1; i <= layer.BandCount; i++)
                map._slots[i] = new BandSource(layer, i);
            return map;
        }

        /// <summary>由“槽位 → 波段”的字典构造映射表，越界槽位会被忽略。</summary>
        /// <param name="slotCount">槽位数量。</param>
        /// <param name="sources">槽位到波段的字典。</param>
        /// <returns>映射表。</returns>
        public static BandMapping FromSlots(int slotCount, IDictionary<int, BandSource> sources)
        {
            var map = new BandMapping(slotCount);
            if (sources != null)
            {
                foreach (var pair in sources)
                {
                    if (pair.Key >= 1 && pair.Key <= map.Count && pair.Value != null)
                        map._slots[pair.Key] = pair.Value;
                }
            }
            return map;
        }

        /// <summary>检查表达式用到的槽位是否都已指定，并确认引用的图层仍然可用。</summary>
        /// <param name="usedSlots">表达式用到的槽位号。</param>
        /// <returns>首条中文错误；全部可用时返回 null。</returns>
        public string CheckCoverage(int[] usedSlots)
        {
            if (usedSlots == null) return null;
            foreach (var slot in usedSlots)
            {
                var src = Resolve(slot);
                if (src == null)
                    return Lang.T("占位符 b") + slot + Lang.T(" 还没有指定实际波段，请在右侧“波段映射”里为它选择一个波段");
                if (!src.IsValid)
                    return Lang.T("图层 ") + src.Layer.Name + Lang.T(" 已关闭或波段不足，请重新打开后再计算");
            }
            return null;
        }

        /// <summary>检查所有已映射的波段是否与参考图层同网格（宽高 + 地理变换）。</summary>
        /// <param name="reference">参考图层，通常是输出所用的图层。</param>
        /// <returns>首条中文错误；全部同网格时返回 null。</returns>
        public string CheckGrid(RasterLayer reference)
        {
            if (reference == null) return Lang.T("缺少参考图层");
            for (int i = 1; i < _slots.Length; i++)
            {
                var src = _slots[i];
                if (src == null) continue;
                string reason;
                if (!RasterGrid.Same(reference, src.Layer, out reason))
                    return src.Layer.Name + Lang.T(" 与当前图层网格不一致：") + reason + Lang.T("，不能参与波段计算");
            }
            return null;
        }

        /// <summary>列出“网格一致但投影不同”的图层名，用于界面提示（不阻塞计算）。</summary>
        /// <param name="reference">参考图层。</param>
        /// <returns>图层名数组。</returns>
        public string[] ProjectionMismatches(RasterLayer reference)
        {
            var list = new List<string>();
            var layers = DistinctLayers();
            foreach (var l in layers)
            {
                if (ReferenceEquals(l, reference)) continue;
                string reason;
                if (!RasterGrid.Same(reference, l, out reason)) continue;
                if (!RasterGrid.SameProjection(reference, l)) list.Add(l.Name);
            }
            return list.ToArray();
        }
    }

    /// <summary>同网格判定：宽高一致 + 6 个地理变换系数一致（投影不参与判定）。</summary>
    public static class RasterGrid
    {
        /// <summary>判断两个图层是否可用于同一次逐像素运算。</summary>
        /// <param name="a">图层 A。</param>
        /// <param name="b">图层 B。</param>
        /// <param name="reason">不一致时的中文原因。</param>
        /// <returns>同网格返回 true。</returns>
        public static bool Same(RasterLayer a, RasterLayer b, out string reason)
        {
            reason = null;
            if (a == null || b == null) { reason = Lang.T("图层为空"); return false; }
            if (ReferenceEquals(a, b)) return true;
            if (a.IsDisposed || b.IsDisposed) { reason = Lang.T("图层已关闭"); return false; }

            if (a.Width != b.Width || a.Height != b.Height)
            {
                reason = string.Format(Lang.T("尺寸不同（{0} ≠ {1}）"), a.SizeText, b.SizeText);
                return false;
            }
            if (a.HasGeoTransform != b.HasGeoTransform)
            {
                reason = Lang.T("一侧没有地理变换");
                return false;
            }
            for (int i = 0; i < 6; i++)
            {
                if (!Near(a.GeoTransform[i], b.GeoTransform[i]))
                {
                    reason = string.Format(Lang.T("地理变换不一致（第 {0} 个系数 {1:G6} ≠ {2:G6}）"),
                        i, a.GeoTransform[i], b.GeoTransform[i]);
                    return false;
                }
            }
            return true;
        }

        /// <summary>判断两个图层的投影是否一致（仅用于提示）。</summary>
        /// <param name="a">图层 A。</param>
        /// <param name="b">图层 B。</param>
        /// <returns>投影相同返回 true。</returns>
        public static bool SameProjection(RasterLayer a, RasterLayer b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            if (!a.HasProjection || !b.HasProjection) return a.HasProjection == b.HasProjection;
            return string.Equals(a.ProjectionWkt.Trim(), b.ProjectionWkt.Trim(), StringComparison.Ordinal);
        }

        private static bool Near(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return false;
            double s = Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));
            return Math.Abs(x - y) <= 1e-9 * s;
        }
    }
}
