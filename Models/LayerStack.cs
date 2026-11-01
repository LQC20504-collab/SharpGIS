using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpGIS.Models
{
    /// <summary>
    /// 图层栈：索引 0 为最底层，末尾为最顶层（与 QGIS 面板顺序相反显示）。
    /// </summary>
    public sealed class LayerStack
    {
        private readonly List<RasterLayer> _layers = new List<RasterLayer>();
        private readonly object _sync = new object();

        /// <summary>图层集合或可见性/符号化发生变化时触发。</summary>
        public event EventHandler Changed;

        /// <summary>图层数量。</summary>
        public int Count { get { lock (_sync) return _layers.Count; } }

        /// <summary>按底→顶顺序复制图层数组。</summary>
        /// <returns>图层数组快照。</returns>
        public RasterLayer[] ToArray()
        {
            lock (_sync) return _layers.ToArray();
        }

        /// <summary>取渲染快照并给每层加租约，调用方必须配对 ReleaseAll。</summary>
        /// <returns>带租约的图层数组。</returns>
        public RasterLayer[] TakeSnapshot()
        {
            lock (_sync)
            {
                var arr = _layers.ToArray();
                foreach (var l in arr) l.Acquire();
                return arr;
            }
        }

        /// <summary>释放快照租约。</summary>
        /// <param name="snapshot">由 TakeSnapshot 取得的数组。</param>
        public static void ReleaseAll(RasterLayer[] snapshot)
        {
            if (snapshot == null) return;
            foreach (var l in snapshot) l.Release();
        }

        /// <summary>把图层压到栈顶。</summary>
        /// <param name="layer">要加入的图层。</param>
        public void Add(RasterLayer layer)
        {
            if (layer == null) return;
            lock (_sync) _layers.Add(layer);
            RaiseChanged();
        }

        /// <summary>移除图层并释放其数据集。</summary>
        /// <param name="layer">要移除的图层。</param>
        public void Remove(RasterLayer layer)
        {
            bool removed;
            lock (_sync) removed = _layers.Remove(layer);
            if (removed)
            {
                layer.Dispose();
                RaiseChanged();
            }
        }

        /// <summary>清空并释放全部图层。</summary>
        public void Clear()
        {
            RasterLayer[] all;
            lock (_sync)
            {
                all = _layers.ToArray();
                _layers.Clear();
            }
            foreach (var l in all) l.Dispose();
            RaiseChanged();
        }

        /// <summary>把图层上移一层。</summary>
        /// <param name="layer">目标图层。</param>
        /// <returns>是否实际移动。</returns>
        public bool MoveUp(RasterLayer layer)
        {
            lock (_sync)
            {
                int i = _layers.IndexOf(layer);
                if (i < 0 || i >= _layers.Count - 1) return false;
                _layers.RemoveAt(i);
                _layers.Insert(i + 1, layer);
            }
            RaiseChanged();
            return true;
        }

        /// <summary>把图层下移一层。</summary>
        /// <param name="layer">目标图层。</param>
        /// <returns>是否实际移动。</returns>
        public bool MoveDown(RasterLayer layer)
        {
            lock (_sync)
            {
                int i = _layers.IndexOf(layer);
                if (i <= 0) return false;
                _layers.RemoveAt(i);
                _layers.Insert(i - 1, layer);
            }
            RaiseChanged();
            return true;
        }

        /// <summary>取图层在栈中的索引（0 = 栈底），不存在返回 -1。</summary>
        /// <param name="layer">目标图层。</param>
        /// <returns>索引或 -1。</returns>
        public int IndexOf(RasterLayer layer)
        {
            lock (_sync) return _layers.IndexOf(layer);
        }

        /// <summary>取最顶层的可见图层。</summary>
        /// <returns>图层或 null。</returns>
        public RasterLayer TopVisible()
        {
            lock (_sync)
            {
                for (int i = _layers.Count - 1; i >= 0; i--)
                    if (_layers[i].Visible && !_layers[i].IsDisposed) return _layers[i];
            }
            return null;
        }

        /// <summary>所有可见图层范围的并集。</summary>
        /// <returns>并集范围，无图层时无效。</returns>
        public GeoRect UnionExtent()
        {
            GeoRect r = new GeoRect();
            lock (_sync)
            {
                foreach (var l in _layers)
                {
                    if (!l.Visible || l.IsDisposed) continue;
                    r = r.Union(l.Extent);
                }
            }
            return r;
        }

        /// <summary>通知“样式已变更”并触发重绘（图层顺序/可见性/符号化改动后调用）。</summary>
        public void NotifyStyleChanged()
        {
            RaiseChanged();
        }

        /// <summary>主动触发 Changed 事件。</summary>
        public void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
