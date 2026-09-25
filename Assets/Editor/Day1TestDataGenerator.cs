using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using UnityEditor;
using UnityEngine;

namespace ChatSystem.EditorTools
{
    /// <summary>
    /// 生成全部测试数据（<c>Plan-4Days.md</c> 任务 1.7）。
    ///
    /// 【为什么用脚本而不是手填 Inspector】
    /// 长中文正文、Emoji 码位、时间戳这类内容手填容易出错，且无法复现。
    /// 生成器可重复执行：已存在的资产在原位覆盖，不会产生重复文件。
    ///
    /// 【为什么每个会话都停在 Choice 节点上】
    /// <c>ChatAppController</c> 在 <c>Start</c> 时会把<b>全部</b>会话都启动（§5.2.7 后台会话照常推进），
    /// 而 <c>Choice</c> 节点会让状态机停下来等玩家输入。把每个会话的首段路径都收敛到一个 Choice 节点上，
    /// 就保证了"打开游戏后不会有任何对话自己跑完"—— 各会话各自停在选项面板前，
    /// 未读红点、预览文案、切换后重新弹面板这些行为才有稳定的观察时机。
    ///
    /// 【六个联系人各自负责覆盖什么】
    /// <code>
    /// robin    知更鸟  3 轮 / 选项 3→2→2   展示会话：长文、Emoji、表情包、两条分割线、分支汇合
    /// march7   三月七  2 轮 / 选项 2→2     间隔 1 分钟 &lt; 阈值 → 不应插线（TimeDividerPolicy 的 false 分支）
    /// danheng  丹恒    1 轮 / 选项 1       单选项布局边界；全是短句
    /// himeko   姬子    2 轮 / 选项 2→3     Image 消息、Wait 停顿节点、段落换行、三选项
    /// welt     瓦尔特  2 轮 / 选项 2→2     超长多段落文本、delay=0（立即发出、不显示正在输入）
    /// asta     艾丝妲  3 轮 / 选项 3→2→2   连珠炮短消息（首屏 15 条 &gt; 窗口 13 行，触发裁剪）、Emoji 密集
    /// </code>
    /// 四种 <see cref="NodeKind"/>（Message / Choice / Wait / End）与四种
    /// <see cref="MessageKind"/>（Text / Sticker / Image / TimeDivider）在数据里都有实例。
    /// 这很重要：Day 3 的校验器和 Day 4 的录屏都需要真实数据把每条分支都走一遍，
    /// 只测到一半的代码路径等于没测。
    ///
    /// 用法：菜单 Tools / ChatSystem / 生成 Day1 测试数据。
    /// </summary>
    public static class Day1TestDataGenerator
    {
        // 不用 Assets/Content 做目录名：场景里已经有个 UI 节点叫 Content，容易混淆
        private const string RootFolder = "Assets/GameData";
        private const string ContactFolder = RootFolder + "/Contacts";
        private const string ConversationFolder = RootFolder + "/Conversations";

        /// <summary>头像目录。目前只有两张图，因此所有 NPC 共用同一张 —— 见 <see cref="AssignAvatar"/>。</summary>
        private const string AvatarFolder = "Assets/ArtRes";

        private const string PlayerAvatarFile = "Kard.png";
        private const string NpcAvatarFile = "流萤.png";

        // 联系人稳定 ID。一经发布不可更改（存档键），见 ContactProfile.id 的注释
        private const string PlayerId = "player";
        private const string RobinId = "robin";
        private const string March7Id = "march7";
        private const string DanhengId = "danheng";
        private const string HimekoId = "himeko";
        private const string WeltId = "welt";
        private const string AstaId = "asta";

        // 压力测试用的联系人。由单独的菜单项生成，删掉不影响上面六个
        private const string StressId = "stress";
        private const int StressMessageCount = 500;
        private const float StressDelay = 0.02f;

        // ------------------------------------------------------------------
        //  Emoji
        // ------------------------------------------------------------------

        // EmojiOne 精灵资产只烘焙了 15 个字形，而且全部是"脸"—— 没有爱心、手势、物件。
        // 这里把可用的码位一次列全：随手用一个没烘焙的码位不会报任何错，
        // 只会在气泡里显示成一个缺字方框，而那种问题只有肉眼盯着看才发现得了。
        // 用 \U 转义而非字面量，避免源码文件编码影响内容。
        private const string EmojiGrin = "\U0001F600";      // 咧嘴笑
        private const string EmojiBeaming = "\U0001F601";   // 眉眼弯弯
        private const string EmojiJoy = "\U0001F602";       // 笑哭
        private const string EmojiSmileOpen = "\U0001F603"; // 张嘴笑
        private const string EmojiSmileEye = "\U0001F604";  // 眯眼笑
        private const string EmojiSweat = "\U0001F605";     // 苦笑冒汗
        private const string EmojiSquint = "\U0001F606";    // 挤眼笑
        private const string EmojiWink = "\U0001F609";      // 眨眼
        private const string EmojiSmile = "\U0001F60A";     // 微笑
        private const string EmojiYum = "\U0001F60B";       // 舔嘴
        private const string EmojiHeartEyes = "\U0001F60D"; // 星星眼
        private const string EmojiCool = "\U0001F60E";      // 墨镜
        private const string EmojiRofl = "\U0001F923";      // 打滚笑
        private const string EmojiRelaxed = "☺";       // 放松
        private const string EmojiFrown = "☹";         // 皱眉

