// TEMPORARY authoring tool: dresses the hand-edited god-mountains scene (small props, ivy, the cloud sea shader, the
// detailed far massifs) without touching anything else in it. Run through the editor bridge, review, then delete this
// file and its .meta (AGENTS.md: no builders left as the source of truth). Re-running replaces only its own groups.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hakaton.GodMountains;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

static class GodMountainsDressing
{
    const string Root = "Assets/GodMountains";
    const string ArtDir = Root + "/Art";
    const string ModelDir = ArtDir + "/Models";
    const string PrefabDir = ArtDir + "/Prefabs";
    const string MatDir = ArtDir + "/Materials";
    const string TexDir = ArtDir + "/Textures";
    const string ShaderDir = ArtDir + "/Shaders";
    const string ScenePath = Root + "/Scenes/GodMountains.unity";
    const string LevelName = "God mountains — level";
    const string RouteName = "Маршрут (узлы N00–N32)";
    const string PropsGroup = "Мелкие пропсы";
    const string IvyGroup = "Плющ";
    const float PlatX = 42.4f, PlatZ0 = 48.3f, LipZ = 62.3f;

    // Route node widths (docs/level-design/god-mountains/data/route.json).
    static readonly (string id, float w)[] Widths =
    {
        ("N00", 0f), ("N01", 1.4f), ("N02", 1.6f), ("N03", 1.5f), ("N04", 1.8f), ("N05", 1.4f), ("N06", 1.6f), ("N07", 1.6f),
        ("N08", 1.6f), ("N09", 1.6f), ("N10", 1.6f), ("N11", 1.6f), ("N12", 1.6f), ("N13", 1.6f), ("N14", 1.6f), ("N15", 1.6f),
        ("N16", 1.4f), ("N17", 1.2f), ("N18", 1f), ("N19", 1.6f), ("N20", 5f), ("N21", 3f), ("N22", 3f), ("N23", 2.8f),
        ("N24", 3.2f), ("N25", 3f), ("N26", 1.7f), ("N27", 1.6f), ("N28", 1.6f), ("N29", 1.8f), ("N30", 2f), ("N31", 14f), ("N32", 14f),
    };

    // Heights of the v1 far massifs (Unity y): the detailed ones are taller; instances keep their old height.
    static readonly Dictionary<string, float> OldMassifHeight = new Dictionary<string, float>
    { { "A09 Massif A", 311.48f }, { "A09 Massif B", 303.46f }, { "A09 Massif C", 294.62f } };

    static StringBuilder log;
    static System.Random rng;
    static Transform levelRoot;
    static Vector3[] nodes;
    static readonly Dictionary<string, (GameObject prefab, Bounds b)> Props = new Dictionary<string, (GameObject, Bounds)>();
    static Material ivyMat;

    public static string Dress()
    {
        log = new StringBuilder();
        rng = new System.Random(20261112);
        var cloudSea = MakeCloudSeaMaterial();
        ivyMat = MakeIvyMaterial();
        ImportProps();

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool wasLoaded = scene.isLoaded;
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        if (scene.isDirty) log.AppendLine("NOTE scene had unsaved edits before dressing; they are saved with it");
        try
        {
            var root = scene.GetRootGameObjects().First(g => g.name == LevelName).transform;
            levelRoot = root;
            var route = root.Find(RouteName);
            nodes = Widths.Select(w => route.Find(w.id).position).ToArray();
            foreach (var name in new[] { PropsGroup, IvyGroup })
            {
                var old = root.Find(name);
                if (old) Object.DestroyImmediate(old.gameObject);
            }
            var players = scene.GetRootGameObjects().Where(g => g.name.StartsWith("Игрок")).ToArray();
            foreach (var p in players) p.SetActive(false);
            Physics.SyncTransforms();

            var sea = root.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == "Облачное море");
            if (sea) { sea.sharedMaterial = cloudSea; sea.shadowCastingMode = ShadowCastingMode.Off; sea.receiveShadows = false; log.AppendLine("cloud sea: shader set"); }
            else log.AppendLine("ERROR cloud sea not found");
            FixMassifs(root);

            var props = new GameObject(PropsGroup).transform;
            props.SetParent(root, false);
            PlaceProps(props);
            Physics.SyncTransforms();
            var ivy = new GameObject(IvyGroup).transform;
            ivy.SetParent(root, false);
            PlaceIvy(ivy);

            foreach (var p in players) p.SetActive(true);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) log.AppendLine("ERROR scene not saved");
            else log.AppendLine("scene saved");
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ------------------------------------------------------------------ materials

