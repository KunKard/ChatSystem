using UnityEngine;

namespace ChatSystem.Runtime
{
    /// <summary>
    /// "对方正在输入"持续多久的规则。抽成纯函数便于单测，与 <see cref="TimeDividerPolicy"/> 同一路子。
    /// </summary>
    /// <remarks>
    /// <b>为什么时长要跟字数挂钩</b>：不挂钩的话，配置里写死一个延迟，一条"嗯"和一段 200 字的
    /// 独白会花一样长的时间出现 —— 前者显得迟钝，后者显得敷衍。让人感觉"对方正在打字"的唯一办法，
    /// 是让等待时间随要打的内容量变化。
    /// <para>
    /// <b>为什么是 <c>Max</c> 而不是加法</b>：配置的 <c>delaySeconds</c> 是策划对节奏的显式安排
    /// （比如"这里停两秒更揪心"），字数算出来的只是<b>下限</b> —— 保证不至于把一段长文瞬间糊上去。
    /// 取 <c>Max</c> 让两种意图共存：策划想拖长可以拖得更长，但没法让长文变快。
    /// </para>
    /// <para>
    /// 这个选择还顺带保住了一整批既有单测：它们用的是 <c>"A"</c> 这类单字符消息配 1~2 秒延迟，
    /// 算出来的字数时长必然低于配置值，<c>Max</c> 之后取值不变，时序断言全部原样成立。
    /// </para>
    /// </remarks>
    public static class TypingDurationPolicy
    {
        /// <summary>每个字符折算的"打字"时长（秒）。</summary>
        /// <remarks>
        /// 0.05 是按中文速读调的：40 字的消息约 2 秒，接近真人在手机上打完一句话的观感。
        /// </remarks>
        public const float SecondsPerCharacter = 0.05f;

        /// <summary>时长下限（秒）。短消息也不该一点就出来，否则"正在输入"根本来不及被看见。</summary>
        public const float MinSeconds = 0.8f;

        /// <summary>时长上限（秒）。</summary>
        /// <remarks>
        /// 必须有上限：200 字按 0.05 折算要 10 秒，没有人会为一条聊天消息等这么久，
        /// 那时候"真实"反而变成了"卡住了"。到顶之后保持恒定，只是不再继续增长。
        /// </remarks>
        public const float MaxSeconds = 4f;

        /// <summary>
        /// 计算一条消息实际的等待时长。
        /// </summary>
        /// <param name="configuredDelay">节点上配置的 <c>delaySeconds</c>，即 Delay 的下限。</param>
        /// <param name="text">消息正文，可为 <c>null</c>（表情包、图片等媒体消息）。</param>
        /// <returns>实际应等待的秒数。</returns>
        /// <remarks>
        /// <paramref name="configuredDelay"/> 为 0 或负数时<b>原样返回</b>，不做任何加长 ——
        /// 那表示策划要这条消息立刻出现（连"正在输入"都不显示），是个有意义的开关，不该被覆盖。
        /// </remarks>
        public static float EffectiveDelay(float configuredDelay, string text)
        {
            if (configuredDelay <= 0f) return configuredDelay;

            // 用 string.Length 而非更精确的字素计数：代理对（Emoji）会被算作两个字符，
            // 但这里只是拿它估个量级，多算一个字符不影响观感，不值得为此引入文本分段逻辑
            int length = text != null ? text.Length : 0;

            float byLength = Mathf.Clamp(length * SecondsPerCharacter, MinSeconds, MaxSeconds);

            return Mathf.Max(configuredDelay, byLength);
        }
    }
}
