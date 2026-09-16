using System;
using System.Collections.Generic;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using UnityEngine;

namespace ChatSystem.Runtime
{
    /// <summary>
    /// 对话推进状态机。纯 C#，不继承 MonoBehaviour，可在<b>不进入 Play 模式</b>的情况下单测。
    /// </summary>
    /// <remarks>
    /// 时间通过 <see cref="Tick"/> 显式注入，而不是用协程：协程依赖 MonoBehaviour，
    /// 会破坏架构约束，而且测试时无法"快进 10 秒"。
    /// <para>
    /// 一个会话一个实例。多联系人并行延迟（§5.2.7）靠"每会话各持一个 Runner、各自 Tick"实现，
    /// 切换联系人时<b>挂起而非丢弃</b>，切回来继续倒计时。
    /// </para>
    /// </remarks>
    public class DialogueRunner
    {
        /// <summary>一条消息被发出（含时间分割线，它是 <see cref="MessageKind.TimeDivider"/> 的普通消息）。</summary>
        public event Action<MessageData> OnMessageEmitted;

        /// <summary>进入 Choice 节点，需要展示选项。</summary>
        /// <remarks>
        /// 传 <see cref="IReadOnlyList{T}"/> 而非 <c>List</c>：这份数据属于 ScriptableObject，
        /// 交给视图层一个可变列表，等于给了它改坏资产序列化数据的机会。
        /// </remarks>
        public event Action<IReadOnlyList<ChoiceOption>> OnChoicesPresented;

        /// <summary>输入状态变化：(contactId, 是否正在输入)。</summary>
        public event Action<string, bool> OnTypingChanged;

        /// <summary>对话结束（正常走完，或因节点缺失而中断）。</summary>
        public event Action OnEnded;

        private readonly ConversationAsset _asset;
        private readonly string _contactId;

        private string _currentNodeId;
        private float _remainingDelay;
        private DialogueNode _delayedNode;   // 延迟结束后要发出的 Message 节点；Wait 节点为 null
        private bool _waitingForChoice;
        private bool _running;
        private bool _typing;

        public DialogueRunner(ConversationAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));

