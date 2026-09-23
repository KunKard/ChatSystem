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
    /// 生成 Day 1 的测试数据（`Plan-4Days.md` 任务 1.7）。
    ///
    /// 【为什么用脚本而不是手填 Inspector】
    /// 长中文正文、Emoji 码位、时间戳这类内容手填容易出错，且无法复现。
    /// 生成器可重复执行：已存在的资产在原位覆盖，不会产生重复文件。
    ///
    /// 【造出来的内容覆盖了什么】
    /// 5 条消息，其中 <b>2 条配置了时间</b>、其余留空：
    /// <list type="number">
    /// <item>短文本 + 时间 —— 会话首条带时间的消息，按 <c>TimeDividerPolicy</c> 插入分割线</item>
    /// <item>约 200 字中文 —— 验证明文换行与宽度上限</item>
    /// <item>Emoji 与文字混排 —— 验证 <c>m_enableEmojiSupport</c> 走 TMP 精灵资产</item>
    /// <item>表情包 + 时间（与上一条间隔 &gt; 5 分钟）—— 插入第二条分割线</item>
    /// <item>短文本，<b>时间留空</b> —— 验证"未配置则不显示时间、也不触发分割线"</item>
    /// </list>
    ///
    /// 用法：菜单 Tools / ChatSystem / 生成 Day1 测试数据。
    /// </summary>
    public static class Day1TestDataGenerator
    {
        // 不用 Assets/Content 做目录名：场景里已经有个 UI 节点叫 Content，容易混淆
        private const string RootFolder = "Assets/GameData";
        private const string ContactFolder = RootFolder + "/Contacts";
        private const string ConversationFolder = RootFolder + "/Conversations";

        private const string ContactId = "robin";

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

            var contact = CreateOrLoad<ContactProfile>(ContactFolder + "/Contact_Robin.asset");
            contact.id = ContactId;
            contact.displayName = "知更鸟";
            contact.signature = "正在筹备下一场演出";
            contact.defaultPreview = "你终于回我消息了";
            contact.avatar = null; // 美术资源待补，见 Day 4
            EditorUtility.SetDirty(contact);

            var conversation = CreateOrLoad<ConversationAsset>(ConversationFolder + "/Conv_Robin.asset");
            conversation.contact = contact;
            conversation.entryNodeId = "n1";
            conversation.nodes = BuildNodes();
            EditorUtility.SetDirty(conversation);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(conversation);
            Selection.activeObject = conversation;
            Debug.Log($"[Day1TestDataGenerator] 已生成 {conversation.nodes.Count} 个节点，" +
                      $"其中 2 条配置时间、1 条显式留空。路径：{ConversationFolder}");
        }

        private static List<DialogueNode> BuildNodes()
        {
            // 固定时间戳，不用 DateTime.Now —— 测试数据必须可复现
            long yesterday2130 = Utc(2026, 9, 16, 13, 30); // UTC+8 的 2026-09-16 21:30
            long today0920 = Utc(2026, 9, 17, 1, 20);      // UTC+8 的 2026-09-17 09:20，与上条间隔 11h50m

            return new List<DialogueNode>
            {
                // ① 短文本 + 时间：会话首条带时间的消息 → 分割线出现在最前面
                Message("n1", "在吗？", nextId: "n2",
                        timeLabel: "昨天 21:30", timeValueUtc: yesterday2130),

                // ② 约 200 字中文：时间留空
                Message("n2", LongChineseText, nextId: "n3"),

                // ③ Emoji 与文字混排：时间留空
                Message("n3", $"明天见{EmojiSmile} 记得把票带上{EmojiCool}", nextId: "n4"),

                // ④ 表情包 + 时间：与 ① 间隔 11h50m > 5 分钟 → 插入第二条分割线
                new DialogueNode
                {
                    id = "n4",
                    kind = NodeKind.Message,
                    message = new MessageData
                    {
                        kind = MessageKind.Sticker,
                        senderId = ContactId,
                        text = string.Empty,
                        assetName = "sticker_robin_01",
                    },
                    delaySeconds = 0.8f,
                    nextId = "n5",
                    timeLabel = "今天 09:20",
                    timeValueUtc = today0920,
                },

                // ⑤ 短文本，时间显式留空 → 验证"未配置则不显示"
                Message("n5", "先这样，回头聊", nextId: "end"),

                new DialogueNode { id = "end", kind = NodeKind.End },
            };
        }

        /// <summary>构造一个 NPC 文本节点的简写。</summary>
        private static DialogueNode Message(string id, string text, string nextId,
                                            string timeLabel = null, long timeValueUtc = 0L)
        {
            return new DialogueNode
            {
                id = id,
                kind = NodeKind.Message,
                message = new MessageData
                {
                    kind = MessageKind.Text,
                    senderId = ContactId,
                    text = text,
                    assetName = string.Empty,
                },
                delaySeconds = 0.5f,
                nextId = nextId,
                timeLabel = timeLabel,
                timeValueUtc = timeValueUtc,
            };
        }

        private static long Utc(int year, int month, int day, int hour, int minute)
        {
            return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        }

        // ------------------------------------------------------------------
        //  资产读写
        // ------------------------------------------------------------------

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
