using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem.View
{
    /// <summary>
    /// 把本节点的 preferredWidth / minWidth 钳到 <see cref="maxWidth"/> 上限，用于气泡宽度自适应。
    ///
    /// 【为什么需要它】
    /// TMP 的 preferredWidth 返回的是**完全不换行的整行宽度**，不是换行后的宽度。
    /// 依据：<c>TMP_Text.GetPreferredWidth()</c> 内部
    /// <code>
    /// Vector2 margin = k_LargePositiveVector2;                                  // (INT_MAX, INT_MAX)
    /// float preferredWidth = CalculatePreferredValues(ref fontSize, margin, false, false).x;
    ///                                                                     ↑ isWordWrappingEnabled = false
    /// </code>
    /// 即：以"无限可用宽度 + 关闭自动换行"计算。
    /// 于是长文本会报出一个极大的 preferredWidth，气泡一路撑破面板且永不换行。
    ///
    /// 【为什么用 layoutPriority 覆盖，而不是单纯实现 ILayoutElement】
    /// <c>LayoutUtility.GetLayoutProperty</c> 遍历同一 GameObject 上的所有 ILayoutElement：
    /// 优先级**更高者整个覆盖**低优先级的值，只有**同优先级**才取最大值。
    /// 内置组件的优先级：LayoutGroup = 0，TMP_Text = 0，LayoutElement = 1。
    /// 所以本组件用 2 才能真正接管宽度；若用 0 或 1，只会被 max 掉，完全不起作用。
    ///
    /// 【为什么高度一律返回 -1】
    /// <c>LayoutUtility</c> 把负值视为"该组件不提供此属性"并跳过（见 GetLayoutProperty 中 <c>if (prop &lt; 0) continue;</c>）。
    /// 因此高度方向让位给同节点的 VerticalLayoutGroup，本组件只负责宽度。
    ///
    /// 【用法】
    /// 挂在气泡底图节点（与 VerticalLayoutGroup 同节点）上，其下是 TMP 文本。
    /// 该节点自身**不能**同时挂 LayoutElement 的 preferredWidth —— 那会与这里打架。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Layout/Clamp Preferred Width")]
    [DisallowMultipleComponent]
    public sealed class ClampPreferredWidth : MonoBehaviour, ILayoutElement
    {
        [Tooltip("气泡宽度上限（像素）。建议取聊天面板宽度的 70% 左右。")]
        [SerializeField] private float m_MaxWidth = 760f;

        [Tooltip("必须高于 LayoutGroup(0) 与 LayoutElement(1)，否则钳不住。")]
        [SerializeField] private int m_LayoutPriority = 2;

        private RectTransform _rect;
        private HorizontalOrVerticalLayoutGroup _group;

        /// <summary>宽度上限。运行时改动会立即触发布局重建。</summary>
        public float maxWidth
        {
            get => m_MaxWidth;
            set
            {
                if (Mathf.Approximately(m_MaxWidth, value)) return;
                m_MaxWidth = value;
                SetDirty();
            }
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _group = GetComponent<HorizontalOrVerticalLayoutGroup>();
        }

        private void OnEnable()
        {
            // Awake 在对象池取出复用时不会重跑，这里兜一次底
            if (_rect == null) _rect = (RectTransform)transform;
            if (_group == null) _group = GetComponent<HorizontalOrVerticalLayoutGroup>();
            SetDirty();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_LayoutPriority = Mathf.Max(2, m_LayoutPriority); // 低于 2 就失去意义
            m_MaxWidth = Mathf.Max(1f, m_MaxWidth);
            SetDirty();
        }
#endif

        private void SetDirty()
        {
            if (!isActiveAndEnabled) return;
            if (_rect == null) _rect = (RectTransform)transform;
            LayoutRebuilder.MarkLayoutForRebuild(_rect);
        }

        // ------------------------------------------------------------------
        //  ILayoutElement
        // ------------------------------------------------------------------

        public int layoutPriority => m_LayoutPriority;

        public float minWidth => ClampedContentWidth();
        public float preferredWidth => ClampedContentWidth();
        public float flexibleWidth => 0f;

        // 负值 = "本组件不提供该属性"，高度交还给同节点的 VerticalLayoutGroup
        public float minHeight => -1f;
        public float preferredHeight => -1f;
        public float flexibleHeight => -1f;

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }

        // ------------------------------------------------------------------
        //  内部
        // ------------------------------------------------------------------

        /// <summary>
        /// 子节点的原始 preferred 宽度 + 内边距，再钳上限。
        ///
        /// 注意这里必须**自己遍历子节点**算，不能调 LayoutUtility.GetPreferredSize(自身) ——
        /// 本组件就在自身节点上且优先级最高，那样会递归到自己，栈溢出。
        /// </summary>
        private float ClampedContentWidth()
        {
            if (_rect == null) _rect = (RectTransform)transform;

            float raw = 0f;
            int count = _rect.childCount;

            if (_group is HorizontalLayoutGroup)
            {
                // 横排：子节点宽度累加，并计入间距
                for (int i = 0; i < count; i++)
                {
                    var child = _rect.GetChild(i) as RectTransform;
                    if (!IsActive(child)) continue;
                    if (raw > 0f) raw += _group.spacing;
                    raw += LayoutUtility.GetPreferredSize(child, 0);
                }
                raw += _group.padding.horizontal;
            }
            else
            {
                // 竖排（气泡的正常情况）：取子节点最宽的一个
                for (int i = 0; i < count; i++)
                {
                    var child = _rect.GetChild(i) as RectTransform;
                    if (!IsActive(child)) continue;
                    raw = Mathf.Max(raw, LayoutUtility.GetPreferredSize(child, 0));
                }
                if (_group != null) raw += _group.padding.horizontal;
            }

            return Mathf.Min(raw, m_MaxWidth);
        }

        private static bool IsActive(RectTransform child)
        {
            return child != null && child.gameObject.activeInHierarchy;
        }
    }
}
