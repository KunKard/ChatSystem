using ChatSystem.Data.Model;

namespace ChatSystem.Runtime
{
    /// <summary>
    /// 把策划配置（节点 / 选项）转成运行时消息。
    /// </summary>
    /// <remarks>
    /// 所有工厂方法都<b>返回新实例，不返回配置对象里的那个</b>：
    /// 产出的 <see cref="MessageData"/> 会被会话历史持有、被存档序列化，
    /// 若与 ScriptableObject 共享同一实例，运行时对历史的任何改动都会写回资产文件。
    /// </remarks>
    public static class MessageFactory
    {
        /// <summary>玩家消息的 <c>senderId</c> 约定值：空字符串。</summary>
        public const string PlayerSenderId = "";

        /// <summary>由 Message 节点生成消息。</summary>
        public static MessageData FromNode(DialogueNode node)
        {
            var src = node?.message;
            if (src == null) return null;

            return new MessageData
            {
                kind = src.kind,
                senderId = src.senderId ?? PlayerSenderId,
                text = src.text ?? string.Empty,
                assetName = src.assetName ?? string.Empty,
            };
        }

        /// <summary>由玩家选中的选项生成消息。</summary>
        public static MessageData CreatePlayerText(string text)
        {
            return new MessageData
            {
                kind = MessageKind.Text,
                senderId = PlayerSenderId,
                text = text ?? string.Empty,
                assetName = string.Empty,
            };
        }

        /// <summary>
        /// 生成一条时间分割线。
        /// </summary>
        /// <remarks>
        /// 分割线就是一条普通的 <see cref="MessageKind.TimeDivider"/> 消息，
        /// 文案存在 <c>text</c> 里，因此天然随存档持久化，恢复时无需重算。
        /// </remarks>
        public static MessageData CreateDivider(string timeLabel)
        {
            return new MessageData
            {
                kind = MessageKind.TimeDivider,
                senderId = PlayerSenderId,
                text = timeLabel ?? string.Empty,
                assetName = string.Empty,
            };
        }
    }
}
