using System;
using System.Collections.Generic;
using System.IO;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using UnityEditor;
using UnityEngine;

namespace ChatSystem.EditorTools
{
    /// <summary>
    /// 生成对话测试数据（`Plan-4Days.md` 任务 1.7，Day 2 扩到 3 个联系人）。
    ///
    /// 【为什么用脚本而不是手填 Inspector】
    /// 长中文正文、Emoji 码位、时间戳这类内容手填容易出错，且无法复现。
    /// 生成器可重复执行：已存在的资产在原位覆盖，不会产生重复文件。
    ///
    /// 【为什么每个会话都停在 Choice 节点上】
    /// <c>ChatAppController</c> 在 <c>Start</c> 时会把<b>全部</b>会话都启动（§5.2.7 后台会话照常推进），
    /// 而 <c>Choice</c> 节点会让状态机停下来等玩家输入。把每个会话的路径都收敛到一个 Choice 节点上，
    /// 就保证了"打开游戏后不会有任何对话自己跑完"—— 三个会话各自停在选项面板前，
    /// 未读红点、预览文案、切换后重新弹面板这些行为才有稳定的观察时机。
    ///
    /// 【三个联系人的选项数分别是 3 / 2 / 1】
    /// 这是 Day 2 验收项"选项数量为 1 / 2 / 3 时布局均正常"的直接覆盖：
    /// 三种数量在同一次 Play 里都能点到，不需要改代码或改资产。
    ///
    /// 用法：菜单 Tools / ChatSystem / 生成 Day1 测试数据。
    /// </summary>
    public static class Day1TestDataGenerator
    {
        // 不用 Assets/Content 做目录名：场景里已经有个 UI 节点叫 Content，容易混淆
        private const string RootFolder = "Assets/GameData";
        private const string ContactFolder = RootFolder + "/Contacts";
        private const string ConversationFolder = RootFolder + "/Conversations";

        // 联系人稳定 ID。一经发布不可更改（存档键），见 ContactProfile.id 的注释
        private const string RobinId = "robin";
        private const string March7Id = "march7";
        private const string DanhengId = "danheng";

        // 压力测试用的联系人。由单独的菜单项生成，删掉不影响上面三个
        private const string StressId = "stress";
        private const int StressMessageCount = 500;
        private const float StressDelay = 0.02f;

        // EmojiOne 精灵资产只烘焙了 15 个字形，这里必须用其中存在的码位。
        // 用 \U 转义而非直接写字面量，避免源码文件编码影响内容。
        private const string EmojiSmile = "\U0001F60A"; // 😊 Smiling face with smiling eyes
        private const string EmojiCool = "\U0001F60E";  // 😎 Smiling face with sunglasses

        /// <summary>约 200 字中文，用于验证换行与气泡宽度上限。</summary>
        private const string LongChineseText =
            "下周的演出曲目我改了三遍，最后决定还是用最开始那一版。有时候第一直觉就是对的，" +
            "改来改去反而把最打动人的那部分磨掉了。你要是那天有空的话就过来吧，我给你留了第三排靠中间的位置，" +
            "那个角度能看清整个舞台的灯光变化，也能听清返场那首的换气声。对了，散场之后别急着走，" +
            "后台的门禁我让工作人员给你留着，我们可以在休息室坐一会儿，顺便把上次没说完的那件事聊完。" +
            "别再说什么「我只是路过」了，票都给你留了。";

        [MenuItem("Tools/ChatSystem/生成 Day1 测试数据")]
        public static void Generate()
        {
            EnsureFolder(ContactFolder);
            EnsureFolder(ConversationFolder);

            BuildRobin();
            BuildMarch7();
            BuildDanheng();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var robin = AssetDatabase.LoadAssetAtPath<ConversationAsset>(ConversationFolder + "/Conv_Robin.asset");
            EditorGUIUtility.PingObject(robin);
            Selection.activeObject = robin;

            Debug.Log("[Day1TestDataGenerator] 已生成 3 个联系人（知更鸟 3 选项 / 三月七 2 选项 / 丹恒 1 选项）。" +
                      $"路径：{ConversationFolder}。下一步：菜单 工具 / ChatSystem / 接线 Day 2 场景。");
        }

