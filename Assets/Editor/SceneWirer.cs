using System.Collections.Generic;
using ChatSystem.Data;
using ChatSystem.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem.EditorTools
{
    /// <summary>
    /// Day 2 场景接线（`Plan-4Days.md` 任务 2.1 ~ 2.8 的装配部分）。
    ///
    /// 【为什么用脚本接线而不是手工拖引用】
    /// 本次要挂 4 个脚本、填 16 个引用、改 3 个预制体、清 4 个样本实例、修 2 个 Viewport。
    /// 手工做一遍要几十次拖拽，每一次都可能拖错或漏拖，而漏拖的表现是"运行时静默不显示"，
    /// 排查成本远高于写这个脚本。脚本还顺带把这段配置变成了可复现、可 diff 的代码。
    ///
    /// 【幂等】
    /// 可重复执行：已挂的脚本不会重复挂，已填的引用会被覆盖为当前值。
    ///
    /// 用法：先跑 `生成 Day1 测试数据`，再跑本菜单。
    /// </summary>
    public static class SceneWirer
    {
        private const string PrefabFolder = "Assets/Prefabs";
        private const string ConversationFolder = "Assets/GameData/Conversations";

        private const string NpcBubblePath = PrefabFolder + "/ChatBubble.prefab";
        private const string PlayerBubblePath = PrefabFolder + "/MyChatBubble.prefab";
        private const string ContactItemPath = PrefabFolder + "/ChatPartner.prefab";
        private const string TypingBubblePath = PrefabFolder + "/TypingBubble.prefab";
        private const string TimeDividerPath = PrefabFolder + "/TimeDivider.prefab";

        /// <summary>右侧滚动条占的宽度。Viewport 右边缘要让出这么多，否则内容会被滚动条压住。</summary>
        private const float ScrollbarGutter = 20f;

        /// <summary>表情包占位尺寸。没有美术资源时也必须有值，否则气泡会塌成 0 高。</summary>
        private const float StickerSize = 200f;

        [MenuItem("Tools/ChatSystem/接线 Day 2 场景")]
        public static void Wire()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var conversations = LoadConversations();
            if (conversations.Count == 0)
            {
                EditorUtility.DisplayDialog("接线 Day 2 场景",
                    $"在 {ConversationFolder} 下找不到任何 ConversationAsset。\n\n" +
                    "请先运行菜单 工具 / ChatSystem / 生成 Day1 测试数据，再执行本操作。",
                    "知道了");
                return;
            }

            string warning = scene.isDirty
                ? "\n\n⚠️ 当前场景有未保存的改动，接线完成后会被一并保存。"
                : string.Empty;

            if (!EditorUtility.DisplayDialog("接线 Day 2 场景",
                "将要修改 SampleScene 与 3 个预制体：\n\n" +
                "· 修复两个 ScrollView 的 Viewport 锚点\n" +
                "· 清空两个 Content 下的静态样本实例\n" +
                "· ChooseBackGround 只保留一个 Button 作为模板\n" +
                "· 挂载 4 个脚本并填好全部引用\n\n" +
                $"找到 {conversations.Count} 个对话资产。" + warning +
                "\n\n所有改动都可用 Ctrl+Z 撤销。是否继续？",
                "继续", "取消"))
            {
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Wire Day 2 Scene");

            try
            {
                // 三点气泡与时间分割线的预制体是 Day 2 才引入的。缺失时先补齐，
                // 否则接线只能绑到 null，运行时表现为"三点气泡不出现"这类静默故障
                ViewPrefabBuilder.BuildMissing();

                BuildPrefabs();
                WireScene(conversations);
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SceneWirer] 接线完成，已绑定 {conversations.Count} 个对话资产。直接 Play 即可。" +
                      "若消息列表不显示，先检查 Assets/GameData 是否已生成。");
        }

        // ------------------------------------------------------------------
        //  预制体改造
        // ------------------------------------------------------------------

        private static void BuildPrefabs()
        {
            BuildBubblePrefab(NpcBubblePath, isPlayerSide: false);
            BuildBubblePrefab(PlayerBubblePath, isPlayerSide: true);
            BuildContactItemPrefab(ContactItemPath);
        }

        private static void BuildBubblePrefab(string path, bool isPlayerSide)
        {
            if (!EditPrefab(path, root =>
            {
                var view = EnsureComponent<BubbleView>(root);
                view.ResolveReferences();

                var setter = new RefSetter(view);
                setter.Bool("isPlayerSide", isPlayerSide).Apply();

                // 归零 TMP 的边距。ChatBubble 的正文 TMP 上残留着 z = -55.45 的负右边距，
                // 那会让排版宽度比矩形宽 55px、文字溢出气泡。当初只有一条固定文案所以看不出来，
                // 接上长短不一的数据就会暴露
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.margin = Vector4.zero;
                }

                // Sticker 是 Content 纵向布局组的子节点，而该组勾了 Child Control Height。
                // 没有 LayoutElement 时它的首选高度由 Image 的贴图算出 —— 贴图为空即 0，
                // 表情包气泡会塌成 0 高、和相邻气泡重叠
                var sticker = ViewHierarchy.FindDeep(root.transform, ViewHierarchy.Sticker);
                if (sticker != null)
                {
                    var element = EnsureComponent<LayoutElement>(sticker.gameObject);
                    element.minWidth = StickerSize;
                    element.minHeight = StickerSize;
                    element.preferredWidth = StickerSize;
                    element.preferredHeight = StickerSize;
                    element.flexibleWidth = 0f;
                    element.flexibleHeight = 0f;
                }
            }))
            {
                Debug.LogError($"[SceneWirer] 预制体不存在：{path}");
            }
        }

        private static void BuildContactItemPrefab(string path)
        {
            if (!EditPrefab(path, root =>
            {
                // Button 必须早于 ContactItemView.ResolveReferences —— 后者要靠它取引用。
                // transition 设为 None：选中态的背景色由 ContactItemView 自己控制，
                // 用 ColorTint 的话 Button 会在每次交互后覆写掉那个颜色
                var button = EnsureComponent<Button>(root);
                button.transition = Selectable.Transition.None;
                button.targetGraphic = root.GetComponent<Image>();

                var item = EnsureComponent<ContactItemView>(root);
                item.ResolveReferences();

                // 红点默认隐藏：未读数由运行时决定，静态摆着会让冷启动就出现一个假红点
                var reddot = ViewHierarchy.FindDeep(root.transform, ViewHierarchy.Reddot);
                if (reddot != null) reddot.gameObject.SetActive(false);
            }))
            {
                Debug.LogError($"[SceneWirer] 预制体不存在：{path}");
            }
        }

        /// <summary>用 <c>LoadPrefabContents</c> 打开预制体、执行改动、再存回去。</summary>
        /// <returns>预制体存在并已保存返回 <c>true</c>。</returns>
        private static bool EditPrefab(string path, System.Action<GameObject> edit)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return false;

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return true;
        }

        // ------------------------------------------------------------------
        //  场景接线
        // ------------------------------------------------------------------

        private static void WireScene(List<ConversationAsset> conversations)
        {
            var canvas = FindInScene("Canvas");
            if (canvas == null)
            {
                Debug.LogError("[SceneWirer] 场景里找不到根节点 Canvas，接线中止。");
                return;
            }

            var rightPanel = canvas.transform.Find("RightBackGround");
            var leftPanel = canvas.transform.Find("LeftBackGround");
            var choosePanel = canvas.transform.Find("ChooseBackGround");

            if (rightPanel == null || leftPanel == null || choosePanel == null)
            {
                Debug.LogError("[SceneWirer] 场景层级与预期不符，找不到 RightBackGround / LeftBackGround / " +
                               "ChooseBackGround。接线中止，场景未被修改。");
                return;
            }

            var rightScroll = rightPanel.Find("Scroll View");
            var leftScroll = leftPanel.Find("Scroll View");

            if (rightScroll == null || leftScroll == null)
            {
                Debug.LogError("[SceneWirer] 两个面板下都应有名为 \"Scroll View\" 的子节点，接线中止。");
                return;
            }

            FixViewport(rightScroll);
            FixViewport(leftScroll);

            ClearChildren(rightScroll.Find("Viewport/Content"));
            ClearChildren(leftScroll.Find("Viewport/Content"));

            TrimExtraButtons(choosePanel);

            var window = EnsureSceneComponent<ChatWindowView>(rightScroll.gameObject);
            var list = EnsureSceneComponent<ContactListView>(leftScroll.gameObject);
            var options = EnsureSceneComponent<ReplyOptionsView>(choosePanel.gameObject);
            var app = EnsureSceneComponent<ChatAppController>(canvas.gameObject);

            var npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcBubblePath);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerBubblePath);
            var contactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContactItemPath);
            var typingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TypingBubblePath);
            var dividerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TimeDividerPath);

            WarnIfMissing(npcPrefab, NpcBubblePath);
            WarnIfMissing(playerPrefab, PlayerBubblePath);
            WarnIfMissing(contactPrefab, ContactItemPath);
            WarnIfMissing(typingPrefab, TypingBubblePath);
            WarnIfMissing(dividerPrefab, TimeDividerPath);

            new RefSetter(window)
                .Ref("scrollRect", rightScroll.GetComponent<ScrollRect>())
                .Ref("content", rightScroll.Find("Viewport/Content") as RectTransform)
                .Ref("headerName", rightPanel.Find("Name")?.GetComponent<TMP_Text>())
                .Ref("headerSignature", rightPanel.Find("Signal")?.GetComponent<TMP_Text>())
                .Ref("npcBubblePrefab", npcPrefab != null ? npcPrefab.GetComponent<BubbleView>() : null)
                .Ref("playerBubblePrefab", playerPrefab != null ? playerPrefab.GetComponent<BubbleView>() : null)
                .Ref("dividerPrefab", dividerPrefab != null ? dividerPrefab.GetComponent<TimeDividerView>() : null)
                .Ref("typingIndicatorPrefab", typingPrefab != null ? typingPrefab.GetComponent<TypingIndicator>() : null)
                .RefIfNotNull("playerProfile", AvatarWirer.FindPlayerProfile())
                .Apply();

            new RefSetter(list)
                .Ref("scrollRect", leftScroll.GetComponent<ScrollRect>())
                .Ref("content", leftScroll.Find("Viewport/Content") as RectTransform)
                .Ref("itemPrefab", contactPrefab != null ? contactPrefab.GetComponent<ContactItemView>() : null)
                .Apply();

            new RefSetter(app)
                .AssetList("conversations", conversations)
                .Str("initialContactId", PickInitialContactId(conversations))
                .Ref("contactList", list)
                .Ref("chatWindow", window)
                .Ref("replyOptions", options)
                .Apply();

            EditorUtility.SetDirty(window);
            EditorUtility.SetDirty(list);
            EditorUtility.SetDirty(options);
            EditorUtility.SetDirty(app);
        }

        /// <summary>
        /// 修复 ScrollView 的 Viewport 锚点。
        /// </summary>
        /// <remarks>
        /// 场景磁盘状态里两个 Viewport 都是 <c>anchorMin/Max (0,0)</c> + <c>sizeDelta (0,0)</c>，
        /// 也就是一个 0×0 的矩形。两个后果：
        /// <list type="number">
        /// <item><b>消息列表宽度为 0</b> —— Content 的锚点是横向撑满（<c>(0,1)-(1,1)</c>），
        /// 宽度等于父级宽度，父级为 0 则 Content 为 0；而列表的纵向布局组勾了
        /// Child Force Expand Width，会把每个气泡拉伸成 0 宽，什么都看不见。</item>
        /// <item><b>自动滚底失效</b> —— <c>ScrollRect.verticalNormalizedPosition</c> 是用
        /// viewport 的 rect 算出来的。viewport 高度为 0 时这个归一化坐标不反映
        /// "用户是否往下滚过"，"玩家在底部吗"的判断会变成噪声。</item>
        /// </list>
        /// </remarks>
        private static void FixViewport(Transform scrollView)
        {
            var viewport = scrollView.Find("Viewport") as RectTransform;
            if (viewport == null)
            {
                Debug.LogWarning($"[SceneWirer] \"{scrollView.name}\" 下找不到 Viewport，跳过锚点修复。");
                return;
            }

            Undo.RecordObject(viewport, "Fix Viewport Anchors");

            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.sizeDelta = new Vector2(-ScrollbarGutter, 0f);
            viewport.anchoredPosition = new Vector2(-ScrollbarGutter * 0.5f, 0f);

            EditorUtility.SetDirty(viewport);
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;

            // 倒序删除：正序删除时后续子节点的下标会变
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
            }
        }

        /// <summary>只保留第一个 Button 作为模板，其余删除 —— 数量改由运行时按选项数决定。</summary>
        private static void TrimExtraButtons(Transform panel)
        {
            var buttons = new List<Button>();
            for (int i = 0; i < panel.childCount; i++)
            {
                var button = panel.GetChild(i).GetComponent<Button>();
                if (button != null) buttons.Add(button);
            }

            for (int i = buttons.Count - 1; i >= 1; i--)
            {
                Undo.DestroyObjectImmediate(buttons[i].gameObject);
            }
        }

        // ------------------------------------------------------------------
        //  工具
        // ------------------------------------------------------------------

        private static List<ConversationAsset> LoadConversations()
        {
            var result = new List<ConversationAsset>();
            if (!AssetDatabase.IsValidFolder(ConversationFolder)) return result;

            foreach (var guid in AssetDatabase.FindAssets("t:ConversationAsset", new[] { ConversationFolder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ConversationAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) result.Add(asset);
            }

            // 按资产名排序，保证重复执行时列表顺序稳定 —— 顺序即联系人列表的展示顺序
            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        /// <summary>
        /// 选定启动时要打开的会话。
        /// </summary>
        /// <remarks>
        /// 优先 <c>robin</c>：它的对话最长，同时含长文本、Emoji、表情包与两条时间分割线，
        /// 作为首屏能把渲染路径一次性铺满。按字母序取第一个的话会选中丹恒 ——
        /// 两条消息一个选项，看不出什么。找不到时退回第一个。
        /// </remarks>
        private static string PickInitialContactId(List<ConversationAsset> conversations)
        {
            const string Preferred = "robin";

            for (int i = 0; i < conversations.Count; i++)
            {
                if (conversations[i].contact != null && conversations[i].contact.id == Preferred) return Preferred;
            }

            return conversations[0].contact != null ? conversations[0].contact.id : string.Empty;
        }

        /// <summary>
        /// 在<b>包含未激活对象</b>的层级中按名字查找根节点。
        /// </summary>
        /// <remarks>
        /// 不能用 <c>GameObject.Find</c>：它只搜索激活对象，而 <c>ChooseBackGround</c>
        /// 在场景里是隐藏的，会直接找不到。
        /// </remarks>
        private static GameObject FindInScene(string rootName)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == rootName) return root;
            }

            return null;
        }

        /// <summary>在预制体预览场景里挂组件。</summary>
        /// <remarks>
        /// 那里没有撤销栈，<c>Undo.AddComponent</c> 不适用 —— 预制体的改动靠
        /// <c>SaveAsPrefabAsset</c> 落盘，反悔的方式是不保存，而不是 Ctrl+Z。
        /// </remarks>
        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        /// <summary>在场景里挂组件。这个走 Undo，用户可以用 Ctrl+Z 整体撤销。</summary>
        private static T EnsureSceneComponent<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(go);
        }

        private static void WarnIfMissing(Object asset, string path)
        {
            if (asset == null)
            {
                Debug.LogWarning($"[SceneWirer] 找不到 {path}，对应的引用会留空。" +
                                 "请先运行 工具 / ChatSystem / 生成 View 层预制体。");
            }
        }

        /// <summary>
        /// 批量写 <c>[SerializeField]</c> 私有字段。
        /// </summary>
        /// <remarks>
        /// 必须走 <see cref="SerializedObject"/>：这些字段是私有的，
        /// 从外部直接赋值编译不过；而 SerializedObject 写入会正确标记对象为脏、
        /// 从而被场景保存。每次 Apply 一次而不是逐个 Apply，
        /// 是为了避免同一个对象上反复重建序列化快照而丢掉前一次的修改。
        /// </remarks>
        private sealed class RefSetter
        {
            private readonly SerializedObject _serialized;
            private readonly Object _target;

            public RefSetter(Object target)
            {
                _target = target;
                _serialized = new SerializedObject(target);
            }

            public RefSetter Ref(string field, Object value)
            {
                var property = Find(field);
                if (property != null) property.objectReferenceValue = value;
                return this;
            }

            /// <summary>
            /// 只在值非空时写入。
            /// </summary>
            /// <remarks>
            /// 用于"找不到就保持原样"的引用：<see cref="Ref"/> 传 <c>null</c> 会<b>清掉</b>已有的接线，
            /// 于是资产一时找不到就会静默毁掉一条本来好好的引用。
            /// </remarks>
            public RefSetter RefIfNotNull(string field, Object value)
            {
                return value == null ? this : Ref(field, value);
            }

            public RefSetter Bool(string field, bool value)
            {
                var property = Find(field);
                if (property != null) property.boolValue = value;
                return this;
            }

            public RefSetter Str(string field, string value)
            {
                var property = Find(field);
                if (property != null) property.stringValue = value ?? string.Empty;
                return this;
            }

            public RefSetter AssetList(string field, List<ConversationAsset> assets)
            {
                var property = Find(field);
                if (property == null) return this;

                property.arraySize = assets.Count;
                for (int i = 0; i < assets.Count; i++)
                {
                    property.GetArrayElementAtIndex(i).objectReferenceValue = assets[i];
                }

                return this;
            }

            public void Apply()
            {
                _serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            private SerializedProperty Find(string field)
            {
                var property = _serialized.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError($"[SceneWirer] {_target.GetType().Name} 上没有名为 \"{field}\" 的序列化字段。" +
                                   "字段被重命名过？请同步更新本脚本。");
                }

                return property;
            }
        }
    }
}
