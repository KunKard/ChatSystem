// 把 Assets/Scripts/Tests 里的关键用例在 Unity 之外跑一遍。
// 验的是真实的 Data / Runtime 源码（由 csproj 的 Compile Include 引入），不是复制品。
using System;
using System.Collections.Generic;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using ChatSystem.EditorTools;
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
            validator();
            nodeIdUtility();
            mediaLibrary();

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
            // 输入期间预览**不**变成"正在输入"：整个界面里表示它的只留消息区那个三点气泡。
            // 这条断言守的是那个有意偏离设计文档的决定，别再改回去
            Eq("默认预览", sd.PreviewText, "输入期间预览不被替换");
            Check(sd.IsTyping, "输入状态本身仍在 IsTyping 上（供三点气泡用）");
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

        // ── DialogueValidator ───────────────────────────────────────

        private static int Errors(List<ValidationIssue> issues)
        {
            int n = 0;
            foreach (var issue in issues) if (issue.Severity == IssueSeverity.Error) n++;
            return n;
        }

        private static int Warnings(List<ValidationIssue> issues)
        {
            int n = 0;
            foreach (var issue in issues) if (issue.Severity == IssueSeverity.Warning) n++;
            return n;
        }

        private static bool Flagged(List<ValidationIssue> issues, int nodeIndex)
        {
            foreach (var issue in issues) if (issue.NodeIndex == nodeIndex) return true;
            return false;
        }

        private static bool Mentions(List<ValidationIssue> issues, string fragment)
        {
            foreach (var issue in issues)
            {
                if (issue.Message.Contains(fragment) || (issue.FixHint ?? "").Contains(fragment)) return true;
            }
            return false;
        }

        private static void validator()
        {
            Section("DialogueValidator 基础规则");

            // 干净资产必须零问题 —— 这既是"校验器不误报"的底线，也是真实数据的回归基线
            var clean = Asset("robin", Msg("n1", "A", next: "n2"), End("n2"));
            Eq(0, DialogueValidator.Validate(clean).Count, "干净资产零问题");

            var dangling = Asset("robin", Msg("n1", "A", next: "nope"));
            var dIssues = DialogueValidator.Validate(dangling);
            Eq(1, Errors(dIssues), "断链 → 1 个错误");
            Check(Flagged(dIssues, 0), "断链定位到第 0 个节点");

            // Verify 漏掉的那条：Wait 的 nextId 为空，与 Message 同等严重
            var waitNoNext = Asset("robin", Wait("n1", 1f, null));
            Eq(1, Errors(DialogueValidator.Validate(waitNoNext)), "Wait 没有 nextId → 1 个错误");

            var noNext = Asset("robin", Msg("n1", "A"));
            Eq(1, Errors(DialogueValidator.Validate(noNext)), "Message 没有 nextId → 1 个错误");

            var noEntry = Asset("robin", Msg("n1", "A", next: "n2"), End("n2"));
            noEntry.entryNodeId = null;
            Eq(1, Errors(DialogueValidator.Validate(noEntry)), "入口为空 → 1 个错误");

            var badEntry = Asset("robin", Msg("n1", "A", next: "n2"), End("n2"));
            badEntry.entryNodeId = "gone";
            Eq(1, Errors(DialogueValidator.Validate(badEntry)), "入口不存在 → 1 个错误");

            var noContact = Asset("robin", End("n1"));
            noContact.contact = null;
            Check(Mentions(DialogueValidator.Validate(noContact), "联系人"), "联系人缺失被报出");

            var empty = Asset("robin");
            Eq(1, Errors(DialogueValidator.Validate(empty)), "空节点表 → 1 个错误（不重复报入口）");

            Section("DialogueValidator 节点身份");

            var dup = Asset("robin", End("n1"), End("n1"));
            var dupIssues = DialogueValidator.Validate(dup);
            Eq(1, Errors(dupIssues), "重复 ID → 1 个错误");
            Check(Flagged(dupIssues, 1), "重复 ID 定位到落选的那一个");

            var noId = Asset("robin", End("n1"), End(""));
            Check(Flagged(DialogueValidator.Validate(noId), 1), "空 ID 被报出");

            var nullNode = Asset("robin", End("n1"));
            nullNode.nodes.Add(null);
            Check(Flagged(DialogueValidator.Validate(nullNode), 1), "空引用节点被报出");

            var spaced = Asset("robin", End("n1 "));
            Check(Mentions(DialogueValidator.Validate(spaced), "空白"), "ID 首尾空白被报出");

            Section("DialogueValidator 选项");

            var noOptions = Asset("robin", Choice("n1"));
            Check(Mentions(DialogueValidator.Validate(noOptions), "一个选项都没有"), "Choice 零选项被报出");

            var nullOptions = Asset("robin", Choice("n1"));
            nullOptions.nodes[0].options = null;
            Check(Mentions(DialogueValidator.Validate(nullOptions), "null"), "options 为 null 被单独报出");

            var tooMany = Asset("robin", Choice("n1",
                Opt("a", "e"), Opt("b", "e"), Opt("c", "e"), Opt("d", "e")), End("e"));
            Check(Mentions(DialogueValidator.Validate(tooMany), "上限"), "选项数超上限被报出");

            var optProblems = Asset("robin", Choice("n1", Opt("", "e"), Opt("好的", null), Opt("去吧", "gone")), End("e"));
            var optIssues = DialogueValidator.Validate(optProblems);
            Check(Mentions(optIssues, "没有文案"), "选项空文案被报出");
            Check(Mentions(optIssues, "没有跳转目标"), "选项空跳转 → 警告");
            Check(Mentions(optIssues, "目标不存在"), "选项断链被报出");

            var choiceWithNext = Asset("robin", Choice("n1", Opt("a", "e")), End("e"));
            choiceWithNext.nodes[0].nextId = "e";
            Check(Mentions(DialogueValidator.Validate(choiceWithNext), "不会生效"), "Choice 上的 nextId 被警告");

            Section("DialogueValidator 消息与时间");

            var nullMessage = Asset("robin", new DialogueNode { id = "n1", kind = NodeKind.Message, nextId = "n2" }, End("n2"));
            Check(Mentions(DialogueValidator.Validate(nullMessage), "message 是空的"), "message 为 null 被报出");

            var sticker = Asset("robin", new DialogueNode
            {
                id = "n1",
                kind = NodeKind.Message,
                nextId = "n2",
                message = new MessageData { kind = MessageKind.Sticker, text = "", assetName = "" },
            }, End("n2"));
            Check(Mentions(DialogueValidator.Validate(sticker), "assetName"), "表情包缺资源名被报出");

            var labelOnly = Asset("robin", Msg("n1", "A", next: "n2", timeLabel: "昨天 21:30"), End("n2"));
            Check(Mentions(DialogueValidator.Validate(labelOnly), "没有时间数值"), "有文案无数值 → 警告");

            var valueOnly = Asset("robin", Msg("n1", "A", next: "n2", timeValue: 1000), End("n2"));
            Check(Mentions(DialogueValidator.Validate(valueOnly), "没有文案"), "有数值无文案 → 警告");

            var backwards = Asset("robin",
                Msg("n1", "A", next: "n2", timeLabel: "今天 09:00", timeValue: 5000),
                new DialogueNode { id = "n2", kind = NodeKind.End, timeLabel = "昨天 21:30", timeValueUtc = 1000 });
            Check(Mentions(DialogueValidator.Validate(backwards), "更早"), "时间倒退 → 警告");

            Section("DialogueValidator 图结构");

            var unreachable = Asset("robin", Msg("n1", "A", next: "n2"), End("n2"), End("orphan"));
            var unIssues = DialogueValidator.Validate(unreachable);
            Eq(0, Errors(unIssues), "不可达不是错误");
            Check(Flagged(unIssues, 2), "不可达节点被警告");

            // 零延迟环：唯一一类能把 Unity 直接搞崩的配置错误。
            // DialogueRunner.MaxHistorySteps 只在 _loadingHistory 时生效，选择之后的推进没有保护
            var cycle = Asset("robin", Msg("n1", "A", next: "n2"), Msg("n2", "B", next: "n1"));
            var cIssues = DialogueValidator.Validate(cycle);
            Eq(1, Errors(cIssues), "零延迟环 → 1 个错误");
            Check(Mentions(cIssues, "崩"), "零延迟环的说明点出会崩栈");

            // 环上有一个正延迟 → 递归会在那里断开，是"走不完"而不是崩栈
            var slowCycle = Asset("robin", Msg("n1", "A", next: "n2", delay: 1f), Msg("n2", "B", next: "n1"));
            Check(!Mentions(DialogueValidator.Validate(slowCycle), "崩"), "有延迟的环不报崩栈");

            // 绕回前面某个选项重新问一遍是正常设计，环跨过 Choice 就不该报
            var choiceLoop = Asset("robin",
                Choice("n1", Opt("再问一次", "n2")),
                Msg("n2", "好的", next: "n1"));
            Eq(0, Errors(DialogueValidator.Validate(choiceLoop)), "跨过 Choice 的环不误报");

            Section("DialogueValidator 引用索引");

            var inbound = Asset("robin", Msg("n1", "A", next: "n2"), End("n2"));
            var index = DialogueValidator.BuildInboundIndex(inbound);
            Eq(1, index.CountTo("n2"), "n2 有 1 条入边");
            Eq(0, index.CountTo("n1"), "入口没有入边");
            Check(index.RefsTo("n2")[0].IsDirect, "入边来自节点自身的 nextId");
        }

        private static void nodeIdUtility()
        {
            Section("NodeIdUtility");

            var asset = Asset("robin", Msg("s1", "A", next: "s2"), End("s2"));
            Eq("s3", NodeIdUtility.GenerateId(asset), "生成最小的空闲 ID");

            // 编辑器走的是这条重载：新增节点时数组刚被 SerializedProperty 改过，
            // 托管侧的 asset.nodes 还是旧快照，只能从属性视图那侧读"已占用的 ID"
            Eq("s2", NodeIdUtility.GenerateId(new[] { "s1", "s3" }), "从纯字符串表生成最小空闲 ID");
            Eq("s1", NodeIdUtility.GenerateId(new string[0]), "空表 → s1");
            Eq("s1", NodeIdUtility.GenerateId((IEnumerable<string>)null), "null → s1，不抛异常");
            Eq("s2", NodeIdUtility.GenerateId(new[] { "s1", null, "", "s1" }), "跳过空值，重复项不影响结果");

            var ids = new List<string>(NodeIdUtility.IdsOf(asset));
            Eq(2, ids.Count, "IdsOf 只枚举非空 ID");
            Eq("s1", ids[0], "IdsOf 保持列表顺序");

            // 三处引用：入口本身 + 两个节点的 nextId。改名必须一次性全部跟上，
            // 留一个中间态就可能存出一份断得毫无规律的资产
            var rename = Asset("robin",
                Choice("s1", Opt("甲", "s2"), Opt("乙", "s3")),
                Msg("s2", "A", next: "s4"),
                Msg("s3", "B", next: "s4"),
                End("s4"));
            rename.entryNodeId = "s4";
            Eq(3, NodeIdUtility.RewriteReferences(rename, "s4", "sX"), "改写入口 + 两条 nextId 引用");
            Eq("sX", rename.entryNodeId, "入口已改写");
            Eq("sX", rename.nodes[1].nextId, "节点 nextId 已改写");
            Eq(0, NodeIdUtility.RewriteReferences(rename, "s4", "sY"), "旧 ID 已不存在 → 0 处改写");

            var optRename = Asset("robin", Choice("s1", Opt("甲", "s2")), End("s2"));
            Eq(1, NodeIdUtility.RewriteReferences(optRename, "s2", "sY"), "改写选项引用");
            Eq("sY", optRename.nodes[0].options[0].nextId, "选项 nextId 已改写");

            var clear = Asset("robin", Msg("n1", "A", next: "n2"), End("n2"));
            Eq(1, NodeIdUtility.ClearReferencesTo(clear, "n2"), "清空指向 n2 的引用");
            Eq(null, clear.nodes[0].nextId, "引用已置空");

            var remove = Asset("robin", Msg("n1", "A", next: "n2"), End("n2"));
            Check(NodeIdUtility.RemoveNodeAt(remove, 1), "删除节点成功");
            Eq(1, remove.nodes.Count, "节点数减一");
            Check(!NodeIdUtility.RemoveNodeAt(remove, 9), "越界删除被拒绝");
        }

        // ── MediaLibrary ────────────────────────────────────────────

        /// <summary>
        /// 只验"没有资源库"和"库是空的"两条路径。
        /// </summary>
        /// <remarks>
        /// 命中条目的那条路径需要往 <c>entries</c> 里塞数据，而它是私有字段 ——
        /// 用反射去戳会把测试焊死在字段名上，代价大于收益，留给 Unity 侧的
        /// 编辑器下拉框（它本来就靠这个字段名工作）去覆盖。
        /// <para>
        /// 但这两条"空"路径恰好是最要紧的：它们对应"忘了跑构建表情资源库"，
        /// 症状是所有表情在游戏里变成纯色方块 —— 不报错、不崩溃，只是不对。
        /// 所以至少要钉死"取不到就是 null，且不抛异常"。
        /// </para>
        /// </remarks>
        private static void mediaLibrary()
        {
            Section("MediaLibrary");

            var empty = new MediaLibrary();

            Eq(null, empty.Find(null), "null 资源名 → null，不抛异常");
            Eq(null, empty.Find(""), "空资源名 → null");
            Eq(null, empty.Find("中秋快乐"), "空库里查名字 → null");

            MediaLibrary.SetCurrentForTests(empty);
            Eq(null, MediaLibrary.Resolve("嘿嘿"), "有库但库里没有 → null");

            MediaLibrary.SetCurrentForTests(null);
            Eq(null, MediaLibrary.Resolve("嘿嘿"), "没有库 → null，不抛异常");

            // 取不到时不能把"没找到"缓存下来，否则后来补建了资源库，
            // 不重启编辑器就永远还是取不到
            Eq(null, MediaLibrary.Current, "桩环境下 Resources.Load 取不到 → Current 为 null");
            Eq(null, MediaLibrary.Current, "再取一次仍是 null（没有把失败缓存成非空）");
        }
    }
}
