using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem.EditorTools
{
    /// <summary>
    /// 一次性工具：把 ScrollView 的 Viewport 锚点修正为「撑满 Scroll View，右边留出滚动条宽度」。
    ///
    /// 【为什么需要】
    /// 场景里的两个 Viewport 是 `aMin=(0,0) / aMax=(0,0) / sizeDelta=(0,0)` 的点锚点 + 零尺寸，
    /// 而它们身上挂着启用中的 Mask —— Mask 按 RectTransform 的矩形裁剪，0×0 就等于把消息列表整个裁没。
    ///
    /// 【为什么不用 Inspector 的 Anchor Presets】
    /// 预设弹窗要「按住鼠标拖到格子上再松手」，容易点不中；Debug 模式入口也藏在 Inspector 标签栏的 ⋮ 里。
    /// 这里直接写值，可撤销（Ctrl+Z）。
    ///
    /// 【用法】
    /// 在 Hierarchy 里选中一个或多个 Viewport（或直接选中 Scroll View），
    /// 菜单 Tools / ChatSystem / 修复 ScrollView 的 Viewport 锚点。
    ///
    /// 用完可以删掉这个文件，不影响运行时代码。
    /// </summary>
    public static class ViewportAnchorFixer
    {
        /// <summary>给竖向滚动条让出的宽度，单位像素。</summary>
        private const float ScrollbarGutter = 20f;

        [MenuItem("Tools/ChatSystem/修复 ScrollView 的 Viewport 锚点")]
        private static void FixSelected()
        {
            var targets = CollectTargets();
            if (targets.Length == 0)
            {
                Debug.LogWarning("[ViewportAnchorFixer] 请先在 Hierarchy 里选中 Viewport，或选中带 ScrollRect 的节点。");
                return;
            }

            foreach (RectTransform rect in targets)
            {
                Undo.RecordObject(rect, "Fix Viewport Anchors");

                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                // Left=0 / Right=ScrollbarGutter / Top=0 / Bottom=0
                // 推导：Left = anchoredPosition.x - sizeDelta.x * pivot.x
                //       Right = -(anchoredPosition.x + sizeDelta.x * (1 - pivot.x))
                rect.sizeDelta = new Vector2(-ScrollbarGutter, 0f);
                rect.anchoredPosition = new Vector2(-ScrollbarGutter * 0.5f, 0f);

                EditorUtility.SetDirty(rect);
                Debug.Log($"[ViewportAnchorFixer] 已修正 {GetPath(rect)}", rect);
            }

            Debug.Log($"[ViewportAnchorFixer] 完成，共 {targets.Length} 个。记得 Ctrl+S 保存场景。");
        }

        [MenuItem("Tools/ChatSystem/修复 ScrollView 的 Viewport 锚点", true)]
        private static bool FixSelectedValidate() => CollectTargets().Length > 0;

        /// <summary>
        /// 选中的是 Viewport 就直接用；选中的是 ScrollRect 就取其 viewport。
        /// </summary>
        private static RectTransform[] CollectTargets()
        {
            var result = new System.Collections.Generic.List<RectTransform>();

            foreach (GameObject go in Selection.gameObjects)
            {
                var scroll = go.GetComponent<ScrollRect>();
                if (scroll != null)
                {
                    if (scroll.viewport != null) result.Add(scroll.viewport);
                    continue;
                }

                if (go.TryGetComponent(out RectTransform rect)) result.Add(rect);
            }

            return result.ToArray();
        }

        private static string GetPath(RectTransform rect)
        {
            string path = rect.name;
            Transform t = rect.parent;
            while (t != null)
            {
                path = t.name + "/" + path;
                t = t.parent;
            }
            return path;
        }
    }
}
