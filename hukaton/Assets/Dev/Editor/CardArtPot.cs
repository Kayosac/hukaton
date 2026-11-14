using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Dev
{
    /// <summary>
    /// Card art is drawn 1024×1536 and shown with mipmaps; Unity cannot compress a non-power-of-two texture that has
    /// mipmaps, so each such art would ship raw (8 MB). This stretches the PNG itself to the next power of two
    /// (1024×2048, premultiplied Catmull-Rom) — every card surface samples it by 0..1 UVs, card UI keeps the 2:3 shape
    /// through CardArtAspect, the card shaders measure texels by the card's 2:3 width — so it looks the same and
    /// compresses to BC7 (2.7 MB with mips). Only sprites used as card artwork (CardDefinition.artwork,
    /// DeckPrototypeConfig.returnArtwork). Idempotent: run by TextureCompression before every Windows/macOS build,
    /// so a card drawn later at 1024×1536 is fixed too (menu Hakaton/Build/Card Art To Power Of Two).
    /// </summary>
    static class CardArtPot
    {
        [MenuItem("Hakaton/Build/Card Art To Power Of Two")]
        static void Menu() => Debug.Log("[Build] " + Apply(""));

        public static IEnumerable<string> CardArtPaths()
        {
            var paths = new HashSet<string>();
            void Add(string type, string field)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:" + type))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                    if (!asset) continue;
                    var sprite = new SerializedObject(asset).FindProperty(field)?.objectReferenceValue as Sprite;
                    if (sprite) paths.Add(AssetDatabase.GetAssetPath(sprite));
                }
            }
            Add("CardDefinition", "artwork");
            Add("DeckPrototypeConfig", "returnArtwork");
            return paths.Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p);
        }

        public static string Apply(string _)
        {
            var fixedPaths = new List<string>();
            foreach (var path in CardArtPaths())
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || !importer.mipmapEnabled) continue;
                importer.GetSourceTextureWidthAndHeight(out int w, out int h);
                int pw = Mathf.NextPowerOfTwo(w), ph = Mathf.NextPowerOfTwo(h);
                if (pw == w && ph == h) continue;
                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!src.LoadImage(File.ReadAllBytes(path))) { UnityEngine.Object.DestroyImmediate(src); continue; }
                var dst = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
                dst.SetPixels32(Resample(src.GetPixels32(), w, h, pw, ph));
                File.WriteAllBytes(path, dst.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(src);
                UnityEngine.Object.DestroyImmediate(dst);
                // Texels are now 4:3 tall; anisotropic filtering keeps the minified art as sharp across as before.
                importer.anisoLevel = Mathf.Max(importer.anisoLevel, 2);
                importer.SaveAndReimport();
                fixedPaths.Add(Path.GetFileName(path));
            }
            if (fixedPaths.Count > 0) AssetDatabase.Refresh();
            return $"card art to power of two: {fixedPaths.Count}" + (fixedPaths.Count > 0 ? " (" + string.Join(", ", fixedPaths) + ")" : "");
        }

        /// <summary>Separable Catmull-Rom on premultiplied colour (no dark fringes at the transparent corners).</summary>
        static Color32[] Resample(Color32[] src, int w, int h, int nw, int nh)
        {
            var pre = new float[w * h * 4];
            for (int i = 0; i < src.Length; i++)
            {
                float a = src[i].a / 255f;
                pre[i * 4] = src[i].r * a; pre[i * 4 + 1] = src[i].g * a; pre[i * 4 + 2] = src[i].b * a; pre[i * 4 + 3] = src[i].a;
            }
            var mid = Pass(pre, w, h, nw, true);
            var res = Pass(mid, nw, h, nh, false);
            var outPx = new Color32[nw * nh];
            for (int i = 0; i < outPx.Length; i++)
            {
                float a = Mathf.Clamp(res[i * 4 + 3], 0, 255);
                float k = a > 0.5f ? 255f / a : 0;
                outPx[i] = new Color32(B(res[i * 4] * k), B(res[i * 4 + 1] * k), B(res[i * 4 + 2] * k), B(a));
            }
            return outPx;
        }

        static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);

        static float[] Pass(float[] src, int w, int h, int n, bool horizontal)
        {
            int len = horizontal ? w : h, other = horizontal ? h : w;
            int ow = horizontal ? n : w;
            var dst = new float[(horizontal ? n * h : w * n) * 4];
            float scale = (float)len / n;
            for (int i = 0; i < n; i++)
            {
                float s = (i + .5f) * scale - .5f;
                int s0 = Mathf.FloorToInt(s);
                float t = s - s0;
                float w0 = ((-.5f * t + 1f) * t - .5f) * t, w1 = (1.5f * t - 2.5f) * t * t + 1f;
                float w2 = ((-1.5f * t + 2f) * t + .5f) * t, w3 = (.5f * t - .5f) * t * t;
                int i0 = Mathf.Clamp(s0 - 1, 0, len - 1), i1 = Mathf.Clamp(s0, 0, len - 1);
                int i2 = Mathf.Clamp(s0 + 1, 0, len - 1), i3 = Mathf.Clamp(s0 + 2, 0, len - 1);
                for (int o = 0; o < other; o++)
                {
                    int a0, a1, a2, a3, d;
                    if (horizontal) { a0 = (o * w + i0) * 4; a1 = (o * w + i1) * 4; a2 = (o * w + i2) * 4; a3 = (o * w + i3) * 4; d = (o * n + i) * 4; }
                    else { a0 = (i0 * w + o) * 4; a1 = (i1 * w + o) * 4; a2 = (i2 * w + o) * 4; a3 = (i3 * w + o) * 4; d = (i * ow + o) * 4; }
                    for (int c = 0; c < 4; c++)
                        dst[d + c] = src[a0 + c] * w0 + src[a1 + c] * w1 + src[a2 + c] * w2 + src[a3 + c] * w3;
                }
            }
            return dst;
        }

        /// <summary>
        /// One-off: card art Images that kept the 2:3 shape through Image.preserveAspect switch to CardArtAspect
        /// (invoke Hakaton.Dev.CardArtPot.SetupPrefabs).
        /// </summary>
        public static string SetupPrefabs(string _)
        {
            var targets = new (string prefab, string component, string field)[]
            {
                ("Assets/Decks/Prefabs/CardTile.prefab", "DeckCardView", "art"),
                ("Assets/Decks/Prefabs/DeckWorkshop.prefab", "DeckPrototype", "detailArt"),
                ("Assets/Backroom/Prefabs/BrazierCard.prefab", "OfferCardView", "face"),
                ("Assets/Backroom/Prefabs/GoblinCardTable.prefab", "OfferCardView", "face"),
                ("Assets/Computer/Prefabs/PcCardTile.prefab", "PcCardTile", "face"),
            };
            var aspect = Type.GetType("Hakaton.Decks.Unity.CardArtAspect, Hakaton.Decks.Runtime");
            if (aspect == null) return "FAILED CardArtAspect type not found";
            var log = new List<string>();
            foreach (var (prefab, component, field) in targets)
            {
                var root = PrefabUtility.LoadPrefabContents(prefab);
                int n = 0;
                try
                {
                    foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m && m.GetType().Name == component))
                    {
                        if (!(new SerializedObject(mb).FindProperty(field)?.objectReferenceValue is Image image)) continue;
                        image.preserveAspect = false;
                        if (!image.GetComponent(aspect)) image.gameObject.AddComponent(aspect);
                        n++;
                    }
                    if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, prefab);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                log.Add($"{Path.GetFileName(prefab)}:{n}");
            }
            return "OK " + string.Join(", ", log);
        }
    }
}
