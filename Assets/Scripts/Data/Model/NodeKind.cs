namespace ChatSystem.Data.Model
{
    /// <summary>
    /// 对话节点类型。
    /// </summary>
    public enum NodeKind
    {
        /// <summary>发送一条消息，内容见 <see cref="DialogueNode.message"/>。</summary>
        Message = 0,

        /// <summary>弹出玩家回复选项并等待选择，选项见 <see cref="DialogueNode.options"/>。</summary>
        Choice = 1,

        /// <summary>等待指定时长（用于制造节奏），时长见 <see cref="DialogueNode.delaySeconds"/>。</summary>
        Wait = 2,

        /// <summary>对话结束。</summary>
        End = 3,
    }
}