        // ------------------------------------------------------------------
        //  知更鸟 —— 3 个选项
        // ------------------------------------------------------------------

        /// <summary>
        /// 开场白自动播完（含长文本、Emoji、表情包、两条时间分割线），
        /// 然后停在 3 选项的 Choice 节点上。这一段同时承担 Day 1 遗留的四项渲染验证。
        /// </summary>
        private static void BuildRobin()
        {
            var contact = CreateContact("Contact_Robin", RobinId, "知更鸟", "正在筹备下一场演出", "你终于回我消息了");
            var conversation = CreateConversation("Conv_Robin", contact, "n1");

            conversation.nodes = new List<DialogueNode>
            {
                // ① 短文本 + 时间：会话首条带时间的消息 → 按策略插入首条分割线
                Npc("n1", RobinId, "在吗？", "n2",
                    timeLabel: "昨天 21:30", timeValueUtc: Utc(2026, 9, 16, 13, 30)),

                // ② 约 200 字中文，时间留空：验证换行与宽度上限
                Npc("n2", RobinId, LongChineseText, "n3"),

                // ③ Emoji 与文字混排，时间留空
                Npc("n3", RobinId, $"明天见{EmojiSmile} 记得把票带上{EmojiCool}", "n4"),

                // ④ 表情包 + 时间：与 ① 间隔 11h50m > 5 分钟 → 第二条分割线
                Sticker("n4", RobinId, "sticker_robin_01", "n5",
                        timeLabel: "今天 09:20", timeValueUtc: Utc(2026, 9, 17, 1, 20)),

                // ⑤ 短文本，时间显式留空 → 验证"未配置则不显示"
                Npc("n5", RobinId, "所以，那天你会来吗？", "n6"),

                // ⑥ 停在这里等玩家。三种选项数量里的 3
                Choice("n6",
                    Option("我那天有空，票给我留着", "a1"),
                    Option("尽量吧，最近有点忙", "b1"),
                    Option("……我考虑一下", "c1")),

                Npc("a1", RobinId, "那就说定了。第三排，中间。", "a2"),
                Npc("a2", RobinId, $"散场别急着走{EmojiCool}", "end"),

                Npc("b1", RobinId, "忙也要吃饭。我给你留到开场前。", "end"),

                // 延迟比其他回复长：验证"思考型"回复的节奏，也顺便看一下更久的输入态
                Npc("c1", RobinId, "好，我等你。", "end", delay: 2.0f),

                End("end"),
            };

            Save(conversation);
        }

        // ------------------------------------------------------------------
        //  三月七 —— 2 个选项
        // ------------------------------------------------------------------

        /// <summary>
        /// 两条开场白，第二条与第一条只隔 1 分钟 —— 小于 5 分钟阈值，<b>不应</b>插入第二条第分割线。
        /// 这是对 <c>TimeDividerPolicy</c> 阈值分支的界面级验证（单测已覆盖，这里让它在真实数据上可见）。
        /// </summary>
        private static void BuildMarch7()
        {
            var contact = CreateContact("Contact_March7", March7Id, "三月七", "今天也要元气满满！", "喂！在吗在吗");
            var conversation = CreateConversation("Conv_March7", contact, "m1");

            conversation.nodes = new List<DialogueNode>
            {
                Npc("m1", March7Id, "喂！你在干嘛呢", "m2",
                    timeLabel: "今天 14:02", timeValueUtc: Utc(2026, 9, 17, 6, 2)),

                // 与上一条间隔 1 分钟 < 5 分钟 → 只更新基线，不插线
                Npc("m2", March7Id, "我在整理上次拍的照片，好几张拍糊了", "m3",
                    timeLabel: "今天 14:03", timeValueUtc: Utc(2026, 9, 17, 6, 3)),

                Choice("m3",
                    Option("发我看看", "ma1"),
                    Option("你拍的我肯定不看", "mb1")),

                Npc("ma1", March7Id, "好嘞，等我挑一下", "ma2"),
                Sticker("ma2", March7Id, "sticker_march7_01", "mend"),

                Npc("mb1", March7Id, "喂！！生气了", "mb2", delay: 0.4f),
                Npc("mb2", March7Id, "……还是发你吧", "mend", delay: 1.4f),

                End("mend"),
            };

            Save(conversation);
        }

