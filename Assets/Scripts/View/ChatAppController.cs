using System;
using System.Collections.Generic;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using ChatSystem.Runtime;
using UnityEngine;

namespace ChatSystem.View
{
    /// <summary>
    /// 全局中枢：场景里唯一常驻的装配点。持有全部会话，每帧推进它们，并把
    /// 状态机事件路由到三个视图。
    /// </summary>
    /// <remarks>
    /// <b>本类不含任何渲染逻辑</b>，只做三件事：Unity 装配（拖引用）、驱动时间、转发事件。
    /// 视图之间互不引用 —— 联系人列表不知道聊天窗口存在，切换由这里居中调度。
    /// <para>
    /// 所有事件处理函数都带一个 <see cref="ChatSession"/> 参数。这是多会话不串台的关键：
    /// 三个会话的 <c>DialogueRunner</c> 同时活着，回调里必须知道消息是谁发的，
    /// 否则后台会话的消息会被画到前台会话的窗口里。
    /// </para>
    /// </remarks>
    public class ChatAppController : MonoBehaviour
    {
        [Header("数据")]
        [Tooltip("按展示顺序排列的对话资产。顺序即联系人列表顺序。")]
        [SerializeField] private List<ConversationAsset> conversations = new List<ConversationAsset>();

        [Tooltip("留空则默认打开第一个。")]
        [SerializeField] private string initialContactId = "Ayang";

        [Header("视图")]
        [SerializeField] private ContactListView contactList;
        [SerializeField] private ChatWindowView chatWindow;
        [SerializeField] private ReplyOptionsView replyOptions;

        private readonly Dictionary<string, ChatSession> _sessions =
            new Dictionary<string, ChatSession>(StringComparer.Ordinal);

        private readonly List<ChatSession> _ordered = new List<ChatSession>();

        private ChatSession _active;

        /// <summary>当前打开的会话，可能为 <c>null</c>。</summary>
        public ChatSession ActiveSession => _active;

        /// <summary>全部会话，按配置顺序。Day 3 的存档服务会从这里取数据。</summary>
        public IReadOnlyList<ChatSession> Sessions => _ordered;