    static Material MakeCloudSeaMaterial()
    {
        string texPath = TexDir + "/T_GM_CloudNoise.png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = false;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.mipmapEnabled = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 512;
        ti.SaveAndReimport();
        AssetDatabase.ImportAsset(ShaderDir + "/CloudSea.shader", ImportAssetOptions.ForceUpdate);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + "/CloudSea.shader");
        string path = MatDir + "/GM Cloud Sea.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        m.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        void F(string k, float v) => m.SetFloat(k, v);
        void C(string k, string hex) { ColorUtility.TryParseHtmlString(hex, out var c); m.SetColor(k, c); }
        // Big billows (~330 m) over a slow macro field, small puffs ~50 m, faint detail; colours of the pixel sky.
        F("_Thickness", 260); F("_PuffScale", 2600); F("_SmallScale", 1600); F("_MacroScale", 14000); F("_DetailScale", 160);
        F("_Coverage", .6f); F("_Puffiness", 1.25f);
        m.SetVector("_Wind", new Vector4(7f, 2.5f, 0, 0)); F("_Boil", .02f); F("_AnimFPS", 0); F("_TimeOffset", 0);
        C("_LitColor", "#F6F0FA"); C("_MidColor", "#D3CBE8"); C("_ShadowColor", "#A097C6"); C("_DeepColor", "#6E6699");
        C("_SunTint", "#FFF0D8"); C("_RimColor", "#FFF7EE");
        F("_Rim", .6f); F("_Scatter", .5f); F("_Wrap", .3f); F("_ShadowLength", 320); F("_Steps", 5); F("_BandDither", .6f);
        F("_SoftDepth", 14); F("_CollarWidth", 45); F("_Collar", .55f); F("_PixelResolution", 360);
        EditorUtility.SetDirty(m);
        log.AppendLine($"cloud shader: {(shader ? shader.name : "MISSING")}, errors {(shader ? ShaderUtil.GetShaderMessageCount(shader) : -1)}");
        if (shader)
            foreach (var msg in ShaderUtil.GetShaderMessages(shader)) log.AppendLine($"  shader {msg.severity}: {msg.message} line {msg.line}");
        return m;
    }

    static Material MakeIvyMaterial()
    {
        string texPath = TexDir + "/T_GM_Ivy.png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
        ti.textureType = TextureImporterType.Default;
        ti.filterMode = FilterMode.Point;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = true;
        ti.mipMapsPreserveCoverage = true;
        ti.alphaTestReferenceValue = .5f;
        ti.alphaIsTransparency = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 256;
        ti.SaveAndReimport();
        string path = MatDir + "/GM Ivy.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BaseMap", tex);
        m.SetTexture("_MainTex", tex);
        m.SetFloat("_Smoothness", 0);
        m.SetFloat("_Metallic", 0);
        m.SetFloat("_SpecularHighlights", 0);
        m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetFloat("_EnvironmentReflections", 0);
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        m.SetFloat("_AlphaClip", 1);
        m.SetFloat("_Cutoff", .5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cull", 0);
        m.doubleSidedGI = true;
        m.renderQueue = 2450;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ props kit

    static void ImportProps()
    {
        Props.Clear();
        Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/GM {n}.mat");
        var remap = new Dictionary<string, Material>
        {
            { "GM_Top", Mat("Rock Top") }, { "GM_Side", Mat("Rock Side") }, { "GM_FarTop", Mat("Far") }, { "GM_FarSide", Mat("Far") },
            { "GM_Leaf", Mat("Foliage") }, { "GM_Crack", Mat("Crack") },
        };
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("A09")) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); continue; }
            if (!name.StartsWith("P0")) continue;
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            mi.globalScale = 1;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.isReadable = true;
            mi.addCollider = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in remap) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool plant = name.StartsWith("P06");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = name.Replace('_', ' ');
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (plant) { r.shadowCastingMode = ShadowCastingMode.Off; continue; }
                // Small stones cast no shadow (noise at this size) but are solid like everything made of rock.
                if (mf.sharedMesh.bounds.size.magnitude < .35f) r.shadowCastingMode = ShadowCastingMode.Off;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = true;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{go.name}.prefab");
            Object.DestroyImmediate(go);
            Props[name] = (prefab, Bounds(prefab));
        }
        log.AppendLine("props: " + string.Join(", ", Props.Select(p => $"{p.Key} {p.Value.b.size.x:F2}×{p.Value.b.size.y:F2}×{p.Value.b.size.z:F2}")));
    }

    static Bounds Bounds(GameObject prefab)
    {
        var root = prefab.transform.worldToLocalMatrix;
        var pts = new List<Vector3>();
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh || mf.name.EndsWith("_crack")) continue;
            var m = root * mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices) pts.Add(m.MultiplyPoint3x4(v));
        }
        var b = new Bounds(pts[0], Vector3.zero);
        foreach (var p in pts) b.Encapsulate(p);
        return b;
    }

    static void FixMassifs(Transform root)
    {
        int n = 0;
        var done = new Dictionary<string, float>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (!src || !OldMassifHeight.TryGetValue(src.name, out float oldH)) continue;
            float newH = Bounds(src).size.y;
            // The height the instances were last sized for (re-running must not shrink them again).
            float sizedFor = EditorPrefs.GetFloat("GMDress.massifH." + src.name, oldH);
            if (Mathf.Abs(sizedFor - newH) < .01f) continue;
            // Bottom stays where it was (the kit's foot is at y = 0), only the height comes back to the old one.
            var s = t.localScale;
            t.localScale = new Vector3(s.x, s.y * sizedFor / newH, s.z);
            n++;
            done[src.name] = newH;
        }
        foreach (var kv in done) EditorPrefs.SetFloat("GMDress.massifH." + kv.Key, kv.Value);
        log.AppendLine($"far massifs: {n} instances kept at their old height");
    }

    // ------------------------------------------------------------------ props

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    static Vector3 LeftOf(Vector3 d) => new Vector3(-d.z, 0, d.x);
    static float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static T Pick<T>(params T[] items) => items[rng.Next(items.Length)];
    static int Index(string id) => Array.FindIndex(Widths, w => w.id == id);

    static bool Hit(Vector3 from, Vector3 dir, float dist, out RaycastHit hit)
    {
        foreach (var h in Physics.RaycastAll(from, dir, dist, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (h.collider.transform.IsChildOf(levelRoot)) { hit = h; return true; }
        }
        hit = default;
        return false;
    }

    static string Zone(int seg) => seg < Index("N20") ? "inner" : seg < Index("N21") ? "hollow" : seg < Index("N25") ? "ledge" : seg < Index("N30") ? "gorge" : "platform";

    static GameObject Put(Transform parent, string kit, Vector3 worldFloor, Quaternion worldRot, float scale, string name)
    {
        var (prefab, b) = Props[kit];
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.rotation = worldRot;
        go.transform.localScale = Vector3.one * scale;
        // The kit's pivot is on the ground; sink a few centimetres so nothing floats on uneven rock.
        go.transform.position = worldFloor + Vector3.down * (.03f * scale);
        return go;
    }

    static void PlaceProps(Transform parent)
    {
        int count = 0;
        var counts = new Dictionary<string, int>();
        void Count(string kit) { counts[kit] = counts.TryGetValue(kit, out var c) ? c + 1 : 1; count++; }

        // Along the walls: at the foot of a wall, never more than 0.45 m out from it, so the path stays clear.
        for (int i = 0; i + 1 < nodes.Length - 2; i++)
        {
            if (Widths[i].id == "N16" || Widths[i].id == "N20" || Widths[i].id == "N00") continue;  // stairs, hollow, pocket (own rules)
            Vector3 a = nodes[i], b = nodes[i + 1], d = Flat(b - a);
            float len = d.magnitude;
            d /= Mathf.Max(len, 1e-4f);
            string zone = Zone(i);
            for (float s = Rand(.3f, .9f); s < len; s += Rand(.75f, 1.5f))
            {
                float t = s / len;
                float hw = Mathf.Lerp(Widths[i].w, Widths[i + 1].w, t) * .5f;
                var p = Vector3.Lerp(a, b, t);
                foreach (int sign in new[] { 1, -1 })
                {
                    if (rng.NextDouble() > (zone == "ledge" && sign < 0 ? .7 : .72)) continue;
                    var side = LeftOf(d) * sign;
                    bool rim = zone == "ledge" && sign < 0;  // the ledge's outer edge: no wall, the rim stones
                    Vector3 spot, n;
                    if (rim)
                    {
                        spot = p + side * (hw - Rand(.2f, .5f));
                        n = -side;
                    }
                    else
                    {
                        if (!Hit(p + Vector3.up * .35f, side, hw + 2.5f, out var wall) || Mathf.Abs(wall.normal.y) > .5f) continue;
                        n = Flat(wall.normal).normalized;
                        spot = wall.point;
                    }
                    if (!Hit(spot + n * .3f + Vector3.up * 1.2f, Vector3.down, 3f, out var floor) || floor.normal.y < .6f) continue;
                    var foot = new Vector3(spot.x, floor.point.y, spot.z);
                    string kit;
                    double roll = rng.NextDouble();
                    if (rim) kit = roll < .45 ? Pick("P01_Pebbles_A", "P01_Pebbles_B") : roll < .8 ? Pick("P06_Tuft_A", "P06_Tuft_B") : "P03_Flat_stones";
                    else if (zone == "gorge") kit = roll < .4 ? Pick("P02_Rubble_A", "P02_Rubble_B") : roll < .6 ? Pick("P05_Chunk_A", "P05_Chunk_B") : roll < .8 ? Pick("P01_Pebbles_A", "P01_Pebbles_B") : Pick("P06_Tuft_A", "P06_Tuft_B");
                    else if (zone == "ledge") kit = roll < .3 ? Pick("P06_Tuft_A", "P06_Tuft_B") : roll < .55 ? Pick("P02_Rubble_A", "P02_Rubble_B") : roll < .8 ? Pick("P01_Pebbles_A", "P01_Pebbles_B") : Pick("P05_Chunk_A", "P03_Flat_stones");
                    else kit = roll < .3 ? Pick("P02_Rubble_A", "P02_Rubble_B") : roll < .55 ? Pick("P01_Pebbles_A", "P01_Pebbles_B") : roll < .72 ? Pick("P05_Chunk_A", "P05_Chunk_B") : roll < .85 ? "P03_Flat_stones" : Pick("P06_Tuft_A", "P06_Tuft_B");
                    float scale = kit.StartsWith("P05") ? Rand(.7f, 1f) : Rand(.8f, 1.25f);
                    var b2 = Props[kit].b;
                    // The walking band in the middle stays clear: props only between it and the wall (or the rim).
                    float wallDist = rim ? hw : Mathf.Abs(Vector3.Dot(Flat(spot - p), side));
                    float clearHalf = Mathf.Max(.45f, hw - .5f);
                    float room = wallDist - clearHalf;
                    if (room < .15f) continue;
                    bool leans = kit.StartsWith("P02") && !rim;
                    float extent = leans ? b2.size.z : Mathf.Max(b2.size.x, b2.size.z);
                    scale = Mathf.Min(scale, room / extent);
                    if (scale < .35f) continue;
                    // Rubble leans on the wall (its back into it); the rest lies a little out from the foot.
                    Vector3 pos = leans ? foot : foot + n * (extent * scale * .5f + .03f);
                    var rot = leans ? Quaternion.LookRotation(n) : Quaternion.Euler(0, Rand(0, 360), 0);
                    Put(parent, kit, pos, rot, scale, $"{kit.Replace('_', ' ')} · {Widths[i].id}");
                    Count(kit);
                }
            }
        }

        // The pocket: rubble in its corners, one cairn in the dark behind the spawn.
        var pocket = new[] { new Vector3(-30.9f, 0, -25.6f), new Vector3(-31.6f, 0, -32.6f), new Vector3(-38.0f, 0, -32.1f), new Vector3(-38.7f, 0, -26.1f) };
        foreach (var c in pocket)
        {
            var w = levelRoot.TransformPoint(c);
            if (!Hit(w + Vector3.up * 2f, Vector3.down, 4f, out var f)) continue;
            var toCentre = Flat(levelRoot.TransformPoint(new Vector3(-35f, 0, -28.4f)) - w).normalized;
            Put(parent, Pick("P02_Rubble_A", "P02_Rubble_B"), f.point, Quaternion.LookRotation(toCentre), Rand(.9f, 1.2f), "P02 Rubble · карман");
            Count("P02");
        }
        {
            var w = levelRoot.TransformPoint(new Vector3(-36.8f, 0, -32.0f));
            if (Hit(w + Vector3.up * 2f, Vector3.down, 4f, out var f)) { Put(parent, "P04_Cairn", f.point, Quaternion.Euler(0, Rand(0, 360), 0), 1f, "P04 Cairn · карман, за спиной"); Count("P04"); }
        }

        // The hollow: around the foot of the ring, never in its two gaps (west to N19, east to the frame).
        var hollow = nodes[Index("N20")];
        for (float ang = Rand(0, 20); ang < 360; ang += Rand(18, 30))
        {
            if (Mathf.Abs(Mathf.DeltaAngle(ang, 137.5f)) < 30 || Mathf.Abs(Mathf.DeltaAngle(ang, 0)) < 24) continue;
            var dir = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), 0, Mathf.Sin(ang * Mathf.Deg2Rad));
            if (!Hit(hollow + Vector3.up * .5f, dir, 6f, out var wall)) continue;
            var spot = wall.point - dir * Rand(.15f, .35f);
            if (!Hit(spot + Vector3.up * 1.5f, Vector3.down, 3f, out var f)) continue;
            string kit = Pick("P06_Tuft_A", "P06_Tuft_B", "P01_Pebbles_A", "P02_Rubble_A", "P06_Tuft_B", "P05_Chunk_B");
            var rot = kit.StartsWith("P02") ? Quaternion.LookRotation(-dir) : Quaternion.Euler(0, Rand(0, 360), 0);
            Put(parent, kit, f.point, rot, Rand(.8f, 1.15f), $"{kit.Replace('_', ' ')} · карман с рамкой");
            Count(kit);
        }

        // The platform: along the inside of its boulder rows and by the lip, the middle stays empty. Two cairns at the
        // lip, looking at the god: someone stood here before.
        for (int k = 0; k < 26; k++)
        {
            float x = PlatX + (rng.Next(2) == 0 ? -1 : 1) * Rand(4.4f, 5.7f);
            float z = Rand(PlatZ0 + 1.5f, LipZ - 1.2f);
            if (k >= 18) { x = PlatX + Rand(-5.6f, 5.6f); z = LipZ - Rand(.9f, 1.6f); if (Mathf.Abs(x - PlatX) < 2.6f) continue; }
            var w = levelRoot.TransformPoint(new Vector3(x, 30, z));
            if (!Hit(w, Vector3.down, 40f, out var f) || f.normal.y < .8f) continue;
            string kit = k >= 18 ? Pick("P01_Pebbles_A", "P06_Tuft_A", "P03_Flat_stones") : Pick("P01_Pebbles_A", "P01_Pebbles_B", "P06_Tuft_A", "P06_Tuft_B", "P03_Flat_stones", "P05_Chunk_A");
            Put(parent, kit, f.point, Quaternion.Euler(0, Rand(0, 360), 0), Rand(.85f, 1.25f), $"{kit.Replace('_', ' ')} · площадка");
            Count(kit);
        }
        foreach (float x in new[] { -3.3f, 3.6f })
        {
            var w = levelRoot.TransformPoint(new Vector3(PlatX + x, 30, LipZ - 1.3f));
            if (Hit(w, Vector3.down, 40f, out var f)) { Put(parent, "P04_Cairn", f.point, Quaternion.Euler(0, Rand(0, 360), 0), Rand(1f, 1.2f), "P04 Cairn · у кромки"); Count("P04"); }
        }
        log.AppendLine($"props: {count} — " + string.Join(", ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key} {c.Value}")));
    }

    // ------------------------------------------------------------------ ivy

    /// <summary>One patch of ivy grown over the rock: stems climb (or hang) along the surface found by raycasts, leaf
    /// cards from the ivy atlas sit on them a few cm off the stone, facing out and drooping a little. One mesh per
    /// patch, no collider (foliage is passable).</summary>
    static bool GrowIvy(Transform parent, string name, Vector3 start, Vector3 normal, float height, float width, bool hanging)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var norms = new List<Vector3>();
        var tris = new List<int>();
        var origin = start;
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 uv0, Vector2 uv1)
        {
            int i = verts.Count;
            verts.AddRange(new[] { a - origin, b - origin, c - origin, d - origin });
            uvs.AddRange(new[] { new Vector2(uv0.x, uv0.y), new Vector2(uv1.x, uv0.y), new Vector2(uv1.x, uv1.y), new Vector2(uv0.x, uv1.y) });
            norms.AddRange(new[] { n, n, n, n });
            tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }
        int leaves = 0;
        var right0 = Vector3.Cross(Vector3.up, normal).normalized;
        int stems = rng.Next(5, 10);
        var queue = new Queue<(Vector3 pos, Vector3 n, Vector3 dir, float budget, int depth)>();
        for (int s = 0; s < stems; s++)
        {
            var p0 = start + right0 * Rand(-width * .5f, width * .5f) + (hanging ? Vector3.down : Vector3.up) * Rand(0, .4f);
            if (!Hit(p0 + normal * .6f, -normal, 1.5f, out var h0)) continue;
            var dir0 = ((hanging ? Vector3.down : Vector3.up) + right0 * Rand(-.35f, .35f)).normalized;
            queue.Enqueue((h0.point + h0.normal * .015f, h0.normal, dir0, height * Rand(.45f, 1f), 0));
        }
        while (queue.Count > 0)
        {
            var (pos, n, dir, budget, depth) = queue.Dequeue();
            float walked = 0;
            float stemU = rng.Next(2) * .25f;
            while (walked < budget)
            {
                var wander = Vector3.Cross(n, dir).normalized * Rand(-.35f, .35f);
                var along = (dir + wander * .5f);
                along = (along - n * Vector3.Dot(along, n)).normalized;
                var next = pos + along * .13f;
                // Keep inside the patch, sideways.
                if (Mathf.Abs(Vector3.Dot(next - start, right0)) > width * .65f) along = (along - right0 * Mathf.Sign(Vector3.Dot(next - start, right0)) * .6f).normalized;
                next = pos + along * .13f;
                if (!Hit(next + n * .5f, -n, 1.1f, out var h) || Mathf.Abs(h.normal.y) > .8f) break;
                var nn = h.normal;
                var q = h.point + nn * .015f;
                // Stem: a thin strip lying on the stone.
                var side = Vector3.Cross(nn, (q - pos).normalized).normalized * .012f;
                Quad(pos - side, pos + side, q + side, q - side, nn, new Vector2(stemU + .109f, .0f), new Vector2(stemU + .141f, .25f));
                // Leaves: one or two per step, thinning toward the end of the stem.
                float progress = walked / Mathf.Max(budget, .01f);
                int count = rng.NextDouble() < .9 - progress * .3 ? (rng.NextDouble() < .6 ? 2 : 1) : 0;
                for (int k = 0; k < count; k++)
                {
                    float size = Rand(.13f, .24f) * (1 - progress * .3f);
                    var ln = (nn + Vector3.up * Rand(.1f, .45f) + Vector3.Cross(nn, Vector3.up).normalized * Rand(-.3f, .3f)).normalized;
                    var tip = Vector3.ProjectOnPlane(Vector3.up * Rand(.2f, 1f) + Vector3.Cross(nn, Vector3.up).normalized * Rand(-1f, 1f) + Vector3.down * (hanging ? .7f : 0), ln).normalized;
                    if (tip.sqrMagnitude < .01f) tip = Vector3.ProjectOnPlane(Vector3.up, ln).normalized;
                    var rt = Vector3.Cross(ln, tip).normalized;
                    var basePt = q + rt * Rand(-.06f, .06f) + nn * Rand(.01f, .05f);
                    var c = basePt + tip * size * .45f;
                    int cell = rng.Next(12);
                    float u0 = (cell % 4) * .25f, v0 = 1 - (cell / 4 + 1) * .25f;
                    Quad(c - rt * size * .5f - tip * size * .5f, c + rt * size * .5f - tip * size * .5f, c + rt * size * .5f + tip * size * .5f, c - rt * size * .5f + tip * size * .5f,
                        ln, new Vector2(u0, v0), new Vector2(u0 + .25f, v0 + .25f));
                    leaves++;
                }
                // Clusters of three now and then, and side branches.
                if (rng.NextDouble() < .12)
                {
                    float size = Rand(.22f, .32f);
                    var ln = (nn + Vector3.up * .3f).normalized;
                    var tip = Vector3.ProjectOnPlane(Vector3.up, ln).normalized;
                    var rt = Vector3.Cross(ln, tip).normalized;
                    var c = q + nn * .03f + tip * size * .3f;
                    float u0 = rng.Next(2) == 0 ? .5f : .75f;
                    Quad(c - rt * size * .5f - tip * size * .5f, c + rt * size * .5f - tip * size * .5f, c + rt * size * .5f + tip * size * .5f, c - rt * size * .5f + tip * size * .5f,
                        ln, new Vector2(u0, 0), new Vector2(u0 + .25f, .25f));
                    leaves += 3;
                }
                if (depth < 2 && rng.NextDouble() < .09)
                {
                    var bdir = (along + Vector3.Cross(nn, along).normalized * (rng.Next(2) == 0 ? .9f : -.9f)).normalized;
                    queue.Enqueue((q, nn, bdir, (budget - walked) * Rand(.3f, .6f), depth + 1));
                }
                pos = q;
                n = nn;
                dir = (dir * .8f + along * .2f).normalized;
                walked += .13f;
            }
        }
        if (leaves < 12) return false;
        var mesh = new Mesh { name = "GM_Ivy_" + name };
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        string meshPath = $"{ArtDir}/Meshes/GM_Ivy_{name}.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);
        var go = new GameObject("Плющ · " + name);
        go.transform.SetParent(parent, true);
        go.transform.position = origin;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = ivyMat;
        r.shadowCastingMode = ShadowCastingMode.On;
        return true;
    }

    static void PlaceIvy(Transform parent)
    {
        foreach (var old in AssetDatabase.FindAssets("GM_Ivy_", new[] { ArtDir + "/Meshes" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(old));
        // Patches per stretch of the route: (from node, to node, how many). Light and air decide: more where light
        // comes in (the slot's seams, the hollow, the ledge, the gorge), few in the dark descent.
        var plan = new[] { ("N01", "N06", 7), ("N06", "N15", 9), ("N15", "N19", 3), ("N21", "N25", 10), ("N25", "N30", 9) };
        int made = 0, tries = 0;
        foreach (var (from, to, want) in plan)
        {
            int a = Index(from), b = Index(to), got = 0;
            for (int attempt = 0; attempt < want * 6 && got < want; attempt++)
            {
                tries++;
                int i = rng.Next(a, b);
                if (Widths[i].id == "N16") continue;
                Vector3 pa = nodes[i], pb = nodes[i + 1], d = Flat(pb - pa).normalized;
                float t = Rand(.15f, .85f);
                var p = Vector3.Lerp(pa, pb, t);
                float hw = Mathf.Lerp(Widths[i].w, Widths[i + 1].w, t) * .5f;
                int sign = rng.Next(2) == 0 ? 1 : -1;
                if (from == "N21") sign = 1;  // the ledge: only on the ridge wall
                bool hanging = rng.NextDouble() < .3;
                float y = hanging ? Rand(3.2f, 5.5f) : Rand(.05f, .6f);
                if (!Hit(p + Vector3.up * y, LeftOf(d) * sign, hw + 2.5f, out var wall) || Mathf.Abs(wall.normal.y) > .5f) continue;
                if (GrowIvy(parent, $"{Widths[i].id}-{made + 1}", wall.point, wall.normal, hanging ? Rand(1.6f, 3.2f) : Rand(1.6f, 4.5f), Rand(.8f, 2.4f), hanging)) { made++; got++; }
            }
        }
        // The hollow: open to the sky, the ring is greenest.
        var hollow = nodes[Index("N20")];
        for (int k = 0; k < 12; k++)
        {
            float ang = Rand(0, 360);
            if (Mathf.Abs(Mathf.DeltaAngle(ang, 137.5f)) < 30 || Mathf.Abs(Mathf.DeltaAngle(ang, 0)) < 22) continue;
            var dir = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), 0, Mathf.Sin(ang * Mathf.Deg2Rad));
            bool hanging = k % 3 == 2;
            if (!Hit(hollow + Vector3.up * (hanging ? Rand(4f, 6f) : .3f), dir, 6f, out var wall)) continue;
            if (GrowIvy(parent, $"N20-{made + 1}", wall.point, wall.normal, Rand(2.5f, 5.5f), Rand(1.2f, 2.8f), hanging)) made++;
        }
        log.AppendLine($"ivy: {made} patches ({tries} tries)");
    }

    // ------------------------------------------------------------------ shots (also temporary)

    public static string Shots()
    {
        var sb = new StringBuilder();
        var previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool wasLoaded = scene.isLoaded;
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var camGo = new GameObject("shot camera");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var players = scene.GetRootGameObjects().Where(g => g.name.StartsWith("Игрок")).ToArray();
        foreach (var p in players) p.SetActive(false);
        // The shots change the scene's render settings (fog, ambient, a runtime copy of the sky): put them back after.
        var saved = (RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogDensity, RenderSettings.ambientMode,
            RenderSettings.ambientLight, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.skybox);
        GodMountainsAtmosphere atm = null;
        Camera viewCamera = null;
        try
        {
            var root = scene.GetRootGameObjects().First(g => g.name == LevelName).transform;
            var route = root.Find(RouteName);
            atm = root.GetComponentInChildren<GodMountainsAtmosphere>();
            viewCamera = atm.viewCamera;
            Vector3 N(string id) => route.Find(id).position;
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.targetTexture = rt;
            cam.nearClipPlane = .05f;
            cam.farClipPlane = 12000;
            cam.fieldOfView = 75;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge", "shots");
            Directory.CreateDirectory(dir);
            var eye = Vector3.up * 1.6f;
            Vector3 L(float x, float y, float z) => root.TransformPoint(new Vector3(x, y, z));
            var list = new List<(string name, Vector3 pos, Vector3 look, bool useAir)>
            {
                ("01-spawn", N("N00") + eye, N("N00") + eye + Vector3.back, true),
                ("02-spawn-turned", N("N00") + eye, N("N01") + eye, true),
                ("03-slot", N("N03") + eye, N("N04") + eye, true),
                ("04-corner1", N("N06") + eye, N("N07") + eye, true),
                ("05-corner3", N("N12") + eye, N("N13") + eye, true),
                ("06-stairs", N("N16") + eye - Flat(N("N16") - N("N15")).normalized, N("N17") + eye, true),
                ("07-choke", N("N17") + eye, N("N18") + eye, true),
                ("08-frame", N("N20") + eye + Vector3.left * 2.2f, N("N20") + new Vector3(3.8f, 1.3f, 0), true),
                ("09-ledge-start", N("N21") + eye, N("N22") + eye, true),
                ("10-ledge-mid", N("N23") + eye, N("N24") + eye, true),
                ("11-ledge-down", N("N23") + eye, N("N23") + eye + Vector3.back * 2 + Vector3.down * 1.6f, true),
                ("12-gorge", N("N27") + eye, N("N28") + eye, true),
                ("13-last-bend", N("N29") + eye + (N("N29") - N("N28")).normalized * -1.5f, N("N30") + eye, true),
                ("14-mouth-M", N("N30") + eye, N("N30") + eye + Vector3.forward * 10 + Vector3.up * 3.5f, true),
                ("15-edge-E", N("N32") + eye, N("N32") + eye + Vector3.forward * 10 + Vector3.up * 4f, true),
                ("16-edge-back", N("N32") + eye, N("N30") + eye, true),
                ("17-clouds-down", N("N32") + Vector3.up * 7 + Vector3.forward * 2f, N("N32") + new Vector3(250, -470, 800), true),
                ("18-clouds-west", N("N32") + Vector3.up * 7, N("N32") + new Vector3(-800, -470, 500), true),
                ("19-clouds-east", N("N32") + Vector3.up * 7, N("N32") + new Vector3(800, -470, 350), true),
                ("20-reveal-wide", L(30, 50, 5), L(42.4f, 300, 1665), false),
                ("21-ivy-slot", N("N02") + eye, N("N03") + eye + Vector3.down * .4f, true),
                ("22-ivy-hollow", N("N20") + eye + Vector3.right * 1.5f, N("N20") + new Vector3(-3, 2.5f, 1.5f), true),
                ("23-props-gorge", N("N26") + eye, N("N27") + Vector3.up * .4f, true),
                ("24-props-platform", N("N31") + eye + Vector3.right * 2f, N("N31") + new Vector3(-6, 0, 3), true),
            };
            foreach (var (name, pos, look, useAir) in list)
            {
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
                var air = useAir ? atm.Evaluate(pos) : new GodMountainsAtmosphere.Air { name = "без тумана", fog = new Color(.67f, .63f, .78f), density = 0, sky = new Color(.73f, .69f, .84f), ambient = new Color(.71f, .68f, .78f), skyHidden = 0 };
                atm.viewCamera = cam;
                cam.clearFlags = atm.sky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                atm.Apply(air);
                if (!useAir) RenderSettings.fog = false;
                cam.backgroundColor = air.sky;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, "god-" + name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                sb.AppendLine($"{name}: «{air.name}»");
            }
            // The cloud sea moving: 32 frames, 1.2 s of game time apart, for a preview loop.
            var sea = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/GM Cloud Sea.mat");
            if (sea)
            {
                string fdir = Path.Combine(dir, "clouds");
                Directory.CreateDirectory(fdir);
                var eyePos = N("N32") + Vector3.up * 7 + Vector3.forward * 2f;
                cam.transform.SetPositionAndRotation(eyePos, Quaternion.LookRotation(N("N32") + new Vector3(-420, -470, 700) - eyePos, Vector3.up));
                var air = atm.Evaluate(eyePos);
                atm.Apply(air);
                cam.backgroundColor = air.sky;
                for (int f = 0; f < 32; f++)
                {
                    sea.SetFloat("_TimeOffset", f * 1.2f);
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(fdir, $"f{f:00}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                sea.SetFloat("_TimeOffset", 0);
                sb.AppendLine("cloud frames: 32");
            }
        }
        finally
        {
            (RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogDensity, RenderSettings.ambientMode,
                RenderSettings.ambientLight, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.skybox) = saved;
            if (atm) atm.viewCamera = viewCamera;
            foreach (var p in players) p.SetActive(true);
            Object.DestroyImmediate(camGo);
            rt.Release();
            Object.DestroyImmediate(rt);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
        return sb.ToString();
    }
}
