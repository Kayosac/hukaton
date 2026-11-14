// One-off baker of «Склад» pixel icons for loot items that only have a 3D model.
// Source copy: output/pc-computer/bake_stock_icons.cs (put it under Assets/<any>/Editor/ to run again, then delete it).
// Bridge: invoke WhStockIconBaker.Bake
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Computer;
using Hakaton.Loot;
using UnityEditor;
using UnityEngine;

public static class WhStockIconBaker
{
    const string CatalogPath = "Assets/Loot/Loot catalog.asset";
    const string Folder = "Assets/Computer/Art/Stock";
    const string IconsPath = Folder + "/PcStockIcons.asset";
    const int Size = 32;          // final icon, px (one PC-screen pixel per texel in a 32x32 list cell)
    const int Super = 8;          // rendered at Size*Super, then box-averaged down
    const float AlphaCut = .4f;   // hard alpha after averaging
    static readonly Color32 Outline = new Color32(42, 28, 18, 255);

    public static string BakeAll() => Bake("");

    public static string Bake(string only)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<LootCatalog>(CatalogPath);
        if (!catalog) return "no catalog";
        Directory.CreateDirectory(Folder);
        var made = new List<(string id, string path)>();
        var pru = new PreviewRenderUtility();
        try
        {
            var cam = pru.camera;
            cam.orthographic = true;
            cam.nearClipPlane = .01f;
            cam.farClipPlane = 50;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            pru.lights[0].intensity = 1.25f;
            pru.lights[0].color = new Color(1f, .96f, .9f);
            pru.lights[0].transform.rotation = Quaternion.Euler(35, 30, 0);
            pru.lights[1].intensity = .55f;
            pru.lights[1].color = new Color(.75f, .82f, 1f);
            pru.lights[1].transform.rotation = Quaternion.Euler(-10, 200, 0);
            pru.ambientColor = new Color(.42f, .42f, .46f);

            foreach (var item in catalog.items)
            {
                if (!item || item.icon) continue;
                if (!string.IsNullOrEmpty(only) && item.id != only) continue;
                var source = item.ShelfModel;
                if (!source) continue;
                var go = Object.Instantiate(source);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                pru.AddSingleGO(go);
                // Only what is drawn: enabled mesh renderers on active objects (a stock prefab may carry a disabled glint or helper).
                var renderers = go.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if (renderers.Length == 0) { Object.DestroyImmediate(go); continue; }
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);

                // Models face −X of the prefab (towards the shop's customer camera): look at the front, a bit from above and the side.
                cam.transform.rotation = Quaternion.Euler(22, 62, 0);
                var corners = new List<Vector3>();
                for (int i = 0; i < 8; i++)
                    corners.Add(bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                // Tighter: the mesh vertices themselves (bounds corners if a mesh cannot be read).
                var points = new List<Vector3>();
                try
                {
                    foreach (var r in renderers)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf && mf.sharedMesh) foreach (var v in mf.sharedMesh.vertices) points.Add(mf.transform.TransformPoint(v));
                    }
                }
                catch (System.Exception) { points.Clear(); }
                if (points.Count > 0) corners = points;
                var inv = Quaternion.Inverse(cam.transform.rotation);
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
                foreach (var c in corners)
                {
                    var l = inv * (c - bounds.center);
                    min = Vector2.Min(min, l); max = Vector2.Max(max, l);
                }
                var mid = (min + max) * .5f;
                float half = Mathf.Max(max.x - min.x, max.y - min.y) * .5f;
                // One clear pixel for the outline on every side.
                cam.orthographicSize = half * Size / (Size - 3f);
                cam.transform.position = bounds.center + cam.transform.rotation * new Vector3(mid.x, mid.y, 0) - cam.transform.forward * (bounds.extents.magnitude * 3 + 1);
                cam.farClipPlane = bounds.extents.magnitude * 8 + 5;

                var b = Render(pru, Color.black, out int side);
                var w = Render(pru, Color.white, out _);
                Object.DestroyImmediate(go);