        // ------------------------------------------------------------------
        //  长文本
        // ------------------------------------------------------------------

        /// <summary>约 200 字中文单段，用于验证自动换行与气泡宽度上限。</summary>
        private const string LongSingleParagraph =
            "下周的演出曲目我改了三遍，最后决定还是用最开始那一版。有时候第一直觉就是对的，" +
            "改来改去反而把最打动人的那部分磨掉了。你要是那天有空的话就过来吧，我给你留了第三排靠中间的位置，" +
            "那个角度能看清整个舞台的灯光变化，也能听清返场那首的换气声。对了，散场之后别急着走，" +
            "后台的门禁我让工作人员给你留着，我们可以在休息室坐一会儿，顺便把上次没说完的那件事聊完。" +
            "别再说什么「我只是路过」了，票都给你留了。";

        /// <summary>
        /// 多段落长文本（含 <c>\n\n</c>）。
        /// </summary>
        /// <remarks>
        /// 单段长文和分段长文是两种排版：前者只考验自动换行，后者还考验 TMP 对空行的行高处理。
        /// 手机聊天里没人会发这么长的消息，但策划确实会配，得知道它长什么样。
        /// </remarks>
        private const string MultiParagraph =
            "我把这件事拆成了三部分。\n\n" +
            "第一部分是事实：谁在什么时候做了什么。这部分没有争议，档案里写得很清楚，谁都能查。\n\n" +
            "第二部分是解释：为什么会这样。分歧通常出在这里，而分歧的根源往往不在事实本身，" +
            "在于各自站在什么位置上，能看到什么、看不到什么。\n\n" +
            "第三部分是判断：接下来该怎么做。前两部分没理清就跳到这一步，" +
            "得到的只会是立场，不是结论。";

        /// <summary>无空格的长串，用来试探 TMP 的断行策略（中英混排 + 标点）。</summary>
        private const string DenseMixed =
            "「星穹列车 · Passenger Information Terminal / 乘客信息终端」——这个名字是帕姆坚持要加的，" +
            "说是「这样才有正式感」。我本来想改成短一点的，但想到它为此开心了一整天，就随它去了。";

        // ------------------------------------------------------------------
        //  菜单
        // ------------------------------------------------------------------

