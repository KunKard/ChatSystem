using System;
using System.Collections.Generic;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using ChatSystem.Runtime;

namespace ChatSystem.EditorTools
{
    /// <summary>指向某个节点的一条引用。</summary>
    /// <remarks>删除节点、重命名 ID 之前要先知道"谁指着我"，这个结构就是那个答案的一项。</remarks>
    public readonly struct InboundRef
    {
        /// <summary>引用方节点在 <c>nodes</c> 里的下标。</summary>
        public readonly int FromNodeIndex;

        /// <summary><c>-1</c> 表示走节点自身的 <c>nextId</c>；<c>&gt;= 0</c> 表示走该下标的选项。</summary>
        public readonly int OptionIndex;

        public InboundRef(int fromNodeIndex, int optionIndex)
        {
            FromNodeIndex = fromNodeIndex;
            OptionIndex = optionIndex;
        }

        /// <summary>是否为"节点 → 节点"的直接跳转，而非经由某个选项。</summary>
        public bool IsDirect => OptionIndex < 0;
    }

    /// <summary>哪些地方指向了某个节点 ID。</summary>
    /// <remarks>
    /// 扫的是<b>原始数据</b>，包含重复 ID 的落选节点与不可达节点 —— 它服务于"改配置"而不是
    /// "跑对话"，改一条没被用到的引用同样要改。
    /// </remarks>
    public sealed class InboundIndex
    {
        private static readonly List<InboundRef> Empty = new List<InboundRef>();

        private readonly Dictionary<string, List<InboundRef>> _map;

        internal InboundIndex(Dictionary<string, List<InboundRef>> map)
        {
            _map = map;
        }

        /// <summary>指向 <paramref name="nodeId"/> 的全部引用；没有则返回空列表（不返回 null）。</summary>
        public IReadOnlyList<InboundRef> RefsTo(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return Empty;
            return _map.TryGetValue(nodeId, out var list) ? list : (IReadOnlyList<InboundRef>)Empty;
        }

        /// <summary>指向 <paramref name="nodeId"/> 的引用条数。"这个节点能不能安全删掉"看它。</summary>
        public int CountTo(string nodeId) => RefsTo(nodeId).Count;
    }

    /// <summary>
    /// 对话资产的静态校验。
    /// </summary>
    /// <remarks>
    /// <b>只算不报。</b>返回问题清单，自己不打印任何东西 —— 打印是调用方的事。
    /// 于是同一份规则既能被 Inspector 的内联报告消费，也能被单元测试直接断言，
    /// 而不用去截获日志。
    /// <para>
    /// 因此本文件，连同 <see cref="ValidationIssue"/> 与 <see cref="NodeIdUtility"/>，
    /// <b>不得出现 using UnityEditor</b>：<c>.logiccheck</c> 要在 Unity 之外编译它们。
    /// 校验器里一旦出现 <c>AssetDatabase</c>，那条最便宜的验证路径就断了。
    /// </para>
    /// <para>
    /// 规则里引用的每个常量都取自它真正的定义处（<see cref="ConversationLimits.MaxOptions"/>、
    /// <see cref="TimeDividerPolicy.ThresholdSeconds"/>），不另抄一份：校验器和运行时漂移之后
    /// 会开始说假话，而一个说假话的校验器比没有校验器更糟 —— 策划会连真话一起不信。
    /// </para>
    /// </remarks>
    public static class DialogueValidator
    {
        /// <summary>连续 Wait 的预读上限，取自 <c>DialogueRunner.PeekNextMessage</c> 的循环保护。</summary>
        public const int MaxConsecutiveWaits = 64;

        /// <summary>校验整个资产。</summary>
        public static List<ValidationIssue> Validate(ConversationAsset asset)
        {
            var issues = new List<ValidationIssue>();

            if (asset == null)
            {
                issues.Add(ValidationIssue.Asset(IssueSeverity.Error, "资产为 null。"));
                return issues;
            }

            CheckContact(asset, issues);

            var nodes = asset.nodes;
            if (nodes == null || nodes.Count == 0)
            {
                issues.Add(ValidationIssue.Asset(IssueSeverity.Error,
                    "节点表是空的。",
                    "打开这个联系人会是一条空会话。用「＋ 添加节点」加一条 —— 第一条会自动成为入口。"));
                return issues;
            }

            // 自建索引，绝不调 asset.GetNode：它内部那份缓存在整批改写 nodes 之后仍然指向旧节点
            // （只有 Inspector 改动触发的 OnValidate 会让它失效，而 OnValidate 是私有的）。
            // 用 Ordinal —— ID 是程序标识符，不该受区域设置影响（土耳其语 i/I 问题）
            var indexOf = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
            var effective = new bool[nodes.Count];

            CheckNodeIdentity(nodes, indexOf, effective, issues);
            CheckEntry(asset, indexOf, issues);

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!effective[i]) continue;
                CheckNode(nodes, i, indexOf, issues);
            }

