using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChatSystem.Data
{
    /// <summary>
    /// 「资源名 → Sprite」的对照表。对话节点上的 <c>assetName</c> 填的就是这里的名字。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要它</b>：<c>Design.md</c> §7.1 的存档格式把消息的图写成
    /// <c>"assetName": "xxx"</c> 这个<b>字符串</b>，而不是直接引用 <see cref="Sprite"/> ——
    /// <see cref="JsonUtility"/> 存不下跨会话有效的资源引用。字符串存得下来，
    /// 也就意味着它自己变不回图片，中间必须有一张对照表。
    /// <para>
    /// <b>为什么查找是线性扫描而不是字典</b>：表里只有十几项，一次查找是十几次字符串比较，
    /// 而查找发生在气泡绑定上，一屏也就十几次。换成字典要多维护一份缓存，那份缓存又只能靠
    /// <c>OnValidate</c> 失效 —— 工具代码改完 <c>entries</c> 拿到旧结果，正是
    /// <c>ConversationAsset._lookup</c> 踩过的坑。这里拿性能换掉一整类 bug。
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "MediaLibrary", menuName = "ChatSystem/资源库")]
    public class MediaLibrary : ScriptableObject
    {
        /// <summary>
        /// 资产所在的 <c>Resources</c> 路径（不带扩展名）。
        /// 必须放在 <c>Assets/Resources/</c> 下且文件名与它一致，否则运行时找不到。
        /// </summary>
        public const string ResourcePath = "MediaLibrary";

        /// <summary>一条对照。</summary>
        [Serializable]
        public struct Entry
        {
            [Tooltip("对话节点的 assetName 字段填的名字。")]
            public string name;

            [Tooltip("这个名字对应的图。")]
            public Sprite sprite;
        }

        [Tooltip("全部表情包 / 图片资源。对话节点的 assetName 从这里选。")]
        [SerializeField] private List<Entry> entries = new List<Entry>();

        /// <summary>全部条目，供编辑器下拉框使用。</summary>
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>按资源名取图。名字为空、或没登记过，都返回 <c>null</c>。</summary>
        public Sprite Find(string assetName)
        {
            if (string.IsNullOrEmpty(assetName) || entries == null) return null;

            for (int i = 0; i < entries.Count; i++)
            {
                // Ordinal 不能省：资源名是标识符不是自然语言，用区域敏感比较会让
                // 土耳其语环境下的 i / I 对不上（同 ConversationAsset 的节点索引）
                if (string.Equals(entries[i].name, assetName, StringComparison.Ordinal))
                {
                    return entries[i].sprite;
                }
            }

            return null;
        }

        // ── 全局入口 ──────────────────────────────────────────────

        private static MediaLibrary _current;

        /// <summary>
        /// 全局唯一的那一份，首次访问时从 <c>Resources</c> 加载并缓存。
        /// </summary>
        /// <remarks>
        /// 走 <c>Resources</c> 而不是让 <c>ChatAppController</c> 序列化引用再逐层传下来：
        /// <see cref="ChatSystem.View.BubbleView.Bind"/> 的签名里没有它的位置，
        /// 为了递一个「字符串→资源」的查表器去改三层调用链，代价大于收益。
        /// 代价是这份资产会随包体无条件带上 —— 目前十几张图，可以接受。
        /// <para>
        /// 加载失败<b>不缓存</b>：缓存了就等于"工程里补上资源库之后，编辑器不重启永远看不到"。
        /// 而这只会发生在资源库缺失的错误状态下，重复 Load 一个不存在的路径本身很便宜。
        /// </para>
        /// </remarks>
        public static MediaLibrary Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Resources.Load<MediaLibrary>(ResourcePath);
                }

                return _current;
            }
        }

        /// <summary>
        /// 按资源名取图。资源库不存在、或名字没登记，都返回 <c>null</c>。
        /// </summary>
        /// <remarks>
        /// 取不到<b>不是错误</b>：<see cref="ChatSystem.View.BubbleView"/> 会因此画一个
        /// 纯色四边形当作缺图占位 —— 位置和尺寸能看出对错，比整块隐形强。
        /// </remarks>
        public static Sprite Resolve(string assetName)
        {
            var library = Current;
            return library != null ? library.Find(assetName) : null;
        }

        /// <summary>测试用：绕过 <c>Resources</c> 直接指定一份。</summary>
        public static void SetCurrentForTests(MediaLibrary library)
        {
            _current = library;
        }
    }
}
