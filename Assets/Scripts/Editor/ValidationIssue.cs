namespace ChatSystem.EditorTools
{
    /// <summary>校验问题的严重程度。</summary>
    public enum IssueSeverity
    {
        /// <summary>一定会出问题：对话跑不通、报错，或永久卡住。</summary>
        Error = 0,

        /// <summary>可疑但合法：多半是配置疏忽，也可能是刻意为之。</summary>
        Warning = 1,
    }

    /// <summary>
    /// 一条校验结果。
    /// </summary>
    /// <remarks>
    /// 这是<b>纯数据，不含任何打印行为</b> —— 算问题和把问题说出去是两件事。
    /// 拆开之后同一份规则既能被 Inspector 的内联报告消费，也能被单元测试直接断言，
    /// 而不用去截获日志。
    /// <para>
    /// 也正因如此，本文件与 <see cref="DialogueValidator"/>、<see cref="NodeIdUtility"/>
    /// <b>不得出现 using UnityEditor</b>：<c>.logiccheck</c> 脚手架要在 Unity 之外编译它们，
    /// 那是目前唯一能立刻跑起来的验证路径。
    /// </para>
    /// </remarks>
    public sealed class ValidationIssue
    {
        private ValidationIssue(IssueSeverity severity, int nodeIndex, string nodeId,
                                string message, string fixHint)
        {
            Severity = severity;
            NodeIndex = nodeIndex;
            NodeId = nodeId;
            Message = message;
            FixHint = fixHint;
        }

        /// <summary>属于某个节点的问题。</summary>
        /// <param name="index">该节点在 <c>nodes</c> 里的下标，内联报告靠它定位。</param>
        public static ValidationIssue Node(IssueSeverity severity, int index, string nodeId,
                                           string message, string fixHint = null)
        {
            return new ValidationIssue(severity, index, nodeId, message, fixHint);
        }

        /// <summary>不属于任何节点的问题（入口、联系人一类）。</summary>
        public static ValidationIssue Asset(IssueSeverity severity, string message, string fixHint = null)
        {
            return new ValidationIssue(severity, -1, null, message, fixHint);
        }

        public IssueSeverity Severity { get; }

        /// <summary>出问题的节点在 <c>nodes</c> 里的下标；<c>-1</c> 表示不属于任何节点。</summary>
        public int NodeIndex { get; }

        /// <summary>出问题的节点 ID。节点压根没有 ID 时为空。</summary>
        public string NodeId { get; }

        /// <summary>面向策划的描述。</summary>
        public string Message { get; }

        /// <summary>怎么修。为空表示没有比"改对"更具体的建议。</summary>
        public string FixHint { get; }

        /// <summary>面向日志的单行输出。</summary>
        public override string ToString()
        {
            string tag = Severity == IssueSeverity.Error ? "错误" : "警告";
            string where = NodeIndex < 0
                ? string.Empty
                : string.IsNullOrEmpty(NodeId) ? $"节点 #{NodeIndex}：" : $"节点 #{NodeIndex} \"{NodeId}\"：";

            return FixHint == null
                ? $"[{tag}] {where}{Message}"
                : $"[{tag}] {where}{Message} —— {FixHint}";
        }
    }
}
