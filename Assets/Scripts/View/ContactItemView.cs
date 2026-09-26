using System;
using ChatSystem.Data;
using ChatSystem.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem.View
{
    /// <summary>
    /// 联系人列表里的一项。挂在 <c>ChatPartner.prefab</c> 的根节点上。
    /// </summary>
    /// <remarks>
    /// 预览文案直接用 <see cref="ChatSession.PreviewText"/>，<b>这里不再自己判一遍</b> ——
    /// "正在输入 &gt; 最后一条消息 &gt; 默认预览"这个优先级属于会话状态，已经由运行时层实现并单测覆盖，
    /// 视图层再写一份必然与它漂移。
    /// </remarks>
    public class ContactItemView : MonoBehaviour
    {
        [Header("引用（可由 工具 / ChatSystem / 接线 Day 2 场景 自动填充）")]
        [SerializeField] private Image background;
        [SerializeField] private Image avatarImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text previewText;
        [SerializeField] private GameObject reddot;
        [SerializeField] private ReddotPulse reddotPulse;
        [SerializeField] private Button button;

        [Header("配色（占位值，待正式视觉稿）")]
        [Tooltip("未选中时的背景色。默认值与预制体现有底色一致，避免接线后视觉突然变化。")]
        [SerializeField] private Color normalColor = new Color(0.831f, 0.831f, 0.831f, 1f);

        [Tooltip("选中时的背景色。取同色系压暗一档，在浅色面板上可辨且不突兀。")]
        [SerializeField] private Color selectedColor = new Color(0.722f, 0.722f, 0.741f, 1f);

        private ChatSession _session;
        private Action<ChatSession> _onPick;

        /// <summary>
        /// 上一次刷新后红点的显隐状态。
        /// </summary>
        /// <remarks>
        /// 存在的唯一理由是分辨"红点本来就在"和"红点刚出现"—— 只有后者该播动画。
        /// 少了它就只能靠 <c>UnreadCount &gt; 0</c> 判断，那样每来一条消息都会重播一次，
        /// 连收三条就是连闪三下。
        /// </remarks>
        private bool _reddotShown;

        /// <summary>本项对应的会话。列表靠它做身份比对。</summary>
        public ChatSession Session => _session;

        /// <summary>按节点名重新解析引用。编辑器接线脚本会调用它来把引用烘焙进预制体。</summary>
        public void ResolveReferences()
        {
            var root = transform;

            if (background == null) background = GetComponent<Image>();
            if (button == null) button = GetComponent<Button>();
            if (avatarImage == null) avatarImage = ViewHierarchy.FindDeep<Image>(root, ViewHierarchy.Avatar);
            if (nameText == null) nameText = ViewHierarchy.FindDeep<TMP_Text>(root, ViewHierarchy.Name);
            if (previewText == null) previewText = ViewHierarchy.FindDeep<TMP_Text>(root, ViewHierarchy.LastMessage);

            var dot = ViewHierarchy.FindDeep(root, ViewHierarchy.Reddot);
            if (dot != null)
            {
                if (reddot == null) reddot = dot.gameObject;
                if (reddotPulse == null) reddotPulse = dot.GetComponent<ReddotPulse>();
            }

            // 预览必须单行截断（§5.1）：长文案不能换行撑高列表项，也不能画到箭头外面。
            // 在代码里设而不是只在预制体上设，是为了让"预制体被改坏"不至于变成静默的视觉错误。
            if (previewText != null)
            {
                previewText.enableWordWrapping = false;
                previewText.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        private void OnEnable()
        {
            if (nameText == null || previewText == null) ResolveReferences();
        }

        /// <summary>绑定一个会话。</summary>
        /// <param name="session">要展示的会话。</param>
        /// <param name="selected">是否为当前打开的会话。</param>
        /// <param name="onPick">点击回调，参数为本项对应的会话。</param>
        public void Bind(ChatSession session, bool selected, Action<ChatSession> onPick)
        {
            _session = session;
            _onPick = onPick;

            if (button != null)
            {
                // 先清再加：Rebuild 复用同一个实例时，不清会一次点击触发多次回调
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(HandleClick);
            }

            var contact = ContactOf(session);

            if (nameText != null) nameText.SetText(contact != null ? contact.displayName : string.Empty);

            if (avatarImage != null)
            {
                var sprite = contact != null ? contact.avatar : null;
                avatarImage.sprite = sprite;
                // 关掉 Image 组件而不是 GameObject：头像位的 80×80 由 LayoutElement 撑着，
                // 整块隐藏会让名字和预览往左跳
                avatarImage.enabled = sprite != null;
            }

            SetSelected(selected);

            // 绑定是"这一项换了个联系人"，不是"来了条新消息"：红点按当前状态直接落位，不播动画。
            // 列表池化之后 Bind 会被频繁调用，在这里播动画会变成满屏红点齐闪
            RefreshPreviewCore(animateReddot: false);
        }

        /// <summary>只更新选中态，不重新绑定。</summary>
        public void SetSelected(bool selected)
        {
            if (background != null) background.color = selected ? selectedColor : normalColor;
        }

        /// <summary>只刷新预览文案与红点。消息到达、输入状态变化时走这条，代价远低于整项重绑。</summary>
        public void RefreshPreview()
        {
            RefreshPreviewCore(animateReddot: true);
        }

        private void RefreshPreviewCore(bool animateReddot)
        {
            if (previewText != null) previewText.SetText(_session != null ? _session.PreviewText : string.Empty);
            ApplyReddot(animateReddot);
        }

        /// <summary>红点的显隐，以及"刚出现时弹一次"。</summary>
        /// <param name="animate">是否允许播动画；绑定新会话时传 <c>false</c>。</param>
        /// <remarks>
        /// <b>只在"不显示 → 显示"这一种转移上播。</b>红点本来就在、又来一条消息时不重播 ——
        /// 那会把动画变成连续闪烁，而红点要传达的恰恰是"这里有未读"这个稳定状态。
        /// </remarks>
        private void ApplyReddot(bool animate)
        {
            bool shouldShow = _session != null && _session.UnreadCount > 0;
            if (shouldShow == _reddotShown) return;

            _reddotShown = shouldShow;

            if (reddot == null) return;
            reddot.SetActive(shouldShow);

            if (!shouldShow) return;

            // 没接线时红点照样能正常显隐，只是少了弹出动画。不值得为它报错中断
            if (reddotPulse == null) return;

            if (animate) reddotPulse.Play();
            else reddotPulse.SnapToFull();
        }

        private void HandleClick()
        {
            _onPick?.Invoke(_session);
        }

        private static ContactProfile ContactOf(ChatSession session)
        {
            return session != null && session.Asset != null ? session.Asset.contact : null;
        }
    }
}
