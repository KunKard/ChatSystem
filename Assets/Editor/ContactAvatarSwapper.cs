using System.Collections.Generic;
using System.Text;
using ChatSystem.Data;
using ChatSystem.Data.Model;
using ChatSystem.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChatSystem.EditorTools.Migrations
{
    /// <summary>
    /// 一次性迁移：把联系人的头像换成 <c>ArtRes</c> 下的图，id / 显示名 / 资产文件名一并对齐。
    /// </summary>
    /// <remarks>
    /// <b>为什么走编辑器脚本而不是直接改 .asset 文件</b>：Unity 开着的时候，磁盘上的资产和它内存里
    /// 的副本是两份。外部改写要赌刷新时机 —— 赌输了改动会被内存副本覆盖回去，而且不留痕迹。
    /// 走 <c>AssetDatabase</c> 则始终只有一份，且资产改名是 GUID 安全的。
    /// <para>
    /// <b>顺带要改 <c>senderId</c></b>：气泡按 id 字符串认说话人，id 改了而它没改，
    /// 头像和左右分栏都会塌掉 —— 而且是静默的，只有画面不对，没有任何报错。
    /// </para>
    /// <para>
    /// 放在 <c>Assets/Editor/</c> 而非 <c>Assets/Scripts/Editor/</c>：这是一次性装配脚手架，
    /// 跑完就没用了，和 <c>SceneWirer</c> / <c>Day1TestDataGenerator</c> 同类，不是产品的一部分。
    /// </para>
    /// </remarks>
    internal static class ContactAvatarSwapper
    {
        private const string ArtFolder = "Assets/ArtRes";
        private const string ContactFolder = "Assets/GameData/Contacts";
        private const string ConversationFolder = "Assets/GameData/Conversations";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        /// <summary>头像贴图的导入参数，见 <see cref="EnsureSpriteImports"/>。</summary>
        private const int AvatarMaxSize = 256;

        /// <summary>一次替换。</summary>
        private sealed class Swap
        {
            /// <summary>现有联系人的 id；<c>null</c> 表示这个联系人是新建的。</summary>
            public string OldId;

            /// <summary>新的 id 与显示名，同时也是资产文件的新名字。</summary>
            public string NewId;

            /// <summary><c>ArtRes</c> 下的图片文件名。</summary>
            public string Image;
        }

        /// <summary>
        /// 替换表。<b>顺序即左侧联系人列表的展示顺序</b>（沿用各自对话资产在场景里的位置）。
        /// </summary>
        /// <remarks>
        /// 图片和人的对应是任意指定的 —— 换谁配谁只影响观感，不影响任何逻辑。
        /// 想调换直接在 Inspector 里拖另一个 Sprite 即可。
        /// </remarks>
        private static readonly Swap[] Swaps =
        {
            new Swap { OldId = "asta",    NewId = "cosB",   Image = "cosB.jpg"   },
            new Swap { OldId = "danheng", NewId = "hzz",    Image = "hzz.jpg"    },
            new Swap { OldId = "himeko",  NewId = "kk",     Image = "kk.jpg"     },
            new Swap { OldId = "march7",  NewId = "sister", Image = "sister.jpg" },
            new Swap { OldId = "robin",   NewId = "冰冻",   Image = "冰冻.jpg"   },
            new Swap { OldId = "welt",    NewId = "江毅",   Image = "江毅.jpg"   },
            new Swap { OldId = "Ayang",   NewId = "Ayang",  Image = "洋.jpg"     },
            new Swap { OldId = null,      NewId = "烁",     Image = "烁.jpg"     },
        };

        /// <summary>玩家头像。显示名保持"我"—— 自己的消息上不该出现自己的名字。</summary>
        private const string PlayerId = "player";
        private const string PlayerImage = "Kard.jpg";

        [MenuItem("Tools/ChatSystem/一次性/套用 ArtRes 头像与改名")]
        private static void Apply()
        {
            var preview = new StringBuilder();
            preview.Append("将执行：\n\n");

            for (int i = 0; i < Swaps.Length; i++)
            {
                preview.Append("  ")
                       .Append(Swaps[i].OldId ?? "(新建)")
                       .Append("  →  ")
                       .Append(Swaps[i].NewId)
                       .Append("   [")
                       .Append(Swaps[i].Image)
                       .Append("]\n");
            }

            preview.Append("\n  ").Append(PlayerId).Append("  →  头像换成 ").Append(PlayerImage)
                   .Append("（显示名不变）\n\n")
                   .Append("同时会改写全部对话里的 senderId、更新场景的联系人列表，\n")
                   .Append("并把资产文件重命名成新的 id。此操作可用 Ctrl+Z 部分撤销，但建议先提交一次。");

            if (!EditorUtility.DisplayDialog("套用 ArtRes 头像与改名", preview.ToString(), "执行", "取消"))
            {
                return;
            }

            // Play 模式下打不开场景，跑下去就是改一半：资产换了名、场景里的列表没跟上。
            // 半成品状态比不改更糟 —— 它看起来像成功了
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("套用 ArtRes 头像与改名",
                    "请先退出 Play 模式再执行。\n\n" +
                    "这个操作要打开场景改联系人列表，Play 模式下做不到。", "知道了");
                return;
            }

            // 先把图验完再动任何资产。缺一张就整体中止 ——
            // 部分成功会留下一份"哪些换了哪些没换"要靠人回忆的工程
            if (!EnsureSpriteImports())
            {
                EditorUtility.DisplayDialog("套用 ArtRes 头像与改名",
                    "有头像没能导入，已中止。工程未做任何改动。\n\n详见 Console。", "知道了");
                return;
            }

            var contacts = LoadAll<ContactProfile>(ContactFolder);
            var conversations = LoadAll<ConversationAsset>(ConversationFolder);

            var contactById = new Dictionary<string, ContactProfile>();
            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                if (c != null && !string.IsNullOrEmpty(c.id)) contactById[c.id] = c;
            }

            var conversationByContactId = new Dictionary<string, ConversationAsset>();
            for (int i = 0; i < conversations.Count; i++)
            {
                var conv = conversations[i];
                var owner = conv != null ? conv.contact : null;
                if (owner != null && !string.IsNullOrEmpty(owner.id))
                {
                    conversationByContactId[owner.id] = conv;
                }
            }

            var created = new List<ConversationAsset>();
            var renames = new List<KeyValuePair<Object, string>>();
            var log = new StringBuilder();
            int renamedContacts = 0;
            int rewrittenMessages = 0;

            for (int i = 0; i < Swaps.Length; i++)
            {
                var swap = Swaps[i];
                var sprite = LoadSprite(swap.Image);
                if (sprite == null)
                {
                    Debug.LogError($"[ChatSystem] 找不到头像 \"{swap.Image}\"，本条跳过。");
                    continue;
                }

                ContactProfile contact;
                ConversationAsset conversation;

                if (swap.OldId == null)
                {
                    // 新建：联系人 + 一个空对话。没有对话资产的联系人不会出现在列表里 ——
                    // 列表是照着 ChatAppController.conversations 建的，不是扫联系人目录建
                    contact = ScriptableObject.CreateInstance<ContactProfile>();
                    contact.id = swap.NewId;
                    contact.displayName = swap.NewId;
                    contact.avatar = sprite;
                    contact.signature = string.Empty;
                    contact.defaultPreview = string.Empty;
                    AssetDatabase.CreateAsset(contact, $"{ContactFolder}/Contact_{swap.NewId}.asset");

                    conversation = ScriptableObject.CreateInstance<ConversationAsset>();
                    conversation.contact = contact;
                    conversation.entryNodeId = string.Empty;
                    conversation.nodes = new List<DialogueNode>();
                    AssetDatabase.CreateAsset(conversation,
                        $"{ConversationFolder}/Conv_{swap.NewId}.asset");

                    created.Add(conversation);
                    log.Append("  新建 ").Append(swap.NewId).Append("（空对话，等内容导入）\n");
                    continue;
                }

                if (!contactById.TryGetValue(swap.OldId, out contact) || contact == null)
                {
                    Debug.LogError($"[ChatSystem] 找不到 id 为 \"{swap.OldId}\" 的联系人，本条跳过。");
                    continue;
                }

                string oldName = contact.displayName;

                contact.id = swap.NewId;
                contact.displayName = swap.NewId;
                contact.avatar = sprite;
                EditorUtility.SetDirty(contact);

                conversationByContactId.TryGetValue(swap.OldId, out conversation);
                if (conversation != null)
                {
                    // 重新指一遍。正常情况下本来就是它，这一步是幂等的；
                    // 但联系人资产跟着改了名，趁这里把引用钉死，省得日后有人只改了一半
                    conversation.contact = contact;

                    int n = RewriteSenderId(conversation, swap.OldId, swap.NewId);
                    rewrittenMessages += n;
                    EditorUtility.SetDirty(conversation);

                    log.Append("  ").Append(swap.OldId).Append(" → ").Append(swap.NewId)
                       .Append("   改写 ").Append(n).Append(" 条 senderId\n");
                }
                else
                {
                    Debug.LogWarning($"[ChatSystem] \"{swap.OldId}\" 没有对应的对话资产，" +
                                     $"它不会出现在联系人列表里。");
                    log.Append("  ").Append(swap.OldId).Append(" → ").Append(swap.NewId)
                       .Append("   ⚠ 无对话资产\n");
                }

                if (!string.Equals(oldName, swap.NewId, System.StringComparison.Ordinal))
                {
                    renamedContacts++;
                }

                renames.Add(new KeyValuePair<Object, string>(contact, $"Contact_{swap.NewId}"));
                if (conversation != null)
                {
                    renames.Add(new KeyValuePair<Object, string>(conversation, $"Conv_{swap.NewId}"));
                }
            }

            // ── 玩家头像。显示名保持"我"：自己的消息上不该出现自己的名字 ──
            if (contactById.TryGetValue(PlayerId, out var player) && player != null)
            {
                var sprite = LoadSprite(PlayerImage);
                if (sprite != null)
                {
                    player.avatar = sprite;
                    EditorUtility.SetDirty(player);
                    log.Append("  player → 头像换成 ").Append(PlayerImage).Append("（显示名不变）\n");
                }
                else
                {
                    Debug.LogError($"[ChatSystem] 找不到玩家头像 \"{PlayerImage}\"。");
                }
            }
            else
            {
                Debug.LogWarning($"[ChatSystem] 找不到 id 为 \"{PlayerId}\" 的联系人，玩家头像未更换。");
            }

            AssetDatabase.SaveAssets();

            // 改名放在最后：前面都是按 id 查对象，改名会动路径，先做完再动
            ApplyRenames(renames);

            UpdateScene(created, log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ChatSystem] 套用头像与改名完成：{Swaps.Length} 条替换、" +
                      $"{renamedContacts} 个显示名变化、{rewrittenMessages} 条消息的 senderId 被改写。\n" +
                      log);
        }

        /// <summary>
        /// 头像贴图按 <see cref="AvatarMaxSize"/> 收紧导入参数。
        /// </summary>
        /// <remarks>
        /// 默认导入是 2048 + 开 mipmap。头像在界面上只有几十像素，2048 是纯浪费显存，
        /// mipmap 更是 UI 完全用不到还白占 33% 的东西。940px 的源图按 256 进包，够用且省。
        /// </remarks>
        /// <returns>每一张都取到了 Sprite 才返回 <c>true</c>。</returns>
        private static bool EnsureSpriteImports()
        {
            var names = new List<string> { PlayerImage };
            for (int i = 0; i < Swaps.Length; i++) names.Add(Swaps[i].Image);

            bool allOk = true;

            for (int i = 0; i < names.Count; i++)
            {
                string path = $"{ArtFolder}/{names[i]}";

                // 刚拷进来的图 Unity 可能还没导过，此时取到的是 null ——
                // 表现成"图明明在，脚本却说找不到"。先同步补一次导入
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
                {
                    if (!System.IO.File.Exists(path))
                    {
                        Debug.LogError($"[ChatSystem] 磁盘上没有 \"{path}\"。");
                        allOk = false;
                        continue;
                    }
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogError($"[ChatSystem] \"{path}\" 不是可导入为贴图的文件。");
                    allOk = false;
                    continue;
                }

                bool dirty = false;

                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    dirty = true;
                }
                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    dirty = true;
                }
                if (importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    dirty = true;
                }
                if (importer.maxTextureSize > AvatarMaxSize)
                {
                    importer.maxTextureSize = AvatarMaxSize;
                    dirty = true;
                }
                if (!importer.alphaIsTransparency)
                {
                    // JPEG 没有 alpha 通道，这条对它没有实际作用；
                    // 但换成 PNG 头像（圆角需要 alpha）时就是必须的，先设上免得再踩
                    importer.alphaIsTransparency = true;
                    dirty = true;
                }

                if (dirty) importer.SaveAndReimport();

                // 真正要的是 Sprite，不是 Texture2D —— Texture Type 没设成 Sprite 时
                // 贴图在、Sprite 不在，脚本会拿着 null 一路走下去
                if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
                {
                    Debug.LogError($"[ChatSystem] \"{path}\" 导入后取不到 Sprite，" +
                                   $"把它的 Texture Type 设成 Sprite (2D and UI) 再试。");
                    allOk = false;
                }
            }

            return allOk;
        }

        /// <summary>把对话里所有指向 <paramref name="oldId"/> 的 <c>senderId</c> 改成新 id。</summary>
        /// <returns>改写的消息条数。</returns>
        private static int RewriteSenderId(ConversationAsset conversation, string oldId, string newId)
        {
            var nodes = conversation.nodes;
            if (nodes == null) return 0;

            int changed = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var message = nodes[i] != null ? nodes[i].message : null;
                if (message == null) continue;

                // 空字符串 = 玩家自己（Design.md §4.1），不参与替换
                if (!string.Equals(message.senderId, oldId, System.StringComparison.Ordinal)) continue;

                message.senderId = newId;
                changed++;
            }
            return changed;
        }

        /// <summary>用 <c>AssetDatabase.MoveAsset</c> 改名，GUID 不变，所以引用不会断。</summary>
        private static void ApplyRenames(List<KeyValuePair<Object, string>> renames)
        {
            var moved = new HashSet<string>();

            for (int i = 0; i < renames.Count; i++)
            {
                var asset = renames[i].Key;
                string newName = renames[i].Value;
                if (asset == null) continue;

                string oldPath = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(oldPath)) continue;
                if (!moved.Add(oldPath)) continue;   // 同一个资产只搬一次

                string folder = System.IO.Path.GetDirectoryName(oldPath).Replace('\\', '/');
                string newPath = $"{folder}/{newName}.asset";

                if (string.Equals(oldPath, newPath, System.StringComparison.Ordinal)) continue;
                if (AssetDatabase.LoadAssetAtPath<Object>(newPath) != null)
                {
                    Debug.LogWarning($"[ChatSystem] \"{newPath}\" 已存在，跳过改名。");
                    continue;
                }

                string error = AssetDatabase.MoveAsset(oldPath, newPath);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError($"[ChatSystem] 改名失败 {oldPath} → {newPath}：{error}");
                }
            }
        }

        /// <summary>更新场景里 <see cref="ChatAppController"/> 的联系人列表与初始联系人。</summary>
        private static void UpdateScene(List<ConversationAsset> created, StringBuilder log)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(ScenePath);
            if (!scene.IsValid())
            {
                Debug.LogError($"[ChatSystem] 打不开场景 \"{ScenePath}\"，跳过场景更新。" +
                               $"新建的联系人需要手动拖进 ChatAppController.conversations。");
                return;
            }

            var controller = Object.FindObjectOfType<ChatAppController>();
            if (controller == null)
            {
                Debug.LogError("[ChatSystem] 场景里找不到 ChatAppController，跳过。");
                return;
            }

            var so = new SerializedObject(controller);

            // 新联系人挂在列表末尾。列表顺序 = 左侧显示顺序
            var list = so.FindProperty("conversations");
            if (list != null && created.Count > 0)
            {
                int start = list.arraySize;
                list.arraySize = start + created.Count;
                for (int i = 0; i < created.Count; i++)
                {
                    list.GetArrayElementAtIndex(start + i).objectReferenceValue = created[i];
                }
                log.Append("  场景：追加 ").Append(created.Count).Append(" 个联系人到列表末尾\n");
            }

            // 初始联系人的 id 跟着换。"robin" 这条已经变成"冰冻"
            var initial = so.FindProperty("initialContactId");
            if (initial != null)
            {
                for (int i = 0; i < Swaps.Length; i++)
                {
                    if (Swaps[i].OldId == null) continue;
                    if (!string.Equals(initial.stringValue, Swaps[i].OldId,
                            System.StringComparison.Ordinal)) continue;

                    log.Append("  场景：初始联系人 ").Append(initial.stringValue)
                       .Append(" → ").Append(Swaps[i].NewId).Append('\n');
                    initial.stringValue = Swaps[i].NewId;
                    break;
                }
            }

            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static Sprite LoadSprite(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtFolder}/{fileName}");
        }

        private static List<T> LoadAll<T>(string folder) where T : Object
        {
            var result = new List<T>();
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) result.Add(asset);
            }

            return result;
        }
    }
}
