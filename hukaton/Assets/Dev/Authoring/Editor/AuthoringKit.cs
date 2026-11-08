using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Hakaton.Authoring
{
    /// <summary>Temporary helpers for one-off authoring passes. Removed with the authoring scripts after use.</summary>
    static class Kit
    {
        public static readonly StringBuilder Log = new StringBuilder();
        public static void Say(string line) { Log.AppendLine(line); }

        public static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new Exception("Missing asset " + path + " (" + typeof(T).Name + ")");
            return asset;
        }

        /// <summary>Unity's destroyed/missing components compare equal to null but are not C# null: never use ?? on them.</summary>
        public static T GetOrAdd<T>(Component host) where T : Component => GetOrAdd<T>(host.gameObject);
        public static T GetOrAdd<T>(GameObject host) where T : Component
        {
            var existing = host.GetComponent<T>();
            return existing ? existing : host.AddComponent<T>();
        }

        public static GameObject OuterRoot(GameObject go)
        {
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            return root ? root : go;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static T Asset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static Material Material(string path, string shader, Action<Material> setup)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find(shader);
            setup(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static void ImportTexture(string path, bool sprite, bool mipmaps, int maxSize = 2048)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!importer) throw new Exception("No texture at " + path);
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (sprite) importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = mipmaps;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        public static GameObject Child(Transform parent, string name, params Type[] components)
        {
            var go = new GameObject(name, components);
            go.transform.SetParent(parent, false);
            return go;
        }

        public static void DestroyChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        public static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root) { var found = Find(child, name); if (found) return found; }
            return null;
        }

        public static void EditPrefab(string path, Action<GameObject> edit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static GameObject SavePrefab(GameObject root, string path)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---------- UI ----------

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(.5f, .5f));
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static TMP_FontAsset Ithaca => Load<TMP_FontAsset>("Assets/Content/UI/Typography/Resources/Fonts/Ithaca Pixel.asset");
        public static TMP_FontAsset Pixeloid => Load<TMP_FontAsset>("Assets/Content/UI/Typography/Resources/Fonts/PixeloidSans Pixel.asset");
        public static Material IthacaMaterial => Load<Material>("Assets/Content/UI/Typography/Materials/Ithaca Pixel.mat");
        public static Material PixeloidMaterial => Load<Material>("Assets/Content/UI/Typography/Materials/PixeloidSans Pixel.mat");

        public static TextMeshProUGUI Text(RectTransform rect, bool display, int size, TextAlignmentOptions alignment, Color colour, string sample = "")
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = display ? Ithaca : Pixeloid;
            text.fontSharedMaterial = display ? IthacaMaterial : PixeloidMaterial;
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.alignment = alignment;
            text.color = colour;
            text.raycastTarget = false;
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.text = sample;
            return text;
        }

        public static Image Image(RectTransform rect, Sprite sprite, Color colour)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }
    }
}