                var pixels = Downsample(b, w, side);
                AddOutline(pixels);
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.SetPixels32(pixels);
                tex.Apply();
                string path = $"{Folder}/stock_{item.id}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                made.Add((item.id, path));
            }
        }
        finally
        {
            pru.Cleanup();
        }

        foreach (var (_, path) in made) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        foreach (var (_, path) in made)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // the project's shipping setting (Dev/Editor/TextureCompression)
            importer.spritePixelsPerUnit = 100;
            importer.maxTextureSize = 32;
            importer.SaveAndReimport();
        }

        var icons = AssetDatabase.LoadAssetAtPath<PcStockIcons>(IconsPath);
        if (!icons)
        {
            icons = ScriptableObject.CreateInstance<PcStockIcons>();
            AssetDatabase.CreateAsset(icons, IconsPath);
        }
        foreach (var (id, path) in made)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var entry = icons.baked.FirstOrDefault(e => e.id == id);
            if (entry == null) icons.baked.Add(entry = new PcStockIcons.Entry { id = id });
            entry.icon = sprite;
        }
        // Keep the catalogue's order.
        icons.baked = icons.baked.OrderBy(e => catalog.items.FindIndex(i => i && i.id == e.id)).ToList();
        EditorUtility.SetDirty(icons);
        AssetDatabase.SaveAssets();
        return $"baked {made.Count}: " + string.Join(", ", made.Select(m => m.id));
    }

    /// <summary>
    /// Through the utility's own static preview (Begin → Render → End): it sets and restores the preview lighting
    /// itself. The texture is Size*Super points, so on a Retina screen twice as many pixels: <paramref name="side"/>.
    /// </summary>
    static Color32[] Render(PreviewRenderUtility pru, Color background, out int side)
    {
        pru.BeginStaticPreview(new Rect(0, 0, Size * Super, Size * Super));
        pru.camera.backgroundColor = background;
        pru.camera.clearFlags = CameraClearFlags.SolidColor;
        pru.Render(true, false);
        var tex = pru.EndStaticPreview();
        side = tex.width;
        var pixels = tex.GetPixels32();
        Object.DestroyImmediate(tex);
        return pixels;
    }

    /// <summary>Alpha from the black/white difference; box-average Super×Super blocks; hard alpha.</summary>
    static Color32[] Downsample(Color32[] b, Color32[] w, int big)
    {
        int block = big / Size;
        var result = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float r = 0, g = 0, bl = 0, a = 0;
                for (int j = 0; j < block; j++)
                    for (int i = 0; i < block; i++)
                    {
                        int k = (y * block + j) * big + x * block + i;
                        var cb = b[k]; var cw = w[k];
                        float alpha = 1 - ((cw.r - cb.r) + (cw.g - cb.g) + (cw.b - cb.b)) / (3f * 255f);
                        alpha = Mathf.Clamp01(alpha);
                        a += alpha; r += cb.r; g += cb.g; bl += cb.b;   // black background: premultiplied colour
                    }
                float n = block * block;
                float avgA = a / n;
                if (avgA < AlphaCut) { result[y * Size + x] = new Color32(0, 0, 0, 0); continue; }
                float inv = 1f / Mathf.Max(a, 1e-4f);
                var c = new Color(r * inv / 255f, g * inv / 255f, bl * inv / 255f);
                // A touch more saturation and contrast: a 32 px icon must read at a glance.
                Color.RGBToHSV(c, out float h, out float s, out float v);
                c = Color.HSVToRGB(h, Mathf.Clamp01(s * 1.15f), Mathf.Clamp01((v - .5f) * 1.1f + .5f + .02f));
                result[y * Size + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 255);
            }
        return result;
    }

    /// <summary>A 1 px dark contour around the silhouette (like the world's loot drawings).</summary>
    static void AddOutline(Color32[] p)
    {
        var copy = (Color32[])p.Clone();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (copy[y * Size + x].a != 0) continue;
                bool edge = false;
                for (int d = 0; d < 4 && !edge; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
                    edge = copy[ny * Size + nx].a != 0;
                }
                if (edge) p[y * Size + x] = Outline;
            }
    }
}