            var inbound = BuildInboundIndex(asset);
            CheckUnreachable(nodes, effective, indexOf, asset.entryNodeId, issues);
            CheckCycles(nodes, effective, indexOf, issues);
            CheckWaitChains(nodes, effective, indexOf, inbound, issues);
            CheckTimeOrder(nodes, effective, inbound, issues);

            return issues;
        }

        /// <summary>建立"谁指着谁"的反向索引。删除 / 重命名节点前用它算出连带影响。</summary>
        public static InboundIndex BuildInboundIndex(ConversationAsset asset)
        {
            var map = new Dictionary<string, List<InboundRef>>(StringComparer.Ordinal);
            var nodes = asset != null ? asset.nodes : null;
            if (nodes == null) return new InboundIndex(map);

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null) continue;

                AddRef(map, node.nextId, new InboundRef(i, -1));

                if (node.options == null) continue;
                for (int o = 0; o < node.options.Count; o++)
                {
                    var option = node.options[o];
                    if (option == null) continue;
                    AddRef(map, option.nextId, new InboundRef(i, o));
                }
            }

            return new InboundIndex(map);
        }

        // ── 逐项规则 ────────────────────────────────────────────────

        private static void CheckContact(ConversationAsset asset, List<ValidationIssue> issues)
        {
            if (asset.contact == null)
            {
                issues.Add(ValidationIssue.Asset(IssueSeverity.Error,
                    "没有关联联系人（contact 为空）。",
                    "ChatAppController 会整条跳过这个资产，游戏里根本看不到这个联系人。"));
                return;
            }

            if (string.IsNullOrEmpty(asset.contact.id))
            {
                issues.Add(ValidationIssue.Asset(IssueSeverity.Error,
                    "联系人的 id 是空的。",
                    "它是存档键，也是消息 senderId 的比对依据 —— 空了等于没有键。"));
            }
        }

        /// <summary>节点身份三连：空引用、空 ID、重复 ID。</summary>
        private static void CheckNodeIdentity(List<DialogueNode> nodes, Dictionary<string, int> indexOf,
                                              bool[] effective, List<ValidationIssue> issues)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];

                if (node == null)
                {
                    issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, null,
                        "这一项是空引用（Missing）。",
                        "删掉它，或补回节点内容。"));
                    continue;
                }

                if (string.IsNullOrEmpty(node.id))
                {
                    issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, null,
                        "节点没有 ID。",
                        "运行时索引会跳过它，任何跳转都到不了这里。"));
                    continue;
                }

                if (indexOf.TryGetValue(node.id, out int first))
                {
                    issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                        $"节点 ID 与 #{first} 重复。",
                        "运行时的索引只保留第一个出现的，这一个及其后续内容永远不可达。"));
                    continue;
                }

                indexOf.Add(node.id, i);
                effective[i] = true;

                // ID 首尾带空白：`"s5 "` 和 `"s5"` 看起来一模一样，而 Ordinal 比较认为它们不同。
                // 排查这类问题极其耗时，所以在它变成一条断链之前就指出来
                if (node.id != node.id.Trim())
                {
                    issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, node.id,
                        "节点 ID 的首尾有空白字符。",
                        $"它和「{node.id.Trim()}」看起来完全一样，但 ID 用 Ordinal 比较，两者是不同的节点。"));
                }
            }
        }

        /// <summary>入口是否配置好。</summary>
        /// <remarks>
        /// <b>FixHint 必须说清去哪儿改，不能只说后果。</b>这两条的 <c>NodeIndex</c> 都是 -1，
        /// 内联报告里因此没有「定位」按钮可点；提示再只写一句"会是一条空会话"的话，
        /// 策划拿到的就是一个说得都对、但没有任何下一步的错误。
        /// <para>
        /// 指向 Inspector 的「入口节点」下拉框是刻意为之：<c>FixHint</c> 的全部用途
        /// 就是告诉人下一步做什么，而下一步发生在那个面板里。代价是这个文件知道了 UI 的措辞，
        /// 改标签时要回来一起改。
        /// </para>
        /// </remarks>
        private static void CheckEntry(ConversationAsset asset, Dictionary<string, int> indexOf,
                                       List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(asset.entryNodeId))
            {
                issues.Add(ValidationIssue.Asset(IssueSeverity.Error,
                    "没有设置入口节点（entryNodeId 为空）。",
                    "打开这个联系人会是一条空会话。在 Inspector 顶部标着「入口节点 = entryNodeId」" +
                    "的那一行下拉框里选一个；节点表为空时先加一条节点，它会自动成为入口。"));
                return;
            }

            if (!indexOf.ContainsKey(asset.entryNodeId))
            {
                issues.Add(ValidationIssue.Asset(IssueSeverity.Error,
                    $"入口节点「{asset.entryNodeId}」不在节点表里。",
                    "打开这个联系人会是一条空会话。回到「入口节点」下拉框重新选一个，" +
                    "或在某个节点上点「设为入口」。"));
            }
        }

        private static void CheckNode(List<DialogueNode> nodes, int i, Dictionary<string, int> indexOf,
                                      List<ValidationIssue> issues)
        {
            var node = nodes[i];

            switch (node.kind)
            {
                case NodeKind.Message: CheckMessage(node, i, indexOf, issues); break;
                case NodeKind.Choice: CheckChoice(node, i, indexOf, issues); break;
                case NodeKind.Wait: CheckWait(node, i, indexOf, issues); break;
                case NodeKind.End: CheckEnd(node, i, issues); break;
            }

            CheckTime(node.timeLabel, node.timeValueUtc, i, node.id, "节点", issues);
        }

        private static void CheckMessage(DialogueNode node, int i, Dictionary<string, int> indexOf,
                                         List<ValidationIssue> issues)
        {
            if (node.message == null)
            {
                issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                    "Message 节点的 message 是空的。",
                    "运行时 EmitNodeMessage 会打一条错误日志跳过这条消息，然后照常往下走 —— 玩家看到的是消息凭空少了一条。"));
            }
            else
            {
                switch (node.message.kind)
                {
                    case MessageKind.Sticker:
                    case MessageKind.Image:
                        if (string.IsNullOrEmpty(node.message.assetName))
                        {
                            issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                                $"这是 {node.message.kind} 消息，但没有填 assetName。",
                                "气泡没有可显示的图。"));
                        }
                        break;

                    case MessageKind.TimeDivider:
                        if (string.IsNullOrEmpty(node.message.text))
                        {
                            issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                                "手工配置的时间分割线没有文案。",
                                "运行时会在消息列表里插一条看不见的空行。"));
                        }
                        issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, node.id,
                            "手工配置了一条 TimeDivider 消息。",
                            "间隔超过阈值时运行时会自动插入分割线，手工再配一条会出现两条挨在一起。"));
                        break;
                }
            }

            CheckLinearLink(node, i, indexOf, issues);
        }

        private static void CheckChoice(DialogueNode node, int i, Dictionary<string, int> indexOf,
                                        List<ValidationIssue> issues)
        {
            if (node.options == null)
            {
                issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                    "Choice 节点的 options 是 null，而不是空列表。",
                    "读档路径 DialogueRunner.RestoreTo 会直接读 options.Count，那一行没有判空 —— 空引用异常。"));
                return;
            }

            if (node.options.Count == 0)
            {
                issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                    "Choice 节点一个选项都没有。",
                    "运行时会停在「等待选择」上，而选项面板收到空列表会直接隐藏 —— 玩家点不到任何东西，" +
                    "这个会话再也推不动。设计文档说「视为 End 处理」，但代码并没有这么做。"));
                return;
            }

            if (node.options.Count > ConversationLimits.MaxOptions)
            {
                issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                    $"有 {node.options.Count} 个选项，超过了上限 {ConversationLimits.MaxOptions} 个。",
                    "超出部分不会被渲染，玩家永远选不到 —— 它不是草稿，是看起来能用的死内容。"));
            }

            for (int o = 0; o < node.options.Count; o++)
            {
                var option = node.options[o];

                if (option == null)
                {
                    issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                        $"第 {o + 1} 个选项是空引用。",
                        "按钮会没有文案，点了也没反应。"));
                    continue;
                }

                if (string.IsNullOrEmpty(option.text))
                {
                    issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                        $"第 {o + 1} 个选项没有文案。",
                        "按钮是空白的。"));
                }

                if (string.IsNullOrEmpty(option.nextId))
                {
                    // 运行时有显式的空判断，点了会结束对话 —— 是"被支持"的写法，但更像漏配
                    issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, node.id,
                        $"第 {o + 1} 个选项没有跳转目标。",
                        "选中它会直接结束对话。设计上允许，但若确实要结束，指向一个 End 节点才看得出来是故意的。"));
                }
                else if (!indexOf.ContainsKey(option.nextId))
                {
                    issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                        $"第 {o + 1} 个选项跳转到「{option.nextId}」，目标不存在。",
                        "选中它之后对话会静默结束。"));
                }

                CheckTime(option.timeLabel, option.timeValueUtc, i, node.id,
                          $"第 {o + 1} 个选项", issues);
            }

            if (!string.IsNullOrEmpty(node.nextId))
            {
                issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, node.id,
                    $"Choice 节点还填了 nextId「{node.nextId}」。",
                    "运行时在 Choice 上不看这个字段（走各选项自己的 nextId），它永远不会生效，" +
                    "却会让后来人以为这里有一条路。"));
            }
        }

        private static void CheckWait(DialogueNode node, int i, Dictionary<string, int> indexOf,
                                      List<ValidationIssue> issues)
        {
            CheckLinearLink(node, i, indexOf, issues);
            CheckDelay(node, i, issues);
        }

        private static void CheckEnd(DialogueNode node, int i, List<ValidationIssue> issues)
        {
            if (!string.IsNullOrEmpty(node.nextId))
            {
                issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, node.id,
                    $"End 节点还填了 nextId「{node.nextId}」。",
                    "运行时遇到 End 直接结束，不会读它。"));
            }
        }

        /// <summary>Message / Wait 共用的后继检查 —— 两者都靠 <c>nextId</c> 线性推进。</summary>
        private static void CheckLinearLink(DialogueNode node, int i, Dictionary<string, int> indexOf,
                                            List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(node.nextId))
            {
                issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                    "没有填 nextId。",
                    "运行时把「空 nextId」当作对话结束，剧情会停在这里。" +
                    "如果这是有意的，请改用一个 End 节点 —— 那样才看得出来是故意的。"));
                return;
            }

            if (!indexOf.ContainsKey(node.nextId))
            {
                issues.Add(ValidationIssue.Node(IssueSeverity.Error, i, node.id,
                    $"nextId「{node.nextId}」不存在。",
                    "运行时找不到节点会直接结束对话 —— 不报错、不崩溃，只是这段剧情少了一截。"));
            }

            CheckDelay(node, i, issues);
        }

        private static void CheckDelay(DialogueNode node, int i, List<ValidationIssue> issues)
        {
            if (node.delaySeconds < 0f)
            {
                issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, node.id,
                    $"delaySeconds 是 {node.delaySeconds}。",
                    "运行时把 ≤ 0 一律当作「立即发出、不显示正在输入」，负数没有任何额外含义。"));
            }
        }

        /// <summary>时间配置：文案与数值缺一不可。</summary>
        private static void CheckTime(string timeLabel, long timeValueUtc, int i, string nodeId,
                                      string what, List<ValidationIssue> issues)
        {
            bool hasLabel = !string.IsNullOrEmpty(timeLabel);
            bool hasValue = timeValueUtc > 0;

            if (hasLabel && !hasValue)
            {
                issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, nodeId,
                    $"{what}配置了时间文案「{timeLabel}」但没有时间数值。",
                    "运行时不显示时间，也不参与分割线判断 —— 这个文案永远不会出现在界面上。"));
            }
            else if (!hasLabel && hasValue)
            {
                // 比上一条更隐蔽：运行时第一行就因文案为空而返回，数值被静默吞掉，
                // 界面上完全看不出这里配过东西
                issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, nodeId,
                    $"{what}有时间数值（{timeValueUtc}）但没有文案。",
                    "运行时第一行就因为文案为空而返回，数值被静默忽略 —— 这一点在界面上完全看不出来。"));
            }
        }

        // ── 图结构 ──────────────────────────────────────────────────

        private static void CheckUnreachable(List<DialogueNode> nodes, bool[] effective,
                                             Dictionary<string, int> indexOf, string entryNodeId,
                                             List<ValidationIssue> issues)
        {
            var reached = new bool[nodes.Count];
            Walk(nodes, effective, indexOf, reached, entryNodeId);

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!effective[i] || reached[i]) continue;

                issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, nodes[i].id,
                    "从入口走不到这个节点（草稿？）。",
                    "不影响运行，只是它永远不会出现。"));
            }
        }

        /// <summary>从起点沿所有边做一次可达性遍历。</summary>
        private static void Walk(List<DialogueNode> nodes, bool[] effective, Dictionary<string, int> indexOf,
                                 bool[] reached, string startId)
        {
            if (string.IsNullOrEmpty(startId) || !indexOf.TryGetValue(startId, out int start)) return;

            var queue = new Queue<int>();
            reached[start] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var node = nodes[queue.Dequeue()];

                if (node.options != null)
                {
                    for (int o = 0; o < node.options.Count; o++)
                    {
                        Enqueue(nodes, effective, indexOf, reached, queue, node.options[o]?.nextId);
                    }
                }

                Enqueue(nodes, effective, indexOf, reached, queue, node.nextId);
            }
        }

        private static void Enqueue(List<DialogueNode> nodes, bool[] effective, Dictionary<string, int> indexOf,
                                    bool[] reached, Queue<int> queue, string id)
        {
            if (string.IsNullOrEmpty(id) || !indexOf.TryGetValue(id, out int next)) return;
            if (!effective[next] || reached[next]) return;

            reached[next] = true;
            queue.Enqueue(next);
        }

        /// <summary>
        /// 找出线性推进路径上的环。
        /// </summary>
        /// <remarks>
        /// <b>这是唯一一类能让 Unity 直接崩掉的配置错误。</b>
        /// <c>DialogueRunner.MaxHistorySteps</c> 那道保护只在 <c>_loadingHistory</c> 为真时生效，
        /// 而玩家做出选择之后的推进（<c>Choose</c> → <c>EnterNode</c> → <c>AutoAdvance</c>）没有它。
        /// 环上只要全是零延迟的 Message / Wait，<c>EnterNode</c> 就会无限递归 —— 栈溢出，
        /// 连一条日志都来不及打。
        /// <para>
        /// 只在 <c>nextId</c> 这条边上找环（Choice / End 会终止推进，所以走到它们就停），
        /// 因此"绕回前面某个选项重新问一遍"这类<b>正常设计不会误报</b>。
        /// <c>nextId</c> 出度至多为 1，是函数式图，三色标记即可 O(n)。
        /// </para>
        /// </remarks>
        private static void CheckCycles(List<DialogueNode> nodes, bool[] effective,
                                        Dictionary<string, int> indexOf, List<ValidationIssue> issues)
        {
            var state = new byte[nodes.Count];   // 0 未访问 / 1 在当前路径上 / 2 已判定不在环里
            var path = new List<int>();

            for (int start = 0; start < nodes.Count; start++)
            {
                if (!effective[start] || state[start] != 0) continue;

                path.Clear();
                int cur = start;

                while (true)
                {
                    if (state[cur] == 1)
                    {
                        ReportCycle(nodes, path, path.IndexOf(cur), issues);
                        break;
                    }

                    if (state[cur] == 2) break;

                    state[cur] = 1;
                    path.Add(cur);

                    var node = nodes[cur];

                    // Choice 与 End 都会终止线性推进，环不可能跨过它们
                    if (node.kind == NodeKind.Choice || node.kind == NodeKind.End) break;
                    if (string.IsNullOrEmpty(node.nextId)) break;
                    if (!indexOf.TryGetValue(node.nextId, out int next)) break;   // 断链另有规则报

                    cur = next;
                }

                for (int p = 0; p < path.Count; p++) state[path[p]] = 2;
            }
        }

        private static void ReportCycle(List<DialogueNode> nodes, List<int> path, int from,
                                        List<ValidationIssue> issues)
        {
            if (from < 0) return;

            // 环上只要有一个节点配了正延迟，递归就会在那里断开，退化成"永远走不完"而不是崩栈
            bool allInstant = true;
            for (int p = from; p < path.Count; p++)
            {
                if (nodes[path[p]].delaySeconds > 0f) { allInstant = false; break; }
            }

            int first = path[from];
            var members = new List<string>();
            for (int p = from; p < path.Count; p++) members.Add($"「{nodes[path[p]].id}」");

            issues.Add(ValidationIssue.Node(IssueSeverity.Error, first, nodes[first].id,
                $"线性推进路径上有一个环：{string.Join(" → ", members.ToArray())} → 回到起点。",
                allInstant
                    ? "环上全是零延迟节点，玩家做出选择之后运行时会无限递归 —— 栈溢出，Unity 直接崩掉，" +
                      "连报错都来不及打。载入历史那条路有 1000 步保护，这条没有。"
                    : "对话会永远循环下去，永远到不了选项或结束。"));
        }

        /// <summary>连续 Wait 过多会让打字指示器失准，见 <c>DialogueRunner.PeekNextMessage</c> 的循环保护。</summary>
        private static void CheckWaitChains(List<DialogueNode> nodes, bool[] effective,
                                            Dictionary<string, int> indexOf, InboundIndex inbound,
                                            List<ValidationIssue> issues)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!effective[i] || nodes[i].kind != NodeKind.Wait) continue;
                if (HasWaitPredecessor(nodes, inbound, i)) continue;   // 只在本链的头部报一次

                int count = 0;
                int cur = i;

                while (cur >= 0 && count <= MaxConsecutiveWaits)
                {
                    count++;
                    var node = nodes[cur];
                    if (node.kind != NodeKind.Wait || string.IsNullOrEmpty(node.nextId)) break;
                    cur = indexOf.TryGetValue(node.nextId, out int next) ? next : -1;
                }

                if (count > MaxConsecutiveWaits)
                {
                    issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, nodes[i].id,
                        $"这里连续排了超过 {MaxConsecutiveWaits} 个 Wait 节点。",
                        "运行时的预读有同样步数的循环保护，超出之后就看不清后面第一条消息是不是 NPC 的，" +
                        "打字指示器会不再显示。"));
                }
            }
        }

        /// <summary>时间倒退：某个节点的时间早于指向它的前驱。</summary>
        /// <remarks>
        /// 只比较<b>直接前驱</b>，而运行时比的是"路径上最近一个带时间的节点"—— 中间隔着未配置时间的
        /// 节点时这里会漏报，但不会误报：只要直接前驱确实更晚，从它走过来就确实会倒退。
        /// 漏报可以接受，误报会让策划不再相信这个面板。
        /// <para>
        /// 两边都要求有文案：运行时 <c>MaybeEmitDivider</c> 第一行就因文案为空而返回，
        /// 只有数值没有文案的节点根本不参与比较，拿它当基线判断会得出运行时并不存在的结论。
        /// </para>
        /// </remarks>
        private static void CheckTimeOrder(List<DialogueNode> nodes, bool[] effective,
                                           InboundIndex inbound, List<ValidationIssue> issues)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!effective[i]) continue;

                var node = nodes[i];
                if (node.timeValueUtc <= 0 || string.IsNullOrEmpty(node.timeLabel)) continue;

                var refs = inbound.RefsTo(node.id);
                for (int r = 0; r < refs.Count; r++)
                {
                    var from = nodes[refs[r].FromNodeIndex];
                    if (from == null) continue;

                    long previous;
                    if (refs[r].IsDirect)
                    {
                        if (string.IsNullOrEmpty(from.timeLabel)) continue;
                        previous = from.timeValueUtc;
                    }
                    else
                    {
                        var options = from.options;
                        if (options == null || refs[r].OptionIndex >= options.Count) continue;

                        var option = options[refs[r].OptionIndex];
                        if (option == null || string.IsNullOrEmpty(option.timeLabel)) continue;
                        previous = option.timeValueUtc;
                    }

                    if (previous <= node.timeValueUtc) continue;

                    issues.Add(ValidationIssue.Node(IssueSeverity.Warning, i, node.id,
                        $"时间（{node.timeLabel}）比指向它的「{from.id}」更早。",
                        "运行时会把时间倒退当成配置错误，照样插一条分割线把它摆在界面上 —— " +
                        "界面上看着正常，但数据本身是反的。"));
                    break;   // 同一个节点只报一次
                }
            }
        }

        private static bool HasWaitPredecessor(List<DialogueNode> nodes, InboundIndex inbound, int index)
        {
            var refs = inbound.RefsTo(nodes[index].id);
            for (int r = 0; r < refs.Count; r++)
            {
                var from = nodes[refs[r].FromNodeIndex];
                if (from != null && from.kind == NodeKind.Wait) return true;
            }

            return false;
        }

        private static void AddRef(Dictionary<string, List<InboundRef>> map, string target, InboundRef reference)
        {
            if (string.IsNullOrEmpty(target)) return;

            if (!map.TryGetValue(target, out var list))
            {
                list = new List<InboundRef>();
                map[target] = list;
            }

            list.Add(reference);
        }
    }
}
