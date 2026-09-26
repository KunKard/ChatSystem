using System;
using System.Collections.Generic;
using ChatSystem.Data.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem.View
{
    /// <summary>
    /// 玩家回复选项面板。挂在 <c>ChooseBackGround</c> 上。
    /// </summary>
    /// <remarks>
    /// <b>本节点在场景中是隐藏的（<c>m_IsActive: 0</c>），这决定了本类的写法。</b>
    /// Unity <b>不会</b>对未激活的对象执行 <c>Awake</c> —— 它会推迟到第一次
    /// <c>SetActive(true)</c> 的那一刻才跑。因此所有初始化都必须在
    /// <see cref="Show"/> 里、<c>SetActive(true)</c> <b>之后</b>做，
    /// 在 <c>Awake</c> 里建池、在构造期取引用都会拿到 <c>null</c>。
    /// <para>
    /// 按钮复用场景里已有的 <c>Button</c> 作为模板克隆，而不是另造一个预制体 ——
    /// 模板已经把字号、颜色、<c>LayoutElement</c> 调好了，克隆能原样继承，
    /// 另造预制体则要在两个地方同步维护同一套视觉。
    /// </para>
    /// </remarks>
    public class ReplyOptionsView : MonoBehaviour
    {
        /// <summary>
        /// 选项数量上限。设计文档 §5.3 规定为 1~3 个。
        /// </summary>
        /// <remarks>
        /// 值本身定义在 <see cref="ConversationLimits.MaxOptions"/> —— 编辑器校验器也要引用它，
        /// 而校验器只引用得到 <c>Data</c>。这里保留同名的别名，是为了不打断既有调用方。
        /// </remarks>
        public const int MaxOptions = ConversationLimits.MaxOptions;

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<TMP_Text> _labels = new List<TMP_Text>();

        private Button _template;
        private RectTransform _container;
        private Action<int> _onPick;
        private bool _initialized;

        /// <summary>面板当前是否展示中。</summary>
        public bool IsShowing => gameObject.activeSelf;

        /// <summary>
        /// 展示选项。
        /// </summary>
        /// <param name="options">选项列表，最多显示 <see cref="MaxOptions"/> 个。</param>
        /// <param name="onPick">点击回调，参数为选项下标。</param>
        public void Show(IReadOnlyList<ChoiceOption> options, Action<int> onPick)
        {
            if (options == null || options.Count == 0)
            {
                Hide();
                return;
            }

            // 先激活：这一步会同步触发 Awake，也是下面初始化的前提
            gameObject.SetActive(true);
            EnsureInitialized();

            if (_template == null) return;

            _onPick = onPick;

            int count = Mathf.Min(options.Count, MaxOptions);
            if (options.Count > MaxOptions)
            {
                Debug.LogWarning(
                    $"[ChatSystem] 选项面板收到 {options.Count} 个选项，超出上限 {MaxOptions}，多余的不会显示。", this);
            }

            EnsureButtonCount(count);

            for (int i = 0; i < _buttons.Count; i++)
            {
                bool visible = i < count;
                _buttons[i].gameObject.SetActive(visible);

                if (!visible) continue;

                if (_labels[i] != null) _labels[i].SetText(options[i].text ?? string.Empty);
                WireClick(_buttons[i], i);
            }
        }

        /// <summary>隐藏面板并丢弃回调。</summary>
        public void Hide()
        {
            _onPick = null;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>
        /// 面板当前占据的高度（像素）。消息区靠它决定底部要让出多少。
        /// </summary>
        /// <remarks>
        /// 做成方法而不是属性，是因为它会<b>强制刷新一次布局</b>：面板刚被
        /// <c>SetActive(true)</c> 时画布还没重算，此刻读 <c>rect.height</c> 拿到的是旧值
        /// （首次显示时是 0），据此让位会让消息区纹丝不动，看起来就是"修复没生效"。
        /// 属性里藏一次全画布刷新太隐蔽，方法名至少提示了这里有事发生。
        /// </remarks>
        public float MeasureHeight()
        {
            if (!gameObject.activeInHierarchy) return 0f;

            Canvas.ForceUpdateCanvases();
            return ((RectTransform)transform).rect.height;
        }

        private void Awake()
        {
            // 正常情况下这里跑不到（节点初始隐藏），保留是为了"节点被手动改回显示"时不至于失效
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _container = (RectTransform)transform;

            // 把场景里已有的按钮全部收编：这样无论接线脚本有没有删掉多余的那个，
            // Show 都能保证"显示的数量恰好等于选项数量"
            for (int i = 0; i < _container.childCount; i++)
            {
                var button = _container.GetChild(i).GetComponent<Button>();
                if (button != null) Adopt(button);
            }

            if (_buttons.Count == 0)
            {
                Debug.LogError(
                    "[ChatSystem] ChooseBackGround 下找不到任何 Button 可作模板，选项面板无法工作。" +
                    "请运行菜单 工具 / ChatSystem / 接线 Day 2 场景。", this);
                return;
            }

            _template = _buttons[0];
        }

        private void Adopt(Button button)
        {
            _buttons.Add(button);
            _labels.Add(button.GetComponentInChildren<TMP_Text>(true));
        }

        private void EnsureButtonCount(int count)
        {
            while (_buttons.Count < count)
            {
                // 克隆出来的副本继承模板的全部布局与视觉设置；先禁用，由 Show 决定是否显示
                var clone = Instantiate(_template, _container);
                clone.gameObject.name = $"Button ({_buttons.Count})";
                clone.gameObject.SetActive(false);
                Adopt(clone);
            }
        }

        /// <summary>
        /// 给按钮接上本次的点击回调。
        /// </summary>
        /// <remarks>
        /// 每次 <see cref="Show"/> 都会重接，因此必须先清掉上一次的监听 ——
        /// 否则连点两次选项后，一个按钮会同时挂着两轮的回调，
        /// 点一下会推进两次状态机。<c>onClick</c> 上没有持久监听（预制体里是空的），
        /// 所以整体清空是安全的。
        /// </remarks>
        private void WireClick(Button button, int index)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => HandleClick(index));
        }

        private void HandleClick(int index)
        {
            var callback = _onPick;

            // 必须先隐藏再回调：Runner.Choose 是同步的，会在同一次调用里发出玩家消息
            // 并可能立刻把对方置为"正在输入"。面板若还开着，会盖在刚出现的气泡上
            Hide();

            callback?.Invoke(index);
        }
    }
}
