using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Dev
{
    /// <summary>
    /// Shipping size: every texture imported as "None" (raw RGBA32, 4 bytes a pixel) is switched to
    /// "High Quality" compression — BC7 on desktop, 1 byte a pixel, visually the same, hard pixels stay hard.
    /// Textures whose sides are not multiples of 4 cannot be BC7: Unity keeps them raw by itself.
    /// Platform overrides with an explicit format are left alone. Idempotent: the Windows/macOS builds run it first,
    /// so a texture added later as "None" is caught before shipping (menu Hakaton/Build/Compress Textures, or the
    /// bridge: invoke Hakaton.Dev.TextureCompression.Apply).
    /// </summary>
    static class TextureCompression
    {
        [MenuItem("Hakaton/Build/Compress Textures")]
        static void Menu() => Debug.Log("[Build] " + Apply(""));

        static readonly TextureImporterFormat[] Raw =
        {
            TextureImporterFormat.RGBA32, TextureImporterFormat.ARGB32, TextureImporterFormat.RGB24, TextureImporterFormat.Automatic,
        };

        public static string Apply(string _)
        {
            string cursor = PlayerSettings.defaultCursor ? AssetDatabase.GetAssetPath(PlayerSettings.defaultCursor) : "";
            var targets = new List<string>();
            int changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    // Vendor packs, the cursor (must stay raw RGBA32) and textures scripts read stay as they are.
                    if (path.StartsWith("Assets/Plugins/") || path == cursor) continue;
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                    if (importer.isReadable || importer.textureType == TextureImporterType.SingleChannel) continue;
                    if (importer.GetPlatformTextureSettings("Standalone").overridden) continue;
                    var main = importer.GetDefaultPlatformTextureSettings();
                    if (!Raw.Contains(main.format)) continue; // an explicit compressed or special format was chosen on purpose
                    bool dirty = false;
                    if (main.format != TextureImporterFormat.Automatic) { main.format = TextureImporterFormat.Automatic; dirty = true; }
                    if (main.textureCompression == TextureImporterCompression.Uncompressed) { main.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
                    if (main.textureCompression != TextureImporterCompression.CompressedHQ) continue;
                    targets.Add(path);
                    if (!dirty) continue;
                    importer.SetPlatformTextureSettings(main);
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            // An import that did not pick the new setting up (seen after batch edits) is forced once more.
            int forced = 0;
            foreach (var path in targets)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (!tex || tex.format == (TextureFormat)importer.GetAutomaticFormat("Standalone")) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                forced++;
            }
            return $"OK compressed {changed} textures, reimported {forced} stale";
        }

        /// <summary>The imported format of each given texture (paths split by ';') — to check what a build will carry.</summary>
        public static string Report(string paths)
        {
            return string.Join("\n", paths.Split(';').Select(p =>
            {
                var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                var s = importer ? importer.GetPlatformTextureSettings("Standalone") : null;
                return $"{p}: comp={importer?.textureCompression} fmt={(tex ? tex.format.ToString() : "-")} standalone={(s != null && s.overridden ? s.format.ToString() : "default")} auto={(importer ? importer.GetAutomaticFormat("Standalone").ToString() : "-")}";
            }));
        }
    }
}
