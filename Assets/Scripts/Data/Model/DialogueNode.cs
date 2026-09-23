using System;
using System.Collections.Generic;

namespace ChatSystem.Data.Model
{
    /// <summary>
    /// 对话图中的一个节点。策划在编辑器工具中配置，运行时只读。
    /// </summary>
    [Serializable]
    public class DialogueNode
    {
        /// <summary>
        /// 稳定唯一 ID。
        /// </summary>
        /// <remarks>
        /// 跳转一律走 ID，<b>禁止使用数组下标</b>：策划在编辑器中插入/删除节点时下标会整体位移，
        /// 而存档里记录的旧下标会静默指向错误节点。
        /// </remarks>
        public string id;

        /// <summary>节点类型，决定下面哪些字段有效。</summary>
        public NodeKind kind;

        /// <summary><see cref="NodeKind.Message"/> 时有效。</summary>
        public MessageData message;

        /// <summary><see cref="NodeKind.Choice"/> 时有效。</summary>
        /// <remarks>字段初始化确保永不为 null，避免校验器与运行时反复判空。</remarks>
        public List<ChoiceOption> options = new List<ChoiceOption>();

        /// <summary>发送延迟 / 等待时长（秒）。</summary>
        /// <remarks>
        /// <see cref="NodeKind.Message"/> 节点上它是<b>下限而非最终值</b>：运行时还会按消息字数
        /// 折算一个时长（<c>ChatSystem.Runtime.TypingDurationPolicy</c>），两者取较大者。
        /// 因此这里填 2 秒只保证"至少 2 秒"，长文案会等得更久。
        /// <para>
        /// 填 0 表示立即发出、且不显示"正在输入"。这个开关语义不受字数折算影响，
        /// 0 就是 0。
        /// </para>
        /// <para>
        /// <see cref="NodeKind.Wait"/> 节点上它<b>就是</b>确切的等待时长，不参与字数折算 ——
        /// Wait 不发声，"对方正在打字"无从谈起。
        /// </para>
        /// </remarks>
        public float delaySeconds;

        /// <summary>线性后继节点 ID。<see cref="NodeKind.Choice"/> 节点为 null，走各选项自己的 nextId。</summary>
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
