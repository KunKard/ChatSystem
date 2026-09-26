using System.Collections.Generic;
using ChatSystem.Data;
using UnityEditor;
using UnityEngine;

namespace ChatSystem.EditorTools
{
    /// <summary>
    /// 「从该节点预览」的状态机：临时改写 <c>entryNodeId</c>，退出 Play 后还原。
    /// </summary>
    /// <remarks>
    /// <b>备份为什么用 EditorPrefs 而不是静态字段或 SessionState</b>：进入 Play 会触发域重载，
    /// 静态字段一律清零；<c>SessionState</c> 扛得住域重载，却扛不住编辑器崩溃 ——
    /// 而崩溃恰恰是唯一会让"预览用的入口"永久留在资产里的情形。只有 <c>EditorPrefs</c> 两者都扛得住。
    /// <para>
    /// <b>键按资产 GUID 存</b>，不是按资产名：GUID 全局唯一，重命名资产不会张冠李戴。
    /// </para>
    /// <para>
    /// ⚠ <b>SaveService 落地之后这里会失效，而且是静默失效。</b>
    /// 读档走的是 <c>ChatSession.Restore</c> → <c>Runner.RestoreTo(currentNodeId)</c>，
    /// 它直接把游标放到存档记录的节点上，<b>根本不读 entryNodeId</b> ——
    /// 预览会被存档覆盖，而报错信息里不会出现任何相关字样。届时预览必须同时把该联系人的
    /// 存档条目挪开并在退出时放回。<c>SaveService</c> 的任务单上要有一条。
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    public static class DialoguePreviewState
    {
        private const string BackupPrefix = "ChatSystem.Preview.Backup.";
        private const string TargetPrefix = "ChatSystem.Preview.Target.";

        /// <summary>正处于预览中的资产 GUID 列表（逗号分隔）。</summary>
        /// <remarks>
        /// <c>EditorPrefs</c> 没有遍历键的 API，所以崩溃后想找回"哪些资产被改过"，
        /// 就必须自己维护一份名册。字段很少，拼字符串足够。
        /// </remarks>
        private const string ActiveKey = "ChatSystem.Preview.Active";

        static DialoguePreviewState()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // 兜崩溃：编辑器重启后，资产可能正停在某个预览用的入口上。
            // 延迟到第一次 update 再扫，此时 AssetDatabase 才是可用的
            EditorApplication.delayCall += RestoreOrphans;
        }

        /// <summary>该资产当前是否处于预览中。</summary>
        public static bool IsPreviewing(ConversationAsset asset)
        {
            string guid = GuidOf(asset);
            return !string.IsNullOrEmpty(guid) && EditorPrefs.HasKey(BackupPrefix + guid);
        }

        /// <summary>预览前的入口 ID；不在预览中时返回 <c>null</c>。</summary>
        public static string OriginalEntryOf(ConversationAsset asset)
        {
            string guid = GuidOf(asset);
            return string.IsNullOrEmpty(guid) ? null : EditorPrefs.GetString(BackupPrefix + guid, null);
        }

        /// <summary>开始预览：把入口临时改写到指定节点。</summary>
        public static void Begin(ConversationAsset asset, string previewNodeId)
        {
            if (asset == null || string.IsNullOrEmpty(previewNodeId)) return;

            // Play 中改入口没有意义：Start 早就跑过了。而且会把备份里的"原入口"写脏
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            string guid = GuidOf(asset);
            if (string.IsNullOrEmpty(guid)) return;

            // 已在预览中就只更新目标，**保留最初的原入口** ——
            // 连着预览第二个节点时若覆盖了备份，退出后就再也回不到真正的原入口了
            if (!EditorPrefs.HasKey(BackupPrefix + guid))
            {
                EditorPrefs.SetString(BackupPrefix + guid, asset.entryNodeId ?? string.Empty);
                AddToActive(guid);
            }

            EditorPrefs.SetString(TargetPrefix + guid, previewNodeId);
            WriteEntry(asset, previewNodeId);

            Debug.Log($"[ChatSystem] 「{asset.name}」进入预览：入口临时改为「{previewNodeId}」，" +
                      $"原入口「{EditorPrefs.GetString(BackupPrefix + guid, string.Empty)}」。退出 Play 后自动恢复。");
        }

        /// <summary>结束预览并还原入口。</summary>
        public static void Cancel(ConversationAsset asset)
        {
            Restore(asset, announce: true);
        }

