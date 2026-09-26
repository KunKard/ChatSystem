using UnityEngine;

namespace ChatSystem.View
{
    /// <summary>
    /// 未读红点"出现时弹一次"的缩放动画。挂在 <c>ChatPartner.prefab</c> 的 <c>Reddot</c> 节点上。
    /// </summary>
    /// <remarks>
    /// 设计文档 §5.1 只写了"收到消息时<b>带一次</b>缩放动画"，没给时长与曲线，
    /// 本类按主流 IM 的手感取值（见各常量）。三个设计要点：
    /// <list type="number">
    /// <item><b>只播一次，不循环。</b>红点是状态而不是过程 —— 悬停式呼吸会让整列联系人
    /// 一直在动，反而盖过"哪个会话有新消息"这个真正要传达的信息。</item>
    /// <item><b>由外部决定何时播。</b>本类不认识会话，也不知道未读数，
    /// 只负责"把当前这次显示演成一个弹出"。判断该不该播属于 <see cref="ContactItemView"/>。</item>
    /// <item><b>不写 <c>Update</c> 的空转。</b>动画结束后自我禁用，静止的红点每帧零开销；
    /// 而列表里最多同时存在十来个红点，这点开销本来也不该有。</item>
    /// </list>
    /// </remarks>
    public class ReddotPulse : MonoBehaviour
    {
        /// <summary>整段动画时长（秒）。</summary>
        /// <remarks>
        /// 0.25s 是"看得见但不挡路"的常见取值：再短（&lt;0.15s）在 60fps 下只剩几帧，
        /// 会被看成瞬间出现而不是弹；再长（&gt;0.4s）在连收几条消息时会拖出尾巴。
        /// </remarks>
        private const float Duration = 0.25f;

        /// <summary><c>easeOutBack</c> 的张力系数，决定过冲幅度。</summary>
        /// <remarks>
        /// 1.70158 是这条曲线的经典取值，过冲约 10% —— 红点会先胀到 1.1 倍再收回 1.0。
        /// 过冲正是"弹出感"的来源：单纯的 0→1 补间看起来只是放大，不像被"弹"出来。
        /// </remarks>
        private const float Tension = 1.70158f;

        /// <summary>已播放的时长，仅在 <see cref="Update"/> 运行时有效。</summary>
        private float _elapsed;

        /// <summary>从头播放一次弹出动画。</summary>
        public void Play()
        {
            // 先归零再启用：Update 要一帧后才跑，不先设成 0 会闪一帧满尺寸的红点
            transform.localScale = Vector3.zero;
            _elapsed = 0f;
            enabled = true;
        }

        /// <summary>立即落到终态（满尺寸），不播动画。</summary>
        public void SnapToFull()
        {
            enabled = false;
            _elapsed = 0f;
            transform.localScale = Vector3.one;
        }

        private void OnDisable()
        {
            // 中途被禁用（切走会话把红点关掉、或列表项被池回收）会停在半大的尺寸上。
            // 下次这个节点再显示时，除非有人记得复位，否则会是一个小一圈的红点
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            float t = _elapsed / Duration;
            if (t >= 1f)
            {
                SnapToFull();
                return;
            }

            transform.localScale = Vector3.one * EaseOutBack(t);
        }

        /// <summary>
        /// <c>easeOutBack</c>：0 起步、中途越过 1 约 10%、终点恰好回到 1。
        /// </summary>
        /// <remarks>
        /// 用一条解析曲线而不是"放大到 1.1 再缩回 1.0"两段拼接：两段拼接需要在接缝处
        /// 手动对齐速度，否则会在顶点处看出一个折角；而这条曲线终点严格等于 1，
        /// 不存在"动画结束但缩放没归位"的残余误差。
        /// </remarks>
        private static float EaseOutBack(float t)
        {
            float p = t - 1f;
            return 1f + (Tension + 1f) * p * p * p + Tension * p * p;
        }
    }
}
