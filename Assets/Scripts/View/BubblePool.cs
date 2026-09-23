using System;
using UnityEngine;
using UnityEngine.Pool;

namespace ChatSystem.View
{
    /// <summary>
    /// 消息列表用的对象池：NPC 气泡、玩家气泡、时间分割线各一个池。
    /// </summary>
    /// <remarks>
    /// <b>为什么是三个池而不是一个</b>：三种视图的预制体、朝向、接口都不同 ——
    /// 左右气泡的 <c>HorizontalLayoutGroup</c> 对齐方向相反，分割线连 BubbleView 都不是。
    /// 合并成一个池要么加一层类型判断，要么就得让调用方自己保证取对了，两者都更容易出错。
    /// <para>
    /// <b>为什么不缩容</b>：池子存在的意义是复用，缩容会在"切走再切回"这种最常见的
    /// 操作里把实例销毁又重建，正好抵消掉池化的收益。因此 <c>maxSize</c> 给一个足够大的值 ——
    /// <c>ObjectPool</c> 在超出 maxSize 时会直接 Destroy 而不是入池，给 30 会让第 31 个气泡被销毁。
    /// </para>
    /// </remarks>
    public class BubblePool : IDisposable
    {
        /// <summary>
        /// NPC 气泡池的预热数量。<b>同时也是消息列表的可见行数上限</b>（见下）。
        /// </summary>
        /// <remarks>
        /// 验收标准是"收发 500 条消息，<c>Instantiate</c> 调用次数 ≤ 池容量 30"。
        /// 要让这句话<b>证明成立</b>而不是"大概成立"，需要两个前提：
        /// <list type="number">
        /// <item>窗口里同时存活的行数 ≤ <c>maxRealizedRows</c> —— 由 <c>ChatWindowView</c>
        /// "先回收再取用"保证，晚一步就会短暂多出一行。</item>
        /// <item>每个池的容量 ≥ 该类型行在同一时刻可能达到的最大数量。</item>
        /// </list>
        /// 于是三个数各有各的下限：
        /// <code>
        /// NpcCapacity     ≥ 13   一屏可能全是 NPC 消息（独角戏，压力测试就是 500 条纯 NPC）
        /// PlayerCapacity  ≥  6   见下方"为什么玩家池不用一样大"
        /// DividerCapacity ≥  7   分割线永远夹在两条消息之间，13 行里最多 ⌈13/2⌉ = 7 条
        /// ─────────────────────
        /// TotalPrewarm    = 26   ≤ 30 ✓
        /// </code>
        /// <b>为什么玩家池不用和 NPC 池一样大</b>：一屏 13 行里如果有人发了 13 条玩家消息，
        /// 那就一条 NPC 消息都没有，反之亦然 —— 两边是<b>互补</b>的，不会同时占满。
        /// 把一个 13 行的窗口拆成"NPC 池 13 + 玩家池 13"，等于按两边同时满来做准备，
        /// 而那在 13 行里根本放不下。实际对话里玩家消息还要稀疏得多（一次选择才发一条），
        /// 6 条已经留足了余量。
        /// <para>
        /// 代价是"往上翻只能看到最近 13 条"。会话历史本身不受影响（完整保存在
        /// <c>ChatSession.Messages</c>，切走再切回会重新渲染）。要更大的窗口，
        /// 三条路：把 30 这个验收数字谈大、改成左右共用一个池（需要统一预制体）、
        /// 或做列表虚拟化（固定内容高度 + 只实例化可见项）。
        /// </para>
        /// </remarks>
        public const int NpcCapacity = 13;

        /// <summary>玩家气泡池的预热数量。理由见 <see cref="NpcCapacity"/>。</summary>
        public const int PlayerCapacity = 6;

        /// <summary>分割线池的预热数量。理由见 <see cref="NpcCapacity"/>。</summary>
        public const int DividerCapacity = 7;

        /// <summary>预热总量，供验收与自检引用。</summary>
        public const int TotalPrewarm = NpcCapacity + PlayerCapacity + DividerCapacity;

        private const int MaxSize = 4096;

        private readonly ObjectPool<BubbleView> _npcPool;
        private readonly ObjectPool<BubbleView> _playerPool;
        private readonly ObjectPool<TimeDividerView> _dividerPool;

        private int _instantiateCount;