        private void Awake()
        {
            if (conversations == null || conversations.Count == 0)
            {
                Debug.LogError(
                    "[ChatSystem] ChatAppController 没有配置任何对话资产，界面不会有内容。" +
                    "请运行菜单 工具 / ChatSystem / 接线 Day 2 场景。", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < conversations.Count; i++)
            {
                var asset = conversations[i];
                if (asset == null) continue;

                if (asset.contact == null)
                {
                    Debug.LogError($"[ChatSystem] 对话资产 \"{asset.name}\" 没有指定 ContactProfile，已跳过。", asset);
                    continue;
                }

                var session = new ChatSession(asset);

                if (_sessions.ContainsKey(session.ContactId))
                {
                    Debug.LogError(
                        $"[ChatSystem] 联系人 ID \"{session.ContactId}\" 重复（资产 \"{asset.name}\"），已跳过。" +
                        "ID 是存档键，重复会导致两个会话互相覆盖。", asset);
                    continue;
                }

                Subscribe(session);
                _sessions.Add(session.ContactId, session);
                _ordered.Add(session);
            }
        }

        private void Start()
        {
            // 放 Start 而不是 Awake：三个视图的对象池在各自的 Awake 里建，
            // Unity 保证所有 Awake 都先于任何 Start 执行
            contactList?.Rebuild(_ordered, null, OpenSession);

            for (int i = 0; i < _ordered.Count; i++)
            {
                var session = _ordered[i];

                // 载入历史而不是逐条播放：打开会话时该立刻看到已有的聊天记录，
                // 之后由玩家选择触发的新消息才走延迟 + 打字指示器。
                // 这里会同步跑完每个会话到第一个选项之前的所有内容
                session.Runner.StartLoadingHistory(session.Asset.entryNodeId);
            }

            string first = string.IsNullOrEmpty(initialContactId)
                ? (_ordered.Count > 0 ? _ordered[0].ContactId : null)
                : initialContactId;

            OpenSession(first);
        }

        private void Update()
        {
            // ⚠️ 推进**全部**会话，不只是当前激活的那个（§5.2.7）。
            // 后台会话的延迟消息照常走完，只是静默入队并累加未读。
            for (int i = 0; i < _ordered.Count; i++) _ordered[i].Tick(Time.deltaTime);

#if UNITY_EDITOR
            // 验收用：收发若干消息后按 F2，直接读出气泡的实际 Instantiate 次数。
            // 比抓 Profiler 截图可靠，也可复现。
            if (Input.GetKeyDown(KeyCode.F2))
            {
                Debug.Log($"[ChatSystem] 气泡 Instantiate 次数 = {BubbleInstantiateCount}" +
                          $"（池预热总量 {BubblePool.TotalPrewarm}）");
            }
#endif
        }

        /// <summary>本局累计的气泡实例化次数，用于验收"Instantiate ≤ 池容量"。</summary>
        public int BubbleInstantiateCount => chatWindow != null ? chatWindow.InstantiateCount : 0;

        /// <summary>打开某个联系人的会话。已经在看它时不做任何事。</summary>
        public void OpenSession(string contactId)
        {
            if (string.IsNullOrEmpty(contactId)) return;

            if (!_sessions.TryGetValue(contactId, out var target))
            {
                Debug.LogWarning($"[ChatSystem] 找不到联系人 \"{contactId}\" 对应的会话。", this);
                return;
            }

            if (ReferenceEquals(target, _active)) return;

            // 选项面板属于旧会话的 Choice 节点，留着会被误点。
            // 顺带把消息区让出去的高度还回来
            HideChoices();

            if (_active != null) _active.IsActive = false;

            _active = target;
            _active.IsActive = true;
            _active.MarkRead();

            chatWindow?.Show(_active);

            contactList?.SetSelected(contactId);
            contactList?.RefreshItem(_active);   // 未读已清零，红点要立刻消失

            // 切回来时会话可能正停在 Choice 节点上，面板必须重新弹出来（§5.2.7）
            if (_active.Runner.IsWaitingForChoice) PresentChoicesFromNode(_active);
        }

        // ── 事件路由 ────────────────────────────────────────────────

        /// <remarks>
        /// 订阅用闭包捕获 <paramref name="session"/>，因此无法退订。这是可接受的：
        /// 会话与 <c>DialogueRunner</c> 都由本组件持有，生命周期完全一致，
        /// 本组件销毁时会话也一起没了，不存在悬挂引用。
        /// </remarks>
        private void Subscribe(ChatSession session)
        {
            session.Runner.OnMessageEmitted += message => HandleMessageEmitted(session, message);
            session.Runner.OnTypingChanged += (_, typing) => HandleTypingChanged(session, typing);
            session.Runner.OnChoicesPresented += options => HandleChoicesPresented(session, options);
            session.Runner.OnEnded += () => HandleEnded(session);
        }

        private void HandleMessageEmitted(ChatSession session, MessageData message)
        {
            // 只有前台会话画进消息列表。后台会话的消息由 ChatSession 自己收进历史
            if (session.IsActive) chatWindow?.Append(message);

            // 列表项两种情况都要刷新：预览文案变了，非前台会话还要 +1 未读（§5.2.5 ③）
            contactList?.RefreshItem(session);
        }

        private void HandleTypingChanged(ChatSession session, bool typing)
        {
            // 只有前台会话要处理。打字状态既不影响列表预览，也不影响顶部签名 ——
            // 整个界面里表达"对方正在输入"的只剩消息区底部那个三点气泡。
            // 因此后台会话的输入状态变化在这里被完全丢弃，这是有意的（§5.2.5 ②③ 的有意偏离）
            if (session.IsActive) chatWindow?.SetTyping(typing);
        }

        private void HandleChoicesPresented(ChatSession session, IReadOnlyList<ChoiceOption> options)
        {
            // 后台会话静默累积，不弹面板 —— 否则玩家会被一个不属于当前对话的选项打断
            if (!session.IsActive) return;

            PresentChoices(session, options);
        }

        private void HandleEnded(ChatSession session)
        {
            if (session.IsActive) HideChoices();
            contactList?.RefreshItem(session);
        }

        private void PresentChoicesFromNode(ChatSession session)
        {
            var node = session.Asset != null ? session.Asset.GetNode(session.CurrentNodeId) : null;
            PresentChoices(session, node != null ? node.options : null);
        }

        private void PresentChoices(ChatSession session, IReadOnlyList<ChoiceOption> options)
        {
            if (options == null || options.Count == 0)
            {
                HideChoices();
                return;
            }

            // 面板内部会先隐藏自己再回调，因此这里直接推进即可 ——
            // Runner.Choose 是同步的，会在同一次调用里发出玩家消息并可能立刻置为"正在输入"
            replyOptions?.Show(options, index =>
            {
                // 面板是自己隐藏自己的（见 ReplyOptionsView.HandleClick），
                // 控制器收不到通知，只能在这里把让出去的高度还回来。
                // 必须在 Choose 之前：玩家气泡要落在全高的消息区里
                SyncMessageAreaInset();
                session.Runner.Choose(index);
            });

            SyncMessageAreaInset();
        }

        private void HideChoices()
        {
            replyOptions?.Hide();
            SyncMessageAreaInset();
        }

        /// <summary>
        /// 让消息区底部让出选项面板的高度，否则最后几条消息会被面板盖住。
        /// </summary>
        /// <remarks>
        /// 放在控制器里而不是让两个视图互相引用：<c>ChatWindowView</c> 不知道选项面板存在，
        /// 它只知道"底部要空出 N 像素"。谁占用了那块屏幕由这里决定。
        /// </remarks>
        private void SyncMessageAreaInset()
        {
            bool showing = replyOptions != null && replyOptions.IsShowing;
            chatWindow?.SetBottomInset(showing ? replyOptions.MeasureHeight() : 0f);
        }
    }
}
