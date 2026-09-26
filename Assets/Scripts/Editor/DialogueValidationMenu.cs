using System;
using System.Collections.Generic;
using System.Text;
using ChatSystem.Data;
using UnityEditor;
using UnityEngine;

namespace ChatSystem.EditorTools
{
    /// <summary>
    /// 批量校验工程里全部对话资产。
    /// </summary>
    /// <remarks>
    /// 这是 <c>DialogueRunner</c> 报错时让用户去点的那两个菜单项之一 —— 在那之前，
    /// 运行时日志指的 <c>ChatSystem/DialogueValidator</c> 这个菜单<b>根本不存在</b>。
    /// 提示指向一个点不到的地方，比没有提示更糟：它会让人以为是自己找错了。
    /// <para>
    /// 内联报告（<see cref="DialogueEditor"/>）适合改一个资产时看；
    /// 这个是"改完一批，确认没弄坏别的"时用。
    /// </para>
    /// </remarks>
    internal static class DialogueValidationMenu
    {
        /// <summary>每个资产最多列几条问题，避免一次刷屏几百行。</summary>
        private const int MaxLinesPerAsset = 20;

        [MenuItem("Tools/ChatSystem/校验全部对话资产")]
        private static void ValidateAll()
        {
            var paths = CollectAssetPaths();
            if (paths.Count == 0)
            {
                Debug.Log("[ChatSystem] 工程里没有 ConversationAsset 资产。");
                return;
            }

            var report = new StringBuilder();
            int totalErrors = 0;
            int totalWarnings = 0;
            int cleanAssets = 0;

            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                var asset = AssetDatabase.LoadAssetAtPath<ConversationAsset>(path);

                if (asset == null) continue;

                var issues = DialogueValidator.Validate(asset);

                int errors = 0;
                int warnings = 0;
                for (int j = 0; j < issues.Count; j++)
                {
                    if (issues[j].Severity == IssueSeverity.Error) errors++;
                    else warnings++;
                }

                totalErrors += errors;
                totalWarnings += warnings;

                if (issues.Count == 0)
                {
                    cleanAssets++;
                    continue;
                }

                report.Append(path)
                      .Append("  （")
                      .Append(asset.NodeCount).Append(" 个节点，")
                      .Append(errors).Append(" 错误 / ")
                      .Append(warnings).Append(" 警告）")
                      .Append('\n');

                int shown = Mathf.Min(issues.Count, MaxLinesPerAsset);
                for (int j = 0; j < shown; j++)
                {
                    report.Append("    ").Append(issues[j]).Append('\n');
                }

                if (issues.Count > shown)
                {
                    report.Append("    ……还有 ").Append(issues.Count - shown).Append(" 条\n");
                }

                report.Append('\n');
            }

            string head =
                $"[ChatSystem] 校验了 {paths.Count} 个对话资产：" +
                $"{cleanAssets} 个没有问题，{totalErrors} 个错误、{totalWarnings} 个警告。\n";

            string text = head + report;

            // 用不同的日志级别，好让 Console 的级别过滤直接把出问题的筛出来
            if (totalErrors > 0) Debug.LogError(text);
            else if (totalWarnings > 0) Debug.LogWarning(text);
            else Debug.Log(text);
        }

        /// <summary>收集全部对话资产的路径，按路径排序。</summary>
        /// <remarks>
        /// <c>FindAssets</c> 的顺序跟随文件系统，两次运行未必一致。
        /// 排一下序，好让两次校验的结果能直接对照着看。
        /// </remarks>
        private static List<string> CollectAssetPaths()
        {
            string[] guids = AssetDatabase.FindAssets("t:ConversationAsset");
            var paths = new List<string>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);
            return paths;
        }
    }
}