        [MenuItem("Tools/ChatSystem/生成 Day1 测试数据")]
        public static void Generate()
        {
            EnsureFolder(ContactFolder);
            EnsureFolder(ConversationFolder);

            // 玩家自己的资料也要由这里生成：它没有对话资产、不出现在联系人列表里，
            // 但它是玩家气泡头像的入口。手工建的资产没法被复现 —— 换台机器 clone 下来就跑不出头像了
            BuildPlayer();

            var all = new List<ConversationAsset>
            {
                BuildRobin(),
                BuildMarch7(),
                BuildDanheng(),
                BuildHimeko(),
                BuildWelt(),
                BuildAsta(),
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Verify(all);

            var robin = AssetDatabase.LoadAssetAtPath<ConversationAsset>(ConversationFolder + "/Conv_Robin.asset");
            EditorGUIUtility.PingObject(robin);
            Selection.activeObject = robin;

            Debug.Log($"[Day1TestDataGenerator] 已生成 {all.Count} 个会话（含玩家资料 Contact_Player）。" +
                      $"路径：{ConversationFolder}。下一步：菜单 工具 / ChatSystem / 接线 Day 2 场景。");
        }

        // ------------------------------------------------------------------
        //  玩家资料
        // ------------------------------------------------------------------

        /// <summary>
        /// 玩家自己的 <see cref="ContactProfile"/>。id 固定为 <c>player</c>。
        /// </summary>
        /// <remarks>
        /// 它<b>不是</b>一个聊天对象：没有 <c>ConversationAsset</c>，因此不会出现在左侧列表里
        /// （列表由 <c>ChatAppController.conversations</c> 驱动）。它唯一的用途是给玩家气泡提供名字与头像，
        /// 由 <c>ChatWindowView.playerProfile</c> 引用，那条引用靠
        /// <c>Tools / ChatSystem / 检查并修复头像接线</c> 写入。
        /// </remarks>
        private static void BuildPlayer()
        {
            var contact = CreateOrLoad<ContactProfile>($"{ContactFolder}/Contact_Player.asset");
            contact.id = PlayerId;
            contact.displayName = "我";
            contact.signature = string.Empty;
            contact.defaultPreview = string.Empty;
            AssignAvatar(contact, PlayerAvatarFile);
            EditorUtility.SetDirty(contact);
        }

        // ------------------------------------------------------------------
        //  知更鸟 —— 展示会话：3 轮，选项 3→2→2
        // ------------------------------------------------------------------

        /// <summary>
        /// 全部渲染特性都在这个会话里过一遍：长文、Emoji、表情包、两张图、两条分割线、
        /// Wait 停顿，以及"两条分支汇合到同一个节点"。
        /// </summary>
        /// <remarks>
        /// 汇合是有意设计的：它证明对话是一张<b>图</b>而不是一棵树。只走下标的实现、
        /// 或者假设"每个节点只有一个前驱"的校验器，都会在这里露馅。
        /// </remarks>
        private static ConversationAsset BuildRobin()
        {
            var contact = CreateContact("Contact_Robin", RobinId, "知更鸟", "正在筹备下一场演出", "你终于回我消息了");
            var conversation = CreateConversation("Conv_Robin", contact, "n1");

            conversation.nodes = new List<DialogueNode>
            {
                // ── 第一轮：自动播完，停在 3 选项 ──

                // ① 短文本 + 时间：会话首条带时间的消息 → 按策略插入首条分割线
                Npc("n1", RobinId, "在吗？", "n2",
                    timeLabel: "昨天 21:30", timeValueUtc: Utc(2026, 9, 16, 13, 30)),

                // ② 约 200 字中文，时间留空：验证换行与宽度上限
                Npc("n2", RobinId, LongSingleParagraph, "n3"),

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

                // ── 第二轮：三条分支，其中两条汇合到同一处 ──

                Npc("a1", RobinId, "那就说定了。第三排，中间。", "a2"),
                Npc("a2", RobinId, $"散场别急着走{EmojiCool}", "a3"),

                // 停顿 2 秒再发图 —— 模拟"翻相册找图"的手感。
                // Wait 不发声，但会让紧随其后的那条消息显示"对方正在输入"
                Wait("a3", 2.0f, "a4"),
                ImageNode("a4", RobinId, "photo_seat_map", "a5"),
                Npc("a5", RobinId, "座位图发你了，别找错门。", "r2a"),

                Npc("b1", RobinId, "忙也要吃饭。我给你留到开场前。", "b2"),
                Npc("b2", RobinId, "……你是不是又忘了我说过的话。", "b3", delay: 1.6f),
                Wait("b3", 1.2f, "b4"),
                Npc("b4", RobinId, "上次也是这样。", "r2a"),

                Npc("c1", RobinId, "好，我等你。", "c2", delay: 2.0f),
                Npc("c2", RobinId, $"不过你要是又放我鸽子{EmojiFrown}", "c3"),
                Sticker("c3", RobinId, "sticker_robin_02", "r2b"),

                // ── 第三轮：两条路径各自的收尾选择 ──

                Npc("r2a", RobinId, "那就提前半小时。", "r2a2"),
                Npc("r2a2", RobinId, "我让工作人员在门口等你，报我名字就行。", "r2a3"),
                Choice("r2a3",
                    Option("好，我提前到", "e1"),
                    Option("不用麻烦，我自己进得去", "e2")),

                Npc("r2b", RobinId, "……", "r2b2", delay: 1.2f),
                Npc("r2b2", RobinId, "算了，随你。", "r2b3"),
                Choice("r2b3",
                    Option("生气了？", "e3"),
                    Option("我尽量早点到", "e4")),

                // ── 结局 ──

                Npc("e1", RobinId, "嗯。到时候见。", "e1b"),
                Sticker("e1b", RobinId, "sticker_robin_03", "end"),

                Npc("e2", RobinId, "……随你。", "end"),

                Npc("e3", RobinId, "没有。", "e3b"),
                Npc("e3b", RobinId, "我生什么气。", "e3c", delay: 1.0f),
                Npc("e3c", RobinId, "……记得带票。", "end"),

                // delay 显式给 0：这条会立刻出现，连"正在输入"都不显示。
                // 用它收尾正好 —— 玩家刚道了歉，对方这句近乎自言自语的短句不该配打字动画
                Npc("e4", RobinId, "嗯，尽量。", "end", delay: 0f),

                End("end"),
            };

            Save(conversation);
            return conversation;
        }

        // ------------------------------------------------------------------
        //  三月七 —— 2 轮，选项 2→2
        // ------------------------------------------------------------------

        /// <summary>
        /// 两条开场白只隔 1 分钟 —— 小于 5 分钟阈值，<b>不应</b>插入第二条第分割线。
        /// 这是对 <c>TimeDividerPolicy</c> 阈值分支的界面级验证（单测已覆盖，这里让它在真实数据上可见）。
        /// </summary>
        private static ConversationAsset BuildMarch7()
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

                // ── 第二轮 ──

                Npc("ma1", March7Id, "好嘞，等我挑一下", "ma2"),
                Sticker("ma2", March7Id, "sticker_march7_01", "ma3"),

                // 汇合点：两条分支都会来到这里
                Npc("ma3", March7Id, $"怎么样，这张是不是很有氛围感{EmojiHeartEyes}", "ma4"),
                Choice("ma4",
                    Option("还行", "me1"),
                    Option("糊了", "me2")),

                Npc("mb1", March7Id, "喂！！生气了", "mb2", delay: 0.4f),
                Npc("mb2", March7Id, "……还是发你吧", "mb3", delay: 1.4f),
                Sticker("mb3", March7Id, "sticker_march7_02", "ma3"),

                // ── 结局 ──

                Npc("me1", March7Id, "什么叫「还行」！", "me1b", delay: 0.3f),
                Npc("me1b", March7Id, "你根本不懂欣赏", "me1c", delay: 0.8f),
                Npc("me1c", March7Id, "……算了，反正我发都发了", "mend", delay: 1.2f),

                Npc("me2", March7Id, "……", "me2b", delay: 1.0f),
                Npc("me2b", March7Id, "我滤镜都调了半个小时", "me2c", delay: 1.0f),
                Npc("me2c", March7Id, "不给你看了", "mend"),

                End("mend"),
            };

