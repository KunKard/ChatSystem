using ChatSystem.Runtime;
using NUnit.Framework;

namespace ChatSystem.Tests
{
    /// <summary>
    /// 时间分割线插入规则的单测。纯函数，不涉及任何 Unity 对象。
    /// </summary>
    public class TimeDividerPolicyTests
    {
        [Test]
        public void 会话首条带时间的消息_插入()
        {
            // last == 0 表示会话中还没有带时间的消息
            Assert.IsTrue(TimeDividerPolicy.ShouldInsert(0, 1_700_000_000));
        }

        [Test]
        public void 间隔小于阈值_不插入()
        {
            const long t = 1_700_000_000;
            Assert.IsFalse(TimeDividerPolicy.ShouldInsert(t, t + 1));
            Assert.IsFalse(TimeDividerPolicy.ShouldInsert(t, t + 299));
        }

        [Test]
        public void 间隔等于阈值_不插入_必须严格大于()
        {
            const long t = 1_700_000_000;
            Assert.IsFalse(
                TimeDividerPolicy.ShouldInsert(t, t + TimeDividerPolicy.ThresholdSeconds),
                "规则是\"超过 5 分钟\"，恰好 5 分钟不算超过");
        }

        [Test]
        public void 间隔大于阈值_插入()
        {
            const long t = 1_700_000_000;
            Assert.IsTrue(TimeDividerPolicy.ShouldInsert(t, t + 301));
            Assert.IsTrue(TimeDividerPolicy.ShouldInsert(t, t + 86_400));
        }

        [Test]
        public void 当前消息没有时间值_不插入()
        {
            const long t = 1_700_000_000;
            Assert.IsFalse(TimeDividerPolicy.ShouldInsert(t, 0));
            Assert.IsFalse(TimeDividerPolicy.ShouldInsert(t, -1));
        }

        [Test]
        public void 时间倒退_插入_让配置错误在界面上暴露()
        {
            const long t = 1_700_000_000;
            Assert.IsTrue(TimeDividerPolicy.ShouldInsert(t, t - 3600));
        }

        [Test]
        public void 阈值常量为五分钟()
        {
            Assert.AreEqual(300L, TimeDividerPolicy.ThresholdSeconds);
        }
    }
}
