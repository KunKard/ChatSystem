using System;
using System.Collections.Generic;
using System.IO;
using ChatSystem.Data;
using UnityEditor;
using UnityEngine;

namespace ChatSystem.EditorTools.Migrations
{
    /// <summary>
    /// 一次性装配：把表情包导进工程，并生成「资源名 → Sprite」的资源库。
    /// </summary>
    /// <remarks>
    /// 对话节点上的 <c>assetName</c> 是一个字符串，运行时靠
    /// <see cref="MediaLibrary"/> 变回图片。这份资源库就是那张对照表，
    /// 没有它，所有表情节点在游戏里都只是一块纯色方块。
    /// <para>
    /// 源图放在 <c>ChatFiles/emojis/</c>（该目录已被 .gitignore 挡住，不入库），
    /// 本脚本把它们复制进 <c>Assets/ArtRes/Emoji/</c>，入库的是复制件。
    /// </para>
    /// <para>
    /// 可重复执行：已存在的图会被源图覆盖，资源库按当前文件列表整体重建。
    /// 路径不变，所以 <c>.meta</c> 与 GUID 都保住，已有的引用不会断。
    /// </para>
    /// </remarks>
    internal static class EmojiLibraryBuilder
    {
        private const string SourceFolder = "ChatFiles/emojis";
        private const string DestFolder = "Assets/ArtRes/Emoji";
        private const string LibraryPath = "Assets/Resources/MediaLibrary.asset";

        /// <summary>表情在气泡里按 200×200 显示，256 进包留一点高分辨率余量。</summary>
        private const int MaxSize = 256;

        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

        [MenuItem("Tools/ChatSystem/一次性/构建表情资源库")]
        private static void Build()
        {
            if (!Directory.Exists(SourceFolder))
            {
                EditorUtility.DisplayDialog("构建表情资源库",
                    $"找不到源目录 {SourceFolder}/。\n\n" +
                    "表情包的原始文件应该放在工程根目录的 ChatFiles/emojis/ 下。", "知道了");
                return;
            }

            var sources = new List<string>();
            var sourcePaths = new List<string>();

            var files = Directory.GetFiles(SourceFolder);
            Array.Sort(files, StringComparer.Ordinal);

            for (int i = 0; i < files.Length; i++)
            {
                string ext = Path.GetExtension(files[i]).ToLowerInvariant();

                bool known = false;
                for (int e = 0; e < Extensions.Length; e++)
                {
                    if (ext == Extensions[e]) { known = true; break; }
                }
                if (!known) continue;

                // 资源名去掉 emoji_ 前缀和扩展名：下拉框里显示的是"中秋快乐"而不是
                // "emoji_中秋快乐.jpg"，节点的 assetName 也就跟着好读
                string name = Path.GetFileNameWithoutExtension(files[i]);
                if (name.StartsWith("emoji_", StringComparison.Ordinal))
                {
                    name = name.Substring("emoji_".Length);
                }

                sources.Add(name);
                sourcePaths.Add(files[i]);
            }

            if (sources.Count == 0)
            {
                EditorUtility.DisplayDialog("构建表情资源库",
                    $"{SourceFolder}/ 里没有找到 png/jpg。", "知道了");
                return;
            }

            EnsureFolder(DestFolder);

            var sprites = new List<Sprite>();
            var failed = new List<string>();

            for (int i = 0; i < sources.Count; i++)
            {
                string fileName = Path.GetFileName(sourcePaths[i]);
                string destPath = $"{DestFolder}/{fileName}";

                File.Copy(sourcePaths[i], destPath, true);
                AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceSynchronousImport);

                ApplyImportSettings(destPath);

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(destPath);
                if (sprite == null)
                {
                    failed.Add(fileName);
                }

                sprites.Add(sprite);
            }

            var library = WriteLibrary(sources, sprites);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (failed.Count > 0)
            {
                Debug.LogError($"[ChatSystem] 有 {failed.Count} 张图导入后取不到 Sprite：" +
                               string.Join("、", failed));
                EditorUtility.DisplayDialog("构建表情资源库",
                    $"有 {failed.Count} 张图没能导入成 Sprite，详见 Console。\n" +
                    "资源库已生成，但那几项是空的。", "知道了");
                return;
            }

            Debug.Log($"[ChatSystem] 表情资源库就绪：{sources.Count} 项 → {LibraryPath}");
            Selection.activeObject = library;
            EditorGUIUtility.PingObject(library);
        }

        /// <summary>按 <see cref="MaxSize"/> 设置导入参数，并落盘。</summary>
        private static void ApplyImportSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

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
                // UI 完全用不到 mipmap，白占 33% 内存
                importer.mipmapEnabled = false;
                dirty = true;
            }
            if (importer.maxTextureSize > MaxSize)
            {
                importer.maxTextureSize = MaxSize;
                dirty = true;
            }
            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                dirty = true;
            }

            if (dirty) importer.SaveAndReimport();
        }

        /// <summary>整体重建资源库的条目表。</summary>
        /// <remarks>
        /// 走 <see cref="SerializedObject"/> 而不是直接写 <c>entries</c> 字段：
        /// 字段是 <c>private</c>，而且直接改托管字段不会立刻反映到已序列化的资产上。
        /// </remarks>
        private static MediaLibrary WriteLibrary(List<string> names, List<Sprite> sprites)
        {
            EnsureFolder("Assets/Resources");

            var library = AssetDatabase.LoadAssetAtPath<MediaLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<MediaLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var so = new SerializedObject(library);
            var entries = so.FindProperty("entries");
            entries.arraySize = names.Count;

            for (int i = 0; i < names.Count; i++)
            {
                var element = entries.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = names[i];
                element.FindPropertyRelative("sprite").objectReferenceValue = sprites[i];
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(library);

            return library;
        }

        /// <summary>
        /// 确保 <paramref name="path"/> 这一串目录存在。
        /// </summary>
        /// <remarks>
        /// <c>AssetDatabase.CreateFolder</c> 一次只建一层，所以得逐级来。
        /// 用 <c>IsValidFolder</c> 判存在而不是 <c>Directory.Exists</c>：
        /// 目录要先进 AssetDatabase 才算数，否则后面 <c>CreateAsset</c> 会失败。
        /// </remarks>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
