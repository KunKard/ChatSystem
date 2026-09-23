using ChatSystem.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem.EditorTools
{
    /// <summary>
    /// 生成 View 层需要、但工程里原本不存在的两个预制体（`Plan-4Days.md` 任务 2.6 / 2.4）。
    ///
    /// 【为什么这两个预制体不存在】
    /// Day 1 只做了数据层与状态机，时间分割线由单元测试验证正确性，从未有过 UI 表现；
    /// 三点输入指示器则是 Day 2 才引入的概念。两者都需要新预制体。
    ///
    /// 【为什么用脚本生成而不是手搭】
    /// 三点的间距、直径、相位容器、以及"固定尺寸不被布局拉伸"的 LayoutElement 组合，
    /// 靠肉眼在 Inspector 里对不出准确值，而且无法复现、无法 diff。
    /// 脚本生成可以重复执行、可以写进 README，与工程已有的
    /// <c>Day1TestDataGenerator</c> / <c>ViewportAnchorFixer</c> 是同一套路子。
    ///
    /// 用法：菜单 Tools / ChatSystem / 生成 View 层预制体。
    /// 已存在的预制体会被<b>覆盖</b>（原地重写，保留 GUID 与既有引用）。
    /// </summary>
    public static class ViewPrefabBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs";
        private const string TypingBubblePath = PrefabFolder + "/TypingBubble.prefab";
        private const string TimeDividerPath = PrefabFolder + "/TimeDivider.prefab";

        // 与 ChatBubble.prefab 保持一致的几何常量，否则 NPC 气泡与三点气泡的左边缘对不齐
        private const float AvatarSize = 80f;
        private const float BubbleSpacing = 20f;

        private const float DotSize = 14f;
        private const float DotSpacing = 10f;
        private const float BubbleWidth = 106f;
        private const float BubbleHeight = 54f;

        [MenuItem("Tools/ChatSystem/生成 View 层预制体")]
        public static void BuildAll()
        {
            BuildTypingBubble();
            BuildTimeDivider();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var typing = AssetDatabase.LoadAssetAtPath<GameObject>(TypingBubblePath);
            EditorGUIUtility.PingObject(typing);
            Selection.activeObject = typing;

            Debug.Log($"[ViewPrefabBuilder] 已生成 {TypingBubblePath} 与 {TimeDividerPath}。");
        }

        /// <summary>
        /// 只生成缺失的预制体，已存在的一律不动。
        /// </summary>
        /// <remarks>
        /// 供 <c>SceneWirer</c> 调用。接线脚本若直接调 <see cref="BuildAll"/>，
        /// 会把用户在 Inspector 里手动调过的尺寸、颜色、间距全部冲掉 ——
        /// 而"生成"与"接线"是两个独立动作，前者不该是后者的副作用。
        /// </remarks>
        public static void BuildMissing()
        {
            bool built = false;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(TypingBubblePath) == null)
            {
                BuildTypingBubble();
                built = true;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(TimeDividerPath) == null)
            {
                BuildTimeDivider();
                built = true;
            }

            if (!built) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ViewPrefabBuilder] 检测到缺失的 View 层预制体，已自动补齐。");
        }

        // ------------------------------------------------------------------
        //  TypingBubble —— 三点输入指示
        // ------------------------------------------------------------------

        /// <summary>
        /// 结构（对齐 ChatBubble.prefab 的命名与几何）：
        /// <code>
        /// TypingBubble  [HorizontalLayoutGroup][ContentSizeFitter][TypingIndicator]
        /// ├─ Avatar  [空占位 + LayoutElement 80×80]      ← 撑出与 NPC 气泡相同的缩进
        /// └─ Bubble  [Image][LayoutElement 固定 106×54]
        ///     └─ Dots [HorizontalLayoutGroup]
        ///         ├─ Dot0 [Image 14×14]
        ///         ├─ Dot1 [Image 14×14]
        ///         └─ Dot2 [Image 14×14]
        /// </code>
        /// </summary>
        private static void BuildTypingBubble()
        {
            var root = new GameObject("TypingBubble", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(300f, 80f);

            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = BubbleSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 头像位只是个占位，不放 Image —— 没有美术资源时 Image 会画出一个色块。
            // 它存在的唯一目的是让三点气泡的左边缘与 NPC 气泡对齐。
            var avatar = CreateNode("Avatar", rootRect, new Vector2(AvatarSize, AvatarSize));
            var avatarElement = avatar.gameObject.AddComponent<LayoutElement>();
            avatarElement.minWidth = AvatarSize;
            avatarElement.minHeight = AvatarSize;
            avatarElement.preferredWidth = AvatarSize;
            avatarElement.preferredHeight = AvatarSize;
            avatarElement.flexibleWidth = 0f;
            avatarElement.flexibleHeight = 0f;

            var bubble = CreateNode("Bubble", rootRect, new Vector2(BubbleWidth, BubbleHeight));
            var bubbleImage = bubble.gameObject.AddComponent<Image>();
            bubbleImage.color = Color.white;
            bubbleImage.raycastTarget = false;

            // 固定尺寸、flexible = 0 → 父级 HorizontalLayoutGroup 取 preferred，
            // 于是气泡不随内容拉伸，正是设计文档 §5.2.5 要的"固定小尺寸"，
            // 因此这里**不挂** ClampPreferredWidth
            var bubbleElement = bubble.gameObject.AddComponent<LayoutElement>();
            bubbleElement.minWidth = BubbleWidth;
            bubbleElement.minHeight = BubbleHeight;
            bubbleElement.preferredWidth = BubbleWidth;
            bubbleElement.preferredHeight = BubbleHeight;
            bubbleElement.flexibleWidth = 0f;
            bubbleElement.flexibleHeight = 0f;

            var dots = CreateNode("Dots", bubble, Vector2.zero);
            Stretch(dots);
            var dotLayout = dots.gameObject.AddComponent<HorizontalLayoutGroup>();
            dotLayout.childAlignment = TextAnchor.MiddleCenter;
            dotLayout.spacing = DotSpacing;
            dotLayout.childControlWidth = true;
            dotLayout.childControlHeight = true;
            dotLayout.childForceExpandWidth = false;
            dotLayout.childForceExpandHeight = false;

            var dotSprite = LoadBuiltinSprite();

            for (int i = 0; i < 3; i++)
            {
                var dot = CreateNode($"Dot{i}", dots, new Vector2(DotSize, DotSize));
                var image = dot.gameObject.AddComponent<Image>();
                image.sprite = dotSprite;
                image.color = Color.white;
                // 三个点每秒改上百次颜色，没必要再被 GraphicRaycaster 扫一遍
                image.raycastTarget = false;

                var element = dot.gameObject.AddComponent<LayoutElement>();
                element.minWidth = DotSize;
                element.minHeight = DotSize;
                element.preferredWidth = DotSize;
                element.preferredHeight = DotSize;
                element.flexibleWidth = 0f;
                element.flexibleHeight = 0f;
            }

            var indicator = root.AddComponent<TypingIndicator>();
            indicator.ResolveReferences();   // 把三个点烘进序列化字段，运行时不必再找

            SaveAsPrefab(root, TypingBubblePath);
        }

        // ------------------------------------------------------------------
        //  TimeDivider —— 时间分割线
        // ------------------------------------------------------------------

        /// <summary>
        /// 结构：
        /// <code>
        /// TimeDivider [LayoutElement 高 56]
        /// └─ Label [TMP 居中]
        /// </code>
        /// 分割线不是气泡：没有头像、没有朝向、横跨整行，因此不能复用任何一个气泡预制体。
        /// </summary>
        private static void BuildTimeDivider()
        {
            var root = new GameObject("TimeDivider", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(600f, 56f);

            // 父级 Content 的纵向布局组勾了 Child Control Height，没有 LayoutElement 的
            // 裸 RectTransform 会被问出 0 高 —— 所有分割线就会叠在一起
            var element = root.AddComponent<LayoutElement>();
            element.minHeight = 56f;
            element.preferredHeight = 56f;
            element.flexibleWidth = 1f;

            var label = CreateNode("Label", rootRect, Vector2.zero);
            Stretch(label);

            var text = label.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = "昨天 21:30";
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 28f;
            text.color = new Color(0.42f, 0.42f, 0.45f, 1f);
            text.raycastTarget = false;

            var view = root.AddComponent<TimeDividerView>();
            view.ResolveReferences();

            SaveAsPrefab(root, TimeDividerPath);
        }

        // ------------------------------------------------------------------
        //  工具
        // ------------------------------------------------------------------

        /// <summary>
        /// 取 Unity 内置的圆点贴图。
        /// </summary>
        /// <remarks>
        /// 工程里没有任何圆形美术资源，用 <c>Image</c> 加空贴图会画出方块。
        /// <c>UI/Skin/Knob</c> 是 UGUI 自带的圆形贴图，随预制体一起保存，
        /// 不需要额外的美术资源就能得到正确的圆点。
        /// 万一取不到，三个点会退化成方块 —— 形状不对，但动画逻辑仍然可验证。
        /// </remarks>
        private static Sprite LoadBuiltinSprite()
        {
            var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            if (sprite == null)
            {
                Debug.LogWarning("[ViewPrefabBuilder] 取不到内置圆形贴图 UI/Skin/Knob，三点将显示为方块。");
            }

            return sprite;
        }

        private static RectTransform CreateNode(string name, RectTransform parent, Vector2 size)
        {
            var node = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        private static void SaveAsPrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }
    }
}