        /// <summary>
        /// 本池自创建以来触发过的 <c>Instantiate</c> 次数。
        /// </summary>
        /// <remarks>
        /// 这是<b>不依赖 Profiler 的可复现指标</b>。验收要求"收发 500 条消息后
        /// Instantiate ≤ 池容量"，靠抓 Profiler 截图证明既麻烦又不可复现；
        /// 直接在 <c>createFunc</c> 里计数，跑完读一个整数即可。
        /// </remarks>
        public int InstantiateCount => _instantiateCount;

        /// <param name="parent">新建实例的父节点。释放时也会被移回这里。</param>
        public BubblePool(BubbleView npcPrefab, BubbleView playerPrefab, TimeDividerView dividerPrefab,
                          Transform parent)
        {
            if (npcPrefab == null) throw new ArgumentNullException(nameof(npcPrefab));
            if (playerPrefab == null) throw new ArgumentNullException(nameof(playerPrefab));
            if (dividerPrefab == null) throw new ArgumentNullException(nameof(dividerPrefab));
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            _npcPool = CreatePool(npcPrefab, parent, NpcCapacity);
            _playerPool = CreatePool(playerPrefab, parent, PlayerCapacity);
            _dividerPool = CreatePool(dividerPrefab, parent, DividerCapacity);
        }

        /// <summary>取出一个气泡。调用方必须成对调用 <see cref="ReleaseBubble"/>。</summary>
        public BubbleView GetBubble(bool isPlayer)
        {
            return (isPlayer ? _playerPool : _npcPool).Get();
        }

        /// <summary>归还一个气泡。</summary>
        /// <param name="isPlayer">必须与取出时的取值一致，否则会把玩家气泡塞进 NPC 池。</param>
        public void ReleaseBubble(bool isPlayer, BubbleView view)
        {
            if (view == null) return;
            (isPlayer ? _playerPool : _npcPool).Release(view);
        }

        /// <summary>取出一个时间分割线视图。</summary>
        public TimeDividerView GetDivider()
        {
            return _dividerPool.Get();
        }

        /// <summary>归还一个时间分割线视图。</summary>
        public void ReleaseDivider(TimeDividerView view)
        {
            if (view == null) return;
            _dividerPool.Release(view);
        }

        /// <summary>
        /// 预热：把实例先造出来放回池里。
        /// </summary>
        /// <remarks>
        /// <b>必须显式预热</b>：<c>ObjectPool</c> 构造函数上的 <c>defaultCapacity</c>
        /// 只是内部 List 的初始容量，<b>不会真的创建实例</b>。
        /// 不预热的话前 N 条消息仍然会各触发一次 Instantiate —— 功能上没错，
        /// 但那 N 次分配恰好落在"进入会话的第一屏"，是卡顿最显眼的位置。
        /// </remarks>
        public void Prewarm()
        {
            PrewarmPool(_npcPool, NpcCapacity);
            PrewarmPool(_playerPool, PlayerCapacity);
            PrewarmPool(_dividerPool, DividerCapacity);
        }

        public void Dispose()
        {
            _npcPool.Dispose();
            _playerPool.Dispose();
            _dividerPool.Dispose();
        }

        private static void PrewarmPool<T>(ObjectPool<T> pool, int count) where T : Component
        {
            var buffer = new T[count];
            for (int i = 0; i < count; i++) buffer[i] = pool.Get();
            for (int i = 0; i < count; i++) pool.Release(buffer[i]);
        }

        private ObjectPool<T> CreatePool<T>(T prefab, Transform parent, int capacity) where T : Component
        {
            return new ObjectPool<T>(
                createFunc: () =>
                {
                    _instantiateCount++;
                    return UnityEngine.Object.Instantiate(prefab, parent);
                },
                actionOnGet: view =>
                {
                    view.gameObject.SetActive(true);

                    // 必须每次取出都提到末位：新 Instantiate 的实例天然在末尾，但从池里复用的
                    // 实例保留着上次的 sibling index，会插进列表中间 —— 表现为"新消息跑进历史里"
                    view.transform.SetAsLastSibling();
                },
                actionOnRelease: view =>
                {
                    view.gameObject.SetActive(false);
                    view.transform.SetParent(parent, false);
                },
                actionOnDestroy: view => UnityEngine.Object.Destroy(view.gameObject),
                // 仅在 Editor / Development 构建生效，用于抓"同一个对象被 Release 两次"
                collectionCheck: true,
                defaultCapacity: capacity,
                maxSize: MaxSize);
        }
    }
}