            Save(conversation);
            return conversation;
        }

        // ------------------------------------------------------------------
        //  丹恒 —— 1 轮，选项 1
        // ------------------------------------------------------------------

        /// <summary>
        /// 只有一个选项的 Choice 节点。这是最容易出问题的边界：面板按运行时按钮数布局，
        /// 一个按钮时若沿用了"固定高 241"的旧假设，会看到一大片空白。
        /// <para>同时它也是唯一一个全程只有短句的会话 —— 排版上不该出现任何换行。</para>
        /// </summary>
        private static ConversationAsset BuildDanheng()
        {
            var contact = CreateContact("Contact_Danheng", DanhengId, "丹恒", "勿扰", "资料放在桌上了");
            var conversation = CreateConversation("Conv_Danheng", contact, "d1");

            conversation.nodes = new List<DialogueNode>
            {
                Npc("d1", DanhengId, "资料放在桌上了。", "d2",
                    timeLabel: "今天 08:15", timeValueUtc: Utc(2026, 9, 17, 0, 15)),

                Npc("d2", DanhengId, "记得看第三页的批注。", "d3"),

                Choice("d3", Option("……好，我看完再说", "da1")),

                Npc("da1", DanhengId, "嗯。", "da2", delay: 1.6f),

                // delay 0：收尾的一句不需要打字动画
                Npc("da2", DanhengId, "有疑问随时问。", "dend", delay: 0f),

                End("dend"),
            };

            Save(conversation);
            return conversation;
        }

        // ------------------------------------------------------------------
        //  姬子 —— 2 轮，选项 2→3
        // ------------------------------------------------------------------

        /// <summary>
        /// 覆盖两个别处没有的东西：<see cref="MessageKind.Image"/> 消息与 <see cref="NodeKind.Wait"/> 节点。
        /// </summary>
        /// <remarks>
        /// 图片目前没有美术资源，会画成一块纯色 —— 位置和尺寸能看出对错，比整块隐形强（Day 4 补图）。
        /// <para>
        /// Wait 用在"对方停顿了一下"的位置：它本身不发声，但会让紧随其后的消息显示"正在输入"。
        /// 这个组合是别处测不到的 —— <c>DialogueRunner</c> 要向前窥探一条才知道该不该显示三点气泡。
        /// </para>
        /// </remarks>
        private static ConversationAsset BuildHimeko()
        {
            var contact = CreateContact("Contact_Himeko", HimekoId, "姬子", "咖啡还有半壶", "报告我看过了");
            var conversation = CreateConversation("Conv_Himeko", contact, "h1");

            conversation.nodes = new List<DialogueNode>
            {
                Npc("h1", HimekoId, "咖啡煮多了，给你留了一杯。", "h2",
                    timeLabel: "昨天 16:40", timeValueUtc: Utc(2026, 9, 16, 8, 40)),

                ImageNode("h2", HimekoId, "photo_coffee", "h3"),
                Npc("h3", HimekoId, "放你桌上了。凉了记得自己热，别又喝冷的。", "h4"),

                Wait("h4", 2.0f, "h5"),
                Npc("h5", HimekoId, "对了，下周那份星轨观测报告，我看过了。", "h6"),

                Choice("h6",
                    Option("写得很粗糙吧", "ha1"),
                    Option("谢谢姬子姐", "hb1")),

                // ── 第二轮 ──

                Npc("ha1", HimekoId, "倒也不是。", "ha2", delay: 1.0f),
                Npc("ha2", HimekoId, "结论是对的，但论证跳了三大步。", "ha3"),
                ImageNode("ha3", HimekoId, "photo_notes", "ha4"),
                Npc("ha4", HimekoId, "我把问题标在图上了。", "ha5"),

                Npc("hb1", HimekoId, "不用谢。", "hb2"),
                Npc("hb2", HimekoId, "不过报告本身，我们得聊聊。", "ha3"),  // 汇合到批注图

                // 三种选项数量里的 3（另外两个在 robin 与 asta）
                Choice("ha5",
                    Option("我重写一版", "he1"),
                    Option("能不能就按这个交", "he2"),
                    Option("……我回去看看再说", "he3")),

                // ── 结局 ──

                Npc("he1", HimekoId, "重写倒不必。", "he1b"),
                Npc("he1b", HimekoId, "把那三处补上就行，别的不用动。", "hend"),

                Npc("he2", HimekoId, "可以。", "he2b", delay: 1.0f),
                Npc("he2b", HimekoId, "但答辩的时候被问到，你得自己答。想清楚再决定。", "hend"),

                Npc("he3", HimekoId, "嗯，不急。", "he3b"),
                Npc("he3b", HimekoId, "周四之前给我答复。", "hend", delay: 0f),

                End("hend"),
            };

            Save(conversation);
            return conversation;
        }

