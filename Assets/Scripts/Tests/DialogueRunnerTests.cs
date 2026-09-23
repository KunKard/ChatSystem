using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using ChatSystem.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChatSystem.Tests
{
    /// <summary>
    /// <see cref="DialogueRunner"/> 的状态机单测。
    /// </summary>
    /// <remarks>
    /// 全部用例都<b>不进入 Play 模式</b>——这正是把时间设计成 <c>Tick(deltaTime)</c> 显式输入、
    /// 而不是用协程的收益：测试里"快进 2 秒"就是传一个 2f，无需真实等待。
    /// </remarks>
    public class DialogueRunnerTests
    {
        // ── 测试夹具 ────────────────────────────────────────────────

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            // ScriptableObject 实例不受 GC 管理，必须显式销毁，否则用例之间会互相污染
            foreach (var obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private ContactProfile MakeContact(string id)
        {
            var contact = ScriptableObject.CreateInstance<ContactProfile>();
            contact.id = id;
            contact.displayName = id;
            contact.signature = "个性签名";
            contact.defaultPreview = "默认预览";
            _created.Add(contact);
            return contact;
        }

        private ConversationAsset MakeAsset(string contactId, params DialogueNode[] nodes)
        {
            var asset = ScriptableObject.CreateInstance<ConversationAsset>();
            asset.name = "TestConversation";
            asset.contact = MakeContact(contactId);
            asset.nodes = new List<DialogueNode>(nodes);
            asset.entryNodeId = nodes.Length > 0 ? nodes[0].id : null;
            _created.Add(asset);
            return asset;
        }

        private static DialogueNode Msg(string id, string text, string next = null, string sender = "npc",
                                        float delay = 0f, string timeLabel = null, long timeValue = 0)
        {
            return new DialogueNode
            {
                id = id,
                kind = NodeKind.Message,
                nextId = next,
                delaySeconds = delay,
                timeLabel = timeLabel,
                timeValueUtc = timeValue,
                message = new MessageData
                {
                    kind = MessageKind.Text,
                    senderId = sender,
                    text = text,
                    assetName = string.Empty,
                },
            };
        }

        private static DialogueNode Wait(string id, float seconds, string next)
        {
            return new DialogueNode { id = id, kind = NodeKind.Wait, delaySeconds = seconds, nextId = next };
        }

        private static DialogueNode Choice(string id, params ChoiceOption[] options)
        {
            return new DialogueNode { id = id, kind = NodeKind.Choice, options = new List<ChoiceOption>(options) };
        }

        private static DialogueNode End(string id)
        {
            return new DialogueNode { id = id, kind = NodeKind.End };
        }

        private static ChoiceOption Opt(string text, string next, string timeLabel = null, long timeValue = 0)
        {
            return new ChoiceOption { text = text, nextId = next, timeLabel = timeLabel, timeValueUtc = timeValue };
        }

        private class Recorder
        {
            public readonly List<MessageData> Messages = new List<MessageData>();
            public readonly List<IReadOnlyList<ChoiceOption>> Choices = new List<IReadOnlyList<ChoiceOption>>();
            public readonly List<bool> Typing = new List<bool>();
            public int EndedCount;

            public bool LastTyping => Typing.Count > 0 && Typing[Typing.Count - 1];
        }

        private static DialogueRunner Run(ConversationAsset asset, out Recorder recorder)
        {
            // 用局部变量承接：out 参数不能被 lambda 捕获（CS1628）
            var rec = new Recorder();
            var runner = new DialogueRunner(asset);
            runner.OnMessageEmitted += m => rec.Messages.Add(m);
            runner.OnChoicesPresented += o => rec.Choices.Add(o);
            runner.OnTypingChanged += (_, typing) => rec.Typing.Add(typing);
            runner.OnEnded += () => rec.EndedCount++;
            recorder = rec;
            return runner;
        }

        // ── 基本推进 ────────────────────────────────────────────────

        [Test]
        public void 线性推进_逐条发出消息并在末节点结束()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2"),
                Msg("n2", "B", next: "n3"),
                End("n3"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(2, rec.Messages.Count);
            Assert.AreEqual("A", rec.Messages[0].text);
            Assert.AreEqual("B", rec.Messages[1].text);
            Assert.AreEqual("npc", rec.Messages[0].senderId);
            Assert.AreEqual(1, rec.EndedCount);
            Assert.IsFalse(runner.IsRunning);
        }

        [Test]
        public void 入口节点不存在_报错并结束_不抛异常()
        {
            var asset = MakeAsset("robin", End("n1"));
            var runner = Run(asset, out var rec);

            LogAssert.Expect(LogType.Error, new Regex("不存在"));

            Assert.DoesNotThrow(() => runner.Start("does_not_exist"));
            Assert.AreEqual(1, rec.EndedCount);
            Assert.IsFalse(runner.IsRunning);
        }

        [Test]
        public void 断链_报错并结束_已发出的消息不回滚()
        {
            var asset = MakeAsset("robin", Msg("n1", "A", next: "missing"));
            var runner = Run(asset, out var rec);

            LogAssert.Expect(LogType.Error, new Regex("不存在"));
            runner.Start("n1");

            Assert.AreEqual(1, rec.Messages.Count);
            Assert.AreEqual(1, rec.EndedCount);
        }

        // ── 延迟与输入状态 ──────────────────────────────────────────

        [Test]
        public void 延迟节点_Tick走完才发出消息()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2", delay: 2f),
                End("n2"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(0, rec.Messages.Count, "延迟未走完不应发出消息");
            Assert.IsTrue(rec.LastTyping, "延迟期间应处于\"对方正在输入\"");

            runner.Tick(1f);
            Assert.AreEqual(0, rec.Messages.Count, "只走了 1 秒，还差 1 秒");

            runner.Tick(1f);
            Assert.AreEqual(1, rec.Messages.Count);
            Assert.AreEqual("A", rec.Messages[0].text);
            Assert.IsFalse(rec.LastTyping, "消息发出后应结束输入状态");
            Assert.AreEqual(1, rec.EndedCount);
        }

        [Test]
        public void 单次Tick只走完一个延迟()
        {
            // 这是 Tick 的既定语义：一帧只结算一个节点，避免一次大 delta 把整段剧情瞬间播完
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2", delay: 1f),
                Msg("n2", "B", next: "n3", delay: 1f),
                End("n3"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");
            runner.Tick(100f);

            Assert.AreEqual(1, rec.Messages.Count, "第二次延迟需要再调一次 Tick");
            runner.Tick(1f);
            Assert.AreEqual(2, rec.Messages.Count);
        }

        [Test]
        public void 延迟_长消息等得比配置值更久()
        {
            // 100 字折算到上限 4 秒，配置的 0.5 秒只是下限，不足以发出
            var asset = MakeAsset("robin",
                Msg("n1", new string('字', 100), next: "n2", delay: 0.5f),
                End("n2"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            runner.Tick(0.5f);
            Assert.AreEqual(0, rec.Messages.Count, "走完配置的 0.5 秒还不够，字数算出的等待更长");

            runner.Tick(3.5f);
            Assert.AreEqual(1, rec.Messages.Count, "累计 4 秒后应发出");
        }

        [Test]
        public void 延迟_短消息到点即发_不被字数拖长()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "嗯", next: "n2", delay: 2f),
                End("n2"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            // 用 1.5 + 0.5 而不是 1.9 + 0.1：后者在 float 里减不干净
            //（2 - 1.9 = 0.100000024），会残留一个正数让 Tick 提前返回
            runner.Tick(1.5f);
            Assert.AreEqual(0, rec.Messages.Count);

            runner.Tick(0.5f);
            Assert.AreEqual(1, rec.Messages.Count, "字数时长低于配置值，起决定作用的仍是配置值");
        }

        [Test]
        public void 延迟_Wait节点不受字数折算影响()
        {
            // Wait 不发声，没有"字数"可言；这里就是确切的等待时长
            var asset = MakeAsset("robin",
                Wait("w1", 1f, "n1"),
                Msg("n1", new string('字', 200), next: "n2"),
                End("n2"));

            var runner = Run(asset, out var rec);
            runner.Start("w1");

            Assert.AreEqual(0, rec.Messages.Count);
            runner.Tick(1f);
            Assert.AreEqual(1, rec.Messages.Count, "Wait 走完后进入的是无延迟节点，应立即发出");
        }

        [Test]
        public void 玩家消息不触发正在输入()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2", delay: 1f, sender: MessageFactory.PlayerSenderId),
                End("n2"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(0, rec.Typing.Count, "玩家自己的消息不该显示\"对方正在输入\"");
        }

        [Test]
        public void Wait节点_延迟后自动推进且自身不发出消息()
        {
            var asset = MakeAsset("robin",
                Wait("n1", 2f, "n2"),
                Msg("n2", "A", next: "n3"),
                End("n3"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(0, rec.Messages.Count);
            Assert.IsTrue(rec.LastTyping, "Wait 后紧接 NPC 消息，等待期间应显示正在输入");

            runner.Tick(2f);
            Assert.AreEqual(1, rec.Messages.Count);
            Assert.AreEqual("A", rec.Messages[0].text);
        }

        [Test]
        public void Wait节点后是玩家消息_不触发正在输入()
        {
            var asset = MakeAsset("robin",
                Wait("n1", 1f, "n2"),
                Msg("n2", "A", next: "n3", sender: MessageFactory.PlayerSenderId),
                End("n3"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(0, rec.Typing.Count);
        }

        [Test]
        public void 中断时清理正在输入状态()
        {
            var asset = MakeAsset("robin", Msg("n1", "A", next: "missing", delay: 1f));
            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.IsTrue(rec.LastTyping);

            LogAssert.Expect(LogType.Error, new Regex("不存在"));
            runner.Tick(1f);

            Assert.IsFalse(rec.LastTyping, "对话中断后签名不能永远卡在\"对方正在输入…\"");
        }

        // ── 选项 ────────────────────────────────────────────────────

        [Test]
        public void 选项节点_广播选项_选择后作为玩家消息发出并跳转()
        {
            var asset = MakeAsset("robin",
                Choice("n1", Opt("想去看演出", "n2"), Opt("下次吧", "n3")),
                Msg("n2", "好呀", next: "n4"),
                Msg("n3", "那下次", next: "n4"),
                End("n4"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(1, rec.Choices.Count);
            Assert.AreEqual(2, rec.Choices[0].Count);
            Assert.IsTrue(runner.IsWaitingForChoice);
            Assert.AreEqual(0, rec.Messages.Count, "选择前不应发出任何消息");

            runner.Choose(0);

            Assert.AreEqual(2, rec.Messages.Count);
            Assert.AreEqual("想去看演出", rec.Messages[0].text);
            Assert.AreEqual(MessageFactory.PlayerSenderId, rec.Messages[0].senderId,
                "选项文案以玩家身份发出");
            Assert.AreEqual("好呀", rec.Messages[1].text, "应跳到选中项指向的节点");
            Assert.IsFalse(runner.IsWaitingForChoice);
        }

        [Test]
        public void 非法选项下标_被忽略且不改变状态()
        {
            var asset = MakeAsset("robin",
                Choice("n1", Opt("唯一选项", "n2")),
                End("n2"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            LogAssert.Expect(LogType.Error, new Regex("非法选项下标"));
            runner.Choose(5);
            LogAssert.Expect(LogType.Error, new Regex("非法选项下标"));
            runner.Choose(-1);

            Assert.IsTrue(runner.IsWaitingForChoice, "非法输入不应消耗掉选择机会");
            Assert.AreEqual(0, rec.Messages.Count);
        }

        [Test]
        public void 未进入选项节点时Choose无效果()
        {
            var asset = MakeAsset("robin", Msg("n1", "A", next: "n2"), End("n2"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.DoesNotThrow(() => runner.Choose(0));
            Assert.AreEqual(1, rec.Messages.Count);
        }

        // ── 时间分割线 ──────────────────────────────────────────────

        [Test]
        public void 分割线_会话首条带时间的消息_插入()
        {
            var asset = MakeAsset("robin", Msg("n1", "A", timeLabel: "昨天 21:30", timeValue: 1000));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(2, rec.Messages.Count);
            Assert.AreEqual(MessageKind.TimeDivider, rec.Messages[0].kind);
            Assert.AreEqual("昨天 21:30", rec.Messages[0].text);
            Assert.AreEqual("A", rec.Messages[1].text);
        }

        [Test]
        public void 分割线_间隔不足五分钟_不插入()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2", timeLabel: "21:30", timeValue: 1000),
                Msg("n2", "B", next: "n3", timeLabel: "21:33", timeValue: 1180),
                End("n3"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(3, rec.Messages.Count, "首条插一条分割线，第二条不插");
            Assert.AreEqual(MessageKind.TimeDivider, rec.Messages[0].kind);
            Assert.AreEqual("A", rec.Messages[1].text);
            Assert.AreEqual("B", rec.Messages[2].text);
        }

        [Test]
        public void 分割线_间隔超过五分钟_插入且用后一条的文案()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2", timeLabel: "21:30", timeValue: 1000),
                Msg("n2", "B", next: "n3", timeLabel: "今天 09:00", timeValue: 1400),
                End("n3"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(4, rec.Messages.Count);
            Assert.AreEqual("21:30", rec.Messages[0].text);
            Assert.AreEqual("A", rec.Messages[1].text);
            Assert.AreEqual(MessageKind.TimeDivider, rec.Messages[2].kind);
            Assert.AreEqual("今天 09:00", rec.Messages[2].text, "分割线取后一条消息的文案");
            Assert.AreEqual("B", rec.Messages[3].text);
        }

        [Test]
        public void 分割线_未配置时间的消息自身不显示时间()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2"),
                Msg("n2", "B", next: "n3"),
                End("n3"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(2, rec.Messages.Count, "全部未配置时间，不应有任何分割线");
            Assert.IsTrue(rec.Messages.TrueForAll(m => m.kind != MessageKind.TimeDivider));
        }

        [Test]
        public void 分割线_未配置时间的消息被完全跳过_与最近的带时间消息比较()
        {
            // 判定语义：未配置时间的消息既不显示也不参与比较，比较发生在"最近一条带时间的消息"之间。
            // 因此 n2 不显示时间，而 n3 相对 n1 已过 30 分钟 → 插入分割线。
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2", timeLabel: "21:30", timeValue: 1000),
                Msg("n2", "B", next: "n3"),
                Msg("n3", "C", next: "n4", timeLabel: "22:00", timeValue: 2800),
                End("n4"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(5, rec.Messages.Count);
            Assert.AreEqual("21:30", rec.Messages[0].text);
            Assert.AreEqual("A", rec.Messages[1].text);
            Assert.AreEqual("B", rec.Messages[2].text, "未配置时间 → 前面不插线");
            Assert.AreEqual(MessageKind.TimeDivider, rec.Messages[3].kind);
            Assert.AreEqual("22:00", rec.Messages[3].text);
            Assert.AreEqual("C", rec.Messages[4].text);
        }

        [Test]
        public void 分割线_配了文案但缺时间值_告警且不插入()
        {
            var asset = MakeAsset("robin", Msg("n1", "A", timeLabel: "昨天 21:30", timeValue: 0));

            var runner = Run(asset, out var rec);

            LogAssert.Expect(LogType.Warning, new Regex("timeValueUtc 为 0"));
            runner.Start("n1");

            Assert.AreEqual(1, rec.Messages.Count);
            Assert.AreEqual("A", rec.Messages[0].text);
        }

        [Test]
        public void 分割线_玩家选项同样参与判断()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2", timeLabel: "21:30", timeValue: 1000),
                Choice("n2", Opt("我在", "n3", timeLabel: "22:30", timeValue: 4600)),
                End("n3"));

            var runner = Run(asset, out var rec);
            runner.Start("n1");
            runner.Choose(0);

            Assert.AreEqual(4, rec.Messages.Count);
            Assert.AreEqual("21:30", rec.Messages[0].text);
            Assert.AreEqual("A", rec.Messages[1].text);
            Assert.AreEqual(MessageKind.TimeDivider, rec.Messages[2].kind);
            Assert.AreEqual("22:30", rec.Messages[2].text);
            Assert.AreEqual("我在", rec.Messages[3].text);
        }

        [Test]
        public void 分割线_插入后比较基线前移()
        {
            var asset = MakeAsset("robin", Msg("n1", "A", timeLabel: "21:31", timeValue: 1060));

            var runner = Run(asset, out var rec);
            runner.Start("n1");

            Assert.AreEqual(2, rec.Messages.Count);
            Assert.AreEqual(1060, runner.LastTimedValueUtc);
        }

        [Test]
        public void SeedTimeBaseline_接上比较基线()
        {
            var asset = MakeAsset("robin", Msg("n1", "A", timeLabel: "21:31", timeValue: 1060));

            var runner = Run(asset, out var rec);
            runner.SeedTimeBaseline(1050);   // 存档里上一条带时间的消息
            runner.Start("n1");

            Assert.AreEqual(1, rec.Messages.Count, "仅隔 10 秒，不该插分割线");
            Assert.AreEqual("A", rec.Messages[0].text);
        }

        // ── 存档恢复 ────────────────────────────────────────────────

        [Test]
        public void RestoreTo_不重放已持久化的历史()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2"),
                Msg("n2", "B", next: "n3"),
                Msg("n3", "C", next: "n4"),
                End("n4"));

            var runner = Run(asset, out var rec);
            runner.RestoreTo("n2");

            Assert.AreEqual(0, rec.Messages.Count, "恢复不应重发消息");
            Assert.AreEqual("n2", runner.CurrentNodeId);
            Assert.IsTrue(runner.IsRunning);

            // 游标停在 n2 —— 它的内容在存档前已 flush 发出，所以 Advance 推进到 n3
            runner.Advance();
            Assert.AreEqual(1, rec.Messages.Count);
            Assert.AreEqual("C", rec.Messages[0].text, "推进到存档位置的下一个节点");
        }

        [Test]
        public void RestoreTo_停在选项节点时重新广播选项()
        {
            var asset = MakeAsset("robin",
                Choice("n1", Opt("甲", "n2"), Opt("乙", "n3")),
                Msg("n2", "A", next: "n4"),
                Msg("n3", "B", next: "n4"),
                End("n4"));

            var runner = Run(asset, out var rec);
            runner.RestoreTo("n1");

            Assert.AreEqual(1, rec.Choices.Count, "读档后选项面板应重新出现");
            Assert.IsTrue(runner.IsWaitingForChoice);
            Assert.AreEqual(0, rec.Messages.Count);
        }

        [Test]
        public void RestoreTo_存档指向的节点不存在_报错并结束()
        {
            var asset = MakeAsset("robin", End("n1"));
            var runner = Run(asset, out var rec);

            LogAssert.Expect(LogType.Error, new Regex("不存在"));
            Assert.DoesNotThrow(() => runner.RestoreTo("gone"));

            Assert.AreEqual(1, rec.EndedCount);
        }

        // ── ChatSession ─────────────────────────────────────────────

        [Test]
        public void 会话_累积历史_非激活时累加未读()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2"),
                Msg("n2", "B", next: "n3"),
                End("n3"));

            var session = new ChatSession(asset) { IsActive = false };
            session.Runner.Start("n1");

            Assert.AreEqual("robin", session.ContactId);
            Assert.AreEqual(2, session.Messages.Count);
            Assert.AreEqual(2, session.UnreadCount);

            session.MarkRead();
            Assert.AreEqual(0, session.UnreadCount);
        }

        [Test]
        public void 会话_激活时不累加未读()
        {
            var asset = MakeAsset("robin", Msg("n1", "A", next: "n2"), End("n2"));

            var session = new ChatSession(asset) { IsActive = true };
            session.Runner.Start("n1");

            Assert.AreEqual(1, session.Messages.Count);
            Assert.AreEqual(0, session.UnreadCount);
        }

        [Test]
        public void 会话预览_无消息时回落到默认预览()
        {
            var asset = MakeAsset("robin", End("n1"));
            var session = new ChatSession(asset) { IsActive = true };

            Assert.AreEqual("默认预览", session.PreviewText);
        }

        [Test]
        public void 会话预览_输入中不被正在输入覆盖()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2", delay: 1f),
                Msg("n2", "B", next: "n3"),
                End("n3"));

            var session = new ChatSession(asset) { IsActive = true };
            session.Runner.Start("n1");

            Assert.IsTrue(session.IsTyping, "打字状态本身仍在，供消息区那个三点气泡使用");
            Assert.AreEqual("默认预览", session.PreviewText,
                "预览不再替换为\"对方正在输入…\"——打字只由三点气泡表达（§5.2.5 ③ 的有意偏离）");

            session.Tick(1f);
            Assert.AreEqual("B", session.PreviewText, "输入结束后显示最后一条消息");
        }

        [Test]
        public void 会话预览_跳过时间分割线()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2", timeLabel: "21:30", timeValue: 1000),
                End("n2"));

            var session = new ChatSession(asset) { IsActive = true };
            session.Runner.Start("n1");

            Assert.AreEqual(2, session.Messages.Count, "分割线本身也在历史里");
            Assert.AreEqual("A", session.PreviewText, "预览不该显示\"21:30\"");
        }

        [Test]
        public void 会话_恢复存档_接上基线并还原游标()
        {
            var asset = MakeAsset("robin",
                Msg("n1", "A", next: "n2"),
                Msg("n2", "B", next: "n3", timeLabel: "今天 09:00", timeValue: 5000),
                End("n3"));

            var session = new ChatSession(asset) { IsActive = true };
            session.Restore(
                new List<MessageData> { new MessageData { kind = MessageKind.Text, senderId = "npc", text = "A" } },
                "n2",
                4000);

            Assert.AreEqual(1, session.Messages.Count);
            Assert.AreEqual("n2", session.CurrentNodeId);
            Assert.AreEqual(4000, session.LastTimedValueUtc);
        }
    }
}