            _asset = asset;
            _contactId = asset.contact != null ? asset.contact.id : string.Empty;
        }

        /// <summary>当前所在节点 ID。未开始时为 <c>null</c>。</summary>
        public string CurrentNodeId => _currentNodeId;

        /// <summary>是否正在推进中。</summary>
        public bool IsRunning => _running;

        /// <summary>是否正等待玩家选择。</summary>
        public bool IsWaitingForChoice => _waitingForChoice;

        /// <summary>是否正处于"对方正在输入"状态。</summary>
        public bool IsTyping => _typing;

        /// <summary>
        /// 上一条<b>配置了时间</b>的消息的时间值；<c>0</c> 表示本会话尚无带时间的消息。
        /// </summary>
        /// <remarks>需随存档持久化，否则重进游戏后首条新消息会误判为会话首条而多插一条分割线。</remarks>
        public long LastTimedValueUtc { get; private set; }

        // ── 对外驱动 ────────────────────────────────────────────────

        /// <summary>从指定入口节点开始推进。</summary>
        public void Start(string entryNodeId)
        {
            _running = true;
            EnterNode(entryNodeId);
        }

        /// <summary>推进到当前节点的 <c>nextId</c>。</summary>
        public void Advance()
        {
            if (!_running) return;

            var node = _asset.GetNode(_currentNodeId);
            if (node == null)
            {
                Debug.LogError($"[ChatSystem] 当前节点 \"{_currentNodeId}\" 已不存在，对话中断。", _asset);
                End();
                return;
            }

            if (node.kind == NodeKind.End || string.IsNullOrEmpty(node.nextId))
            {
                End();
                return;
            }

            EnterNode(node.nextId);
        }

        /// <summary>选择第 <paramref name="index"/> 个选项：该文案作为玩家消息发出，然后推进。</summary>
        public void Choose(int index)
        {
            if (!_running || !_waitingForChoice) return;

            var node = _asset.GetNode(_currentNodeId);
            if (node?.options == null || index < 0 || index >= node.options.Count)
            {
                Debug.LogError(
                    $"[ChatSystem] 节点 \"{_currentNodeId}\" 收到非法选项下标 {index}（共 {node?.options?.Count ?? 0} 项）。已忽略。",
                    _asset);
                return;
            }

            var option = node.options[index];
            _waitingForChoice = false;

            // 选项的时间配置同样参与分割线判断——玩家消息也带时间
            MaybeEmitDivider(option.timeLabel, option.timeValueUtc);
            OnMessageEmitted?.Invoke(MessageFactory.CreatePlayerText(option.text));

            if (string.IsNullOrEmpty(option.nextId))
            {
                End();
                return;
            }

            EnterNode(option.nextId);
        }

        /// <summary>推进内部计时。由外部每帧调用，或测试中直接传入大值以"快进"。</summary>
        public void Tick(float deltaTime)
        {
            if (!_running || _remainingDelay <= 0f) return;

            _remainingDelay -= deltaTime;
            if (_remainingDelay > 0f) return;

            _remainingDelay = 0f;
            CompleteDelay();
        }

        /// <summary>
        /// 从存档恢复时接上时间比较基线。
        /// </summary>
        /// <remarks>
        /// 已产生的分割线本身随存档持久化，这里补的是"下一条新消息该和谁比"。
        /// </remarks>
        public void SeedTimeBaseline(long lastTimedValueUtc)
        {
            LastTimedValueUtc = lastTimedValueUtc;
        }

        /// <summary>
        /// 从存档恢复到指定节点，<b>不重放该节点的内容</b>。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="Start"/> 的区别：<c>Start</c> 会进入节点并发出消息，恢复时那会导致
        /// 已持久化的历史被重复追加一遍。这里只把游标放回去。
        /// <para>
        /// 停在 Choice 节点时会重新广播一次选项 —— 玩家在选项面板前存档，读档后就该看到面板。
        /// 停在 Message / Wait 节点时不重发：退出流程已经 flush 过待执行队列（§7.2），
        /// 那些消息早已落地并随存档保存。
        /// </para>
        /// </remarks>
        public void RestoreTo(string nodeId)
        {
            _running = true;
            _waitingForChoice = false;
            _remainingDelay = 0f;
            _delayedNode = null;
            _currentNodeId = nodeId;

            var node = _asset.GetNode(nodeId);
            if (node == null)
            {
                Debug.LogError($"[ChatSystem] 存档指向的节点 \"{nodeId}\" 在当前对话资产中不存在。对话中断。", _asset);
                End();
                return;
            }

            if (node.kind == NodeKind.Choice && node.options.Count > 0)
            {
                _waitingForChoice = true;
                OnChoicesPresented?.Invoke(node.options);
            }
        }

        // ── 节点调度 ────────────────────────────────────────────────

        private void EnterNode(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                End();
                return;
            }

            var node = _asset.GetNode(id);
            if (node == null)
            {
                Debug.LogError(
                    $"[ChatSystem] 节点 \"{id}\" 不存在（由 \"{_currentNodeId}\" 跳转而来）。对话中断。" +
                    $"请用 ChatSystem/DialogueValidator 检查断链。",
                    _asset);
                End();
                return;
            }

            _currentNodeId = id;

            switch (node.kind)
            {
                case NodeKind.Message:
                    if (node.delaySeconds > 0f)
                    {
                        _delayedNode = node;
                        _remainingDelay = node.delaySeconds;
                        SetTyping(IsNpcMessage(node));
                    }
                    else
                    {
                        EmitNodeMessage(node);
                        AutoAdvance(node);
                    }
                    break;

                case NodeKind.Choice:
                    _waitingForChoice = true;
                    SetTyping(false);
                    OnChoicesPresented?.Invoke(node.options);
                    break;

                case NodeKind.Wait:
                    _delayedNode = null;
                    _remainingDelay = node.delaySeconds > 0f ? node.delaySeconds : 0f;
                    // Wait 本身不发声，看它后面第一条消息是不是 NPC 的，来决定要不要显示"正在输入"
                    SetTyping(IsNpcMessage(PeekNextMessage(node)));
                    if (_remainingDelay <= 0f) AutoAdvance(node);
                    break;

                case NodeKind.End:
                default:
                    End();
                    break;
            }
        }

        private void CompleteDelay()
        {
            var node = _delayedNode;
            _delayedNode = null;
            SetTyping(false);

            if (node != null) EmitNodeMessage(node);

            var current = _asset.GetNode(_currentNodeId);
            if (current != null) AutoAdvance(current);
        }

        private void AutoAdvance(DialogueNode node)
        {
            if (node.kind == NodeKind.End || string.IsNullOrEmpty(node.nextId))
            {
                End();
                return;
            }

            EnterNode(node.nextId);
        }

        private void EmitNodeMessage(DialogueNode node)
        {
            var msg = MessageFactory.FromNode(node);
            if (msg == null)
            {
                Debug.LogError($"[ChatSystem] Message 节点 \"{node.id}\" 的 message 字段为空。已跳过。", _asset);
                return;
            }

            MaybeEmitDivider(node.timeLabel, node.timeValueUtc);
            OnMessageEmitted?.Invoke(msg);
        }

        /// <summary>按 §5.2.4 的规则决定是否在当前消息前插入时间分割线。</summary>
        private void MaybeEmitDivider(string timeLabel, long timeValueUtc)
        {
            if (string.IsNullOrEmpty(timeLabel)) return;   // 未配置时间：不显示，也不参与判断

            if (timeValueUtc <= 0)
            {
                Debug.LogWarning(
                    $"[ChatSystem] 节点配置了 timeLabel \"{timeLabel}\" 但 timeValueUtc 为 0，" +
                    $"无法参与间隔比较，本次不插入分割线。",
                    _asset);
                return;
            }

            if (TimeDividerPolicy.ShouldInsert(LastTimedValueUtc, timeValueUtc))
            {
                OnMessageEmitted?.Invoke(MessageFactory.CreateDivider(timeLabel));
            }

            // 无论是否插线，它都是新的比较基线
            LastTimedValueUtc = timeValueUtc;
        }

        private void End()
        {
            if (!_running) return;

            _running = false;
            _waitingForChoice = false;
            _remainingDelay = 0f;
            _delayedNode = null;

            // 结束/中断时务必清掉输入态，否则签名会永远卡在"对方正在输入…"
            SetTyping(false);

            OnEnded?.Invoke();
        }

        private void SetTyping(bool typing)
        {
            if (_typing == typing) return;

            _typing = typing;
            OnTypingChanged?.Invoke(_contactId, typing);
        }

        private static bool IsNpcMessage(DialogueNode node)
        {
            return node != null
                && node.kind == NodeKind.Message
                && node.message != null
                && !string.IsNullOrEmpty(node.message.senderId);
        }

        /// <summary>跳过连续的 Wait 节点，找到真正的下一条消息。带循环保护。</summary>
        private DialogueNode PeekNextMessage(DialogueNode node)
        {
            var cur = node;
            int guard = 0;

            while (cur != null && cur.kind == NodeKind.Wait && guard++ < 64)
            {
                cur = _asset.GetNode(cur.nextId);
            }

            return cur;
        }
    }
}
