using UnityEngine;

namespace ChatSystem.View
{
    /// <summary>
    /// 视图层的层级查找工具。
    /// </summary>
    /// <remarks>
    /// 存在的理由是<b>让引用绑定能自我修复</b>：预制体上拖好的引用一旦丢失（换预制体、
    /// 复制节点、合并冲突），运行时表现为静默不显示，排查成本很高。这里保留一条
    /// "按名字重新找回来"的退路，编辑器接线脚本也用同一套名字常量，
    /// 于是节点改名时会同时失效，而不是只有一边失效。
    /// </remarks>
    public static class ViewHierarchy
    {
        // 节点名常量集中在这里，避免字符串散落在运行时与编辑器两边。
        // 名字与 Assets/Prefabs/ 下现有预制体一致，改名前先改这里。
        public const string Avatar = "Avatar";
        public const string Name = "Name";
        public const string Content = "Content";
        public const string TextBubble = "TextBubble";
        public const string Text = "Text";
        public const string Sticker = "Sticker";
        public const string Reddot = "Reddot";
        public const string LastMessage = "LastMessage";
        public const string Label = "Label";

        /// <summary>在 <paramref name="root"/> 的子孙中按名字找节点（不含 root 自身）。</summary>
        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name) return child;

                var found = FindDeep(child, name);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>在子孙中按名字找组件，找不到返回 <c>null</c>。</summary>
        public static T FindDeep<T>(Transform root, string name) where T : Component
        {
            var t = FindDeep(root, name);
            return t != null ? t.GetComponent<T>() : null;
        }
    }
}
