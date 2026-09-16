namespace ChatSystem.Data.Model
{
    /// <summary>
    /// 消息内容类型。
    /// </summary>
    /// <remarks>
    /// Emoji 不单独建一种类型 —— 它作为 Text 内嵌的 TMP 富文本标签处理
    /// （例：<c>明天见 &lt;sprite name="smile"&gt;</c>），图文混排由 TMP 原生排版引擎负责。
    /// <para>
    /// 枚举值显式赋值且不可随意改动：存档以整数形式持久化本枚举。
    /// </para>
    /// </remarks>
    public enum MessageKind
    {
        /// <summary>纯文本，可内嵌 Emoji 富文本标签。</summary>
        Text = 0,

        /// <summary>表情包（图片，固定尺寸）。</summary>
        Sticker = 1,

        /// <summary>图片消息（保持宽高比，有最大宽高限制）。</summary>
        Image = 2,

        /// <summary>时间分割线（居中显示，Text 字段为策划配置的显示文案）。</summary>
        TimeDivider = 3,
    }
}
