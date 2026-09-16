// 把 Assets/Scripts/Tests 里的关键用例在 Unity 之外跑一遍。
// 验的是真实的 Data / Runtime 源码（由 csproj 的 Compile Include 引入），不是复制品。
using System;
using System.Collections.Generic;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using ChatSystem.Runtime;

namespace LogicCheck
{
    internal static class Program
    {
        private static int _pass;
        private static readonly List<string> _failures = new List<string>();

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                _pass++;
            }
            else
            {
                _failures.Add(label);
                Console.WriteLine("    FAIL  " + label);
            }
        }

        private static void Eq<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                _failures.Add(label);
                Console.WriteLine($"    FAIL  {label}   (期望 {expected}，实际 {actual})");
                return;
            }
            _pass++;
        }

        private static void Section(string title) => Console.WriteLine("\n== " + title + " ==");

        // ── 构造辅助 ────────────────────────────────────────────────

        private static ConversationAsset Asset(string contactId, params DialogueNode[] nodes)
        {
            var a = new ConversationAsset { name = "TestConversation" };
            a.contact = new ContactProfile
            {
                name = "TestContact",
                id = contactId,
                displayName = contactId,
                signature = "个性签名",
                defaultPreview = "默认预览",
            };
            a.nodes = new List<DialogueNode>(nodes);
            a.entryNodeId = nodes.Length > 0 ? nodes[0].id : null;
            return a;
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
            => new DialogueNode { id = id, kind = NodeKind.Wait, delaySeconds = seconds, nextId = next };

        private static DialogueNode Choice(string id, params ChoiceOption[] options)
            => new DialogueNode { id = id, kind = NodeKind.Choice, options = new List<ChoiceOption>(options) };

        private static DialogueNode End(string id) => new DialogueNode { id = id, kind = NodeKind.End };

        private static ChoiceOption Opt(string text, string next, string timeLabel = null, long timeValue = 0)
            => new ChoiceOption { text = text, nextId = next, timeLabel = timeLabel, timeValueUtc = timeValue };

        private sealed class Recorder
        {
            public readonly List<MessageData> Messages = new List<MessageData>();
            public readonly List<IReadOnlyList<ChoiceOption>> Choices = new List<IReadOnlyList<ChoiceOption>>();
            public readonly List<bool> Typing = new List<bool>();
            public int EndedCount;

            public bool LastTyping => Typing.Count > 0 && Typing[Typing.Count - 1];
        }

        private static DialogueRunner Run(ConversationAsset asset, out Recorder rec)
        {
            // 用局部变量承接：out 参数不能被 lambda 捕获
            var recorder = new Recorder();
            var runner = new DialogueRunner(asset);
            runner.OnMessageEmitted += m => recorder.Messages.Add(m);
            runner.OnChoicesPresented += o => recorder.Choices.Add(o);
            runner.OnTypingChanged += (_, t) => recorder.Typing.Add(t);
            runner.OnEnded += () => recorder.EndedCount++;
            rec = recorder;
            return runner;
        }

        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            timeDividerPolicy();
            linear();
            delays();
            choices();
            dividers();
            restore();
            session();

            Console.WriteLine($"\n通过 {_pass}，失败 {_failures.Count}");
            if (_failures.Count > 0)
            {
                Console.WriteLine("失败项：");
                foreach (var f in _failures) Console.WriteLine("  - " + f);
                return 1;
            }
            Console.WriteLine("全部通过。");
            return 0;
        }

        // ── TimeDividerPolicy（纯函数） ──────────────────────────────

        private static void timeDividerPolicy()
        {
            Section("TimeDividerPolicy 纯函数");
            const long t = 1_700_000_000;

            Check(TimeDividerPolicy.ShouldInsert(0, t), "会话首条带时间的消息 → 插入");
            Check(!TimeDividerPolicy.ShouldInsert(t, t + 1), "间隔 1 秒 → 不插");
            Check(!TimeDividerPolicy.ShouldInsert(t, t + 299), "间隔 299 秒 → 不插");
            Check(!TimeDividerPolicy.ShouldInsert(t, t + 300), "间隔恰好 300 秒 → 不插（须严格大于）");
            Check(TimeDividerPolicy.ShouldInsert(t, t + 301), "间隔 301 秒 → 插");
            Check(!TimeDividerPolicy.ShouldInsert(t, 0), "当前无时间值 → 不插");
            Check(!TimeDividerPolicy.ShouldInsert(t, -1), "当前时间值为负 → 不插");
            Check(TimeDividerPolicy.ShouldInsert(t, t - 3600), "时间倒退 → 插（暴露配置错误）");
            Eq(300L, TimeDividerPolicy.ThresholdSeconds, "阈值常量为 300 秒");
        }

        // ── 基本推进 ────────────────────────────────────────────────

        private static void linear()
        {
            Section("线性推进 / 断链");

            var a1 = Asset("robin", Msg("n1", "A", next: "n2"), Msg("n2", "B", next: "n3"), End("n3"));
            var r1 = Run(a1, out var rec1);
            r1.Start("n1");
            Eq(2, rec1.Messages.Count, "线性推进发出 2 条");
            Eq("A", rec1.Messages[0].text, "第 1 条文案");
            Eq("B", rec1.Messages[1].text, "第 2 条文案");
            Eq("npc", rec1.Messages[0].senderId, "发送者 ID");
            Eq(1, rec1.EndedCount, "结束时广播一次");
            Check(!r1.IsRunning, "结束后不再处于运行态");

            var a2 = Asset("robin", End("n1"));
            var r2 = Run(a2, out var rec2);
            r2.Start("does_not_exist");
            Eq(1, rec2.EndedCount, "入口不存在 → 结束");
            Check(!r2.IsRunning, "入口不存在 → 不处于运行态");

            var a3 = Asset("robin", Msg("n1", "A", next: "missing"));
            var r3 = Run(a3, out var rec3);
            r3.Start("n1");
            Eq(1, rec3.Messages.Count, "断链前已发出的消息不回滚");
            Eq(1, rec3.EndedCount, "断链 → 结束");
        }

        // ── 延迟 ────────────────────────────────────────────────────

        private static void delays()
        {
            Section("延迟 / 输入状态");

            var a = Asset("robin", Msg("n1", "A", next: "n2", delay: 2f), End("n2"));
            var r = Run(a, out var rec);
            r.Start("n1");
            Eq(0, rec.Messages.Count, "延迟未走完不发出消息");
            Check(rec.LastTyping, "延迟期间处于正在输入");

            r.Tick(1f);
            Eq(0, rec.Messages.Count, "只走了 1 秒，还差 1 秒");

            r.Tick(1f);
            Eq(1, rec.Messages.Count, "延迟走完发出消息");
            Eq("A", rec.Messages[0].text, "延迟后发出的文案");
            Check(!rec.LastTyping, "消息发出后结束输入状态");
            Eq(1, rec.EndedCount, "走到末节点结束");

            var b = Asset("robin",
                Msg("n1", "A", next: "n2", delay: 1f),
                Msg("n2", "B", next: "n3", delay: 1f),
                End("n3"));
            var rb = Run(b, out var recb);
            rb.Start("n1");
            rb.Tick(100f);
            Eq(1, recb.Messages.Count, "单次 Tick 只结算一个延迟");
            rb.Tick(1f);
            Eq(2, recb.Messages.Count, "再 Tick 一次结算第二个");

            var c = Asset("robin", Msg("n1", "A", next: "n2", delay: 1f, sender: MessageFactory.PlayerSenderId), End("n2"));
            var rc = Run(c, out var recc);
            rc.Start("n1");
            Eq(0, recc.Typing.Count, "玩家消息不触发正在输入");

            var d = Asset("robin", Wait("n1", 2f, "n2"), Msg("n2", "A", next: "n3"), End("n3"));
            var rd = Run(d, out var recd);
            rd.Start("n1");
            Eq(0, recd.Messages.Count, "Wait 自身不发出消息");
            Check(recd.LastTyping, "Wait 后是 NPC 消息 → 显示正在输入");
            rd.Tick(2f);
            Eq(1, recd.Messages.Count, "Wait 走完自动推进");
            Eq("A", recd.Messages[0].text, "Wait 后发出的文案");

            var e = Asset("robin", Wait("n1", 1f, "n2"), Msg("n2", "A", next: "n3", sender: MessageFactory.PlayerSenderId), End("n3"));
            var re = Run(e, out var rece);
            re.Start("n1");
            Eq(0, rece.Typing.Count, "Wait 后是玩家消息 → 不显示正在输入");

            var f = Asset("robin", Msg("n1", "A", next: "missing", delay: 1f));
            var rf = Run(f, out var recf);
            rf.Start("n1");
            Check(recf.LastTyping, "中断前处于正在输入");
            rf.Tick(1f);
            Check(!recf.LastTyping, "中断后清理输入状态（签名不会卡住）");
        }

        // ── 选项 ────────────────────────────────────────────────────

        private static void choices()
        {
            Section("选项节点");

            var a = Asset("robin",
                Choice("n1", Opt("想去看演出", "n2"), Opt("下次吧", "n3")),
                Msg("n2", "好呀", next: "n4"),
                Msg("n3", "那下次", next: "n4"),
                End("n4"));
            var r = Run(a, out var rec);
            r.Start("n1");
            Eq(1, rec.Choices.Count, "进入 Choice 广播一次选项");
            Eq(2, rec.Choices[0].Count, "选项数量");
            Check(r.IsWaitingForChoice, "等待选择");
            Eq(0, rec.Messages.Count, "选择前不发出消息");

            r.Choose(0);
            Eq(2, rec.Messages.Count, "选择后发出玩家消息 + NPC 回应");
            Eq("想去看演出", rec.Messages[0].text, "选项文案作为消息发出");
            Eq(MessageFactory.PlayerSenderId, rec.Messages[0].senderId, "选项以玩家身份发出");
            Eq("好呀", rec.Messages[1].text, "跳到选中项指向的节点");
            Check(!r.IsWaitingForChoice, "选择后不再等待");

            var b = Asset("robin", Choice("n1", Opt("唯一选项", "n2")), End("n2"));
            var rb = Run(b, out var recb);
            rb.Start("n1");
            rb.Choose(5);
            rb.Choose(-1);
            Check(rb.IsWaitingForChoice, "非法下标不消耗选择机会");
            Eq(0, recb.Messages.Count, "非法下标不发出消息");

            var c = Asset("robin", Msg("n1", "A", next: "n2"), End("n2"));
            var rc = Run(c, out var recc);
            rc.Start("n1");
            rc.Choose(0);
            Eq(1, recc.Messages.Count, "未处于选项节点时 Choose 无效果");
        }

        // ── 时间分割线 ──────────────────────────────────────────────

        private static void dividers()
        {
            Section("时间分割线");

            var a = Asset("robin", Msg("n1", "A", timeLabel: "昨天 21:30", timeValue: 1000));
            var r = Run(a, out var rec);
            r.Start("n1");
            Eq(2, rec.Messages.Count, "首条带时间的消息前插入分割线");
            Eq(MessageKind.TimeDivider, rec.Messages[0].kind, "分割线的 kind");
            Eq("昨天 21:30", rec.Messages[0].text, "分割线文案");
            Eq("A", rec.Messages[1].text, "分割线后是原消息");

            var b = Asset("robin",
                Msg("n1", "A", next: "n2", timeLabel: "21:30", timeValue: 1000),
                Msg("n2", "B", next: "n3", timeLabel: "21:33", timeValue: 1180),
                End("n3"));
            var rb = Run(b, out var recb);
            rb.Start("n1");
            Eq(3, recb.Messages.Count, "间隔 3 分钟 → 第二条前不插线");
            Eq("B", recb.Messages[2].text, "第二条消息");

            var c = Asset("robin",
                Msg("n1", "A", next: "n2", timeLabel: "21:30", timeValue: 1000),
                Msg("n2", "B", next: "n3", timeLabel: "今天 09:00", timeValue: 1400),
                End("n3"));
            var rc = Run(c, out var recc);
            rc.Start("n1");
            Eq(4, recc.Messages.Count, "间隔超 5 分钟 → 插入第二条分割线");
            Eq(MessageKind.TimeDivider, recc.Messages[2].kind, "第二条分割线");
            Eq("今天 09:00", recc.Messages[2].text, "分割线取后一条消息的文案");

            var d = Asset("robin", Msg("n1", "A", next: "n2"), Msg("n2", "B", next: "n3"), End("n3"));
            var rd = Run(d, out var recd);
            rd.Start("n1");
            Eq(2, recd.Messages.Count, "全部未配置时间 → 无任何分割线");
            Check(recd.Messages.TrueForAll(m => m.kind != MessageKind.TimeDivider), "确认没有分割线");

            var e = Asset("robin",
                Msg("n1", "A", next: "n2", timeLabel: "21:30", timeValue: 1000),
                Msg("n2", "B", next: "n3"),
                Msg("n3", "C", next: "n4", timeLabel: "22:00", timeValue: 2800),
                End("n4"));
            var re = Run(e, out var rece);
            re.Start("n1");
            Eq(5, rece.Messages.Count, "未配置时间的消息被跳过，与最近带时间的比较");
            Eq("B", rece.Messages[2].text, "未配置时间的消息前不插线");
            Eq(MessageKind.TimeDivider, rece.Messages[3].kind, "其后的带时间消息仍插线");
            Eq("22:00", rece.Messages[3].text, "分割线文案");

            var f = Asset("robin", Msg("n1", "A", timeLabel: "昨天 21:30", timeValue: 0));
            var rf = Run(f, out var recf);
            rf.Start("n1");
            Eq(1, recf.Messages.Count, "配了文案但缺时间值 → 不插线");
            Eq("A", recf.Messages[0].text, "原消息仍发出");

            var g = Asset("robin",
                Msg("n1", "A", next: "n2", timeLabel: "21:30", timeValue: 1000),
                Choice("n2", Opt("我在", "n3", timeLabel: "22:30", timeValue: 4600)),
                End("n3"));
            var rg = Run(g, out var recg);
            rg.Start("n1");
            rg.Choose(0);
            Eq(4, recg.Messages.Count, "玩家选项同样参与分割线判断");
            Eq(MessageKind.TimeDivider, recg.Messages[2].kind, "选项前的分割线");
            Eq("22:30", recg.Messages[2].text, "选项分割线文案");
            Eq("我在", recg.Messages[3].text, "选项消息");

            var h = Asset("robin", Msg("n1", "A", timeLabel: "21:31", timeValue: 1060));
            var rh = Run(h, out var rech);
            rh.Start("n1");
            Eq(2, rech.Messages.Count, "插入分割线");
            Eq(1060L, rh.LastTimedValueUtc, "插入后基线前移");

            var i = Asset("robin", Msg("n1", "A", timeLabel: "21:31", timeValue: 1060));
            var ri = Run(i, out var reci);
            ri.SeedTimeBaseline(1050);
            ri.Start("n1");
            Eq(1, reci.Messages.Count, "接上基线后仅隔 10 秒 → 不插线");
        }

        // ── 存档恢复 ────────────────────────────────────────────────

        private static void restore()
        {
            Section("存档恢复");

            var a = Asset("robin",
                Msg("n1", "A", next: "n2"),
                Msg("n2", "B", next: "n3"),
                Msg("n3", "C", next: "n4"),
                End("n4"));
            var r = Run(a, out var rec);
            r.RestoreTo("n2");
            Eq(0, rec.Messages.Count, "恢复不重放已持久化的历史");
            Eq("n2", r.CurrentNodeId, "游标回到存档位置");
            Check(r.IsRunning, "恢复后处于运行态");
            // 游标停在 n2（其内容存档前已 flush 发出），因此 Advance 推进到 n3
            r.Advance();
            Eq(1, rec.Messages.Count, "恢复后可继续推进");
            Eq("C", rec.Messages[0].text, "推进到存档位置的下一个节点");

            var b = Asset("robin",
                Choice("n1", Opt("甲", "n2"), Opt("乙", "n3")),
                Msg("n2", "A", next: "n4"),
                Msg("n3", "B", next: "n4"),
                End("n4"));
            var rb = Run(b, out var recb);
            rb.RestoreTo("n1");
            Eq(1, recb.Choices.Count, "停在选项节点 → 重新广播选项");
            Check(rb.IsWaitingForChoice, "重新进入等待选择");
            Eq(0, recb.Messages.Count, "重新广播选项不产生消息");

            var c = Asset("robin", End("n1"));
            var rc = Run(c, out var recc);
            rc.RestoreTo("gone");
            Eq(1, recc.EndedCount, "存档指向的节点不存在 → 结束");
        }

        // ── ChatSession ─────────────────────────────────────────────

        private static void session()
        {
            Section("ChatSession");

            var a = Asset("robin", Msg("n1", "A", next: "n2"), Msg("n2", "B", next: "n3"), End("n3"));
            var sa = new ChatSession(a) { IsActive = false };
            sa.Runner.Start("n1");
            Eq("robin", sa.ContactId, "联系人 ID");
            Eq(2, sa.Messages.Count, "会话累积历史");
            Eq(2, sa.UnreadCount, "非激活会话累加未读");
            sa.MarkRead();
            Eq(0, sa.UnreadCount, "进入聊天清零未读");

            var b = Asset("robin", Msg("n1", "A", next: "n2"), End("n2"));
            var sb = new ChatSession(b) { IsActive = true };
            sb.Runner.Start("n1");
            Eq(0, sb.UnreadCount, "激活会话不累加未读");

            var c = Asset("robin", End("n1"));
            var sc = new ChatSession(c) { IsActive = true };
            Eq("默认预览", sc.PreviewText, "无消息时回落到默认预览");

            var d = Asset("robin", Msg("n1", "A", next: "n2", delay: 1f), Msg("n2", "B", next: "n3"), End("n3"));
            var sd = new ChatSession(d) { IsActive = true };
            sd.Runner.Start("n1");
            Eq(ChatSession.TypingText, sd.PreviewText, "输入中优先显示正在输入");
            sd.Tick(1f);
            Eq("B", sd.PreviewText, "输入结束后回落到最后一条消息");

            var e = Asset("robin", Msg("n1", "A", next: "n2", timeLabel: "21:30", timeValue: 1000), End("n2"));
            var se = new ChatSession(e) { IsActive = true };
            se.Runner.Start("n1");
            Eq(2, se.Messages.Count, "分割线也在历史里");
            Eq("A", se.PreviewText, "预览跳过时间分割线");

            var f = Asset("robin",
                Msg("n1", "A", next: "n2"),
                Msg("n2", "B", next: "n3", timeLabel: "今天 09:00", timeValue: 5000),
                End("n3"));
            var sf = new ChatSession(f) { IsActive = true };
            sf.Restore(
                new List<MessageData> { new MessageData { kind = MessageKind.Text, senderId = "npc", text = "A" } },
                "n2",
                4000);
            Eq(1, sf.Messages.Count, "恢复载入历史");
            Eq("n2", sf.CurrentNodeId, "恢复还原游标");
            Eq(4000L, sf.LastTimedValueUtc, "恢复接上时间基线");
        }
    }
}