        // ── 生命周期 ────────────────────────────────────────────────

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode) RestoreOrphans();
        }

        /// <summary>
        /// 还原所有处于预览中的资产。
        /// </summary>
        /// <remarks>
        /// <b>必须在 Play 期间被挡住。</b>进入 Play 会触发域重载，于是
        /// <c>[InitializeOnLoad]</c> 的静态构造函数会再跑一遍；若此时不判断状态就还原，
        /// 预览会在开始的那一瞬间被撤销掉，表现为"点了预览但没有任何效果"。
        /// </remarks>
        private static void RestoreOrphans()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var active = ActiveGuids();
            if (active.Count == 0) return;

            for (int i = 0; i < active.Count; i++)
            {
                var asset = LoadByGuid(active[i]);
                if (asset == null)
                {
                    // 资产被删了（或改名后 GUID 对不上）：清掉遗留的键，别让它一直挂在名册里
                    Forget(active[i]);
                    continue;
                }

                Restore(asset, announce: true);
            }
        }

        private static void Restore(ConversationAsset asset, bool announce)
        {
            if (asset == null) return;

            string guid = GuidOf(asset);
            if (string.IsNullOrEmpty(guid) || !EditorPrefs.HasKey(BackupPrefix + guid)) return;

            string backup = EditorPrefs.GetString(BackupPrefix + guid, string.Empty);
            string target = EditorPrefs.GetString(TargetPrefix + guid, string.Empty);
            Forget(guid);

            // 当前值既不是预览值，说明有人在预览期间手工改过入口。
            // **人的改动优先** —— 把他改的东西覆盖掉，比留下一个没还原的预览更让人恼火
            if (asset.entryNodeId != target)
            {
                Debug.LogWarning(
                    $"[ChatSystem] 「{asset.name}」的入口已是「{asset.entryNodeId}」，" +
                    $"既不是预览值「{target}」，也不是预览前的「{backup}」—— 判定为手工修改，不覆盖。" +
                    "若那其实是预览残留，请手动改回。");
                return;
            }

            WriteEntry(asset, backup);

            // 这一步只在"预览值已经落到磁盘上"时才真正起作用。落到磁盘的路径很现实：
            // 策划在 Play 期间按了 Ctrl+S。只改内存的话，编辑器一关又变回预览值
            AssetDatabase.SaveAssets();

            if (announce)
            {
                Debug.Log($"[ChatSystem] 「{asset.name}」预览结束，入口已还原为「{backup}」。");
            }
        }

        // ── 工具 ────────────────────────────────────────────────────

        private static void WriteEntry(ConversationAsset asset, string nodeId)
        {
            var serialized = new SerializedObject(asset);
            var property = serialized.FindProperty("entryNodeId");
            if (property == null) return;

            property.stringValue = nodeId ?? string.Empty;

            // 不记 Undo：预览的状态机不该能通过 Ctrl+Z 重新进入。
            // 在 Play 中按下撤销会把入口改回预览值，而名册那边已经清干净了 —— 那个值就再也回不去
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 也**不**在这里 SaveAssets：预览值不该落盘。
            // 唯一的例外在 Restore 里，那一步是补救已经落盘的情况
            EditorUtility.SetDirty(asset);
        }

        private static string GuidOf(ConversationAsset asset)
        {
            if (asset == null) return null;

            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
        }

        private static ConversationAsset LoadByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<ConversationAsset>(path);
        }

        private static void Forget(string guid)
        {
            EditorPrefs.DeleteKey(BackupPrefix + guid);
            EditorPrefs.DeleteKey(TargetPrefix + guid);
            RemoveFromActive(guid);
        }

        private static List<string> ActiveGuids()
        {
            var result = new List<string>();
            string raw = EditorPrefs.GetString(ActiveKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return result;

            var parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string guid = parts[i].Trim();
                if (guid.Length > 0) result.Add(guid);
            }

            return result;
        }

        private static void AddToActive(string guid)
        {
            var active = ActiveGuids();
            if (active.Contains(guid)) return;

            active.Add(guid);
            EditorPrefs.SetString(ActiveKey, string.Join(",", active.ToArray()));
        }

        private static void RemoveFromActive(string guid)
        {
            var active = ActiveGuids();
            if (!active.Remove(guid)) return;

            EditorPrefs.SetString(ActiveKey, string.Join(",", active.ToArray()));
        }
    }
}
