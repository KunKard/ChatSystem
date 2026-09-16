using System;
using System.Collections.Generic;
using ChatSystem.Data.Model;
using UnityEngine;

namespace ChatSystem.Data
{
    /// <summary>
    /// 一段对话的全部节点。策划在 Inspector 中配置，运行时只读。
    /// </summary>
    [CreateAssetMenu(menuName = "ChatSystem/Conversation", fileName = "Conv_")]
    public class ConversationAsset : ScriptableObject
    {
        /// <summary>本对话所属的联系人。</summary>
        [Tooltip("本对话所属的联系人。")]
        public ContactProfile contact;

        /// <summary>入口节点 ID。运行时从该节点开始播放。</summary>
        [Tooltip("入口节点 ID。运行时从该节点开始播放。")]
        public string entryNodeId;

        /// <summary>
        /// 节点表。
        /// </summary>
        /// <remarks>
        /// 这里的<b>顺序只影响 Inspector 中的可读性</b>，不承载任何语义 ——
        /// 跳转一律走 <see cref="DialogueNode.id"/>。策划增删节点时数组下标会整体位移，
        /// 若用它做跳转依据，存档里的旧下标会静默指向错误节点。
        /// </remarks>
        [Tooltip("节点表。顺序仅影响 Inspector 可读性，跳转一律走节点 ID。")]
        public List<DialogueNode> nodes = new List<DialogueNode>();

        /// <summary>ID → 节点 的索引缓存。不序列化，首次访问时惰性构建。</summary>
        [NonSerialized] private Dictionary<string, DialogueNode> _lookup;

        /// <summary>节点总数（含未连通的孤立节点）。</summary>
        public int NodeCount => nodes?.Count ?? 0;

        /// <summary>按 ID 取节点。ID 为空或不存在时返回 <c>null</c>。</summary>
        public DialogueNode GetNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureLookup();
            return _lookup.TryGetValue(id, out var node) ? node : null;
        }

        /// <summary>按 ID 取节点，用于需要区分"不存在"与"存在但为 null"的场合。</summary>
        public bool TryGetNode(string id, out DialogueNode node)
        {
            node = null;
            if (string.IsNullOrEmpty(id)) return false;
            EnsureLookup();
            return _lookup.TryGetValue(id, out node);
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;

            // 用 Ordinal 比较：ID 是程序标识符，不该受区域设置影响（土耳其语 i/I 问题）
            _lookup = new Dictionary<string, DialogueNode>(NodeCount, StringComparer.Ordinal);

            for (int i = 0; i < NodeCount; i++)
            {
                var node = nodes[i];
                if (node == null || string.IsNullOrEmpty(node.id)) continue;

                // 重复 ID 保留首个并报错。静默覆盖会让跳转指向"另一个"节点，是最难排查的一类 bug
                if (_lookup.ContainsKey(node.id))
                {
                    Debug.LogError(
                        $"[ChatSystem] ConversationAsset \"{name}\" 存在重复节点 ID: \"{node.id}\"（下标 {i}）。已保留首个出现的节点。",
                        this);
                    continue;
                }

                _lookup.Add(node.id, node);
            }
        }

        private void OnValidate()
        {
            // Inspector 中任何改动都会让缓存失效，下次访问时重建
            _lookup = null;
        }
    }
}
