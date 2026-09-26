namespace ChatSystem.Data.Model
{
    /// <summary>
    /// 对话格式自身的硬性上限。
    /// </summary>
    /// <remarks>
    /// 放在 <c>Data</c> 而不是使用它的视图层，理由有三条：
    /// <list type="number">
    /// <item>"一个 Choice 节点最多几个选项"是<b>对话格式的事实</b>，不是某个面板的绘制细节；</item>
    /// <item>编辑器校验器要说这句话（"第 4 个选项永远不会显示"），而它只引用得到
    /// <c>Data</c> —— 引用 <c>View</c> 会把 <c>UnityEngine.UI</c> 拖进来，
    /// 于是 <c>.logiccheck</c> 那条"不开 Unity 就能验"的路就断了；</item>
    /// <item>常量写两份必然漂移，漂移的校验器比没有校验器更糟 —— 策划会停止相信它。
    /// 由 <see cref="ChatSystem.View.ReplyOptionsView"/> 反过来引用这里，
    /// 编译器就成了两边一致性的守卫。</item>
    /// </list>
    /// </remarks>
    public static class ConversationLimits
    {
        /// <summary>一个 Choice 节点的选项数量上限。设计文档 §5.3 规定为 1~3 个。</summary>
        public const int MaxOptions = 3;
    }
}
