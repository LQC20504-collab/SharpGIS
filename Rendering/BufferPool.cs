using System;
using System.Collections.Concurrent;

namespace SharpGIS.Rendering
{
    /// <summary>极简数组池：避免每帧重复分配几 MB 的缓冲区导致 LOH 压力。</summary>
    internal static class BufferPool
    {
        private static readonly ConcurrentDictionary<int, ConcurrentBag<float[]>> FloatPool =
            new ConcurrentDictionary<int, ConcurrentBag<float[]>>();
        private static readonly ConcurrentDictionary<int, ConcurrentBag<byte[]>> BytePool =
            new ConcurrentDictionary<int, ConcurrentBag<byte[]>>();

        /// <summary>租用指定长度的 float 缓冲区。</summary>
        /// <param name="length">元素个数。</param>
        /// <returns>缓冲区（内容不保证清零）。</returns>
        public static float[] RentFloat(int length)
        {
            ConcurrentBag<float[]> bag;
            if (FloatPool.TryGetValue(length, out bag))
            {
                float[] item;
                if (bag.TryTake(out item)) return item;
            }
            return new float[length];
        }

        /// <summary>归还 float 缓冲区。</summary>
        /// <param name="buffer">先前租用的缓冲区。</param>
        public static void Return(float[] buffer)
        {
            if (buffer == null) return;
            var bag = FloatPool.GetOrAdd(buffer.Length, _ => new ConcurrentBag<float[]>());
            if (bag.Count < 4) bag.Add(buffer);
        }

        /// <summary>租用指定长度的 byte 缓冲区。</summary>
        /// <param name="length">元素个数。</param>
        /// <returns>缓冲区（内容不保证清零）。</returns>
        public static byte[] RentByte(int length)
        {
            ConcurrentBag<byte[]> bag;
            if (BytePool.TryGetValue(length, out bag))
            {
                byte[] item;
                if (bag.TryTake(out item)) return item;
            }
            return new byte[length];
        }

        /// <summary>归还 byte 缓冲区，可选清零。</summary>
        /// <param name="buffer">先前租用的缓冲区。</param>
        /// <param name="clear">是否清零后再入池。</param>
        public static void Return(byte[] buffer, bool clear = false)
        {
            if (buffer == null) return;
            if (clear) Array.Clear(buffer, 0, buffer.Length);
            var bag = BytePool.GetOrAdd(buffer.Length, _ => new ConcurrentBag<byte[]>());
            if (bag.Count < 4) bag.Add(buffer);
        }
    }
}
