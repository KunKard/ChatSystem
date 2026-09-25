using System.Collections.Generic;
using System.Text;
using ChatSystem.Data;
using ChatSystem.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChatSystem.EditorTools
{
    /// <summary>
    /// 检查并修复"玩家头像"这条引用链，并把结果打成一份可核对的清单。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要这个工具</b>：玩家身份（<c>Contact_Player.asset</c>）与
    /// <c>ChatWindowView.playerProfile</c> 之间的引用是<b>场景里的一个 GUID 指针</b>。
    /// 用文本编辑器改场景文件是修不好它的 —— 场景如果正开在 Unity 里，
    /// 内存中的那份仍是旧值，不会因为磁盘变了就自动重载；而一旦在 Unity 里保存场景，
    /// 内存版本还会反过来把磁盘上的修改覆盖掉。表现为：编译通过、不报任何错、
    /// 名字显示正常（脚本里 <c>playerDisplayName</c> 的默认值兜住了），只有头像不出现。
    /// <para>
    /// 因此这条引用必须由 Unity 自己写 —— 这个菜单就是干这件事的：写入 → 校验 → 保存场景。
    /// </para>
    /// <para>
    /// 可重复运行：已经是正确值时就只做校验，不做任何改动。
    /// </para>
    /// </remarks>
    public static class AvatarWirer
    {
        private const string MenuPath = "Tools/ChatSystem/检查并修复头像接线";

        [MenuItem(MenuPath)]
        public static void Wire()
        {
            var window = FindWindow();
            if (window == null)
            {
                Debug.LogError(
                    "[AvatarWirer] 当前场景里找不到 ChatWindowView。" +
                    "请先打开 SampleScene，或运行 工具 / ChatSystem / 接线 Day 2 场景。");
                return;
            }

            var player = FindPlayerProfile();
            if (player == null)
            {
                Debug.LogError(
                    "[AvatarWirer] 找不到 id 为 \"player\" 的 ContactProfile 资产。" +
                    "它应当位于 Assets/GameData/Contacts/Contact_Player.asset。" +
                    "没有它，玩家气泡就不会有头像。");
                return;
            }

            var serialized = new SerializedObject(window);
            var field = serialized.FindProperty("playerProfile");
            var nameField = serialized.FindProperty("playerDisplayName");

            if (field == null)
            {
                Debug.LogError("[AvatarWirer] ChatWindowView 上没有 playerProfile 字段，脚本与预制体版本不匹配。");
                return;
            }

            bool changed = !ReferenceEquals(field.objectReferenceValue, player);
            field.objectReferenceValue = player;

            // 名字只在为空时补，不覆盖策划自己填的
            if (nameField != null && string.IsNullOrEmpty(nameField.stringValue))
            {
                nameField.stringValue = string.IsNullOrEmpty(player.displayName) ? "我" : player.displayName;
                changed = true;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (changed)
            {
                EditorUtility.SetDirty(window);
                EditorSceneManager.MarkSceneDirty(window.gameObject.scene);
                EditorSceneManager.SaveOpenScenes();
            }

            Report(window, player, changed);
        }

        /// <summary>把整条链路逐环打出来，哪一环空了一眼就能看见。</summary>
        private static void Report(ChatWindowView window, ContactProfile player, bool changed)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[AvatarWirer] 玩家资料已写入 playerProfile{(changed ? "（场景已保存）" : "（原本就是它）")}");
            sb.AppendLine($"  玩家资料资产：{AssetDatabase.GetAssetPath(player)}");
            sb.AppendLine($"  {Describe("玩家头像", player)}");
            sb.AppendLine($"  名字：\"{player.displayName}\"");
            sb.AppendLine();

            // 联系人那头一起查：两侧用的是同一套 Bind 逻辑，
            // 只有一侧空说明是那条引用的问题，两侧都空说明是贴图导入的问题
            var seen = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:ConversationAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ConversationAsset>(path);
                if (asset?.contact == null) continue;
                if (!seen.Add(asset.contact.id)) continue;

                sb.AppendLine($"  联系人「{asset.contact.displayName}」({asset.contact.id})：{Describe("头像", asset.contact)}");
            }

            sb.AppendLine();
            sb.AppendLine("  若上面每一行都显示了尺寸，说明贴图与引用都没问题，Play 即可看到头像。");

            Debug.Log(sb.ToString(), window);
        }

        private static string Describe(string label, ContactProfile profile)
        {
            var sprite = profile.avatar;
            if (sprite == null) return $"{label} = 空 ❌（ContactProfile.avatar 没有指到贴图）";

            var path = AssetDatabase.GetAssetPath(sprite);
            var rect = sprite.rect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return $"{label} = {path} ⚠️ 尺寸为 {rect.width}×{rect.height}，贴图没有正常解码";
            }

            return $"{label} = {path}（{rect.width:0}×{rect.height:0}）";
        }

        private static ChatWindowView FindWindow()
        {
            // 带上未激活的：脚本可能挂在被临时关掉的节点上
            var found = Object.FindObjectsOfType<ChatWindowView>(true);
            if (found.Length == 0) return null;
            if (found.Length > 1)
            {
                Debug.LogWarning(
                    $"[AvatarWirer] 场景里有 {found.Length} 个 ChatWindowView，只处理第一个。" +
                    "这通常意味着场景被复制过，请手工清掉多余的那个。", found[1]);
            }

            return found[0];
        }

        /// <summary>
        /// 按 <c>id</c> 找玩家资料，而不是按文件名 —— 文件名可以随便改，id 是契约。
        /// </summary>
        /// <remarks>公开给 <see cref="SceneWirer"/> 复用，保证两条接线路径取的是同一个资产。</remarks>
        public static ContactProfile FindPlayerProfile()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ContactProfile"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<ContactProfile>(path);
                if (profile != null && profile.id == "player") return profile;
            }

            return null;
        }
    }
}