        // ------------------------------------------------------------------
        //  瓦尔特 —— 2 轮，选项 2→2
        // ------------------------------------------------------------------

        /// <summary>
        /// 排版压力最大的一路：多段落长文本、中英混排的长串，以及<b>整条会话零延迟</b>的节奏。
        /// </summary>
        /// <remarks>
        /// 他说话的节奏就是"一次说完、不急着要回应"—— 开场四条里两条 <c>delay=0</c>，
        /// 于是玩家会看到消息几乎同时落下来，最后一条短句才带一点停顿。
        /// 这是 <c>delay=0</c> 这个开关在真实数据里的用法：不是"没配"，是"刻意不要打字动画"。
        /// </remarks>
        private static ConversationAsset BuildWelt()
        {
            var contact = CreateContact("Contact_Welt", WeltId, "瓦尔特", "在的", "不必急着回");
            var conversation = CreateConversation("Conv_Welt", contact, "w1");

            conversation.nodes = new List<DialogueNode>
            {
                Npc("w1", WeltId, "关于你上次问的那个问题，我想了一晚上。", "w2",
                    timeLabel: "9月15日 23:10", timeValueUtc: Utc(2026, 9, 15, 15, 10)),

                Npc("w2", WeltId, MultiParagraph, "w3", delay: 0f),
                Npc("w3", WeltId, DenseMixed, "w4", delay: 0f),

                // 整段说完之后才停顿 —— 停顿是给玩家留的，不是给他自己的
                Wait("w4", 2.5f, "w5"),
                Npc("w5", WeltId, "不必急着回。想清楚再说。", "w6", delay: 0f),
                Npc("w6", WeltId, "另外，那份名单我核过了，没有问题。", "w7"),

                Choice("w7",
                    Option("我明白了", "wa1"),
                    Option("我还是不太理解", "wb1")),

                // ── 第二轮 ──

                Npc("wa1", WeltId, "明白就好。", "wa2", delay: 1.0f),
                Npc("wa2", WeltId, "有些事不需要现在就理解。", "wb2"),   // 汇合

                Npc("wb1", WeltId, "没关系。", "wb1b"),
                Npc("wb1b", WeltId, "我也不指望一次说清。", "wb2"),

                Choice("wb2",
                    Option("……", "we1"),
                    Option("谢谢。", "we2")),

                // ── 结局 ──

                Npc("we1", WeltId, "去休息吧。", "wend", delay: 1.2f),

                Npc("we2", WeltId, "嗯。", "we2b", delay: 0.8f),
                Npc("we2b", WeltId, "晚安。", "wend", delay: 0f),

                End("wend"),
            };

            Save(conversation);
            return conversation;
        }

        // ------------------------------------------------------------------
        //  艾丝妲 —— 3 轮，选项 3→2→2
        // ------------------------------------------------------------------

