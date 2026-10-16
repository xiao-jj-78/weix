
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using WeAutoCommon.Configs;
using WeAutoCommon.Models;
using WeAutoCommon.Utils;
using WeChatAuto.Components;
using WeChatAuto.Services;

namespace WeChatAuto.Utils
{
    public static class LcsHelper
    {
        /// <summary>
        /// 两序列求出LCS
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="a">旧序列</param>
        /// <param name="b">新序列</param>
        /// <param name="comparer">比较器</param>
        /// <returns></returns>
        public static List<T> Lcs<T>(
            IReadOnlyList<T> a,
            IReadOnlyList<T> b,
            IEqualityComparer<T> comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;

            int n = a.Count;
            int m = b.Count;

            // dp[i,j] = a[0..i) 和 b[0..j) 的 LCS 长度
            var dp = new int[n + 1, m + 1];

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    if (comparer.Equals(a[i - 1], b[j - 1]))
                    {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    }
                    else
                    {
                        dp[i, j] = Math.Max(
                            dp[i - 1, j],
                            dp[i, j - 1]);
                    }
                }
            }

            // 从右下角开始回溯
            var result = new List<T>();

            int x = n;
            int y = m;

            while (x > 0 && y > 0)
            {
                if (comparer.Equals(a[x - 1], b[y - 1]))
                {
                    result.Add(a[x - 1]);

                    x--;
                    y--;
                }
                else if (dp[x - 1, y] >= dp[x, y - 1])
                {
                    x--;
                }
                else
                {
                    y--;
                }
            }

            result.Reverse();

