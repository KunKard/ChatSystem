using ChatSystem.Runtime;
using NUnit.Framework;

namespace ChatSystem.Tests
{
    /// <summary>
    /// <see cref="TypingDurationPolicy"/> 的单测。
    /// </summary>
    /// <remarks>
    /// 纯函数，不需要场景、不需要 <c>Tick</c>，因此这里可以放心地对"字数 → 时长"这条曲线
    /// 做逐点断言。要测的是<b>形状</b>（单调、有上下限、配置值是下限）而不是某几个具体数字 ——
    /// 那样调整 <see cref="TypingDurationPolicy.SecondsPerCharacter"/> 时不会连测试一起改。
    /// </remarks>
    public class TypingDurationPolicyTests
    {
        /// <summary>造一段指定字数的文本。</summary>
        private static string Chars(int count)
        {
            return new string('字', count);
        }

        // ── 开关语义 ────────────────────────────────────────────────

        [Test]
        public void 配置为0_原样返回0()
        {
            Assert.AreEqual(0f, TypingDurationPolicy.EffectiveDelay(0f, Chars(200)),
                "0 表示立即发出，是个有意义的开关，不该被字数折算覆盖");
        }

        [Test]
        public void 配置为负_原样返回()
        {
            Assert.AreEqual(-1f, TypingDurationPolicy.EffectiveDelay(-1f, Chars(200)));
        }

        // ── 取较大者 ────────────────────────────────────────────────

        [Test]
        public void 短消息_配置值更大时取配置值()
        {
            // 1 字折算 0.05 → 抬到下限 0.8；配置的 2 秒更大
            Assert.AreEqual(2f, TypingDurationPolicy.EffectiveDelay(2f, "嗯"));
        }

        [Test]
        public void 长消息_字数更大时取字数()
        {
            // 100 字折算 5 秒 → 截断到上限 4 秒；配置的 0.5 秒更小
            Assert.AreEqual(TypingDurationPolicy.MaxSeconds, TypingDurationPolicy.EffectiveDelay(0.5f, Chars(100)));
        }

        [Test]
        public void 策划可以把节奏拖得比字数更长()
        {
            // 与上一条互为对照：Max 是单向的，配置值想更长就能更长，
            // 但不能让一条长文变快
            Assert.AreEqual(6f, TypingDurationPolicy.EffectiveDelay(6f, "嗯"));
        }

        // ── 上下限 ──────────────────────────────────────────────────

        [Test]
        public void 极短消息_不低于下限()
        {
            // 配置低于下限时被抬到 0.8：短消息也不该"一点就出来"，
            // 否则三点气泡根本来不及被看见
            Assert.AreEqual(TypingDurationPolicy.MinSeconds, TypingDurationPolicy.EffectiveDelay(0.1f, "A"));
        }

        [Test]
        public void 超长消息_不超过上限()
        {
            Assert.AreEqual(TypingDurationPolicy.MaxSeconds, TypingDurationPolicy.EffectiveDelay(0.1f, Chars(1000)),
                "到顶之后保持恒定，不再继续增长——否则真实感会变成卡住感");
        }

        [Test]
        public void 空文本_按零字算_落到下限()
        {
            // 表情包、图片这类消息没有 text，长度按 0 处理
            Assert.AreEqual(TypingDurationPolicy.MinSeconds, TypingDurationPolicy.EffectiveDelay(0.1f, null));
            Assert.AreEqual(TypingDurationPolicy.MinSeconds, TypingDurationPolicy.EffectiveDelay(0.1f, string.Empty));
        }

        // ── 单调性 ──────────────────────────────────────────────────

        [Test]
        public void 字数越多_等待不更短()
        {
            // 这条规则的目的本身就是"时长与字数正相关"，所以逐点比一遍整条曲线。
            // 允许持平：撞到上下限时本来就不再变化
            float previous = 0f;

            for (int length = 0; length <= 200; length++)
            {
                float current = TypingDurationPolicy.EffectiveDelay(0.5f, Chars(length));

                Assert.GreaterOrEqual(current, previous, $"{length} 字的等待比 {length - 1} 字的还短");
                previous = current;
            }
        }

        [Test]
        public void 上下限之间_严格随字数增长()
        {
            // 上一条允许持平，这一条把中段单独钉住，挡住"整个区间被压成常数"这种退化
            Assert.Less(
                TypingDurationPolicy.EffectiveDelay(0.1f, Chars(20)),
                TypingDurationPolicy.EffectiveDelay(0.1f, Chars(60)));
        }

        [Test]
        public void 中段折算_与每字秒数一致()
        {
            Assert.AreEqual(
                20 * TypingDurationPolicy.SecondsPerCharacter,
                TypingDurationPolicy.EffectiveDelay(0.1f, Chars(20)),
                0.0001f);
        }
    }
}