        // ------------------------------------------------------------------
        //  丹恒 —— 1 个选项
        // ------------------------------------------------------------------

        /// <summary>
        /// 只有一个选项的 Choice 节点。这是最容易出问题的边界：面板按运行时按钮数布局，
        /// 一个按钮时若沿用了"固定高 241"的旧假设，会看到一大片空白。
        /// </summary>
        private static void BuildDanheng()
        {
            var contact = CreateContact("Contact_Danheng", DanhengId, "丹恒", "勿扰", "资料放在桌上了");
            var conversation = CreateConversation("Conv_Danheng", contact, "d1");

            conversation.nodes = new List<DialogueNode>
            {
                Npc("d1", DanhengId, "资料放在桌上了。", "d2",
                    timeLabel: "今天 08:15", timeValueUtc: Utc(2026, 9, 17, 0, 15)),

                Npc("d2", DanhengId, "记得看第三页的批注。", "d3"),

                Choice("d3", Option("……好，我看完再说", "da1")),

                Npc("da1", DanhengId, "嗯。", "dend", delay: 1.6f),

                End("dend"),
            };

            Save(conversation);
        }

        // ------------------------------------------------------------------
        //  压力测试数据（单独菜单，默认不生成）
        // ------------------------------------------------------------------

        /// <summary>
        /// 生成一条 500 条消息的长链，用于验收"收发 500 条消息，Instantiate 次数 ≤ 池容量"。
        /// </summary>
        /// <remarks>
        /// <b>与主数据分开是有意的。</b>这条链会在 <c>Ctrl+S</c> 之外悄悄产生一个约 250 KB 的资产，
        /// 而它唯一的用途是验收那一刻；日常开发和 Day 4 录屏都不该带着它 ——
        /// 它会以 0.02 秒一条的速度在后台刷未读红点。
        /// <para>
        /// 验收流程：生成 → 重新执行一次"接线 Day 2 场景"（让它进入 <c>conversations</c> 列表）→
        /// Play 并切到【压力测试】→ 等约 10 秒 → 按 <b>F2</b> 读气泡实例化次数 → 用
        /// <c>删除压力测试数据</c> 清掉并重新接线。
        /// </para>
        /// </remarks>
        [MenuItem("Tools/ChatSystem/生成压力测试数据（500 条）")]
        public static void GenerateStressData()
        {
            EnsureFolder(ContactFolder);
            EnsureFolder(ConversationFolder);

            var contact = CreateContact("Contact_Stress", StressId, "【压力测试】",
                                        "0.02 秒一条，用于验证对象池", "准备就绪");
            var conversation = CreateConversation("Conv_Stress", contact, "s0");

            var nodes = new List<DialogueNode>(StressMessageCount + 2);

            for (int i = 0; i < StressMessageCount; i++)
            {
                nodes.Add(Npc($"s{i}", StressId, $"第 {i + 1} 条测试消息。", $"s{i + 1}", delay: StressDelay));
            }

            nodes.Add(End($"s{StressMessageCount}"));
            conversation.nodes = nodes;

            Save(conversation);

            Debug.Log($"[Day1TestDataGenerator] 已生成压力测试数据：{StressMessageCount} 条消息，" +
                      $"每条延迟 {StressDelay} 秒（约 {StressMessageCount * StressDelay:F0} 秒播完）。" +
                      "重新执行一次 工具 / ChatSystem / 接线 Day 2 场景，它才会出现在联系人列表里。");
        }

        /// <summary>删除压力测试数据。录屏或交付前用它清场。</summary>
        [MenuItem("Tools/ChatSystem/删除压力测试数据")]
        public static void DeleteStressData()
        {
            string[] paths =
            {
                ConversationFolder + "/Conv_Stress.asset",
                ContactFolder + "/Contact_Stress.asset",
            };

            bool any = false;
            foreach (var path in paths)
            {
                if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) == null) continue;

