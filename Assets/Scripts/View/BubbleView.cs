using ChatSystem.Data;
using ChatSystem.Data.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem.View
{
    /// <summary>
    /// 一个消息气泡。挂在 <c>ChatBubble.prefab</c> / <c>MyChatBubble.prefab</c> 的根节点上。
    /// </summary>
    /// <remarks>
    /// 本类<b>只负责把一条 <see cref="MessageData"/> 贴到界面上</b>，不决定该显示哪些消息、
    /// 也不决定气泡该有多大 —— 前者是 <c>ChatWindowView</c> 的职责，
    /// 后者交给预制体上的布局组与 <see cref="ClampPreferredWidth"/>。
    /// <para>
    /// 复用的是对象池，因此<b>所有状态都必须在 <see cref="ResetForPool"/> 里清干净</b>。
    /// 池化对象不会重新执行 <c>Awake</c>，这是气泡复用最常见的 bug 来源：
    /// 上一条消息的贴图或激活态漏进下一条。
    /// </para>
    /// </remarks>
    public class BubbleView : MonoBehaviour
    {
        [Header("引用（可由 工具 / ChatSystem / 接线 Day 2 场景 自动填充）")]
        [SerializeField] private Image avatarImage;
        [SerializeField] private TMP_Text nameText;

        [Tooltip("文本气泡根节点（含背景图与 ContentSizeFitter）。媒体消息时整块隐藏。")]
        [SerializeField] private GameObject bodyRoot;
        [SerializeField] private TMP_Text bodyText;

        [Tooltip("表情包/图片根节点。文本消息时整块隐藏。")]
        [SerializeField] private GameObject stickerRoot;
        [SerializeField] private Image stickerImage;

        [Header("朝向")]
        [Tooltip("勾选表示这是玩家自己的气泡（右对齐）。仅用于调试与断言，布局方向由预制体的 HorizontalLayoutGroup 决定。")]
        [SerializeField] private bool isPlayerSide;

        /// <summary>本气泡是否代表玩家一侧。</summary>
        public bool IsPlayerSide => isPlayerSide;

        /// <summary>
        /// 按节点名重新解析引用。丢失引用时由 <see cref="OnEnable"/> 兜底调用，
        /// 编辑器接线脚本也调用它来把引用烘焙进预制体。
        /// </summary>
        public void ResolveReferences()
        {
            var root = transform;

            if (avatarImage == null) avatarImage = ViewHierarchy.FindDeep<Image>(root, ViewHierarchy.Avatar);
            if (nameText == null) nameText = ViewHierarchy.FindDeep<TMP_Text>(root, ViewHierarchy.Name);

            // TextBubble 自身就是背景图节点，它的 Image 同时也是 LayoutElement 的宿主
            var body = ViewHierarchy.FindDeep(root, ViewHierarchy.TextBubble);
            if (body != null)
            {
                if (bodyRoot == null) bodyRoot = body.gameObject;
                if (bodyText == null) bodyText = ViewHierarchy.FindDeep<TMP_Text>(body, ViewHierarchy.Text);
            }

            var sticker = ViewHierarchy.FindDeep(root, ViewHierarchy.Sticker);
            if (sticker != null)
            {
                if (stickerRoot == null) stickerRoot = sticker.gameObject;
                if (stickerImage == null) stickerImage = sticker.GetComponent<Image>();
            }
        }

        private void OnEnable()
        {
            // Awake 在对象池取出复用时不会重跑，这里兜一次底
            if (avatarImage == null || bodyText == null) ResolveReferences();
        }

        /// <summary>把一条消息贴到本气泡上。</summary>
        /// <param name="message">要显示的消息。为 <c>null</c> 时直接返回，不改变现有状态。</param>
        /// <param name="speaker">
        /// <b>说话人</b>的资料，用于取显示名与头像；可为 <c>null</c>。
        /// 注意是说话人而不是联系人 —— 玩家气泡要传玩家自己的资料（见下）。
        /// </param>
        /// <param name="fallbackName">
        /// <paramref name="speaker"/> 为 <c>null</c> 时显示的名字。
        /// </param>
        /// <remarks>
        /// <b>名字每次都会被写一遍</b>，哪怕 <paramref name="speaker"/> 为 <c>null</c>：
        /// 本气泡来自对象池，不写就会留着上一条消息绑上去的名字。
        /// <para>
        /// 玩家气泡不能传联系人的资料 —— 那是"对方"的，会把对方的名字和头像印到玩家自己头上。
        /// 玩家的身份不属于任何 <c>ConversationAsset</c>（它是跨会话的），因此由调用方
        /// 用 <paramref name="fallbackName"/> 提供。
        /// </para>
        /// </remarks>
        public void Bind(MessageData message, ContactProfile speaker, string fallbackName = null)
        {
            if (message == null) return;

            bool isMedia = IsMedia(message.kind);

            // 文本 / 媒体二选一。两个根节点同时隐藏会得到一个看不见的气泡，
            // 因此这里的取值必然是一真一假。
            if (bodyRoot != null) bodyRoot.SetActive(!isMedia);
            if (stickerRoot != null) stickerRoot.SetActive(isMedia);

            if (isMedia)
            {
                // 美术资源待补（Day 4）。贴图为 null 时 Unity 会画一个纯色四边形，
                // 正好当作缺图占位 —— 位置和尺寸能看出对错，比整块隐形强。
                if (stickerImage != null) stickerImage.sprite = null;
            }
            else if (bodyText != null)
            {
                bodyText.text = message.text ?? string.Empty;
            }

            // 无条件写名字：条件写会让复用的气泡留着上一条消息的名字，
            // 而"上一条是另一个联系人"恰恰是最常见的情况
            if (nameText != null)
            {
                nameText.text = speaker != null ? speaker.displayName : (fallbackName ?? string.Empty);
            }

            if (avatarImage != null)
            {
                avatarImage.sprite = speaker != null ? speaker.avatar : null;
                // 没有头像时关掉 Image，避免画出一个纯色方块；布局位置由 LayoutElement 保留，不会跳动
                avatarImage.enabled = avatarImage.sprite != null;
            }
        }

        /// <summary>
        /// 归还对象池前清空状态。
        /// </summary>
        /// <remarks>
        /// 必须把<b>两个根节点恢复到预制体的初始激活态</b>（文本开、媒体关），
        /// 否则上一条表情包会让下一个文本气泡整个消失。
        /// </remarks>
        public void ResetForPool()
        {
            if (bodyText != null) bodyText.text = string.Empty;
            if (stickerImage != null) stickerImage.sprite = null;

            if (bodyRoot != null) bodyRoot.SetActive(true);
            if (stickerRoot != null) stickerRoot.SetActive(false);
        }

        private static bool IsMedia(MessageKind kind)
        {
            return kind == MessageKind.Sticker || kind == MessageKind.Image;
        }
    }
}
