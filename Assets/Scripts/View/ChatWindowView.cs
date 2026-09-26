using System.Collections.Generic;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using ChatSystem.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem.View
{
    /// <summary>
    /// 右侧聊天面板：顶部栏（名字 + 签名）+ 消息列表 + 三点输入指示。
    /// </summary>
    /// <remarks>
    /// <b>本类只渲染，不产生消息。</b>所有消息都来自 <c>DialogueRunner.OnMessageEmitted</c> ——
    /// 连玩家点选项产生的那条也是状态机自己发的。让视图层也能"造"消息，就会有两个
    /// 真相来源，届时"历史里少了一条"或"多了一条"这类问题将无法定位。
    /// </remarks>
    public class ChatWindowView : MonoBehaviour
    {
        [Header("引用（可由 工具 / ChatSystem / 接线 Day 2 场景 自动填充）")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private TMP_Text headerName;
        [SerializeField] private TMP_Text headerSignature;

        [Header("玩家身份")]
        [Tooltip("玩家自己的资料（名字 + 头像），用于右侧气泡。\n\n" +
                 "留空则右侧气泡只显示 playerDisplayName、不显示头像。\n" +
                 "想给玩家配头像时，新建一个 ContactProfile 资产拖到这里即可。")]
        [SerializeField] private ContactProfile playerProfile;

        [Tooltip("没有配置 playerProfile 时，玩家气泡显示的名字。")]
        [SerializeField] private string playerDisplayName = "Kard";

        [Header("预制体")]
        [SerializeField] private BubbleView npcBubblePrefab;
        [SerializeField] private BubbleView playerBubblePrefab;
        [SerializeField] private TimeDividerView dividerPrefab;

        [Tooltip("三点输入指示气泡。常驻单个实例、不进对象池——同一时刻最多只有一个会话在输入，池化没有收益。")]
        [SerializeField] private TypingIndicator typingIndicatorPrefab;

        [Header("行为")]
        [Tooltip("垂直归一化位置低于此值即视为「正贴着底部」。")]
        [SerializeField] private float bottomThreshold = 0.05f;

        [Tooltip("消息列表同时保留的最大行数。超出后从最旧的一端回收。\n\n" +
                 "必须 ≤ BubblePool.NpcCapacity，否则池会扩容，「Instantiate 次数 = 池容量」这条验收就破了。" +
                 "原因是列表里的行可能全部来自同一侧（纯 NPC 独角戏），而 NPC 池必须能独自撑满整个窗口。\n\n" +
                 "它同时也是让对象池有意义的前提：一条消息一个气泡且永不回收的话，500 条消息就是 500 次实例化；" +
                 "而且 VerticalLayoutGroup 装 500 个子节点，任何一次布局重建都是 O(n)。\n\n" +
                 "代价：往上翻只能看到最近这么多条（会话历史本身不受影响，仍完整保存在 ChatSession.Messages 里，" +
                 "切走再切回会重新渲染）。彻底的做法是列表虚拟化——固定内容高度 + 只实例化可见项。")]
        [SerializeField] private int maxRealizedRows = BubblePool.NpcCapacity;

        private BubblePool _pool;
        private TypingIndicator _typing;
        private ChatSession _session;

        /// <summary>消息区 Content 在设计稿里的下内边距。让位量叠加在它之上，见 <see cref="SetBottomInset"/>。</summary>
        private int _basePaddingBottom;

        private bool _basePaddingCaptured;

        private readonly List<Row> _rows = new List<Row>(64);

        private enum RowKind
        {
            Npc,
            Player,
            Divider,
        }

        /// <summary>一行已实例化的列表项。<c>Kind</c> 必须留档，归还时才能送回正确的池子。</summary>
        private readonly struct Row
        {
            public readonly RowKind Kind;
            public readonly Component View;

            public Row(RowKind kind, Component view)
            {
                Kind = kind;
                View = view;
            }
        }

        /// <summary>当前展示的会话。</summary>
        public ChatSession Session => _session;

        /// <summary>本组件创建过的气泡实例总数。验收"Instantiate ≤ 池容量"时读这个值。</summary>
        public int InstantiateCount => _pool != null ? _pool.InstantiateCount : 0;

        private void Awake()
        {
            ResolveReferences();

            if (content == null || npcBubblePrefab == null || playerBubblePrefab == null || dividerPrefab == null)
            {
                Debug.LogError(
                    "[ChatSystem] ChatWindowView 缺少必要引用（Content 或三个预制体），消息列表不会显示。" +
                    "请运行菜单 工具 / ChatSystem / 接线 Day 2 场景。", this);
                enabled = false;
                return;
            }

            _pool = new BubblePool(npcBubblePrefab, playerBubblePrefab, dividerPrefab, content);
            _pool.Prewarm();

            if (typingIndicatorPrefab != null)
            {
                _typing = Instantiate(typingIndicatorPrefab, content);
                _typing.gameObject.SetActive(false);
            }

            WarnIfViewportDegenerate();
        }

        /// <summary>
        /// 检查 Viewport 是否退化成了 0 尺寸。
        /// </summary>
        /// <remarks>
        /// Viewport 上挂的是 stencil 遮罩（<c>Mask</c>），它靠自身图形占的像素决定裁剪范围。
        /// 尺寸一旦是 0，写不进任何 stencil，<b>里面所有消息被整块裁掉且不报任何错</b> ——
        /// 表现为"数据都对、日志也对，就是看不见"。这类问题靠肉眼排查极难，
        /// 所以在这里主动报出来。
        /// <para>
        /// 只报警、不改几何：场景布局是手工调的，运行时不该悄悄把它改掉。
        /// </para>
        /// </remarks>
        private void WarnIfViewportDegenerate()
        {
            var viewport = scrollRect != null ? scrollRect.viewport : null;
            if (viewport == null) return;

            var rect = viewport.rect;
            if (rect.width >= 1f && rect.height >= 1f) return;

            Debug.LogWarning(
                $"[ChatSystem] 消息区 Viewport 的尺寸是 {rect.width}×{rect.height}，" +
                "Viewport 上的 Mask 会把所有消息裁掉（且不会有报错）。\n" +
                "请运行菜单 工具 / ChatSystem / 接线 Day 2 场景 修复锚点。", this);
        }

        private void OnDestroy()
        {
            _pool?.Dispose();
            _pool = null;
        }

        private void OnValidate()
        {
            bottomThreshold = Mathf.Clamp01(bottomThreshold);

            // 0 视为不限。留这个口子是为了排查问题时能先排除裁剪的影响
            if (maxRealizedRows < 0) maxRealizedRows = 0;

            // 上限是硬约束而非建议：调大就意味着 NPC 池要扩容，
            // 「Instantiate 次数 = 池容量」这条验收会静默失效 —— 所以拦在这里并说清楚原因
            if (maxRealizedRows > BubblePool.NpcCapacity)
            {
                Debug.LogWarning(
                    $"[ChatSystem] maxRealizedRows 已从 {maxRealizedRows} 限制为 {BubblePool.NpcCapacity}。" +
                    "该值不能超过 NPC 气泡池的容量，否则池会扩容、验收指标失效。" +
                    "理由见 BubblePool.NpcCapacity 的注释。", this);

                maxRealizedRows = BubblePool.NpcCapacity;
            }
        }

        /// <summary>按节点名重新解析引用。编辑器接线脚本会调用它来把引用烘焙进场景。</summary>
        public void ResolveReferences()
        {
            if (scrollRect == null) scrollRect = GetComponent<ScrollRect>();
            if (scrollRect != null && content == null) content = scrollRect.content;

            // Name / Signal 是 Scroll View 的兄弟节点，不在本节点子树内，
            // 因此只能按"直接子节点"找 —— 用深度搜索会误命中联系人项里的 Name
            if (headerName == null) headerName = FindSiblingText(ViewHierarchy.Name);
            if (headerSignature == null) headerSignature = FindSiblingText("Signal");
        }

        // ── 对外 API ────────────────────────────────────────────────

        /// <summary>切换到某个会话：刷新顶部栏并按历史重建整个列表。</summary>
        public void Show(ChatSession session)
        {
            _session = session;

            Clear();

            var contact = ContactOf(session);
            if (headerName != null) headerName.text = contact != null ? contact.displayName : string.Empty;

            if (session != null) FillFrom(session, contact);

            RefreshSignature();
            SetIndicatorActive(session != null && session.IsTyping);

            // 首次进入固定贴底。滚动位置记忆属于第二轮（ChatSession.ScrollPosition 字段已就绪）
            ScrollToBottom();
        }

        /// <summary>追加一条新消息。</summary>
        /// <remarks>
        /// 只有<b>追加</b>会判断是否贴底：正在往上翻历史时不该被拽回底部。
        /// 切换会话是另一回事，那时固定贴底（见 <see cref="Show"/>）。
        /// </remarks>
        public void Append(MessageData message)
        {
            if (_session == null || message == null) return;

            bool wasAtBottom = IsAtBottom();

            // 裁剪只在贴底时做：正在往上翻的时候把顶部的行抽走，内容会整体往上跳。
            // ⚠️ 必须是"先回收、再取用"，顺序反了窗口会短暂容纳 maxRealizedRows + 1 行，
            // 多出来的那一次 Get 正好是池的第 N+1 次取出 —— 池当场扩容，
            // "Instantiate 次数 = 预热总量"这条验收就在第 14 条消息上破了
            if (wasAtBottom) TrimToWindow(1);

            AddRow(message, ContactOf(_session));

            if (wasAtBottom) ScrollToBottom();
        }

        /// <summary>更新"对方正在输入"状态。只切三点气泡，顶部签名不动。</summary>
        /// <remarks>
        /// 签名是联系人的固有属性，不该随输入状态闪动；让两处同时变化等于把同一件事说两遍。
        /// 这与设计文档 §5.2.5 ② 不一致，是有意的偏离，理由同 <see cref="RefreshSignature"/>。
        /// </remarks>
        public void SetTyping(bool typing)
        {
            SetIndicatorActive(typing);
            if (typing) KeepIndicatorLast();
        }

        /// <summary>把当前列表里的视图全部归还对象池。</summary>
        public void Clear()
        {
            for (int i = 0; i < _rows.Count; i++) ReleaseRow(_rows[i]);
            _rows.Clear();
        }

        /// <summary>
        /// 抬高消息区的底边，给选项面板让出位置。
        /// </summary>
        /// <param name="pixels">要在原有内边距之上再让出的高度（像素）。传 0 恢复原状。</param>
        /// <remarks>
        /// 选项面板浮在消息区上方（两者的下边缘在场景里本来就是对齐的），不让位的话最新几条
        /// 会被面板整个盖住 —— 而玩家恰恰要看着最后一句才能决定怎么回。
        /// <para>
        /// 做法是给 <c>Content</c> 的纵向布局组加下内边距，<b>不动 Viewport</b>。
        /// 曾经改的是 Viewport 的下偏移，那条路隐含了一个前提：Viewport 必须是完整拉伸的锚点
        /// （<c>anchorMin=(0,0)</c> / <c>anchorMax=(1,1)</c>），否则 <c>offsetMin</c> 失去
        /// "从下边缘往内收"的含义，算出来的是负高度，ScrollRect 的归一化位置也跟着失真 ——
        /// 表现就是"让位没生效"，而且不报任何错。加内边距不依赖任何锚点设置。
        /// </para>
        /// <para>
        /// 让位量<b>叠加在设计稿原本的下内边距之上</b>：预制体里那 16px 是留白，
        /// 不是"可以被让位用掉"的空间，直接覆盖掉会让消息贴到面板边缘。
        /// </para>
        /// <para>
        /// 内容高度一变，"贴底"必须重算：内边距撑出来的那块是空的，滚到底时它正好落在面板身后，
        /// 最后一条气泡就停在面板上沿。
        /// </para>
        /// </remarks>
        public void SetBottomInset(float pixels)
        {
            if (content == null) return;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                Debug.LogWarning(
                    "[ChatSystem] 消息区 Content 上没有 VerticalLayoutGroup，" +
                    "选项面板弹出时无法为它让位，最后几条消息会被面板挡住。", this);
                return;
            }

            var current = layout.padding;

            // 第一次被调用时记下设计稿的下内边距，之后都在这条基线上叠加。
            // 放在这里而不是 Awake：这个组件可能被禁用着就进来了，惰性捕获更稳
            if (!_basePaddingCaptured)
            {
                _basePaddingBottom = current.bottom;
                _basePaddingCaptured = true;
            }

            int target = _basePaddingBottom + Mathf.Max(0, Mathf.RoundToInt(pixels));
            if (current.bottom == target) return;

            // 先记下"让位之前是否贴底"：内边距一变，Content 的高度就变了，
            // 那时再判断会把"本来贴着底"误判成"玩家往上翻过"，从而拒绝回滚
            bool wasAtBottom = IsAtBottom();

            // 必须新建一个 RectOffset 再整体赋值。LayoutGroup.padding 的 setter 用引用相等
            // 判断"有没有变"，就地改 current.bottom 再把同一个对象赋回去会被判成没变，
            // 布局不会重建 —— 表现同样是"让位没生效"，且不报错
            layout.padding = new RectOffset(current.left, current.right, current.top, target);

            // 重新贴底，最新一条才会停在面板正上方而不是被挡住
            if (wasAtBottom) ScrollToBottom();
        }

        // ── 内部 ────────────────────────────────────────────────────

        private void FillFrom(ChatSession session, ContactProfile contact)
        {
            var history = session.Messages;
            int start = maxRealizedRows > 0 ? Mathf.Max(0, history.Count - maxRealizedRows) : 0;

            for (int i = start; i < history.Count; i++) AddRow(history[i], contact);
        }

        private void AddRow(MessageData message, ContactProfile contact)
        {
            if (message == null || _pool == null) return;

            // 分割线横跨整行、居中，既不属于左侧也不属于右侧，单独一类视图。
            // ⚠️ 必须先判 kind 再判 senderId：分割线的 senderId 是空字符串（= 玩家），
            // 顺序反了会把它渲染成一个玩家气泡
            if (message.kind == MessageKind.TimeDivider)
            {
                var divider = _pool.GetDivider();
                divider.Bind(message);
                _rows.Add(new Row(RowKind.Divider, divider));
                return;
            }

            bool isPlayer = message.senderId == MessageFactory.PlayerSenderId;

            var bubble = _pool.GetBubble(isPlayer);

            // SetAsLastSibling 已由池的 actionOnGet 负责。
            // 说话人分左右：玩家气泡传玩家自己的资料，传联系人的话会把对方的名字与头像
            // 印到玩家自己发的消息上
            bubble.Bind(message,
                        isPlayer ? playerProfile : contact,
                        isPlayer ? playerDisplayName : null);
            _rows.Add(new Row(isPlayer ? RowKind.Player : RowKind.Npc, bubble));

            KeepIndicatorLast();
        }

        /// <summary>回收超出窗口的最旧若干行。</summary>
        /// <param name="headroom">为马上就要加入的新行预留的空位数。取 0 表示"回收完再取用"。</param>
        private void TrimToWindow(int headroom = 0)
        {
            if (maxRealizedRows <= 0) return;

            while (_rows.Count > maxRealizedRows - headroom)
            {
                var oldest = _rows[0];
                _rows.RemoveAt(0);
                ReleaseRow(oldest);
            }
        }

        private void ReleaseRow(Row row)
        {
            if (_pool == null || row.View == null) return;

            switch (row.Kind)
            {
                case RowKind.Npc:
                    _pool.ReleaseBubble(false, (BubbleView)row.View);
                    break;
                case RowKind.Player:
                    _pool.ReleaseBubble(true, (BubbleView)row.View);
                    break;
                case RowKind.Divider:
                    _pool.ReleaseDivider((TimeDividerView)row.View);
                    break;
            }
        }

        /// <summary>让三点气泡始终排在最后一行。</summary>
        private void KeepIndicatorLast()
        {
            if (_typing != null && _typing.gameObject.activeSelf) _typing.transform.SetAsLastSibling();
        }

        private void SetIndicatorActive(bool active)
        {
            if (_typing == null || _typing.gameObject.activeSelf == active) return;

            bool wasAtBottom = IsAtBottom();
            _typing.gameObject.SetActive(active);

            if (active && wasAtBottom) ScrollToBottom();
        }

        /// <summary>刷新标题栏的个性签名。永远是联系人自己的签名。</summary>
        /// <remarks>
        /// <b>输入期间不再替换成"对方正在输入…"</b>（原设计文档 §5.2.5 ② 的要求，此处有意偏离）。
        /// 打字状态由消息区底部的三点气泡表达就够了，签名跟着变会让它读起来像签名本身在闪。
        /// 由于签名只在切换会话时变化，这个方法现在只需在 <see cref="Show"/> 里调用。
        /// </remarks>
        private void RefreshSignature()
        {
            if (headerSignature == null) return;

            var contact = ContactOf(_session);
            headerSignature.text = contact != null ? contact.signature : string.Empty;
        }

        /// <summary>当前是否贴着列表底部。</summary>
        /// <remarks>
        /// 内容不足一屏时必须直接判 <c>true</c>：此时 <c>verticalNormalizedPosition</c>
        /// 的取值由 ScrollRect 内部的边界比较决定，不反映"用户是否往下滚过"，
        /// 拿它和阈值比较会得到随机结果。
        /// <para>
        /// 这个判断依赖 Viewport 拥有真实尺寸 —— <c>ScrollRect</c> 是用 viewport 的 rect
        /// 去算归一化位置的。Viewport 若退化成 0×0（见 Day 1 遗留问题），
        /// 这里的结论就全是噪声，自动滚底会表现为"有时灵有时不灵"。
        /// </para>
        /// </remarks>
        private bool IsAtBottom()
        {
            if (scrollRect == null || content == null) return true;

            var viewport = scrollRect.viewport;
            if (viewport == null) return true;

            if (content.rect.height <= viewport.rect.height) return true;

            return scrollRect.verticalNormalizedPosition <= bottomThreshold;
        }

        /// <summary>滚到最新一条。</summary>
        /// <remarks>
        /// <c>Canvas.ForceUpdateCanvases()</c> <b>必须在赋值之前</b>：布局组与
        /// <c>ContentSizeFitter</c> 的重算被推迟到 Canvas 更新时执行，不强制刷新的话
        /// 此刻 <c>content.rect.height</c> 还是追加之前的旧值，归一化位置会算错，
        /// 表现为"滚到底了但差几条"。
        /// </remarks>
        private void ScrollToBottom()
        {
            if (scrollRect == null || content == null) return;

            Canvas.ForceUpdateCanvases();

            var viewport = scrollRect.viewport;

            // 内容不满一屏时没有"底部"可滚，此时 verticalNormalizedPosition 的取值由
            // ScrollRect 内部的边界比较决定，不反映真实布局 —— 照设 0 有可能把整块内容
            // 推到消息区下缘，正好藏进选项面板背后。直接按顶对齐归零，与预制体的初始
            // anchoredPosition 一致
            if (viewport != null && content.rect.height <= viewport.rect.height)
            {
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
                return;
            }

            scrollRect.verticalNormalizedPosition = 0f;
        }

        private static ContactProfile ContactOf(ChatSession session)
        {
            return session != null && session.Asset != null ? session.Asset.contact : null;
        }

        /// <summary>在父节点的<b>直接</b>子节点中按名字找 TMP。</summary>
        private TMP_Text FindSiblingText(string nodeName)
        {
            var parent = transform.parent;
            if (parent == null) return null;

            var child = parent.Find(nodeName);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }
    }
}
