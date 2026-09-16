using System;

namespace ChatSystem.Data.Model
{
    /// <summary>
    /// 玩家侧的一个回复选项。选中后该文案会作为玩家消息发出，并跳转到 <see cref="nextId"/>。
    /// </summary>
    [Serializable]
    public class ChoiceOption
    {
        /// <summary>按钮文案。</summary>
        public string text;

        /// <summary>跳转目标节点 ID。指向不存在的节点即为断链，由编辑器校验器拦截。</summary>
        public string nextId;

        /// <summary>时间显示文案，如"昨天 21:30"。留空表示未配置时间。</summary>
        public string timeLabel;

        /// <summary>
        /// 时间值（Unix 秒），仅用于比较相邻消息间隔是否超过阈值。0 表示不参与比较。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="timeLabel"/> 缺一不可：<b>文案无法比较大小，数值无法表达"昨天"</b>。
        /// 本项目没有运行时时钟作为参照系，相对时间描述只能由策划直接给出。
        /// </remarks>
        public long timeValueUtc;

        /// <summary>是否配置了时间。未配置则既不显示时间，也不参与分割线判断。</summary>
        public bool HasTime => !string.IsNullOrEmpty(timeLabel);
    }
}
