using System;

namespace ChatSystem.Data.Model
{
    /// <summary>
    /// 一条消息的运行时数据，同时作为存档模型的元素。
    /// </summary>
    /// <remarks>
    /// 本类<b>不含时间字段</b>。消息时间属于策划配置，挂在 <see cref="DialogueNode"/> /
    /// <see cref="ChoiceOption"/> 上，不进入存档 —— 详见设计文档 §5.2.4。
    /// <para>
    /// 时间分割线不是特殊结构，它就是一条 <see cref="MessageKind.TimeDivider"/> 的普通消息项，
    /// 作为 <see cref="text"/> 保存显示文案，从而随存档一起持久化。
    /// </para>
    /// </remarks>
    [Serializable]
    public class MessageData
    {
        /// <summary>内容类型。</summary>
        public MessageKind kind;

        /// <summary>发送者 ID。空字符串表示玩家自己。</summary>
        public string senderId;

        /// <summary>
        /// 文本内容。
        /// <see cref="MessageKind.Text"/> 时为正文（可含 Emoji 富文本标签）；
        /// <see cref="MessageKind.TimeDivider"/> 时为分割线显示文案。
        /// </summary>
        public string text;

        /// <summary><see cref="MessageKind.Sticker"/> / <see cref="MessageKind.Image"/> 的资源名。</summary>
        public string assetName;
    }
}
