namespace ChatSystem.Runtime
{
    /// <summary>
    /// 时间分割线的插入规则。抽成纯函数便于单测，不依赖任何运行时状态。
    /// </summary>
    /// <remarks>
    /// 时间<b>全部由策划配置</b>，不读运行时时钟，因此这里的比较只发生在两个配置值之间。
    /// </remarks>
    public static class TimeDividerPolicy
    {
        /// <summary>相邻消息间隔超过该值时插入分割线（秒）。</summary>
        public const long ThresholdSeconds = 300; // 5 分钟

        /// <summary>
        /// 判断是否应在当前消息前插入分割线。
        /// </summary>
        /// <param name="lastTimedValueUtc">
        /// 上一条<b>配置了时间</b>的消息的时间值；<c>0</c> 表示会话中还没有这样的消息。
        /// </param>
        /// <param name="currentValueUtc">当前消息的时间值。</param>
        /// <returns>
        /// 应当插入返回 <c>true</c>。
        /// </returns>
        /// <remarks>
        /// 三种返回 <c>true</c> 的情形：
        /// <list type="number">
        /// <item><b>会话首条带时间的消息</b>（<paramref name="lastTimedValueUtc"/> 为 0）。
        /// 气泡预制体上<b>没有时间文本节点</b>，时间只通过分割线呈现 —— 若首条不插，
        /// 策划为它配置的时间将永远不可见，"配置了就显示"这条规则也就落空了。</item>
        /// <item>间隔严格大于 <see cref="ThresholdSeconds"/>。</item>
        /// <item><b>时间倒退</b>（当前值小于上一条）。这必然是配置错误，插入分割线让它在
        /// 界面上暴露出来，好过静默吞掉；Day 3 的校验器会另行报错。</item>
        /// </list>
        /// </remarks>
        public static bool ShouldInsert(long lastTimedValueUtc, long currentValueUtc)
        {
            // 当前消息没有可比较的时间值 → 无法参与判断
            if (currentValueUtc <= 0) return false;

            // 会话中还没有带时间的消息 → 这是首条，显示它的时间
            if (lastTimedValueUtc <= 0) return true;

            // 时间倒退：配置错误，插线暴露它
            if (currentValueUtc < lastTimedValueUtc) return true;

            return currentValueUtc - lastTimedValueUtc > ThresholdSeconds;
        }
    }
}