            return result;
        }

        /// <summary>
        /// 两序列求diff
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="oldList">求列表</param>
        /// <param name="newList">新列表</param>
        /// <param name="comparer">比较器</param>
        /// <returns></returns>
        public static List<DiffItem<T>> Diff<T>(
            IReadOnlyList<T> oldList,
            IReadOnlyList<T> newList,
            IEqualityComparer<T> comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;

            var lcs = Lcs(oldList, newList, comparer);

            var result = new List<DiffItem<T>>();

            int oldIndex = 0;
            int newIndex = 0;
            int lcsIndex = 0;

            while (lcsIndex < lcs.Count)
            {
                var common = lcs[lcsIndex];

                // Old 中到 common 之前的都是 Delete
                while (oldIndex < oldList.Count &&
                       !comparer.Equals(oldList[oldIndex], common))
                {
                    result.Add(new DiffItem<T>(
                        DiffType.Delete,
                        oldList[oldIndex]));

                    oldIndex++;
                }

                // New 中到 common 之前的都是 Insert
                while (newIndex < newList.Count &&
                       !comparer.Equals(newList[newIndex], common))
                {
                    result.Add(new DiffItem<T>(
                        DiffType.Insert,
                        newList[newIndex]));

                    newIndex++;
                }

                // common 本身
                result.Add(new DiffItem<T>(
                    DiffType.Equal,
                    common));

                oldIndex++;
                newIndex++;
                lcsIndex++;
            }

            // Old 剩余
            while (oldIndex < oldList.Count)
            {
                result.Add(new DiffItem<T>(
                    DiffType.Delete,
                    oldList[oldIndex]));

                oldIndex++;
            }

            // New 剩余
            while (newIndex < newList.Count)
            {
                result.Add(new DiffItem<T>(
                    DiffType.Insert,
                    newList[newIndex]));

                newIndex++;
            }

            return result;
        }

        /// <summary>
        /// 将Diff结果转换为连续的DiffBlock。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="diff">Diff结果</param>
        /// <returns></returns>
        public static List<DiffBlock<T>> ToDiffBlocks<T>(
            IReadOnlyList<DiffItem<T>> diff)
        {
            var result = new List<DiffBlock<T>>();

            if (diff == null || diff.Count == 0)
                return result;

            var currentType = diff[0].Type;
            var currentItems = new List<T>();

            foreach (var item in diff)
            {
                if (item.Type != currentType)
                {
                    result.Add(new DiffBlock<T>(
                        currentType,
                        currentItems));

                    currentType = item.Type;
                    currentItems = new List<T>();
                }

                currentItems.Add(item.Value);
            }

            // 最后一个Block
            result.Add(new DiffBlock<T>(
                currentType,
                currentItems));

            return result;
        }

        /// <summary>
        /// 是否包含同一序列
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="oldList">旧快照</param>
        /// <param name="newList">新快照</param>
        /// <param name="comparer">比较器</param>
        /// <param name="minEqualCount">可靠Equal连续数量，默认为3</param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static bool IsSameIncludeSequence<T>(IReadOnlyList<T> oldList,
            IReadOnlyList<T> newList,
            IEqualityComparer<T> comparer = null,
            int minEqualCount = 3)
        {
            if (minEqualCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(minEqualCount));
            if (oldList == null || oldList.Count == 0)
                return false;
            if (newList == null || newList.Count == 0)
                return false;
            var diff = Diff(oldList, newList, comparer);
            var blocks = ToDiffBlocks(diff);
            blocks = blocks.Where(u => u.Type == DiffType.Equal && u.Count >= minEqualCount).ToList();
            if (blocks.Count == 0)
                return false;
            return true;
        }

        /// <summary>
        /// 根据新旧快照比较得到新增消息。
        /// </summary>
        /// <remarks>
        /// 规则：
        /// 1. 如果存在连续 Equal >= minEqualCount，则认为新旧快照属于同一个消息序列。
        /// 2. 取最后一个满足条件的 Equal Block 作为可靠锚点。
        /// 3. 返回可靠锚点之后的 Insert 消息。
        /// 4. 如果不存在可靠锚点，则认为新旧快照属于完全不同的消息序列，返回整个 newList。
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="oldList">旧快照</param>
        /// <param name="newList">新快照</param>
        /// <param name="comparer">比较器</param>
        /// <param name="minEqualCount">可靠Equal连续数量，默认3</param>
        /// <returns>新增消息</returns>
        public static List<T> GetNewItems<T>(
            IReadOnlyList<T> oldList,
            IReadOnlyList<T> newList,
            IEqualityComparer<T> comparer = null,
            int minEqualCount = 3)
        {
            if (minEqualCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(minEqualCount));

            if (newList == null || newList.Count == 0)
                return new List<T>();

            // 没有旧快照，整个新快照都是新消息
            if (oldList == null || oldList.Count == 0)
                return new List<T>(newList);

            var diff = Diff(oldList, newList, comparer);
            var blocks = ToDiffBlocks(diff);

            // 找最后一个满足条件的 Equal Block
            int anchorIndex = -1;

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];

                if (block.Type == DiffType.Equal &&
                    block.Count >= minEqualCount)
                {
                    anchorIndex = i;
                }
            }

            // 没有可靠锚点：
            // A、B无法建立可靠的消息序列关系，
            // 因此认为B是一个新的消息序列。
            if (anchorIndex < 0)
            {
                return new List<T>(newList);
            }

            // 找到可靠锚点后，
            // 锚点之后的 Insert 就是新增消息。
            var result = new List<T>();

            for (int i = anchorIndex + 1; i < blocks.Count; i++)
            {
                var block = blocks[i];

                if (block.Type == DiffType.Insert)
                {
                    result.AddRange(block.Items);
                }
            }

            return result;
        }

    }

    public enum DiffType
    {
        /// <summary>
        /// 相同
        /// </summary>
        Equal,
        /// <summary>
        /// 新增
        /// </summary>
        Insert,
        /// <summary>
        /// 删除
        /// </summary>
        Delete
    }

    public sealed record DiffItem<T>(
    DiffType Type,
    T Value);

    /// <summary>
    /// Diff连续块
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="Type">Diff类型</param>
    /// <param name="Items">连续的元素</param>
    public sealed record DiffBlock<T>(
        DiffType Type,
        IReadOnlyList<T> Items)
    {
        /// <summary>
        /// 连续块中的元素数量
        /// </summary>
        public int Count => Items.Count;
    }
}