                AssetDatabase.DeleteAsset(path);
                any = true;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 资产没了，但场景的 conversations 列表里还留着指向它的引用（会显示为 Missing）。
            // 这一步不能代劳：重新接线是幂等的，让用户点一次比在这里偷偷改场景安全
            Debug.Log(any
                ? "[Day1TestDataGenerator] 已删除压力测试数据。请重新执行一次 工具 / ChatSystem / 接线 Day 2 场景，" +
                  "把场景里残留的 Missing 引用清掉。"
                : "[Day1TestDataGenerator] 没有找到压力测试数据，无需删除。");
        }

        // ------------------------------------------------------------------
        //  节点构造
        // ------------------------------------------------------------------

        /// <summary>构造一个 NPC 文本节点的简写。</summary>
        private static DialogueNode Npc(string id, string contactId, string text, string nextId,
                                        float delay = 0.5f,
                                        string timeLabel = null, long timeValueUtc = 0L)
        {
            return new DialogueNode
            {
                id = id,
                kind = NodeKind.Message,
                message = new MessageData
                {
                    kind = MessageKind.Text,
                    senderId = contactId,
                    text = text,
                    assetName = string.Empty,
                },
                delaySeconds = delay,
                nextId = nextId,
                timeLabel = timeLabel,
                timeValueUtc = timeValueUtc,
            };
        }

        /// <summary>构造一个表情包节点的简写。走 <c>Sticker</c> 节点渲染，没有美术资源时显示为色块。</summary>
        private static DialogueNode Sticker(string id, string contactId, string assetName, string nextId,
                                            float delay = 0.8f,
                                            string timeLabel = null, long timeValueUtc = 0L)
        {
            return new DialogueNode
            {
                id = id,
                kind = NodeKind.Message,
                message = new MessageData
                {
                    kind = MessageKind.Sticker,
                    senderId = contactId,
                    text = string.Empty,
                    assetName = assetName,
                },
                delaySeconds = delay,
                nextId = nextId,
                timeLabel = timeLabel,
                timeValueUtc = timeValueUtc,
            };
        }

        private static DialogueNode Choice(string id, params ChoiceOption[] options)
        {
            return new DialogueNode
            {
                id = id,
                kind = NodeKind.Choice,
                options = new List<ChoiceOption>(options),
            };
        }

        private static ChoiceOption Option(string text, string nextId,
                                           string timeLabel = null, long timeValueUtc = 0L)
        {
            return new ChoiceOption
            {
                text = text,
                nextId = nextId,
                timeLabel = timeLabel,
                timeValueUtc = timeValueUtc,
            };
        }

        private static DialogueNode End(string id)
        {
            return new DialogueNode { id = id, kind = NodeKind.End };
        }

        private static long Utc(int year, int month, int day, int hour, int minute)
        {
            return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        }

        // ------------------------------------------------------------------
        //  资产读写
        // ------------------------------------------------------------------

        private static ContactProfile CreateContact(string assetName, string id,
                                                    string displayName, string signature, string defaultPreview)
        {
            var contact = CreateOrLoad<ContactProfile>($"{ContactFolder}/{assetName}.asset");
            contact.id = id;
            contact.displayName = displayName;
            contact.signature = signature;
            contact.defaultPreview = defaultPreview;
            contact.avatar = null; // 美术资源待补，见 Day 4
            EditorUtility.SetDirty(contact);
            return contact;
        }

        private static ConversationAsset CreateConversation(string assetName, ContactProfile contact, string entryNodeId)
        {
            var conversation = CreateOrLoad<ConversationAsset>($"{ConversationFolder}/{assetName}.asset");
            conversation.contact = contact;
            conversation.entryNodeId = entryNodeId;
            return conversation;
        }

        private static void Save(ConversationAsset conversation)
        {
            EditorUtility.SetDirty(conversation);
            AssetDatabase.SaveAssets();
        }

        /// <summary>已存在则原地复用（保留 GUID，不破坏既有引用），否则新建。</summary>
        private static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
