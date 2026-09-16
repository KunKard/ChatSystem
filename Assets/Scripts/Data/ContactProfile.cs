using UnityEngine;

namespace ChatSystem.Data
{
    /// <summary>
    /// 一个联系人的静态资料。策划在 Inspector 中配置，运行时只读。
    /// </summary>
    [CreateAssetMenu(menuName = "ChatSystem/Contact Profile", fileName = "Contact_")]
    public class ContactProfile : ScriptableObject
    {
        /// <summary>
        /// 稳定 ID，作为存档键与消息的 <c>senderId</c> 比对依据。
        /// </summary>
        /// <remarks>
        /// <b>一经发布不可更改</b>：存档里记录的是这个字符串，改了会导致旧存档找不到会话。
        /// 改显示名请改 <see cref="displayName"/>，那是给人看的。
        /// </remarks>
        [Tooltip("稳定 ID，作为存档键。一经发布不可更改；改显示名请改 displayName。")]
        public string id;

        /// <summary>备注名，显示在联系人列表与聊天顶部栏。</summary>
        [Tooltip("备注名，如\"知更鸟\"。")]
        public string displayName;

        /// <summary>
        /// 个性签名，显示在聊天顶部栏名字下方。
        /// </summary>
        /// <remarks>
        /// 等待对方回复期间，这里会被临时替换为"对方正在输入…"（见 §5.2.5）。
        /// 替换的是显示文本，不是这个字段本身。
        /// </remarks>
        [Tooltip("个性签名。对方输入期间会被临时替换为\"对方正在输入…\"。")]
        public string signature;

        /// <summary>头像。MVP 仅支持本地引用，见 <see cref="IAvatarProvider"/>。</summary>
        [Tooltip("头像。MVP 仅支持本地 Sprite 引用。")]
        public Sprite avatar;

        /// <summary>该会话尚无消息时，联系人列表里显示的预览文案。</summary>
        [Tooltip("无消息时的会话预览文案。")]
        public string defaultPreview;
    }
}
