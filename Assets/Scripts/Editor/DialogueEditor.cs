using System;
using System.Collections.Generic;
using System.Globalization;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using ChatSystem.Runtime;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ChatSystem.EditorTools
{
    /// <summary>
    /// <see cref="ConversationAsset"/> 的策划编辑界面。
    /// </summary>
    /// <remarks>
    /// <b>为什么是固定高度的列表行 + 下方独立详情面板</b>，而不是把字段直接摊在列表项里：
    /// 变量高度的 <c>ReorderableList</c> 元素是 IMGUI 里最容易出错的一块 —— 高度回调算错一像素，
    /// 表现是列表整体错位、点击落到隔壁行，而且只在某些折叠状态下才出现。
    /// 这个工程里第一行 IMGUI 就是它（全项目 <c>[CustomEditor]</c> 匹配数为 0），
    /// 所以选了出错时肉眼可见、且不依赖任何高度计算的那种做法。
    /// <para>
    /// <b>所有结构性改动都通过 <see cref="Defer"/> 推迟到本帧绘制结束之后。</b>
    /// 增删节点/选项会改变后续控件数量，而在 Repaint 途中改变控件数量会让 IMGUI 抛
    /// "Getting control N's position in a group with only M controls" ——
    /// 一个把 Inspector 整块画崩、且看起来和"点了删除"毫无关系的异常。
    /// </para>
    /// <para>
    /// <b>本文件可以放心 using UnityEditor</b>，它不是校验逻辑的一部分；
    /// 与它同目录的 <see cref="DialogueValidator"/> / <see cref="ValidationIssue"/> /
    /// <see cref="NodeIdUtility"/> 则必须保持编辑器无关，理由见那三个文件的注释。
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(ConversationAsset))]
    public class DialogueEditor : Editor
    {
        /// <summary>列表区域的最高像素高度，超出部分内部滚动。</summary>
        /// <remarks>
        /// 必须封顶：当前测试数据有 122 个节点，<c>DoLayoutList</c> 会一路撑到两千多像素，
        /// 详情面板会被推到看不见的地方，Inspector 实际上就废了。
        /// </remarks>
        private const float MaxListViewHeight = 340f;

        /// <summary>滚动区域在 <c>GetHeight()</c> 之外多留的余量。</summary>
        /// <remarks>
        /// <c>ReorderableList.GetHeight()</c> 把 footer 和背景边距算不算得全，Unity 没有公开保证。
        /// 少算几像素，列表底部那个 <c>+</c> 号就会被裁到可视区之外。
        /// 那现在只是备用入口（主入口是工具条上那个按钮），但裁掉一个看起来"应该有"的按钮
        /// 比多留十像素空白更让人困惑。
        /// </remarks>
        private const float ListViewSlack = 10f;

        /// <summary>列表滚动区域该给多高。</summary>
        private float ListViewHeight => Mathf.Min(_list.GetHeight() + ListViewSlack, MaxListViewHeight);

        private const string TimeFormat = "yyyy-MM-dd HH:mm";

        // ── 序列化视图 ──────────────────────────────────────────────
        private SerializedProperty _contact;
        private SerializedProperty _entryNodeId;
        private SerializedProperty _nodes;

        private ReorderableList _list;
        private Vector2 _listScroll;

        // ── 校验结果缓存 ────────────────────────────────────────────
        private readonly List<ValidationIssue> _errors = new List<ValidationIssue>();
        private readonly List<ValidationIssue> _warnings = new List<ValidationIssue>();
        private readonly HashSet<int> _errorNodes = new HashSet<int>();
        private readonly HashSet<int> _warnNodes = new HashSet<int>();

        private InboundIndex _inbound;
        private readonly HashSet<string> _knownIds = new HashSet<string>(StringComparer.Ordinal);
        private bool _stale = true;
        private bool _showIssues = true;

        // ── 下拉框候选表，随校验一起重建 ────────────────────────────
        private string[] _targetIds = new string[0];
        private string[] _targetLabels = new string[0];

        private string _search = string.Empty;
        private int _searchCursor = -1;

        /// <summary>本帧结束后要执行的结构性改动。见类注释。</summary>
        private Action _pending;

        /// <summary>本帧结束后要执行的"ID 输入框提交"。</summary>
        /// <remarks>
        /// <b>单独占一个槽位，不和 <see cref="_pending"/> 抢。</b>
        /// ID 输入框是在绘制途中判断"值变没变"的，而它后面还有详情面板的其他控件。
        /// 两者共用槽位的话，绘制到 ID 输入框时会把前面"点了 + 号"那个待执行动作**覆盖掉** ——
        /// 表现是"点了添加，节点没多出来"，而点第一个节点时又完全正常
        /// （那时节点表是空的，详情面板走的是提示分支，根本不画 ID 输入框）。
        /// </remarks>
        private Action _pendingRename;

        // ── 生命周期 ────────────────────────────────────────────────

        private void OnEnable()
        {
            _contact = serializedObject.FindProperty("contact");
            _entryNodeId = serializedObject.FindProperty("entryNodeId");
            _nodes = serializedObject.FindProperty("nodes");

            _list = new ReorderableList(serializedObject, _nodes, draggable: true,
                                        displayHeader: true, displayAddButton: true, displayRemoveButton: true)
            {
                drawHeaderCallback = DrawListHeader,
                drawElementCallback = DrawElement,
                onAddCallback = OnAddClicked,
                onRemoveCallback = OnRemoveClicked,
                onCanRemoveCallback = list => list.index >= 0 && list.index < _nodes.arraySize,
            };

            // 固定行高。用 elementHeight 而不是 elementHeightCallback：
            // 全部行等高时它是常量，GetHeight() 才是精确值 —— 下面那个滚动区域的高度靠它算
            _list.elementHeight = EditorGUIUtility.singleLineHeight + 4f;

            _stale = true;
        }

        public override void OnInspectorGUI()
        {
            var asset = (ConversationAsset)target;

            serializedObject.Update();
            if (_stale) Revalidate();

            EditorGUI.BeginChangeCheck();

            DrawPreviewBanner(asset);
            DrawSummary();
            EditorGUILayout.Space();

            if (_contact != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(_contact, new GUIContent("联系人"));
                using (new EditorGUI.DisabledScope(asset.contact == null))
                {
                    if (GUILayout.Button("选中", EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        EditorGUIUtility.PingObject(asset.contact);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            DrawEntryField(asset);

            EditorGUILayout.Space();
            DrawIssues();

            EditorGUILayout.Space();
            DrawToolbar(asset);

            // 列表要封顶滚动，否则 122 个节点把详情面板顶到屏幕外
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(ListViewHeight));
            _list.DoLayoutList();
            EditorGUILayout.EndScrollView();

            DrawDetail(asset);

            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            if (changed) _stale = true;

            // 校验必须在 ApplyModifiedProperties **之后**跑。放在之前等于对着上一帧的快照报错，
            // 表现是"改完了但错误还在，再点一下才消失"
            if (_stale) Revalidate();

            RunPending();
        }

        // ── 校验 ────────────────────────────────────────────────────

        private void Revalidate()
        {
            var asset = (ConversationAsset)target;

            _errors.Clear();
            _warnings.Clear();
            _errorNodes.Clear();
            _warnNodes.Clear();
            _knownIds.Clear();

            var issues = DialogueValidator.Validate(asset);
            for (int i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                var bucket = issue.Severity == IssueSeverity.Error ? _errors : _warnings;
                bucket.Add(issue);

                if (issue.NodeIndex >= 0)
                {
                    if (issue.Severity == IssueSeverity.Error) _errorNodes.Add(issue.NodeIndex);
                    else _warnNodes.Add(issue.NodeIndex);
                }
            }

            var nodes = asset != null ? asset.nodes : null;
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var id = nodes[i] != null ? nodes[i].id : null;
                    if (!string.IsNullOrEmpty(id)) _knownIds.Add(id);
                }
            }

            _inbound = DialogueValidator.BuildInboundIndex(asset);
            BuildTargetTable(asset);

            _stale = false;
        }

        /// <summary>
        /// 重建"跳转目标"下拉框的候选表。
        /// </summary>
        /// <remarks>
        /// 第 0 项固定是 <c>(无)</c>，值是 <c>null</c> 而不是空串 —— 空串在
        /// <see cref="DialogueValidator"/> 里是"断链"（Error），<c>null</c> 才是"到此结束"（合法）。
        /// </remarks>
        private void BuildTargetTable(ConversationAsset asset)
        {
            var nodes = asset != null ? asset.nodes : null;

            if (nodes == null || nodes.Count == 0)
            {
                _targetIds = new[] { (string)null };
                _targetLabels = new[] { "（无 —— 到此结束）" };
                return;
            }

            var ids = new string[nodes.Count + 1];
            var labels = new string[nodes.Count + 1];

            ids[0] = null;
            labels[0] = "（无 —— 到此结束）";

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                string id = node != null ? node.id : null;

                ids[i + 1] = id;
                labels[i + 1] = string.IsNullOrEmpty(id)
                    ? $"#{i} <无 ID>"
                    : $"#{i} {id} · {RowSummary(node)}";
            }

            _targetIds = ids;
            _targetLabels = labels;
        }

        // ── 顶部区域 ────────────────────────────────────────────────

        private void DrawPreviewBanner(ConversationAsset asset)
        {
            if (!DialoguePreviewState.IsPreviewing(asset)) return;

            string original = DialoguePreviewState.OriginalEntryOf(asset);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("正在预览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"入口已被临时改为「{asset.entryNodeId}」，原入口是「{original}」。退出 Play 后会自动还原。\n" +
                "这一条不要当正式配置提交。",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button("取消预览并还原入口"))
            {
                Defer(() => DialoguePreviewState.Cancel(asset));
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawSummary()
        {
            int nodes = _nodes != null ? _nodes.arraySize : 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{nodes} 个节点", EditorStyles.boldLabel, GUILayout.Width(80f));

            if (_errors.Count > 0)
            {
                EditorGUILayout.LabelField($"{_errors.Count} 个错误", ErrorStyle(), GUILayout.Width(72f));
            }
            if (_warnings.Count > 0)
            {
                EditorGUILayout.LabelField($"{_warnings.Count} 个警告", WarnStyle(), GUILayout.Width(72f));
            }
            if (_errors.Count == 0 && _warnings.Count == 0)
            {
                EditorGUILayout.LabelField("没有问题", EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("重新校验", EditorStyles.miniButton, GUILayout.Width(64f)))
            {
                Invalidate();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 画「入口节点」这一行。
        /// </summary>
        /// <remarks>
        /// <b>标签旁必须带出字段名 <c>entryNodeId</c>。</b>校验器的报错文案里写的是这个标识符，
        /// 而界面上只有「入口节点」四个字 —— 拿着报错来找一个叫 entryNodeId 的东西是找不到的，
        /// 找一圈没有的结论就是"这里压根没地方能填"。
        /// <para>
        /// 另一个死结在节点表为空时：那时下拉框里唯一一项是「（无）」，没有任何东西可选，
        /// 于是"要设入口"和"要先有节点"互相卡住。下面那段提示就是为了把这个环拆开。
        /// </para>
        /// </remarks>
        private void DrawEntryField(ConversationAsset asset)
        {
            if (_entryNodeId == null) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("入口节点",
                "资产字段 entryNodeId：打开这个联系人时从哪个节点开始。"));

            var popup = PopupOptions.Build(_targetIds, _targetLabels, _entryNodeId.stringValue);
            string picked = popup.Draw();

            if (!string.Equals(picked, _entryNodeId.stringValue, StringComparison.Ordinal))
            {
                _entryNodeId.stringValue = picked ?? string.Empty;
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                bool canPreview = !string.IsNullOrEmpty(_entryNodeId.stringValue)
                                  && _knownIds.Contains(_entryNodeId.stringValue);

                using (new EditorGUI.DisabledScope(!canPreview))
                {
                    if (GUILayout.Button("▶ 试跑", EditorStyles.miniButton, GUILayout.Width(52f)))
                    {
                        string id = _entryNodeId.stringValue;
                        Defer(() => DialoguePreviewState.Begin(asset, id));
                    }
                }
            }

            EditorGUILayout.LabelField("= entryNodeId", EditorStyles.miniLabel, GUILayout.Width(72f));
            EditorGUILayout.EndHorizontal();

            // 报错只出现在折叠的「校验结果」里是不够的：那里没有"定位"按钮
            // （入口问题的 NodeIndex 是 -1），策划拿到了错误却没有任何下一步。
            // 把话说到出问题的地方来
            bool hasNodes = _nodes != null && _nodes.arraySize > 0;

            if (!hasNodes)
            {
                EditorGUILayout.HelpBox(
                    "这一行就是 entryNodeId，但节点表还是空的，下拉框里只有「（无）」可选。\n" +
                    "先点下面的「＋ 添加节点」—— 第一条节点会自动成为入口，不用回来再选一次。",
                    MessageType.Info);
            }
            else if (string.IsNullOrEmpty(_entryNodeId.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "这一行就是 entryNodeId。现在它是空的，打开这个联系人会是一条空会话。\n" +
                    "在左边这个下拉框里选一个节点当开场。",
                    MessageType.Error);
            }
        }

        // ── 校验报告 ────────────────────────────────────────────────

        private void DrawIssues()
        {
            if (_errors.Count == 0 && _warnings.Count == 0) return;

            int total = _errors.Count + _warnings.Count;
            _showIssues = EditorGUILayout.Foldout(_showIssues, $"校验结果（{total}）", true);
            if (!_showIssues) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            for (int i = 0; i < _errors.Count; i++) DrawIssueRow(_errors[i], true);
            for (int i = 0; i < _warnings.Count; i++) DrawIssueRow(_warnings[i], false);

            EditorGUILayout.EndVertical();
        }

        private void DrawIssueRow(ValidationIssue issue, bool isError)
        {
            EditorGUILayout.BeginHorizontal();

            string where = issue.NodeIndex < 0
                ? "资产"
                : string.IsNullOrEmpty(issue.NodeId) ? $"#{issue.NodeIndex}" : $"#{issue.NodeIndex} {issue.NodeId}";

            EditorGUILayout.LabelField(isError ? "错误" : "警告",
                isError ? ErrorStyle() : WarnStyle(), GUILayout.Width(32f));
            EditorGUILayout.LabelField(where, EditorStyles.miniBoldLabel, GUILayout.Width(96f));

            string text = issue.FixHint == null ? issue.Message : $"{issue.Message} —— {issue.FixHint}";
            EditorGUILayout.LabelField(new GUIContent(text, text), EditorStyles.wordWrappedMiniLabel);

            if (issue.NodeIndex >= 0)
            {
                if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(38f)))
                {
                    SelectNode(issue.NodeIndex);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── 工具条 ──────────────────────────────────────────────────

        private void DrawToolbar(ConversationAsset asset)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("查找", GUILayout.Width(32f));
            _search = EditorGUILayout.TextField(_search, GUILayout.Width(120f));

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_search)))
            {
                if (GUILayout.Button("下一个匹配", EditorStyles.miniButton, GUILayout.Width(76f)))
                {
                    SelectNextMatch(asset);
                }
            }

            GUILayout.FlexibleSpace();

            // 主路径的"添加"。画在这里而不是只靠列表底部的 +：
            // 列表被包在滚动区域里，底部按钮的位置取决于高度算法和裁剪，
            // 而添加节点是最高频的动作，不该押在那上面
            if (GUILayout.Button("＋ 添加节点", EditorStyles.miniButton, GUILayout.Width(76f)))
            {
                Defer(AddNode);
            }

            if (GUILayout.Button("取消选中", EditorStyles.miniButton, GUILayout.Width(60f)))
            {
                _list.index = -1;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "节点顺序只影响这里的可读性，跳转一律走 ID。删除也可以在详情面板里做。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void SelectNextMatch(ConversationAsset asset)
        {
            var nodes = asset.nodes;
            if (nodes == null || nodes.Count == 0) return;

            for (int step = 1; step <= nodes.Count; step++)
            {
                int i = ((_searchCursor + step) % nodes.Count + nodes.Count) % nodes.Count;
                if (!Matches(nodes[i], _search)) continue;

                _searchCursor = i;
                SelectNode(i);
                return;
            }

            Debug.Log($"[ChatSystem] 「{asset.name}」里没有匹配「{_search}」的节点。", asset);
        }

        private static bool Matches(DialogueNode node, string query)
        {
            if (node == null || string.IsNullOrEmpty(query)) return false;

            if (!string.IsNullOrEmpty(node.id) &&
                node.id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            var message = node.message;
            if (message != null && !string.IsNullOrEmpty(message.text) &&
                message.text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            var options = node.options;
            if (options != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    if (option != null && !string.IsNullOrEmpty(option.text) &&
                        option.text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }

            return false;
        }

        /// <summary>选中一个节点，并把它滚进可视区域。</summary>
        /// <remarks>
        /// <c>ReorderableList</c> 没有公开的滚动 API，但滚动视图是我们自己开的，
        /// 于是可以按 <c>headerHeight + index * elementHeight</c> 自己算 —— 行高是常量，这个算式是准的。
        /// 算错的后果只是滚动位置偏一点，不会画崩。
        /// </remarks>
        private void SelectNode(int index)
        {
            if (index < 0 || index >= (_nodes != null ? _nodes.arraySize : 0)) return;

            _list.index = index;

            float rowTop = _list.headerHeight + index * _list.elementHeight;
            float rowBottom = rowTop + _list.elementHeight;
            float viewHeight = ListViewHeight;

            if (rowTop < _listScroll.y) _listScroll.y = rowTop;
            else if (rowBottom > _listScroll.y + viewHeight) _listScroll.y = rowBottom - viewHeight;

            if (_listScroll.y < 0f) _listScroll.y = 0f;

            Repaint();
        }

        // ── 列表 ────────────────────────────────────────────────────

        private void DrawListHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, $"节点（{_nodes.arraySize}）  # 类型 ID 摘要");
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var asset = (ConversationAsset)target;
            var nodes = asset.nodes;
            var node = nodes != null && index >= 0 && index < nodes.Count ? nodes[index] : null;

            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;

            float x = rect.x + 4f;

            EditorGUI.LabelField(new Rect(x, rect.y, 32f, rect.height), "#" + index, EditorStyles.miniLabel);
            x += 34f;

            if (node == null)
            {
                EditorGUI.LabelField(new Rect(x, rect.y, rect.xMax - x - 8f, rect.height),
                    "空元素 —— 没有任何跳转能命中它，选中后删掉", ErrorStyle());
                return;
            }

            bool hasError = _errorNodes.Contains(index);
            bool hasWarning = !hasError && _warnNodes.Contains(index);

            // 右侧先算：问题标记 → 入边计数，剩下的宽度全给摘要
            float right = rect.xMax - 4f;

            if (hasError || hasWarning)
            {
                EditorGUI.LabelField(new Rect(right - 14f, rect.y, 14f, rect.height),
                    hasError ? "✖" : "⚠", hasError ? ErrorStyle() : WarnStyle());
                right -= 16f;
            }

            int inbound = _inbound != null ? _inbound.CountTo(node.id) : 0;
            if (inbound > 0)
            {
                string label = "◀" + inbound.ToString(CultureInfo.InvariantCulture);
                float width = EditorStyles.miniLabel.CalcSize(new GUIContent(label)).x;
                EditorGUI.LabelField(new Rect(right - width, rect.y, width, rect.height), label, EditorStyles.miniLabel);
                right -= width + 4f;
            }

            string kind = "[" + KindLabel(node.kind) + "]";
            EditorGUI.LabelField(new Rect(x, rect.y, 40f, rect.height), kind, EditorStyles.miniBoldLabel);
            x += 42f;

            string id = string.IsNullOrEmpty(node.id) ? "<无 ID>" : node.id;
            var idStyle = string.IsNullOrEmpty(node.id) ? ErrorStyle() : EditorStyles.miniLabel;
            EditorGUI.LabelField(new Rect(x, rect.y, 56f, rect.height), id, idStyle);
            x += 58f;

            float summaryWidth = right - x;
            if (summaryWidth > 16f)
            {
                EditorGUI.LabelField(new Rect(x, rect.y, summaryWidth, rect.height), RowSummary(node));
            }
        }

        private void OnAddClicked(ReorderableList list)
        {
            Defer(AddNode);
        }

        private void OnRemoveClicked(ReorderableList list)
        {
            int index = list.index;
            Defer(() => RemoveNodeAt(index));
        }

        /// <summary>追加一个节点，并选中它。</summary>
        /// <remarks>
        /// 两个入口都走这里：工具条上那个按钮，以及 <c>ReorderableList</c> 底部的 <c>+</c>。
        /// 工具条那个是主路径 —— 它画在滚动区域之外，不受列表高度算法和滚动裁剪的影响。
        /// </remarks>
        private void AddNode()
        {
            var asset = (ConversationAsset)target;

            // 先落地挂起的编辑：下面要按数组的当前长度算下标
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(asset, "添加节点");

            int index = _nodes.arraySize;
            _nodes.arraySize = index + 1;

            var element = _nodes.GetArrayElementAtIndex(index);

            string newId = NodeIdUtility.GenerateId(KnownIdsFromProperties());
            SetString(element, "id", newId);
            SetInt(element, "kind", (int)NodeKind.Message);

            // 0.5 而不是 0：delaySeconds 的 0 是一个语义开关（"立刻发出、不显示正在输入"），
            // 新节点不该默默带上这层含义 —— 那样策划会以为"我没配延迟"和"我配了 0"是一回事
            SetFloat(element, "delaySeconds", 0.5f);

            SetString(element, "nextId", string.Empty);
            SetString(element, "timeLabel", string.Empty);
            SetLong(element, "timeValueUtc", 0L);

            var message = element.FindPropertyRelative("message");
            if (message != null)
            {
                SetInt(message, "kind", (int)MessageKind.Text);
                SetString(message, "senderId", asset.contact != null ? asset.contact.id : string.Empty);
                SetString(message, "text", string.Empty);
                SetString(message, "assetName", string.Empty);
            }

            var options = element.FindPropertyRelative("options");
            if (options != null) options.arraySize = 0;

            // 第一条节点自动成为入口。
            // 不做这一步，新建的资产会停在一个死循环里：校验器报"没有入口节点"，
            // 而入口下拉框里唯一的选项是「（无）」—— 因为还一个节点都没有。
            // 两句话各自都对，合起来就把人堵死了
            if (index == 0 && _entryNodeId != null && string.IsNullOrEmpty(_entryNodeId.stringValue))
            {
                _entryNodeId.stringValue = newId;
            }

            // 和上面的 id / 入口一起落地，共用同一个 Undo 步骤
            serializedObject.ApplyModifiedProperties();

            _list.index = index;
        }

        /// <summary>从 <c>SerializedProperty</c> 视图里枚举 ID。</summary>
        /// <remarks>
        /// 不用 <c>asset.nodes</c>：新增节点时数组刚被 <c>SerializedProperty</c> 改过，
        /// 托管侧还是旧快照，拿它生成的 ID 会和接下来要写进去的那个撞车。
        /// </remarks>
        private IEnumerable<string> KnownIdsFromProperties()
        {
            for (int i = 0; i < _nodes.arraySize; i++)
            {
                var element = _nodes.GetArrayElementAtIndex(i);
                var id = element != null ? element.FindPropertyRelative("id") : null;
                if (id != null && !string.IsNullOrEmpty(id.stringValue)) yield return id.stringValue;
            }
        }

        /// <summary>删除节点，并处理指向它的引用。</summary>
        /// <remarks>
        /// 删除前把全部入边摆出来让策划决定，而不是删完让校验器事后报一堆断链 ——
        /// 后者的问题不是"麻烦"，是策划看不出哪条链原本该连到哪里。
        /// </remarks>
        private void RemoveNodeAt(int index)
        {
            var asset = (ConversationAsset)target;
            var nodes = asset.nodes;

            if (nodes == null || index < 0 || index >= nodes.Count) return;

            var node = nodes[index];
            string id = node != null ? node.id : null;

            var refs = string.IsNullOrEmpty(id) || _inbound == null ? null : _inbound.RefsTo(id);
            int refCount = refs != null ? refs.Count : 0;

            if (refCount == 0)
            {
                if (!EditorUtility.DisplayDialog("删除节点",
                        $"删除 #{index}「{id ?? "(空元素)"}」？\n\n没有任何地方引用它。",
                        "删除", "取消")) return;

                ApplyRemove(index, null);
                return;
            }

            string detail = $"删除 #{index}「{id}」？\n\n" +
                            $"有 {refCount} 处引用指着它：\n{DescribeRefs(asset, refs)}\n";
            string successor = SingleSuccessorOf(node);

            if (successor == null)
            {
                if (!EditorUtility.DisplayDialog("删除节点",
                        detail + "\n删除后这些路径会变成「结束对话」（引用被清空）。",
                        "删除并清空引用", "取消")) return;

                ApplyRemove(index, null);
                return;
            }

            int choice = EditorUtility.DisplayDialogComplex("删除节点",
                detail + "\n这些引用要怎么处理？",
                $"改接到后继「{successor}」",
                "取消",
                "清空引用（改为结束对话）");

            if (choice == 1) return;                       // 取消
            ApplyRemove(index, choice == 0 ? successor : null);
        }

        private void ApplyRemove(int index, string retargetTo)
        {
            var asset = (ConversationAsset)target;
            var nodes = asset.nodes;
            if (nodes == null || index < 0 || index >= nodes.Count) return;

            serializedObject.ApplyModifiedProperties();

            var node = nodes[index];
            string id = node != null ? node.id : null;

            Undo.RecordObject(asset, "删除节点");

            // 改接必须在删除**之前**做完：RewriteReferences 扫的是全量数据，
            // 先删掉的话那条自引用（若有）就跟着没了，数目对不上
            if (!string.IsNullOrEmpty(id))
            {
                if (string.IsNullOrEmpty(retargetTo)) NodeIdUtility.ClearReferencesTo(asset, id);
                else NodeIdUtility.RewriteReferences(asset, id, retargetTo);
            }

            NodeIdUtility.RemoveNodeAt(asset, index);

            serializedObject.Update();
            EditorUtility.SetDirty(asset);

            // 删完选中相邻的一个：不然策划会"掉出"列表，得重新找刚才在改的地方
            int next = Mathf.Clamp(index, -1, asset.nodes.Count - 1);
            _list.index = next;
        }

        /// <summary>把入口指到某个节点。由详情面板的「设为入口」调用。</summary>
        /// <remarks>
        /// 走 <see cref="SerializedProperty"/> 而不是直接写 <c>asset.entryNodeId</c>：
        /// 直接写托管字段会绕过序列化视图，在这一帧还没 <c>ApplyModifiedProperties</c> 的时候
        /// 被随后的 <c>Update</c> 覆盖回去，表现是"点了没反应"。
        /// </remarks>
        private void SetEntry(string nodeId)
        {
            if (_entryNodeId == null) return;

            Undo.RecordObject(target, "设置入口节点");
            _entryNodeId.stringValue = nodeId ?? string.Empty;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        /// <summary>该节点的"唯一后继"；不唯一或不存在时返回 <c>null</c>。</summary>
        private string SingleSuccessorOf(DialogueNode node)
        {
            if (node == null) return null;

            // Choice 的每个选项各有各的 nextId，没有"唯一后继"这个东西
            if (node.kind == NodeKind.Choice) return null;

            string next = node.nextId;
            if (string.IsNullOrEmpty(next)) return null;
            if (string.Equals(next, node.id, StringComparison.Ordinal)) return null;   // 自环
            if (!_knownIds.Contains(next)) return null;                                // 本来就是断的

            return next;
        }

        private static string DescribeRefs(ConversationAsset asset, IReadOnlyList<InboundRef> refs)
        {
            const int MaxLines = 8;

            var nodes = asset.nodes;
            var builder = new System.Text.StringBuilder();

            int shown = Mathf.Min(refs.Count, MaxLines);
            for (int i = 0; i < shown; i++)
            {
                var reference = refs[i];
                int from = reference.FromNodeIndex;

                string fromId = nodes != null && from >= 0 && from < nodes.Count && nodes[from] != null
                    ? nodes[from].id
                    : null;

                builder.Append("· 节点 #").Append(from)
                       .Append(string.IsNullOrEmpty(fromId) ? " <无 ID>" : " " + fromId)
                       .Append(reference.IsDirect ? " 的 nextId" : $" 的选项 {reference.OptionIndex + 1}")
                       .Append('\n');
            }

            if (refs.Count > shown) builder.Append($"· ……还有 {refs.Count - shown} 处\n");

            return builder.ToString();
        }

        // ── 详情面板 ────────────────────────────────────────────────

        private void DrawDetail(ConversationAsset asset)
        {
            EditorGUILayout.Space();

            var nodes = asset.nodes;

            if (nodes == null || nodes.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "节点表是空的。用列表下方的 + 加第一个节点，再把它设成入口。", MessageType.Info);
                return;
            }

            int index = _list.index;
            if (index < 0 || index >= nodes.Count)
            {
                EditorGUILayout.HelpBox("在上面的列表里选一个节点来编辑。", MessageType.Info);
                return;
            }

            var node = nodes[index];
            if (node == null)
            {
                EditorGUILayout.HelpBox(
                    $"#{index} 是空元素。它不会被任何跳转命中，还占着列表的位置。", MessageType.Error);

                if (GUILayout.Button("删除这个空元素")) Defer(() => RemoveNodeAt(index));
                return;
            }

            var element = _nodes.GetArrayElementAtIndex(index);
            if (element == null) return;

            DrawDetailHeader(asset, index, node);

            var kindProperty = element.FindPropertyRelative("kind");
            if (kindProperty == null) return;

            // 读 property 而不是 node.kind：PropertyField 的改动要等 ApplyModifiedProperties
            // 才写回托管对象，这里读 node.kind 会慢一帧 —— 表现为"换了类型但字段还是旧的"
            var kind = (NodeKind)kindProperty.intValue;

            EditorGUILayout.PropertyField(kindProperty, new GUIContent("节点类型"));

            EditorGUILayout.Space();

            switch (kind)
            {
                case NodeKind.Message:
                    DrawMessageFields(element, asset);
                    DrawNextId(element, node.id);
                    DrawDelay(element, isWait: false);
                    DrawTime(element, asset, node.id);
                    break;

                case NodeKind.Choice:
                    DrawChoiceFields(element, asset, node.id);
                    DrawTime(element, asset, node.id);
                    break;

                case NodeKind.Wait:
                    DrawDelay(element, isWait: true);
                    DrawNextId(element, node.id);
                    break;

                case NodeKind.End:
                    EditorGUILayout.LabelField("结束节点没有可配置的字段。", EditorStyles.miniLabel);
                    break;
            }
        }

        private void DrawDetailHeader(ConversationAsset asset, int index, DialogueNode node)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField($"#{index}  {KindLabel(node.kind)}  {node.id}", EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            // 「入口」这件事必须在看得到节点的这一侧也能改。
            // 只把它放在面板顶部，等于要求策划先记住节点名、再滚回上面去选一遍 ——
            // 而"就用这个节点开场"是在看着节点时才产生的念头
            bool isEntry = string.Equals(asset.entryNodeId, node.id, StringComparison.Ordinal);

            using (new EditorGUI.DisabledScope(isEntry || string.IsNullOrEmpty(node.id)))
            {
                if (GUILayout.Button(isEntry ? "已是入口" : "设为入口",
                        EditorStyles.miniButton, GUILayout.Width(60f)))
                {
                    string id = node.id;
                    Defer(() => SetEntry(id));
                }
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("▶ 从该节点预览", EditorStyles.miniButton, GUILayout.Width(96f)))
                {
                    string id = node.id;
                    Defer(() => DialoguePreviewState.Begin(asset, id));
                }
            }

            if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(44f)))
            {
                Defer(() => RemoveNodeAt(index));
            }

            EditorGUILayout.EndHorizontal();

            DrawIdField(asset, index, node);
        }

        private void DrawIdField(ConversationAsset asset, int index, DialogueNode node)
        {
            string current = node.id ?? string.Empty;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("ID");

            // DelayedTextField：只在回车或失焦时返回新值。
            // 换成普通的 TextField 会在第一个按键就弹确认框
            string typed = EditorGUILayout.DelayedTextField(current, GUILayout.Width(120f));

            int inbound = _inbound != null ? _inbound.CountTo(current) : 0;
            EditorGUILayout.LabelField($"{inbound} 处引用", EditorStyles.miniLabel, GUILayout.Width(60f));

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(current)))
            {
                if (GUILayout.Button("复制", EditorStyles.miniButton, GUILayout.Width(40f)))
                {
                    EditorGUIUtility.systemCopyBuffer = current;
                }
            }

            EditorGUILayout.EndHorizontal();

            // 和其他结构性改动一样推迟到本帧绘制结束：改 ID 会改写引用、还可能弹确认框，
            // 两件事都不该发生在绘制途中
            if (!string.Equals(typed, current, StringComparison.Ordinal))
            {
                DeferRename(() => TryRename(asset, index, current, typed));
            }
        }

        /// <summary>改 ID，并把全部引用一并改写。</summary>
        /// <remarks>
        /// 改名和改写引用必须在同一次 Undo 里做完。分成两步就存在一个中间态：
        /// ID 已经变了而引用还指着旧 ID，此时若编辑器崩了或用户按了保存，
        /// 得到的就是一份满是断链、且断得毫无规律的资产。
        /// </remarks>
        private void TryRename(ConversationAsset asset, int index, string oldId, string typed)
        {
            string newId = (typed ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(newId))
            {
                EditorUtility.DisplayDialog("ID 不能为空",
                    "节点 ID 是跳转和存档的唯一依据。\n\n这个节点如果不要了，请直接删掉它。", "知道了");
                return;
            }

            if (newId.IndexOf(' ') >= 0 || newId.IndexOf('\t') >= 0)
            {
                EditorUtility.DisplayDialog("ID 不能含空白字符",
                    "ID 会被写进存档，空白字符在里面看不见，排查时也复制不出来。", "知道了");
                return;
            }

            if (!string.Equals(newId, oldId, StringComparison.Ordinal) && _knownIds.Contains(newId))
            {
                EditorUtility.DisplayDialog("ID 已被占用",
                    $"已经有另一个节点叫「{newId}」了。\n\n" +
                    "重复 ID 在运行时只会认第一个出现的那份，第二个节点的跳转全部会落到别人身上 —— " +
                    "这是最难排查的一类问题，所以这里直接拒绝。", "知道了");
                return;
            }

            var refs = _inbound != null ? _inbound.RefsTo(oldId) : null;
            int refCount = refs != null ? refs.Count : 0;

            string prompt = refCount == 0
                ? $"把「{oldId}」改名为「{newId}」？\n\n没有别的地方引用它。"
                : $"把「{oldId}」改名为「{newId}」？\n\n这 {refCount} 处引用会一并改写：\n{DescribeRefs(asset, refs)}";

            if (!EditorUtility.DisplayDialog("重命名节点 ID", prompt, "改名并同步引用", "取消")) return;

            // 先把挂起的 SerializedProperty 改动落地，否则下面 Update 会把它冲掉
            serializedObject.ApplyModifiedProperties();

            Undo.RecordObject(asset, "重命名节点 ID");

            var nodes = asset.nodes;
            if (nodes == null || index < 0 || index >= nodes.Count || nodes[index] == null) return;

            nodes[index].id = newId;
            NodeIdUtility.RewriteReferences(asset, oldId, newId);

            // 重新拉快照：上面是直接改托管对象的，SerializedObject 手里还是旧值，
            // 不 Update 的话本帧末尾的 ApplyModifiedProperties 会把旧值写回去 —— 改名静默失败
            serializedObject.Update();
            EditorUtility.SetDirty(asset);

            // 入边索引和候选表现在都是按旧 ID 建的，不改的话本帧后半段还会拿旧 ID 去查
            Invalidate();
        }

        // ── 按类型分组的字段 ────────────────────────────────────────

        private void DrawMessageFields(SerializedProperty element, ConversationAsset asset)
        {
            var message = element.FindPropertyRelative("message");

            if (message == null)
            {
                // 这里不给"一键修复"按钮：message 是内联序列化的 [Serializable] 类，
                // 没有可用来新建它的 SerializedProperty 途径（那需要 [SerializeReference]，
                // 而改序列化方式会动到存档格式）。把 message.kind 之外的字段归零也修不好它。
                // 真出现这种资产，重开一次 Unity 或重新生成节点即可
                EditorGUILayout.HelpBox(
                    "message 是 null。运行时的 MessageFactory 会返回 null —— 这个节点什么都不会发出去，" +
                    "而且不会有任何报错。建议删掉这个节点重建。", MessageType.Error);
                return;
            }

            var kindProperty = message.FindPropertyRelative("kind");
            if (kindProperty != null)
            {
                EditorGUILayout.PropertyField(kindProperty, new GUIContent("内容类型"));

                if ((MessageKind)kindProperty.intValue == MessageKind.TimeDivider)
                {
                    EditorGUILayout.HelpBox(
                        "时间分割线一般由运行时按时间间隔自动插入，不需要手写。" +
                        "只有确实要一条固定分割线时才自己配一条。", MessageType.Warning);
                }
            }

            DrawSenderId(message.FindPropertyRelative("senderId"), asset);

            var textProperty = message.FindPropertyRelative("text");
            var assetNameProperty = message.FindPropertyRelative("assetName");

            var msgKind = kindProperty != null ? (MessageKind)kindProperty.intValue : MessageKind.Text;

            if (msgKind == MessageKind.Text || msgKind == MessageKind.TimeDivider)
            {
                if (textProperty != null)
                {
                    EditorGUILayout.LabelField("文案");
                    textProperty.stringValue = EditorGUILayout.TextArea(textProperty.stringValue,
                        GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 2f));
                }
            }
            else
            {
                if (assetNameProperty != null)
                {
                    DrawAssetNameField(assetNameProperty, msgKind);
                }
            }
        }

        /// <summary>
        /// 「资源名」下拉框：候选来自 <see cref="MediaLibrary"/>，禁止手输。
        /// </summary>
        /// <remarks>
        /// 与 <c>nextId</c> 同样的理由 —— 资源名是个字符串键，手输差一个字母在面板上看不出来，
        /// 只有进 Play 才发现那个位置是个空的方块。
        /// <para>
        /// 资源库里没登记的名字<b>不会被静默重置成「（无）」</b>：那等于打开面板看一眼，
        /// 一条本来就坏的引用就被改成了"没有图"，问题从看得见变成看不见。
        /// </para>
        /// </remarks>
        private static void DrawAssetNameField(SerializedProperty assetNameProperty, MessageKind kind)
        {
            var library = MediaLibrary.Current;

            var ids = new List<string> { string.Empty };
            var labels = new List<string> { "（无）" };

            if (library != null)
            {
                var entries = library.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (string.IsNullOrEmpty(entry.name)) continue;

                    ids.Add(entry.name);
                    labels.Add(entry.sprite != null
                        ? $"{entry.name}    [{entry.sprite.name}]"
                        : $"{entry.name}    ⚠ 没指定图");
                }
            }

            var popup = PopupOptions.Build(ids, labels, assetNameProperty.stringValue);
            string picked = popup.Draw("资源名");

            if (!string.Equals(picked, assetNameProperty.stringValue, StringComparison.Ordinal))
            {
                assetNameProperty.stringValue = picked ?? string.Empty;
            }

            if (library == null)
            {
                EditorGUILayout.HelpBox(
                    "工程里没有资源库（Assets/Resources/MediaLibrary.asset），候选表是空的。\n" +
                    "用菜单 Tools / ChatSystem / 一次性 / 构建表情资源库 生成一份。",
                    MessageType.Warning);
            }
            else if (string.IsNullOrEmpty(assetNameProperty.stringValue))
            {
                EditorGUILayout.HelpBox(
                    $"{kind} 消息的资源名是空的，运行时取不到图，会发一条空气泡。",
                    MessageType.Error);
            }
            else if (library.Find(assetNameProperty.stringValue) == null)
            {
                EditorGUILayout.HelpBox(
                    $"资源库里没有「{assetNameProperty.stringValue}」这个名字，运行时取不到图。\n" +
                    "重新选一个，或者把它加进资源库。",
                    MessageType.Error);
            }
        }

        private void DrawSenderId(SerializedProperty senderId, ConversationAsset asset)
        {
            if (senderId == null) return;

            var ids = new List<string> { string.Empty };
            var labels = new List<string> { "玩家自己（空）" };

            if (asset.contact != null && !string.IsNullOrEmpty(asset.contact.id))
            {
                ids.Add(asset.contact.id);
                labels.Add($"{asset.contact.displayName}（{asset.contact.id}）");
            }

            var popup = PopupOptions.Build(ids, labels, senderId.stringValue);
            string picked = popup.Draw("发送者");

            if (!string.Equals(picked, senderId.stringValue, StringComparison.Ordinal))
            {
                senderId.stringValue = picked ?? string.Empty;
            }
        }

        private void DrawNextId(SerializedProperty element, string ownerId)
        {
            var nextId = element.FindPropertyRelative("nextId");
            if (nextId == null) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("后继节点");

            var popup = PopupOptions.Build(_targetIds, _targetLabels, nextId.stringValue);
            string picked = popup.Draw();

            if (!string.Equals(picked, nextId.stringValue, StringComparison.Ordinal))
            {
                nextId.stringValue = picked ?? string.Empty;
            }

            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(nextId.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "没有后继节点 —— 对话到这里就静默结束了。如果这是有意的，请把节点类型改成「结束」。",
                    MessageType.Error);
            }
            else if (string.Equals(nextId.stringValue, ownerId, StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox("后继指向自己。除非延迟大于 0，否则会一路循环下去。", MessageType.Warning);
            }
        }

        private void DrawDelay(SerializedProperty element, bool isWait)
        {
            var delay = element.FindPropertyRelative("delaySeconds");
            if (delay == null) return;

            EditorGUILayout.PropertyField(delay, new GUIContent(isWait ? "等待时长（秒）" : "发送延迟（秒）"));

            if (delay.floatValue < 0f)
            {
                EditorGUILayout.HelpBox("负延迟会被当成 0 处理。", MessageType.Warning);
                return;
            }

            if (isWait)
            {
                EditorGUILayout.LabelField("Wait 的时长就是确切值，不参与字数折算（它不发声）。",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            EditorGUILayout.LabelField(
                delay.floatValue <= 0f
                    ? "0 是开关：立刻发出，且不显示「正在输入」。"
                    : $"这是下限，不是最终值。运行时会按文案字数折算一个时长，两者取较大者" +
                      $"（每字 {TypingDurationPolicy.SecondsPerCharacter} 秒，" +
                      $"截断到 {TypingDurationPolicy.MinSeconds}~{TypingDurationPolicy.MaxSeconds} 秒）。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawChoiceFields(SerializedProperty element, ConversationAsset asset, string ownerId)
        {
            var nextId = element.FindPropertyRelative("nextId");
            if (nextId != null && !string.IsNullOrEmpty(nextId.stringValue))
            {
                EditorGUILayout.HelpBox(
                    $"Choice 自身的 nextId（{nextId.stringValue}）会被运行时忽略 —— " +
                    "跳转走的是各选项自己的 nextId。清掉它以免误导。", MessageType.Warning);
            }

            var options = element.FindPropertyRelative("options");
            if (options == null)
            {
                EditorGUILayout.HelpBox("options 是 null。读档时 RestoreTo 会直接空引用异常。", MessageType.Error);
                return;
            }

            int count = options.arraySize;
            EditorGUILayout.LabelField($"选项（{count} / {ConversationLimits.MaxOptions}）", EditorStyles.boldLabel);

            if (count == 0)
            {
                EditorGUILayout.HelpBox(
                    "没有选项的选择节点会让对话永久卡死：回复面板不显示，也没有任何别的出路。",
                    MessageType.Error);
            }

            for (int i = 0; i < count; i++) DrawOption(options, i, asset, ownerId);

            bool full = count >= ConversationLimits.MaxOptions;
            using (new EditorGUI.DisabledScope(full))
            {
                if (GUILayout.Button(full
                        ? $"已达上限（{ConversationLimits.MaxOptions} 个）—— 多出来的选项运行时永远不显示"
                        : "添加选项"))
                {
                    Defer(() => AddOption(options));
                }
            }
        }

        private void AddOption(SerializedProperty options)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(target, "添加选项");

            int index = options.arraySize;
            options.arraySize = index + 1;

            var element = options.GetArrayElementAtIndex(index);
            SetString(element, "text", string.Empty);
            SetString(element, "nextId", string.Empty);
            SetString(element, "timeLabel", string.Empty);
            SetLong(element, "timeValueUtc", 0L);

            serializedObject.ApplyModifiedProperties();
        }

        private void DeleteOption(SerializedProperty options, int index)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(target, "删除选项");

            int before = options.arraySize;
            options.DeleteArrayElementAtIndex(index);

            // 删"托管对象引用"时，第一次调用只是把它置空、元素还在，要再来一次才真的少一项。
            // 判据是 arraySize 变没变，不是元素内容 —— 内容为空不代表它是个待删的引用
            if (options.arraySize == before) options.DeleteArrayElementAtIndex(index);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawOption(SerializedProperty options, int index, ConversationAsset asset, string ownerId)
        {
            var option = options.GetArrayElementAtIndex(index);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"选项 {index + 1}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(44f)))
            {
                Defer(() => DeleteOption(options, index));
            }
            EditorGUILayout.EndHorizontal();

            if (option == null)
            {
                EditorGUILayout.LabelField("这一项是空的，删掉它。", ErrorStyle());
                EditorGUILayout.EndVertical();
                return;
            }

            var text = option.FindPropertyRelative("text");
            if (text != null)
            {
                EditorGUILayout.PropertyField(text, new GUIContent("按钮文案"));
                if (string.IsNullOrEmpty(text.stringValue))
                {
                    EditorGUILayout.HelpBox("文案是空的，玩家会看到一个空白按钮。", MessageType.Error);
                }
            }

            var nextId = option.FindPropertyRelative("nextId");
            if (nextId != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("跳转");

                var popup = PopupOptions.Build(_targetIds, _targetLabels, nextId.stringValue);
                string picked = popup.Draw();

                if (!string.Equals(picked, nextId.stringValue, StringComparison.Ordinal))
                {
                    nextId.stringValue = picked ?? string.Empty;
                }

                EditorGUILayout.EndHorizontal();

                if (string.IsNullOrEmpty(nextId.stringValue))
                {
                    EditorGUILayout.HelpBox("没有跳转目标 —— 玩家点了这个选项会没有反应。", MessageType.Error);
                }
            }

            DrawTime(option, asset, ownerId);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        // ── 时间配置 ────────────────────────────────────────────────

        /// <summary>
        /// 时间字段。<paramref name="holder"/> 可以是节点，也可以是某个选项。
        /// </summary>
        /// <remarks>
        /// <b>为什么不让策划直接填 Unix 秒</b>：<c>timeValueUtc</c> 唯一的用途是判断相邻两条的
        /// <b>间隔</b>是否超过 <see cref="TimeDividerPolicy.ThresholdSeconds"/> 秒，绝对日期毫无意义。
        /// 所以主输入是一个可读的时刻，旁边显示的"距上一条"才是真正在配的东西。
        /// <para>
        /// 出入都用 UTC：只用差值，所以本地时区只会带来"我填的 21:30 怎么变成了 13:30"这类
        /// 说不清的偏差。全用 UTC 就没有这个问题 —— 显示的字符串和存的值严格一一对应。
        /// </para>
        /// </remarks>
        private void DrawTime(SerializedProperty holder, ConversationAsset asset, string ownerId)
        {
            var labelProperty = holder.FindPropertyRelative("timeLabel");
            var valueProperty = holder.FindPropertyRelative("timeValueUtc");
            if (labelProperty == null || valueProperty == null) return;

            bool configured = !string.IsNullOrEmpty(labelProperty.stringValue);
            long value = valueProperty.longValue;

            EditorGUILayout.BeginHorizontal();
            bool want = EditorGUILayout.ToggleLeft("时间", configured, GUILayout.Width(52f));

            using (new EditorGUI.DisabledScope(!configured))
            {
                EditorGUILayout.LabelField("文案", GUILayout.Width(30f));
                string newLabel = EditorGUILayout.TextField(labelProperty.stringValue, GUILayout.Width(110f));
                if (!string.Equals(newLabel, labelProperty.stringValue, StringComparison.Ordinal))
                {
                    labelProperty.stringValue = newLabel;
                }

                EditorGUILayout.LabelField("时刻", GUILayout.Width(30f));
                string shown = value > 0 ? FormatUtc(value) : string.Empty;
                string newTime = EditorGUILayout.TextField(shown, GUILayout.Width(110f));

                if (!string.Equals(newTime, shown, StringComparison.Ordinal) && TryParseUtc(newTime, out long parsed))
                {
                    valueProperty.longValue = parsed;
                    value = parsed;
                }

                if (GUILayout.Button("按时刻填文案", EditorStyles.miniButton, GUILayout.Width(84f)))
                {
                    labelProperty.stringValue = SuggestLabel(value);
                }
            }

            EditorGUILayout.EndHorizontal();

            // 开关只在两个字段都清空、或都填上时切换。
            // 折中做法（只清一个）会留下校验器要报的中间态，而那正是策划最难自己看出来的情况
            if (want != configured)
            {
                if (want)
                {
                    long seed = value > 0 ? value : SuggestTimeAfter(asset, ownerId);
                    labelProperty.stringValue = SuggestLabel(seed);
                    valueProperty.longValue = seed;
                }
                else
                {
                    labelProperty.stringValue = string.Empty;
                    valueProperty.longValue = 0L;
                }
                return;
            }

            // 这一个 LabelField 必须**无条件**画。
            // 它上面的文案框和时刻框都是按键即改的，而"该说哪句话"取决于它们的新值 ——
            // 若按条件决定画不画，同一帧里 Layout 与 Repaint 的控件数量就会对不上，
            // 也就是策划刚敲下第一个字符时 Inspector 抛 "Getting control N's position..."
            EditorGUILayout.LabelField(" ", TimeNote(configured, value, PreviousTimeOf(asset, ownerId)),
                EditorStyles.miniLabel);
        }

        /// <summary>时间行的提示语。空字符串表示没什么可说的。</summary>
        private static string TimeNote(bool configured, long value, long previous)
        {
            if (!configured) return string.Empty;

            if (value <= 0) return "有文案但没时刻 —— 这行时间不会显示，也不参与分割线判断";

            if (!TryFromUnix(value, out _)) return "时刻超出可表示范围，显示不出来 —— 请重新填一个";

            if (previous <= 0) return string.Empty;

            long delta = value - previous;

            if (delta < 0)
            {
                double early = -delta / 60.0;
                return $"比上一条早 {early.ToString("0.#", CultureInfo.InvariantCulture)} 分钟（时间倒流）";
            }

            double minutes = delta / 60.0;
            string note = $"距上一条 +{minutes.ToString("0.#", CultureInfo.InvariantCulture)} 分钟";

            return delta > TimeDividerPolicy.ThresholdSeconds
                ? note + " · 会插入时间分割线"
                : note + " · 不插分割线";
        }

        /// <summary>取一个"上一条"的时间作为默认值；找不到就用此刻。</summary>
        /// <remarks>
        /// 这只是个建议值，不是规则。取的是直接指过来的节点里第一个配了时间的，
        /// 图里有环时它可能不是"真正意义上的上一条" —— 策划随时可以改。
        /// </remarks>
        private long SuggestTimeAfter(ConversationAsset asset, string ownerId)
        {
            long previous = PreviousTimeOf(asset, ownerId);
            if (previous > 0) return previous + TimeDividerPolicy.ThresholdSeconds;

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private long PreviousTimeOf(ConversationAsset asset, string ownerId)
        {
            if (_inbound == null || string.IsNullOrEmpty(ownerId) || asset == null) return 0;

            var refs = _inbound.RefsTo(ownerId);
            var nodes = asset.nodes;
            if (nodes == null) return 0;

            for (int i = 0; i < refs.Count; i++)
            {
                int from = refs[i].FromNodeIndex;
                if (from < 0 || from >= nodes.Count) continue;

                var source = nodes[from];
                if (source == null) continue;

                if (refs[i].IsDirect)
                {
                    if (source.timeValueUtc > 0) return source.timeValueUtc;
                }
                else
                {
                    var options = source.options;
                    int o = refs[i].OptionIndex;
                    if (options != null && o >= 0 && o < options.Count && options[o] != null &&
                        options[o].timeValueUtc > 0)
                    {
                        return options[o].timeValueUtc;
                    }
                }
            }

            return 0;
        }

        private static string SuggestLabel(long unixSeconds)
        {
            // 项目里没有运行时时钟，"今天/昨天"只能以策划本机的此刻为参照。
            // 它仅仅决定显示什么字，不参与任何判断
            var now = DateTimeOffset.UtcNow;
            var then = TryFromUnix(unixSeconds, out var parsed) && unixSeconds > 0 ? parsed : now;

            var span = now - then;
            if (span.TotalSeconds >= 0 && span.TotalSeconds < 60) return "刚刚";

            if (then.UtcDateTime.Date == now.UtcDateTime.Date)
            {
                return "今天 " + then.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            if (then.UtcDateTime.Date == now.UtcDateTime.Date.AddDays(-1))
            {
                return "昨天 " + then.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            return then.ToString("M月d日 HH:mm", CultureInfo.InvariantCulture);
        }

        private static string FormatUtc(long unixSeconds)
        {
            return TryFromUnix(unixSeconds, out var value)
                ? value.ToString(TimeFormat, CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static bool TryParseUtc(string text, out long unixSeconds)
        {
            unixSeconds = 0;
            if (string.IsNullOrEmpty(text)) return false;

            if (!DateTime.TryParseExact(text.Trim(), TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return false;
            }

            // 这个方向不需要判范围：DateTime 的定义域（0001-01-01 ~ 9999-12-31）
            // 换算成 Unix 秒正好是 [-62135596800, 253402300799]，
            // 也就是 FromUnixTimeSeconds 的完整定义域，转得过去就一定转得回来
            unixSeconds = new DateTimeOffset(parsed, TimeSpan.Zero).ToUnixTimeSeconds();
            return true;
        }

        /// <summary>Unix 秒的可表示范围，两端都含。</summary>
        /// <remarks>
        /// 取自 <see cref="DateTimeOffset.FromUnixTimeSeconds"/> 的定义域。
        /// 这两个魔数没有公开常量可引，只能写死 —— 但两边用的是同一对值，
        /// 不会出现"能转过去却转不回来"的不对称。
        /// </remarks>
        private const long MinUnixSeconds = -62135596800L;
        private const long MaxUnixSeconds = 253402300799L;

        /// <summary>Unix 秒 → 时间点；越界返回 <c>false</c>，绝不抛异常。</summary>
        /// <remarks>
        /// 这是画 Inspector 的路径。越界值抛出去 = 整个面板画不出来，
        /// 而那时策划正需要用它去修那个坏掉的字段 —— 越坏越修不了是最要不得的。
        /// </remarks>
        private static bool TryFromUnix(long seconds, out DateTimeOffset value)
        {
            if (seconds < MinUnixSeconds || seconds > MaxUnixSeconds)
            {
                value = default;
                return false;
            }

            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }

        // ── 推迟执行 ────────────────────────────────────────────────

        private void Defer(Action action)
        {
            if (action != null) _pending = action;
        }

        /// <summary>ID 输入框提交专用，见 <see cref="_pendingRename"/>。</summary>
        private void DeferRename(Action action)
        {
            if (action != null) _pendingRename = action;
        }

        private void RunPending()
        {
            var click = _pending;
            var rename = _pendingRename;
            _pending = null;
            _pendingRename = null;

            if (click == null && rename == null) return;

            serializedObject.ApplyModifiedProperties();   // 挂起的编辑先落地

            // 点击优先：它多半会改变数组形状（增删节点），而改名只认一个下标。
            // 顺序反过来的话，改名的下标可能已经指到别的节点上了
            if (click != null) click();                    // 动作可能直接改 asset，也可能弹对话框
            if (rename != null) rename();

            serializedObject.Update();                     // 重新拉快照，别让旧值在下一帧盖回去
            Invalidate();

            // 控件数量已经变了，本帧剩下的绘制必须作废。
            // 这是 Unity 处理"层级变动"的标准做法（EditorGUILayout 内部也这么做），
            // 不调用的话下一句就会抛 "Getting control N's position in a group with only M controls"
            GUIUtility.ExitGUI();
        }

        private void Invalidate()
        {
            _stale = true;
        }

        // ── 小工具 ──────────────────────────────────────────────────

        private static string KindLabel(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Message: return "消息";
                case NodeKind.Choice: return "选择";
                case NodeKind.Wait: return "等待";
                case NodeKind.End: return "结束";
                default: return kind.ToString();
            }
        }

        /// <summary>一行摘要，供列表行与下拉框共用。</summary>
        private static string RowSummary(DialogueNode node)
        {
            if (node == null) return "(空元素)";

            string summary;

            switch (node.kind)
            {
                case NodeKind.Message:
                {
                    var message = node.message;
                    if (message == null)
                    {
                        summary = "无 message 数据";
                        break;
                    }

                    switch (message.kind)
                    {
                        case MessageKind.Text: summary = Truncate(message.text, 16); break;
                        case MessageKind.Sticker: summary = "表情 " + (message.assetName ?? string.Empty); break;
                        case MessageKind.Image: summary = "图片 " + (message.assetName ?? string.Empty); break;
                        case MessageKind.TimeDivider: summary = "分割线 " + Truncate(message.text, 12); break;
                        default: summary = message.kind.ToString(); break;
                    }
                    break;
                }

                case NodeKind.Choice:
                {
                    var options = node.options;
                    int count = options != null ? options.Count : 0;
                    string first = count > 0 && options[0] != null ? Truncate(options[0].text, 12) : string.Empty;
                    summary = $"{count} 个选项 ·「{first}」";
                    break;
                }

                case NodeKind.Wait:
                    summary = node.delaySeconds.ToString("0.##", CultureInfo.InvariantCulture) + "s";
                    break;

                case NodeKind.End:
                    summary = "对话结束";
                    break;

                default:
                    summary = node.kind.ToString();
                    break;
            }

            if (node.kind == NodeKind.Message || node.kind == NodeKind.Wait)
            {
                summary += string.IsNullOrEmpty(node.nextId) ? "  →（无后继）" : "  → " + node.nextId;
            }

            if (node.HasTime) summary += "  " + node.timeLabel;

            return summary;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "（空文案）";
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        private static GUIStyle ErrorStyle()
        {
            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 0.45f, 0.45f)
                : new Color(0.65f, 0.1f, 0.1f);
            return style;
        }

        private static GUIStyle WarnStyle()
        {
            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 0.78f, 0.35f)
                : new Color(0.55f, 0.35f, 0f);
            return style;
        }

        private static void SetString(SerializedProperty parent, string name, string value)
        {
            var property = parent != null ? parent.FindPropertyRelative(name) : null;
            if (property != null) property.stringValue = value ?? string.Empty;
        }

        private static void SetInt(SerializedProperty parent, string name, int value)
        {
            var property = parent != null ? parent.FindPropertyRelative(name) : null;
            if (property != null) property.intValue = value;
        }

        private static void SetFloat(SerializedProperty parent, string name, float value)
        {
            var property = parent != null ? parent.FindPropertyRelative(name) : null;
            if (property != null) property.floatValue = value;
        }

        private static void SetLong(SerializedProperty parent, string name, long value)
        {
            var property = parent != null ? parent.FindPropertyRelative(name) : null;
            if (property != null) property.longValue = value;
        }

        // ── 下拉框：当前值不在候选表里时不能静默改写 ────────────────

        /// <summary>
        /// 一个下拉框的候选表，以及"当前值不在表里"时的处置。
        /// </summary>
        /// <remarks>
        /// <b>为什么不能直接用 <c>EditorGUILayout.Popup</c></b>：当前值不在候选表里时，
        /// Popup 会把下标夹到 0，也就是"(无)"。于是策划只是打开 Inspector 看一眼，
        /// 一条断链就被悄悄改成了"到此结束"，校验器再也报不出来。
        /// 这里的做法是把丢失的值原样追加成末项，选中它时不改任何东西。
        /// </remarks>
        private sealed class PopupOptions
        {
            private string[] _ids;
            private string[] _labels;
            private string _current;
            private int _selected;
            private int _lostIndex = -1;

            public static PopupOptions Build(IList<string> ids, IList<string> labels, string current)
            {
                var options = new PopupOptions { _current = current };

                int found = IndexOf(ids, current);

                if (found >= 0 || string.IsNullOrEmpty(current))
                {
                    options._ids = ToArray(ids);
                    options._labels = ToArray(labels);
                    options._selected = found < 0 ? 0 : found;
                    return options;
                }

                // 当前值不在表里：原样追加，标红，且默认选中它
                options._ids = new string[ids.Count + 1];
                options._labels = new string[labels.Count + 1];

                for (int i = 0; i < ids.Count; i++)
                {
                    options._ids[i] = ids[i];
                    options._labels[i] = labels[i];
                }

                options._ids[ids.Count] = current;
                options._labels[ids.Count] = $"<丢失: {current}>";
                options._lostIndex = ids.Count;
                options._selected = ids.Count;

                return options;
            }

            /// <summary>带前缀标签的版本。</summary>
            public string Draw(string label)
            {
                return DrawInternal(label);
            }

            /// <summary>已经自行画好前缀时用这个。</summary>
            public string Draw()
            {
                return DrawInternal(null);
            }

            private string DrawInternal(string label)
            {
                int picked = label == null
                    ? EditorGUILayout.Popup(_selected, _labels)
                    : EditorGUILayout.Popup(label, _selected, _labels);

                if (picked == _selected) return _current;
                if (picked == _lostIndex) return _current;
                if (picked < 0 || picked >= _ids.Length) return _current;

                return _ids[picked];
            }

            private static int IndexOf(IList<string> ids, string value)
            {
                if (ids == null) return -1;

                for (int i = 0; i < ids.Count; i++)
                {
                    if (string.Equals(ids[i], value, StringComparison.Ordinal)) return i;
                }

                return -1;
            }

            private static string[] ToArray(IList<string> source)
            {
                var array = new string[source.Count];
                for (int i = 0; i < source.Count; i++) array[i] = source[i];
                return array;
            }
        }
    }
}