        /// <summary>
        /// 连珠炮式短消息。开场 15 条几乎全是几个字的短句，用来覆盖两件事：
        /// <list type="number">
        /// <item><b>消息列表的窗口裁剪</b> —— 15 条消息加 1 条分割线共 16 行，
        /// 超过 <c>ChatWindowView.maxRealizedRows</c>（13），打开会话时 <c>FillFrom</c>
        /// 必须从历史中段开始渲染。别处都凑不满 13 行，这条路径只有在这里才走得到。</item>
        /// <item><b>短消息的气泡宽度</b> —— "在吗"两个字和一个表情包不该占同样的宽度。</item>
        /// </list>
        /// </summary>
        /// <remarks>
        /// 首条带时间的消息出现在第 6 条而不是第 1 条 —— 验证"会话中段的第一条带时间消息"
        /// 同样会插入分割线（策略里 <c>lastTimedValueUtc == 0</c> 那条分支）。
        /// </remarks>
        private static ConversationAsset BuildAsta()
        {
            var contact = CreateContact("Contact_Asta", AstaId, "艾丝妲", "预算表什么时候能好", "在吗在吗");
            var conversation = CreateConversation("Conv_Asta", contact, "s1");

            conversation.nodes = new List<DialogueNode>
            {
                // ── 连珠炮开场：前四条零延迟，消息几乎同时落下来 ──

                Npc("s1", AstaId, "在吗", "s2", delay: 0f),
                Npc("s2", AstaId, "在吗在吗", "s3", delay: 0f),
                Npc("s3", AstaId, "急事！", "s4", delay: 0f),
                Npc("s4", AstaId, "……", "s5", delay: 0.3f),
                Npc("s5", AstaId, "算了，也不是很急", "s6", delay: 0.8f),

                // 首条带时间的消息落在会话中段（第 6 条）—— 同样要插分割线
                Npc("s6", AstaId, "就是那个预算表", "s7",
                    timeLabel: "今天 11:20", timeValueUtc: Utc(2026, 9, 17, 3, 20)),

                Npc("s7", AstaId, $"你填的那个数字是不是少了个零{EmojiSweat}", "s8"),
                Npc("s8", AstaId, "我问了财务，他们说对不上", "s9", delay: 0.4f),
                Npc("s9", AstaId, "然后我又自己算了一遍", "s10", delay: 0.4f),
                Npc("s10", AstaId, "还是对不上", "s11", delay: 0.4f),
                Npc("s11", AstaId, $"我是不是算错了{EmojiSweat}", "s12", delay: 0.6f),
                Npc("s12", AstaId, "不可能啊我数学很好的", "s13", delay: 0.4f),
                Npc("s13", AstaId, "……好吧，我重算", "s14", delay: 1.2f),
                Npc("s14", AstaId, "你别走开啊", "s15", delay: 0.4f),
                Npc("s15", AstaId, "在吗", "s16", delay: 0.4f),

                Choice("s16",
                    Option("没少，就是这么多", "sa1"),
                    Option("我看看", "sb1"),
                    Option($"……{EmojiSweat}", "sc1")),   // 纯 Emoji 选项：玩家消息里只有表情

                // ── 第二轮 ──

                Npc("sa1", AstaId, "？？？", "sa2", delay: 0f),
                Npc("sa2", AstaId, $"你怎么这么淡定{EmojiSweat}", "sa3"),
                Npc("sa3", AstaId, "算了，我自己再核一遍。", "sa4"),

                Npc("sb1", AstaId, $"快点啊，我等你{EmojiSmile}", "sb2"),
                Npc("sb2", AstaId, "……还没好吗", "sb3", delay: 1.8f),
                Npc("sb3", AstaId, $"好吧我承认，我也不太确定{EmojiSweat}", "sa3"),   // 汇合

                Npc("sc1", AstaId, "别光发表情！", "sc2", delay: 0.3f),
                Npc("sc2", AstaId, $"给我个痛快话{EmojiSquint}", "sb2"),               // 汇合

                Choice("sa4",
                    Option("辛苦了", "se1"),
                    Option("核完发我一份", "se2")),

                // ── 第三轮结尾 ──

                Npc("se1", AstaId, "辛苦什么，这是我的活。", "se1b"),
                Npc("se1b", AstaId, $"……谢谢{EmojiSmile}", "send", delay: 1.2f),

                Npc("se2", AstaId, "好。", "se2b", delay: 0.6f),
                Npc("se2b", AstaId, "核完发你。", "se2c"),
                Npc("se2c", AstaId, "你也早点睡。", "send", delay: 0f),

                End("send"),
            };

            Save(conversation);
            return conversation;
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
                : "[Day1TestDataGenerator] 没有找到压力测试数据。");
        }

        // ------------------------------------------------------------------
        //  数据自检
        // ------------------------------------------------------------------

        /// <summary>
        /// 走一遍全部会话，报断链、重复 ID、不可达节点，并打印规模统计。
        /// </summary>
        /// <remarks>
        /// 数据长到六个会话、一百多个节点之后，断链已经不可能靠肉眼查了 ——
        /// 而断链的表现是<b>运行时静默停下</b>（<c>EnterNode</c> 找不到节点就直接 End），
        /// 不报错、不崩溃，只是对话少了一截。所以在生成端就把它拦住。
        /// <para>
        /// 不可达只报警不报错：草稿节点、临时留着的分支都属于正常情况，
        /// 而断链一定是错误。两者的严重性不一样，不该用同一种颜色。
        /// </para>
        /// </remarks>
        private static void Verify(List<ConversationAsset> all)
        {
            var problems = new StringBuilder();
            var notes = new StringBuilder();

            int totalNodes = 0, totalMessages = 0, totalChoices = 0, totalWaits = 0, totalOptions = 0;

            foreach (var conversation in all)
            {
                if (conversation == null) continue;

                var nodes = conversation.nodes ?? new List<DialogueNode>();
                totalNodes += nodes.Count;

                // 自己建 ID → 节点 的字典，不用 ConversationAsset.GetNode。
                // 后者内部有一份 [NonSerialized] 的惰性索引缓存，只在 OnValidate（Inspector 改动）
                // 时失效；而本方法是直接给 nodes 整体赋值的，缓存不会失效 ——
                // 于是第二次生成时校验的是上一次那批节点对象，断链检查会报出早已修好的问题
                var byId = new Dictionary<string, DialogueNode>(nodes.Count, StringComparer.Ordinal);
                foreach (var node in nodes)
                {
                    if (node == null || string.IsNullOrEmpty(node.id)) continue;
                    if (!byId.ContainsKey(node.id))
                    {
                        byId.Add(node.id, node);
                    }
                    else
                    {
                        problems.AppendLine($"  · [{conversation.name}] 重复节点 ID：{node.id}");
                    }
                }

                if (!byId.ContainsKey(conversation.entryNodeId ?? string.Empty))
                {
                    problems.AppendLine($"  · [{conversation.name}] 入口节点 \"{conversation.entryNodeId}\" 不存在");
                }

                // 从入口广度优先走一遍，顺带统计规模。断链在这里就暴露出来 ——
                // 运行时的 EnterNode 找不到节点会直接 End，不报错、不崩溃，只是对话少一截
                var reachable = new HashSet<string>(StringComparer.Ordinal);
                var queue = new Queue<string>();
                if (!string.IsNullOrEmpty(conversation.entryNodeId)) queue.Enqueue(conversation.entryNodeId);

                while (queue.Count > 0)
                {
                    string id = queue.Dequeue();
                    if (string.IsNullOrEmpty(id) || !reachable.Add(id)) continue;

                    if (!byId.TryGetValue(id, out var node))
                    {
                        // 目标不存在。哪个节点指过来的，在下面按来源报，这里不重复
                        continue;
                    }

                    switch (node.kind)
                    {
                        case NodeKind.Message:
                            totalMessages++;

                            if (node.message == null)
                            {
                                problems.AppendLine($"  · [{conversation.name}] Message 节点 \"{id}\" 的 message 为空");
                                break;
                            }

                            if (node.message.kind != MessageKind.Text &&
                                string.IsNullOrEmpty(node.message.assetName))
                            {
                                problems.AppendLine(
                                    $"  · [{conversation.name}] 节点 \"{id}\" 是 {node.message.kind} 消息但没有 assetName");
                            }

                            if (string.IsNullOrEmpty(node.nextId))
                            {
                                problems.AppendLine(
                                    $"  · [{conversation.name}] Message 节点 \"{id}\" 没有 nextId，对话会在这里静默结束");
                                break;
                            }

                            ReportIfMissing(problems, conversation, id, node.nextId, byId);
                            queue.Enqueue(node.nextId);
                            break;

                        case NodeKind.Choice:
                            totalChoices++;
                            var options = node.options ?? new List<ChoiceOption>();

                            if (options.Count == 0)
                            {
                                problems.AppendLine($"  · [{conversation.name}] Choice 节点 \"{id}\" 一个选项都没有");
                            }

                            totalOptions += options.Count;
                            foreach (var option in options)
                            {
                                if (string.IsNullOrEmpty(option.text))
                                {
                                    problems.AppendLine($"  · [{conversation.name}] 节点 \"{id}\" 有选项没有文案");
                                }

                                if (string.IsNullOrEmpty(option.nextId) || !byId.ContainsKey(option.nextId))
                                {
                                    problems.AppendLine(
                                        $"  · [{conversation.name}] 节点 \"{id}\" 的某个选项跳转到不存在的 " +
                                        $"\"{option.nextId}\"，这个选项点了没反应");
                                    continue;
                                }

                                queue.Enqueue(option.nextId);
                            }
                            break;

                        case NodeKind.Wait:
                            totalWaits++;
                            if (string.IsNullOrEmpty(node.nextId)) break;

                            ReportIfMissing(problems, conversation, id, node.nextId, byId);
                            queue.Enqueue(node.nextId);
                            break;

                        case NodeKind.End:
                        default:
                            break;
                    }
                }

                foreach (var node in nodes)
                {
                    if (node == null || string.IsNullOrEmpty(node.id)) continue;
                    if (!reachable.Contains(node.id))
                    {
                        notes.AppendLine($"  · [{conversation.name}] 节点 \"{node.id}\" 从入口走不到（草稿？）");
                    }
                }
            }

            string summary =
                $"[Day1TestDataGenerator] 数据自检：{all.Count} 个会话 / {totalNodes} 个节点 —— " +
                $"消息 {totalMessages}、选项 {totalOptions}、选择点 {totalChoices}、等待 {totalWaits}。";

            if (problems.Length > 0)
            {
                Debug.LogError(summary + "\n发现以下问题：\n" + problems);
            }
            else
            {
                Debug.Log(summary + " 未发现断链。");
            }

            if (notes.Length > 0) Debug.LogWarning("[Day1TestDataGenerator] 未被引用到的节点：\n" + notes);
        }

        private static void ReportIfMissing(StringBuilder problems, ConversationAsset conversation,
                                            string fromId, string targetId, Dictionary<string, DialogueNode> byId)
        {
            if (byId.ContainsKey(targetId)) return;

            problems.AppendLine(
                $"  · [{conversation.name}] 节点 \"{fromId}\" 跳转到不存在的 \"{targetId}\"，对话会在这里静默结束");
        }

        // ------------------------------------------------------------------
        //  节点构造
        // ------------------------------------------------------------------

        /// <summary>构造一个 NPC 文本节点的简写。</summary>
        /// <param name="delay">
        /// 发送延迟。<b>0 表示立即发出且不显示"正在输入"</b>，不是"用默认值"——
        /// 别省这个参数，它的默认值 0.5 是有意义的。
        /// </param>
        private static DialogueNode Npc(string id, string contactId, string text, string nextId,
                                        float delay = 0.5f,
                                        string timeLabel = null, long timeValueUtc = 0L)
        {
            return Message(id, contactId, MessageKind.Text, text, string.Empty, nextId,
                           delay, timeLabel, timeValueUtc);
        }

        /// <summary>构造一个表情包节点的简写。走 <c>Sticker</c> 节点渲染，没有美术资源时显示为色块。</summary>
        private static DialogueNode Sticker(string id, string contactId, string assetName, string nextId,
                                            float delay = 0.8f,
                                            string timeLabel = null, long timeValueUtc = 0L)
        {
            return Message(id, contactId, MessageKind.Sticker, string.Empty, assetName, nextId,
                           delay, timeLabel, timeValueUtc);
        }

        /// <summary>
        /// 构造一个图片节点的简写。
        /// </summary>
        /// <remarks>
        /// 名字带 <c>Node</c> 后缀是为了不与 <c>UnityEngine.UI.Image</c> 撞名 —— 本文件现在没引
        /// <c>UnityEngine.UI</c>，但只要有人为别的事加上那行 using，所有调用点会一起变成歧义错误。
        /// <para>
        /// 目前与表情包走同一条渲染路径（<c>BubbleView.IsMedia</c> 对两者都返回 true），
        /// 因此视觉上没有区别。让它进数据是为了把 <see cref="MessageKind.Image"/> 这条分支
        /// 从工厂到视图整条打通 —— Day 4 补上美术资源时，只有"按宽高比缩放"这一步是新的。
        /// </para>
        /// </remarks>
        private static DialogueNode ImageNode(string id, string contactId, string assetName, string nextId,
                                              float delay = 0.8f,
                                              string timeLabel = null, long timeValueUtc = 0L)
        {
            return Message(id, contactId, MessageKind.Image, string.Empty, assetName, nextId,
                           delay, timeLabel, timeValueUtc);
        }

        private static DialogueNode Message(string id, string contactId, MessageKind kind,
                                            string text, string assetName, string nextId,
                                            float delay, string timeLabel, long timeValueUtc)
        {
            return new DialogueNode
            {
                id = id,
                kind = NodeKind.Message,
                message = new MessageData
                {
                    kind = kind,
                    senderId = contactId,
                    text = text,
                    assetName = assetName,
                },
                delaySeconds = delay,
                nextId = nextId,
                timeLabel = timeLabel,
                timeValueUtc = timeValueUtc,
            };
        }

        /// <summary>
        /// 构造一个等待节点。
        /// </summary>
        /// <remarks>
        /// <b>Wait 与"给消息配延迟"不是一回事</b>：消息上的 <c>delaySeconds</c> 是"打完这条要多久"，
        /// 会跟着字数变长；Wait 是"对方停了一下"，时长就是配置值，不参与字数折算。
        /// 用在"说完一句、想了几秒、又补一句"的位置。
        /// </remarks>
        private static DialogueNode Wait(string id, float seconds, string nextId)
        {
            return new DialogueNode
            {
                id = id,
                kind = NodeKind.Wait,
                delaySeconds = seconds,
                nextId = nextId,
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

        /// <summary>
        /// 建/改一个 NPC 资料。
        /// </summary>
        /// <remarks>
        /// 头像统一指到 <see cref="NpcAvatarFile"/>。目前只有一张 NPC 图，于是六个联系人共用同一张脸 ——
        /// 功能上没问题（正好验证"多个人用同一张图"不会互相干扰），但录屏时看着很假。
        /// 补图时把这里的参数换成各自的文件名即可。
        /// </remarks>
        private static ContactProfile CreateContact(string assetName, string id,
                                                    string displayName, string signature, string defaultPreview)
        {
            var contact = CreateOrLoad<ContactProfile>($"{ContactFolder}/{assetName}.asset");
            contact.id = id;
            contact.displayName = displayName;
            contact.signature = signature;
            contact.defaultPreview = defaultPreview;
            AssignAvatar(contact, NpcAvatarFile);
            EditorUtility.SetDirty(contact);
            return contact;
        }

        /// <summary>
        /// 写头像引用。
        /// </summary>
        /// <remarks>
        /// <b>找不到图时保留原值，绝不写 null。</b>这一条是有代价换来的：早先这里直接写
        /// <c>contact.avatar = null</c>，于是每跑一次生成器，策划在 Inspector 里手工配好的头像
        /// 就被清空一次 —— 而且不报任何错，只表现为"头像突然没了"。
        /// 生成器可以覆写自己写下的东西，但不该毁掉人手填的东西。
        /// </remarks>
        private static void AssignAvatar(ContactProfile contact, string fileName)
        {
            string path = $"{AvatarFolder}/{fileName}";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (sprite == null)
            {
                Debug.LogWarning(
                    $"[Day1TestDataGenerator] 找不到头像 {path}，\"{contact.displayName}\" 保留原有的头像引用。" +
                    "（若原本就没有，这个联系人在界面上不会显示头像。）");
                return;
            }

            contact.avatar = sprite;
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
