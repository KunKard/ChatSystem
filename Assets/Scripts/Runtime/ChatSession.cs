using System;
using System.Collections.Generic;
using ChatSystem.Data;
using ChatSystem.Data.Model;

namespace ChatSystem.Runtime
{
    /// <summary>
    /// 一个联系人的会话运行时状态：已渲染的历史、未读数、滚动位置，以及驱动它的状态机。
    /// </summary>
    /// <remarks>
    /// <b>一个联系人一个会话</b>。多联系人并行延迟（§5.2.7）靠各自持有独立的
    /// <see cref="DialogueRunner"/> 实现——切换联系人时挂起而非丢弃，切回来继续倒计时。
    /// <para>
    /// 当前节点 ID 只有 <see cref="DialogueRunner"/> 一个来源，这里只做转发。
    /// 两边各存一份必然漂移。
    /// </para>
    /// </remarks>
    public class ChatSession
    {
        /// <summary>对方输入期间，联系人列表预览与顶部签名显示的临时文案。</summary>
        public const string TypingText = "对方正在输入…";

        /// <summary>表情包/图片消息在预览里的占位文案。</summary>
        private const string MediaPlaceholder = "[表情]";

        /// <summary>本会话对应的对话资产。</summary>
        public readonly ConversationAsset Asset;

        /// <summary>驱动本会话的状态机。</summary>
        public readonly DialogueRunner Runner;

        /// <summary>联系人稳定 ID，与 <see cref="ContactProfile.id"/> 一致。</summary>
        public readonly string ContactId;

        /// <summary>已渲染的历史消息。时间分割线也在其中（它是普通的 <see cref="MessageKind.TimeDivider"/> 消息）。</summary>
        public readonly List<MessageData> Messages = new List<MessageData>();

        /// <summary>未读消息数。进入聊天即清零（非"滚动到底"）。</summary>
        public int UnreadCount;

        /// <summary>记忆的滚动位置，按会话独立保存。</summary>
        public float ScrollPosition;

        /// <summary>本会话是否为当前展示中的会话。</summary>
        /// <remarks>非激活会话静默累积消息与未读，不刷新 UI（§5.2.7）。</remarks>
        public bool IsActive;

        /// <summary>对方是否正在输入。</summary>
        public bool IsTyping { get; private set; }

        public ChatSession(ConversationAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));

            Asset = asset;
            ContactId = asset.contact != null ? asset.contact.id : string.Empty;

            Runner = new DialogueRunner(asset);
            Runner.OnMessageEmitted += HandleMessageEmitted;
            Runner.OnTypingChanged += HandleTypingChanged;
        }

        /// <summary>当前节点 ID。转发自 <see cref="Runner"/>。</summary>
        public string CurrentNodeId => Runner.CurrentNodeId;

        /// <summary>时间比较基线。转发自 <see cref="Runner"/>，存档时需一并写入。</summary>
        public long LastTimedValueUtc => Runner.LastTimedValueUtc;

        /// <summary>联系人列表里显示的会话预览文案。</summary>
        /// <remarks>
        /// 优先级：对方正在输入 &gt; 最后一条消息 &gt; 联系人配置的默认预览。
        /// 非激活会话也会走这条逻辑，因此后台输入时列表项同样会显示"对方正在输入…"（§5.2.5 ③）。
        /// </remarks>
        public string PreviewText
        {
            get
            {
                if (IsTyping) return TypingText;

                for (int i = Messages.Count - 1; i >= 0; i--)
                {
                    var msg = Messages[i];
                    if (msg == null || msg.kind == MessageKind.TimeDivider) continue;
                    if (!string.IsNullOrEmpty(msg.text)) return msg.text;
                    if (!string.IsNullOrEmpty(msg.assetName)) return MediaPlaceholder;
                }

                return Asset.contact != null ? Asset.contact.defaultPreview : string.Empty;
            }
        }

        /// <summary>推进本会话的计时。非激活会话也应继续 Tick——后台延迟照常走完（§5.2.7）。</summary>
        public void Tick(float deltaTime)
        {
            Runner.Tick(deltaTime);
        }

        /// <summary>从存档恢复本会话。</summary>
        /// <param name="history">已持久化的历史消息，含时间分割线。</param>
        /// <param name="currentNodeId">存档记录的当前节点 ID。</param>
        /// <param name="lastTimedValueUtc">存档记录的时间比较基线。</param>
        public void Restore(IEnumerable<MessageData> history, string currentNodeId, long lastTimedValueUtc)
        {
            Messages.Clear();
            if (history != null) Messages.AddRange(history);

            // 先接基线再恢复游标：RestoreTo 在 Choice 节点会广播选项，
            // 若此时基线还是 0，后续第一条带时间的消息会被误判成会话首条。
            Runner.SeedTimeBaseline(lastTimedValueUtc);
            Runner.RestoreTo(currentNodeId);
        }

        /// <summary>进入本会话：清零未读（§5.2.7 决策：进入聊天即清零，非"滚动到底"）。</summary>
        public void MarkRead()
        {
            UnreadCount = 0;
        }

        private void HandleMessageEmitted(MessageData message)
        {
            if (message == null) return;

            Messages.Add(message);
            if (!IsActive) UnreadCount++;
        }

        private void HandleTypingChanged(string contactId, bool typing)
        {
            IsTyping = typing;
        }
    }
}
