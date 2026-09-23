using ChatSystem.Data.Model;
using TMPro;
using UnityEngine;

namespace ChatSystem.View
{
    /// <summary>
    /// 时间分割线。挂在 <c>TimeDivider.prefab</c> 的根节点上。
    /// </summary>
    /// <remarks>
    /// 分割线<b>不是气泡</b>：它横跨整行、居中、没有头像也没有朝向，因此不能塞进
    /// 左右两个气泡池里的任何一个。单独一类视图 + 单独一个小池子。
    /// <para>
    /// 插入时机完全由 <c>TimeDividerPolicy</c> 在运行时层决定，这里只负责显示 ——
    /// 文案已经存在 <see cref="MessageData.text"/> 里，随存档一起持久化，无需重算。
    /// </para>
    /// </remarks>
    public class TimeDividerView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        /// <summary>按节点名重新解析引用。</summary>
        public void ResolveReferences()
        {
            if (label == null) label = ViewHierarchy.FindDeep<TMP_Text>(transform, ViewHierarchy.Label);
        }

        private void OnEnable()
        {
            // Awake 在对象池取出复用时不会重跑，这里兜一次底
            if (label == null) ResolveReferences();
        }

        /// <summary>显示分割线文案。</summary>
        public void Bind(MessageData message)
        {
            if (message == null || label == null) return;

            label.text = message.text ?? string.Empty;
        }

        /// <summary>归还对象池前清空状态。</summary>
        public void ResetForPool()
        {
            if (label != null) label.text = string.Empty;
        }
    }
}
