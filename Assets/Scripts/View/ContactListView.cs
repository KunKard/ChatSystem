using System;
using System.Collections.Generic;
using ChatSystem.Runtime;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace ChatSystem.View
{
    /// <summary>
    /// 左侧联系人列表。挂在 <c>LeftBackGround/Scroll View</c> 上。
    /// </summary>
    /// <remarks>
    /// 列表项走对象池（<c>Plan-4Days.md</c> 任务 2.2）。
    /// <para>
    /// <b>池化在这里省下的到底是什么</b>：不是"显示 50 个联系人只造 10 个实例"——
    /// 列表没有做虚拟化，50 个联系人终究要有 50 个列表项同时存在。省下的是<b>重建</b>：
    /// 池化之前，每次 <see cref="Rebuild"/> 都 <c>Destroy</c> 掉全部旧项再 <c>Instantiate</c>
    /// 一批新的，代价随联系人数量线性增长且每帧分布不均；池化之后重建只是重新绑定数据，
    /// <c>Instantiate</c> 次数在首次构建后就停止增长。
    /// </para>
    /// <para>
    /// 这也正是"未读优先 + 配置顺序"排序所需要的形态：排序会频繁改变项的顺序甚至成员，
    /// 而"复用同一个实例、只重绑数据"恰好是 <see cref="Rebuild"/> 现在做的事。
    /// </para>
    /// </remarks>
    public class ContactListView : MonoBehaviour
    {
        [Header("引用（可由 工具 / ChatSystem / 接线 Day 2 场景 自动填充）")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private ContactItemView itemPrefab;

        /// <summary>列表项池的预热数量（设计文档 §任务 2.2）。</summary>
        public const int ItemCapacity = 10;

        /// <summary>池的上限。给得比预热大得多：列表项按联系人数扩容，这里只是兜底防失控。</summary>
        private const int MaxSize = 256;

        /// <summary>当前正在显示的列表项，顺序与联系人顺序一致。</summary>
        private readonly List<ContactItemView> _items = new List<ContactItemView>();

        private ObjectPool<ContactItemView> _pool;
        private Action<string> _onPick;
        private string _activeContactId;
        private int _instantiateCount;

        /// <summary>本组件创建过的列表项实例总数。验收池化效果时读这个值。</summary>
        public int InstantiateCount => _instantiateCount;

        /// <summary>按节点名重新解析引用。</summary>
        public void ResolveReferences()
        {
            if (scrollRect == null) scrollRect = GetComponent<ScrollRect>();
            if (scrollRect != null && content == null) content = scrollRect.content;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDestroy()
        {
            _pool?.Dispose();
            _pool = null;
        }

        /// <summary>重建列表：把当前显示的项全部归还池，再按新数据取出来重绑。</summary>
        /// <param name="sessions">按展示顺序排列的会话。</param>
        /// <param name="activeContactId">当前打开的会话 ID，可为 <c>null</c>。</param>
        /// <param name="onPick">点击回调，参数为联系人 ID。</param>
        public void Rebuild(IList<ChatSession> sessions, string activeContactId, Action<string> onPick)
        {
            _onPick = onPick;
            _activeContactId = activeContactId;

            ReleaseAll();

            if (content == null || itemPrefab == null)
            {
                Debug.LogError(
                    "[ChatSystem] ContactListView 缺少 Content 或列表项预制体，联系人列表不会显示。" +
                    "请运行菜单 工具 / ChatSystem / 接线 Day 2 场景。", this);
                return;
            }

            if (sessions == null) return;

            EnsurePool(sessions.Count);

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session == null) continue;

                var item = _pool.Get();
                item.gameObject.name = $"Contact_{session.ContactId}";
                item.Bind(session, session.ContactId == activeContactId, HandleItemPicked);
                _items.Add(item);
            }
        }

        /// <summary>刷新某个会话对应项的预览与红点。找不到对应项时静默返回。</summary>
        public void RefreshItem(ChatSession session)
        {
            if (session == null) return;

            for (int i = 0; i < _items.Count; i++)
            {
                if (!ReferenceEquals(_items[i].Session, session)) continue;

                _items[i].RefreshPreview();
                return;
            }
        }

        /// <summary>刷新全部项的预览与红点。</summary>
        public void RefreshAll()
        {
            for (int i = 0; i < _items.Count; i++) _items[i].RefreshPreview();
        }

        /// <summary>切换选中项。只改背景色，不重建列表。</summary>
        public void SetSelected(string contactId)
        {
            _activeContactId = contactId;

            for (int i = 0; i < _items.Count; i++)
            {
                var session = _items[i].Session;
                _items[i].SetSelected(session != null && session.ContactId == contactId);
            }
        }

        private void EnsurePool(int expectedCount)
        {
            if (_pool != null) return;

            _pool = new ObjectPool<ContactItemView>(
                createFunc: () =>
                {
                    _instantiateCount++;
                    return Instantiate(itemPrefab, content);
                },
                actionOnGet: item =>
                {
                    item.gameObject.SetActive(true);

                    // 每次取出都提到末位，顺序才与 Get 的先后一致。
                    // 从池里复用的实例保留着上次的 sibling index，不重排会插进列表中间
                    item.transform.SetAsLastSibling();
                },
                actionOnRelease: item =>
                {
                    item.gameObject.SetActive(false);
                    item.transform.SetParent(content, false);
                },
                actionOnDestroy: item => Destroy(item.gameObject),
                // 仅 Editor / Development 生效，用来抓"同一个项被归还两次"
                collectionCheck: true,
                defaultCapacity: ItemCapacity,
                maxSize: MaxSize);

            // 预热数量取"容量"与"实际要显示的数量"的较小者：
            // 只有 3 个联系人却先造 10 个列表项，那 7 个纯属白造
            int prewarm = Mathf.Min(ItemCapacity, expectedCount);
            var buffer = new ContactItemView[prewarm];

            for (int i = 0; i < prewarm; i++) buffer[i] = _pool.Get();
            for (int i = 0; i < prewarm; i++) _pool.Release(buffer[i]);
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null) continue;
                _pool.Release(_items[i]);
            }

            _items.Clear();
        }

        private void HandleItemPicked(ChatSession session)
        {
            if (session == null) return;
            _onPick?.Invoke(session.ContactId);
        }
    }
}
