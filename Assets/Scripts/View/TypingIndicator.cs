using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem.View
{
    /// <summary>
    /// "对方正在输入"的三点循环动画。挂在 <c>TypingBubble.prefab</c> 的根节点上。
    /// </summary>
    /// <remarks>
    /// <b>为什么是三个独立 Image，而不是一段 TMP 富文本</b>：
    /// 用 <c>&lt;color&gt;</c> 标签改变单个字的颜色，每次都要重新解析标签、重排版、
    /// 重建整张文字网格 —— 每帧一次。三个独立 Image 只影响各自的顶点色，
    /// 代价与文字量无关。
    /// <para>
    /// <b>为什么用 <see cref="CanvasRenderer.SetColor"/> 而不是 <c>Image.color</c></b>：
    /// <c>Image.color</c> 的 setter 内部会调 <c>SetVerticesDirty()</c>，照样触发网格重建。
    /// <c>CanvasRenderer.SetColor</c> 只改渲染层的颜色，不碰网格，是本项目里
    /// "每帧改颜色" 的正确做法。
    /// </para>
    /// </remarks>
    public class TypingIndicator : MonoBehaviour
    {
        /// <summary>一个点从最暗到最亮再回到最暗的时长（秒）。</summary>
        private const float Period = 1.2f;

        /// <summary>相邻两个点的相位差（秒）。让三点呈波浪而非同步闪烁。</summary>
        private const float PhaseOffset = 0.15f;

        /// <summary>最暗时的透明度。不取 0，否则中间会出现"少了一个点"的闪烁感。</summary>
        private const float MinAlpha = 0.25f;

        /// <summary>点数。设计文档 §5.2.5 明确要求三个。</summary>
        private const int DotCount = 3;

        [SerializeField] private Image[] dots = new Image[DotCount];

        [Tooltip("三点的基础颜色，仅透明度随时间变化。默认 #8E8E93（iOS 次级文字灰）。")]
        [SerializeField] private Color baseColor = new Color(0.557f, 0.557f, 0.576f, 1f);

        private CanvasRenderer[] _renderers;

        /// <summary>按节点名重新解析三点引用。编辑器接线脚本会调用它来烘焙引用。</summary>
        public void ResolveReferences()
        {
            if (dots == null || dots.Length != DotCount) dots = new Image[DotCount];

            var container = ViewHierarchy.FindDeep(transform, "Dots");
            if (container == null) container = transform;

            for (int i = 0; i < DotCount; i++)
            {
                dots[i] = ViewHierarchy.FindDeep<Image>(container, $"Dot{i}");
            }

            CacheRenderers();
        }

        private void OnEnable()
        {
            if (!HasAllDots()) ResolveReferences();
            else CacheRenderers();

            // 立刻刷新一次，避免复用时先闪一帧上个会话留下的颜色
            Animate(Time.time);
        }

        private void Update()
        {
            Animate(Time.time);
        }

        private void Animate(float now)
        {
            if (_renderers == null) return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;

                float t = (now - i * PhaseOffset) / Period;
                float alpha = Mathf.Lerp(MinAlpha, 1f, 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f));

                renderer.SetColor(new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
            }
        }

        private void CacheRenderers()
        {
            if (dots == null) { _renderers = null; return; }

            _renderers = new CanvasRenderer[dots.Length];
            for (int i = 0; i < dots.Length; i++)
            {
                _renderers[i] = dots[i] != null ? dots[i].canvasRenderer : null;
            }
        }

        private bool HasAllDots()
        {
            if (dots == null || dots.Length != DotCount) return false;

            for (int i = 0; i < dots.Length; i++)
            {
                if (dots[i] == null) return false;
            }

            return true;
        }
    }
}
