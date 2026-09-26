using System;
using System.Collections.Generic;
using System.Globalization;
using ChatSystem.Data;
using ChatSystem.Data.Model;

namespace ChatSystem.EditorTools
{
    /// <summary>
    /// 节点 ID 的生成，以及改动 ID 时对全部引用的同步改写。
    /// </summary>
    /// <remarks>
    /// <b>本文件不引用 UnityEditor</b>（理由见 <see cref="DialogueValidator"/> 的类注释），
    /// 因此这里只直接改字段值，不碰 <c>SerializedObject</c>、不记 Undo。
    /// 撤销与标脏由调用方（<c>DialogueEditor</c>）在调用前后包一层 <c>Undo.RecordObject</c> 负责 ——
    /// 一次调用 = 一次原子改动，调用方包一次 Undo 即可，中间不会出现"ID 改了但引用没跟上"的状态。
    /// </remarks>
    public static class NodeIdUtility
    {
        /// <summary>自动生成 ID 的前缀。</summary>
        /// <remarks>
        /// 与现有数据（<c>s1</c>…<c>s16</c>）同一种形状：短，所以 122 项的下拉框还扫得动；
        /// 不需要动脑子就能接受，所以策划不会为了"取个好名字"而拖到忘记填。
        /// 语义化命名看着更美，但每加一个节点都要现场想一个不重复的名字，而那正是重复 ID 的来源。
        /// </remarks>
        public const string IdPrefix = "s";

        /// <summary>生成一个当前资产里还没被占用的 ID：最小的空闲 <c>s&lt;N&gt;</c>。</summary>
        public static string GenerateId(ConversationAsset asset)
        {
            return GenerateId(IdsOf(asset));
        }

        /// <summary>
        /// 从一组已占用的 ID 里生成最小的空闲 <c>s&lt;N&gt;</c>。
        /// </summary>
        /// <remarks>
        /// 编辑器里走这条重载：新增节点是通过 <c>SerializedProperty</c> 改数组的，
        /// 此刻 <c>asset.nodes</c> 还是旧快照，拿它算出来的 ID 会撞车。
        /// </remarks>
        public static string GenerateId(IEnumerable<string> takenIds)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            if (takenIds != null)
            {
                foreach (var id in takenIds)
                {
                    if (!string.IsNullOrEmpty(id)) taken.Add(id);
                }
            }

            // 用不变文化格式化：少数区域设置会用本民族的数字字形，那会生成出
            // 看起来像 "s١" 的 ID —— 能在编辑器里用，但没法用键盘打出来
            for (int n = 1; n < int.MaxValue; n++)
            {
                string candidate = IdPrefix + n.ToString(CultureInfo.InvariantCulture);
                if (!taken.Contains(candidate)) return candidate;
            }

            return null;   // int 用完才会到这儿，不可达
        }

        /// <summary>枚举资产里全部已占用的 ID。</summary>
        public static IEnumerable<string> IdsOf(ConversationAsset asset)
        {
            var nodes = asset != null ? asset.nodes : null;
            if (nodes == null) yield break;

            for (int i = 0; i < nodes.Count; i++)
            {
                var id = nodes[i] != null ? nodes[i].id : null;
                if (!string.IsNullOrEmpty(id)) yield return id;
            }
        }

        /// <summary>
        /// 把 <paramref name="oldId"/> 的全部引用改写成 <paramref name="newId"/>。
        /// </summary>
        /// <returns>被改写的引用条数。</returns>
        /// <remarks>
        /// 覆盖三处：<c>entryNodeId</c>、每个节点的 <c>nextId</c>、每个选项的 <c>nextId</c>。
        /// <b>扫的是原始数据</b>，跳过重复 ID 的落选节点与不可达节点 —— 那些引用同样写在资产文件里，
        /// 漏改会让它们在下次被启用时变成断链。
        /// <para>
        /// 必须和"改 ID"在同一次 Undo 里做完。分成两步就会存在一个中间态：
        /// ID 已经变了，而引用还指着旧 ID，此时若编辑器崩了或用户保存，
        /// 得到的就是一份满是断链、且断得毫无规律的资产。
        /// </para>
        /// </remarks>
        public static int RewriteReferences(ConversationAsset asset, string oldId, string newId)
        {
            if (asset == null || string.IsNullOrEmpty(oldId)) return 0;
            if (string.Equals(oldId, newId, StringComparison.Ordinal)) return 0;

            int changed = 0;

            if (string.Equals(asset.entryNodeId, oldId, StringComparison.Ordinal))
            {
                asset.entryNodeId = newId;
                changed++;
            }

            var nodes = asset.nodes;
            if (nodes == null) return changed;

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null) continue;

                if (string.Equals(node.nextId, oldId, StringComparison.Ordinal))
                {
                    node.nextId = newId;
                    changed++;
                }

                if (node.options == null) continue;

                for (int o = 0; o < node.options.Count; o++)
                {
                    var option = node.options[o];
                    if (option == null) continue;

                    if (string.Equals(option.nextId, oldId, StringComparison.Ordinal))
                    {
                        option.nextId = newId;
                        changed++;
                    }
                }
            }

            return changed;
        }

        /// <summary>
        /// 把指向 <paramref name="targetId"/> 的引用全部置空（即"这条路径改为结束对话"）。
        /// </summary>
        /// <returns>被清空的引用条数。</returns>
        /// <remarks>删除节点时的三个选项之一。与 <see cref="RewriteReferences"/> 同样是全量扫描。</remarks>
        public static int ClearReferencesTo(ConversationAsset asset, string targetId)
        {
            return string.IsNullOrEmpty(targetId) ? 0 : RewriteReferences(asset, targetId, null);
        }

        /// <summary>
        /// 删除一个节点。
        /// </summary>
        /// <returns>是否真的删掉了。</returns>
        /// <remarks>
        /// 自带边界检查 —— 调用方多半是 ReorderableList 的回调，下标来自可能已经变化的列表。
        /// </remarks>
        public static bool RemoveNodeAt(ConversationAsset asset, int index)
        {
            var nodes = asset != null ? asset.nodes : null;
            if (nodes == null || index < 0 || index >= nodes.Count) return false;

            nodes.RemoveAt(index);
            return true;
        }
    }
}
