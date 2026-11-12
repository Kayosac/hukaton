// TEMPORARY authoring tool for the god-mountains level art (built on the walk-tested blockout layout). Run once through
// the editor bridge, verify, then delete this file and its .meta (AGENTS.md: no builders left as the source of truth).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hakaton.GodMountains;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

static class GodMountainsArtBuilder
{
    const string Root = "Assets/GodMountains";
    const string OldBlockoutDir = Root + "/Blockout";
    const string ArtDir = Root + "/Art";
    const string MeshDir = ArtDir + "/Meshes";
    const string MatDir = ArtDir + "/Materials";
    const string PrefabDir = ArtDir + "/Prefabs";
    const string TexDir = ArtDir + "/Textures";
    const string ModelDir = ArtDir + "/Models";
    const string GodDir = Root + "/God/MG01";
    const string ScenePath = Root + "/Scenes/GodMountains.unity";
    const string PlayerPrefab = "Assets/Character/Prefabs/FirstPersonPlayer.prefab";
    const string LevelName = "God mountains — level";
    static readonly Vector3 LevelOrigin = new Vector3(-4000, 0, 0);

    // Route nodes from docs/level-design/god-mountains/data/route.json: id, plan x, plan y (north) -> Unity z, floor h, width.
    static readonly (string id, float x, float z, float h, float w)[] Nodes =
    {
        ("N00", -34.5f, -29.5f, 0f, 0f), ("N01", -35f, -23f, 0f, 1.4f), ("N02", -34f, -17f, .4f, 1.6f),
        ("N03", -30f, -12f, .8f, 1.5f), ("N04", -31f, -6f, 1.2f, 1.8f), ("N05", -35.5f, -1f, 1.6f, 1.4f),
        ("N06", -35f, 5f, 2f, 1.6f),
        ("N07", -35f, 9f, 2.25f, 1.6f), ("N08", -32f, 11f, 2.45f, 1.6f), ("N09", -31.5f, 15f, 2.7f, 1.6f),
        ("N10", -31.5f, 19f, 2.95f, 1.6f), ("N11", -28.5f, 21f, 3.15f, 1.6f), ("N12", -28f, 25f, 3.4f, 1.6f),
        ("N13", -28f, 29f, 3.65f, 1.6f), ("N14", -25f, 31f, 3.85f, 1.6f), ("N15", -24.5f, 35f, 4.1f, 1.6f),
        ("N16", -20f, 37.5f, 4.1f, 1.4f), ("N17", -16.98f, 33.47f, .5f, 1.2f), ("N18", -14.34f, 29.95f, .5f, 1f),
        ("N19", -13.14f, 26.13f, 1.9f, 1.6f), ("N20", -10.6f, 23.8f, 1.9f, 5f),
        ("N21", -3.8f, 23.8f, 1.9f, 3f), ("N22", 7.7f, 24.8f, 3.9f, 3f), ("N23", 18.7f, 23.8f, 6f, 2.8f),
        ("N24", 29.7f, 26.6f, 8f, 3.2f), ("N25", 38.7f, 26.1f, 10f, 3f),
        ("N26", 40.7f, 31.8f, 10.7f, 1.7f), ("N27", 37.9f, 36.8f, 11.4f, 1.6f), ("N28", 38.9f, 42.3f, 12.1f, 1.6f),
        ("N29", 42.9f, 44.3f, 12.4f, 1.8f), ("N30", 42.4f, 48.3f, 12.7f, 2f),
        ("N31", 42.4f, 55.3f, 12.9f, 14f), ("N32", 42.4f, 59.8f, 13.1f, 14f),
    };

    // Summit platform: from the mouth N30 to the lip, 14 × 14 m, open middle of the lip 6 m.
    const float PlatX = 42.4f, PlatZ0 = 48.3f, LipZ = 62.3f, PlatH0 = 12.7f, LipH = 13.2f;
    static readonly Vector3 OldPlatform = new Vector3(83.5f, 0, 89f);
    static readonly Vector3 Platform = new Vector3(PlatX, 0, 55.3f);

    static Vector3 P(string id) { var n = Nodes.First(k => k.id == id); return new Vector3(n.x, n.h, n.z); }
    static float HW(string id) => Nodes.First(k => k.id == id).w * .5f;
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    static Vector3 LeftOf(Vector3 d) => new Vector3(-d.z, 0, d.x);

    const string RouteName = "Маршрут (узлы N00–N32)";
    static (Vector3 pos, Quaternion rot, Vector3 scale)? godPose;
    /// <summary>Top faces (normal up) and all other faces of one kind of surface.</summary>
    sealed class Look { public Material top, side; }
    static readonly Dictionary<string, Look> Mats = new Dictionary<string, Look>();
    static Transform levelRoot;
    static readonly string[] Boulders = { "A05_Boulder_A", "A05_Boulder_B", "A05_Boulder_C" };
    static readonly string[] Peaks = { "A09_Massif_A", "A09_Massif_B", "A09_Massif_C" };
    /// <summary>A kit asset (Blender FBX from output/god-mountains-kit): its prefab with a convex collider per part,
    /// the bounds of what is drawn and the middle of its top (prefab space).</summary>
    sealed class Kit { public GameObject prefab; public Bounds b; public Vector3 topCentre; }
    static readonly Dictionary<string, Kit> KitP = new Dictionary<string, Kit>();
    /// <summary>Turns the kit's front (Blender -Y) to the prefab's +Z.</summary>
    static Quaternion kitFix = Quaternion.identity;
    static int meshCount;
    static System.Random rng;
    static StringBuilder log;

    // ------------------------------------------------------------------ entry points

    public static string Build()
    {
        log = new StringBuilder();
        rng = new System.Random(20261112);
        meshCount = 0;
        Mats.Clear();
        var previous = SceneManager.GetActiveScene();
        // Rebuild inside the existing scene asset (it may be open in the editor). Keep where the user put the god.
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool wasLoaded = scene.isLoaded;
        if (!wasLoaded && File.Exists(ScenePath)) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        godPose = null;
        if (scene.IsValid() && scene.isLoaded)
        {
            var oldRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == LevelName);
            var oldGod = oldRoot ? oldRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("MG01 — бог")) : null;
            if (oldGod)
            {
                var platformBefore = oldRoot.transform.Find("Маршрут (узлы N00–N38)") ? OldPlatform : Platform;
                godPose = (oldGod.localPosition - platformBefore + Platform, oldGod.localRotation, oldGod.localScale);
                log.AppendLine($"god kept: {oldGod.localPosition} → {godPose.Value.pos}, scale {oldGod.localScale.x}");
            }
            foreach (var g in scene.GetRootGameObjects()) Object.DestroyImmediate(g);
        }
        if (AssetDatabase.IsValidFolder(OldBlockoutDir)) AssetDatabase.DeleteAsset(OldBlockoutDir);
        foreach (var dir in new[] { MeshDir, MatDir, PrefabDir })
            if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
        foreach (var dir in new[] { Root, Root + "/Scenes", ArtDir, MeshDir, MatDir, PrefabDir, TexDir, ModelDir, Root + "/God", GodDir, GodDir + "/Textures", GodDir + "/Materials" })
            EnsureFolder(dir);
        var godModel = ImportGod();
        MakeMaterials();
        ImportKit();
        var boulder = KitP[Boulders[0]].prefab;
        var peak = KitP[Peaks[0]].prefab;
        var shrubPrefab = KitP["A07_Shrub"].prefab;
        var rosettePrefab = KitP["A08_Rosette"].prefab;

        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var root = new GameObject(LevelName).transform;
            root.position = LevelOrigin;
            levelRoot = root;
            var route = Group(root, RouteName);
            foreach (var n in Nodes) Marker(route, n.id, new Vector3(n.x, n.h, n.z), 1);
            var geo = Group(root, "Уровень");
            BuildPocketAndSlot(Group(geo, "Z0–Z1 Карман и щель"));
            BuildCorners(Group(geo, "Z2 Повтор"), boulder, shrubPrefab);
            BuildDescent(Group(geo, "Z3 Спуск и горловина"));
            BuildThreshold(Group(geo, "Z4 Порог"));
            BuildLedge(Group(geo, "Z5 Белая полка"), boulder);
            BuildGorge(Group(geo, "Z6 Горло"));
            BuildPlatform(Group(geo, "Z7 Площадка"), boulder);
            BuildRidge(Group(geo, "Хребет и опоры"));
            BuildPlants(Group(geo, "Растения: кусты A07 и розетки A08"), shrubPrefab, rosettePrefab);
            BuildLightShafts(Group(root, "Свет из трещин"));
            var god = BuildPanorama(Group(root, "Панорама"), peak, godModel);
            BuildMarkers(Group(root, "Метки"));
            var sun = BuildSun(root);
            var player = BuildPlayer(scene);
            BuildAtmosphere(root, route, player);
            RenderSettings.sun = sun;
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/GM Sky.mat");
            ApplyHeightsAirToSceneSettings();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) log.AppendLine("ERROR scene not saved");
            log.AppendLine($"scene saved {ScenePath}; meshes {meshCount}; god {(god ? "ok" : "missing")}");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded && previous != scene) SceneManager.SetActiveScene(previous);
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ------------------------------------------------------------------ zones

    static void BuildPocketAndSlot(Transform parent)
    {
        var floorMat = Mats["Floor Inner"];
        var poly = new[] { V(-38.6f, -32.6f), V(-31.2f, -33.2f), V(-30.4f, -25.2f), V(-34.2f, -23.4f), V(-35.9f, -23.4f), V(-39.2f, -25.8f) };
        var centroid = V(-35f, -28.4f);
        var under = poly.Select(v => centroid + (v - centroid) * (1 + 1.3f / Flat(v - centroid).magnitude)).ToArray();
        MeshObj(parent, "Пол кармана", PolySlab("GM_Pocket_Floor", under, 0, .6f), floorMat);
        var wallPts = new[] { V(-34.2f, -23.4f), V(-30.4f, -25.2f), V(-31.2f, -33.2f), V(-38.6f, -32.6f), V(-39.2f, -25.8f), V(-35.9f, -23.4f) };
        var outs = OutwardFrame(wallPts, centroid);
        WallBoxes(parent, "Стены кармана", "GM_Pocket_Wall", wallPts, outs, Fill(wallPts.Length, -1), Fill(wallPts.Length, 14), 2.5f, Mats["Wall Z1"]);
        // Roof of the pocket: four heavy slabs, light comes through the seams.
        float[] zs = { -32.2f, -29.6f, -27.0f, -24.6f };
        float[] ys = { 4.8f, 5.6f, 5.1f, 6.0f };
        for (int i = 0; i < zs.Length; i++)
            Slab(parent, "Свод кармана " + (i + 1), new Vector3(-34.8f, ys[i] + .45f, zs[i]), new Vector3(10.5f, .9f, 2.35f),
                Quaternion.Euler(Jit(2.5f), Jit(3), Jit(2.5f)), Mats["Roof"]);

        // Z1 slot N01..N09 under a roof of wedged slabs.
        var ids = Ids("N01", "N06");
        var pts = ids.Select(P).ToArray();
        var hw = ids.Select(HW).ToArray();
        // The floor starts 1.5 m inside the pocket so the two floors overlap.
        var floorPts = new[] { pts[0] + Flat(pts[0] - pts[1]).normalized * 1.5f }.Concat(pts).ToArray();
        var floorHw = Under(new[] { hw[0] }.Concat(hw).ToArray());
        MeshObj(parent, "Пол щели", Ribbon("GM_Slot_Floor", floorPts, floorHw, .6f), floorMat);
        Walls(parent, "Щель", "GM_Slot", pts, hw, p => p.y - 1, p => p.y + 14, 2.5f, Mats["Wall Z1"], true, true);
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            Vector3 a = pts[i], b = pts[i + 1], d = Flat(b - a);
            int count = Mathf.Max(1, Mathf.CeilToInt(d.magnitude / 2.6f));
            float len = d.magnitude / count;
            for (int k = 0; k < count; k++)
            {
                Vector3 c = Vector3.Lerp(a, b, (k + .5f) / count);
                float w = Mathf.Lerp(hw[i], hw[i + 1], (k + .5f) / count) * 2 + 3.2f;
                Slab(parent, $"Плита крыши {i + 1}.{k + 1}", c + Vector3.up * (4.95f + Rand(0, 1f)), new Vector3(w, .9f, len - .22f),
                    Quaternion.LookRotation(d.normalized) * Quaternion.Euler(Jit(3), Jit(3), Jit(3)), Mats["Roof"]);
            }
        }
        Post(parent, "Стык N01 слева", P("N01"), Flat(P("N02") - P("N01")).normalized, true, HW("N01"), 14);
        Post(parent, "Стык N01 справа", P("N01"), Flat(P("N02") - P("N01")).normalized, false, HW("N01"), 14);
        OuterPost(parent, "Стык N06", P("N05"), P("N06"), P("N07"), HW("N06"), 14);
    }

    static void BuildCorners(Transform parent, GameObject boulder, GameObject shrubPrefab)
    {
        var module = BuildCornerModule(parent, boulder, shrubPrefab);
        module.transform.localPosition = P("N06");
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(module, PrefabDir + "/Corner module.prefab", InteractionMode.AutomatedAction);
        var second = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        second.name = "Угол 2";
        second.transform.localPosition = P("N09");
        var third = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        third.name = "Угол 3 (другой)";
        third.transform.localPosition = P("N12");
        var shrub = third.transform.Find("К — куст A07");
        var plate = third.transform.Find("П — плита A06");
        shrub.gameObject.SetActive(false);
        var hole = Box(third.transform, "К — пустая ниша, лунка", shrub.localPosition + new Vector3(0, .02f, 0), new Vector3(.55f, .06f, .35f), shrub.localRotation, Mats["Hole"], 0);
        Object.DestroyImmediate(hole.GetComponent<Collider>());
        var leaves = Box(third.transform, "К — опавшие листья", shrub.localPosition + new Vector3(.12f, .03f, .1f), new Vector3(.4f, .02f, .3f), shrub.localRotation * Quaternion.Euler(0, 25, 0), Mats["Leaf litter"], 0);
        Object.DestroyImmediate(leaves.GetComponent<Collider>());
        FitTo(plate, "A06_Slab_A", new Vector3(.25f, .12f + 2.2f + .225f, 2f), PlateSize, Quaternion.Euler(6, 12, -5) * Quaternion.Euler(0, 90, 0));
        PrefabUtility.SaveAsPrefabAssetAndConnect(third, PrefabDir + "/Corner module third.prefab", InteractionMode.AutomatedAction);
        module.name = "Угол 1";
        log.AppendLine("corners: prefab + variant");
    }

    static GameObject BuildCornerModule(Transform parent, GameObject boulder, GameObject shrubPrefab)
    {
        var go = new GameObject("Угол");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = P("N06");
        var t = go.transform;
        Vector3[] p = { new Vector3(0, 0, 0), new Vector3(0, .25f, 4), new Vector3(3, .45f, 6), new Vector3(3.5f, .7f, 10) };
        float[] hw = { .8f, .8f, .8f, .8f };
        MeshObj(t, "Пол", Ribbon("GM_Corner_Floor", p, Under(hw), .6f), Mats["Floor Inner"]);
        Walls(t, "Угол", "GM_Corner", p, hw, q => q.y - 1, q => q.y + 13, 2.5f, Mats["Wall Z2"], true, true);
        var d0 = Flat(p[1] - p[0]).normalized;
        Post(t, "Стык слева", p[0], d0, true, .8f, 13);
        Post(t, "Стык справа", p[0], d0, false, .8f, 13);
        var off = Frame(p);
        Vector3 leftCorner = p[1] + off[1] * hw[1];
        Vector3 along2 = Flat(p[2] - p[1]).normalized, inward2 = -LeftOf(along2);
        // A bush is foliage: passable, like tree crowns (a solid bush 5 cm from the wall wedged the player).
        var shrub = (GameObject)PrefabUtility.InstantiatePrefab(shrubPrefab, t);
        shrub.name = "К — куст A07";
        shrub.transform.localPosition = leftCorner + along2 * .6f + inward2 * .25f + Vector3.up * .02f;
        shrub.transform.localRotation = Quaternion.LookRotation(inward2);
        shrub.transform.localScale = Vector3.one * .85f;
        // The plate lies along the path: the kit slab's long side (x) turned along local z.
        Fit(t, "A06_Slab_A", "П — плита A06", new Vector3(.6f, .12f + .225f, 2f), PlateSize, Quaternion.Euler(0, 92, 0), Mats["Plate"]);
        Vector3 crackBase = leftCorner + along2 * 1.5f + inward2 * .03f;
        var crack1 = Box(t, "Т — трещина «Г» (вертикаль)", crackBase + Vector3.up * 1.6f, new Vector3(.12f, 2f, .05f), Quaternion.LookRotation(inward2), Mats["Crack"], 0);
        var crack2 = Box(t, "Т — трещина «Г» (перекладина)", crackBase + along2 * .39f + Vector3.up * 2.55f, new Vector3(.9f, .12f, .05f), Quaternion.LookRotation(inward2), Mats["Crack"], 0);
        Object.DestroyImmediate(crack1.GetComponent<Collider>());
        Object.DestroyImmediate(crack2.GetComponent<Collider>());
        Vector3 along3 = Flat(p[3] - p[2]).normalized, right3 = -LeftOf(along3);
        Vector3 wallPoint = p[2] - off[2] * hw[2] + along3 * .9f;
        // Pressed into the right wall (0.3 m of it sticks out), faces along the wall: no wedge between rock and wall.
        Fit(t, "A05_Boulder_B", "В — валун A05", wallPoint + right3 * (.35f - .3f) + Vector3.up * .38f, new Vector3(.7f, .85f, .9f), Quaternion.LookRotation(along3));
        return go;
    }

    static void BuildDescent(Transform parent)
    {
        var ids = new[] { "N15", "N16", "N17", "N18", "N19" };
        var pts = ids.Select(P).ToArray();
        var hw = ids.Select(HW).ToArray();
        MeshObj(parent, "Пол перед лестницей", Ribbon("GM_Descent_Top", new[] { P("N15"), P("N16") }, Under(new[] { HW("N15"), HW("N16") }), .6f), Mats["Floor Inner"]);
        MeshObj(parent, "Пол горловины и пандуса", Ribbon("GM_Descent_Bottom", new[] { P("N17"), P("N18"), P("N19") }, Under(new[] { HW("N17"), HW("N18"), HW("N19") }), .6f), Mats["Floor Inner"]);
        Walls(parent, "Спуск", "GM_Descent", pts, hw, q => 0, q => q.y + 12, 2.5f, Mats["Wall Z3"], true, true);
        // 18 carved steps of 0.2 m from N16 down to N17.
        const int count = 18;
        Vector3 a = P("N16"), b = P("N17"), dir = Flat(b - a).normalized;
        float run = Flat(b - a).magnitude, tread = run / count;
        var steps = Group(parent, "Лестница: 18 ступеней по 0,2 м");
        for (int i = 0; i < count; i++)
        {
            float top = a.y - .2f * (i + 1);
            Vector3 c = a + dir * (tread * (i + .5f));
            float w = Mathf.Lerp(HW("N16"), HW("N17"), (i + .5f) / count) * 2 + 1.2f;
            // Carved: too even on purpose (S4), so no crookedness here.
            Box(steps, "Ступень " + (i + 1), new Vector3(c.x, (top - 1f) * .5f, c.z), new Vector3(w, top + 1f, tread + .02f), Quaternion.LookRotation(dir), Mats["Steps"], 0);
        }
        // Choke ceiling at 2.1 m over the middle of N17–N18: 1.2 m of flat floor before and after it, so the head clears
        // it both coming down the stairs and coming down the ramp.
        Vector3 c0 = P("N17"), c1 = P("N18"), cd = Flat(c1 - c0);
        float from = 1.2f, to = cd.magnitude - 1.2f;
        Slab(parent, "Потолок горловины (2,1 м)", c0 + cd.normalized * ((from + to) * .5f) + Vector3.up * (2.1f + .6f), new Vector3(4f, 1.2f, to - from), Quaternion.LookRotation(cd.normalized), Mats["Roof"]);
        OuterPost(parent, "Стык N15", P("N14"), P("N15"), P("N16"), HW("N15"), 12);
    }

    static Vector3 Hollow => P("N20");
    const float HollowR = 3.3f;

    static void BuildThreshold(Transform parent)
    {
        var disk = new Vector3[24];
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI * 2 / 24;
            disk[i] = new Vector3(Hollow.x + Mathf.Cos(a) * (HollowR + 1.2f), 0, Hollow.z + Mathf.Sin(a) * (HollowR + 1.2f));
        }
        MeshObj(parent, "Пол кармана с рамкой", PolySlab("GM_Hollow_Floor", disk, Hollow.y, .6f), Mats["Floor"]);
        float west = Mathf.Atan2(P("N19").z - Hollow.z, P("N19").x - Hollow.x) * Mathf.Rad2Deg;
        // G03: a ring of leaning slabs, open to the sky, gaps west (to N19) and east (the frame); pivot at the floor centre,
        // its Blender north (+Y) is the level's north.
        Put(parent, "G03_Hollow_ring", "Стена кармана (G03)", Hollow, Face(Vector3.back), Vector3.one, Vector3.zero, Mats["Wall Z4"]);
        // Passage east through the frame to the ledge.
        var pts = new[] { Hollow + Vector3.right * 3.0f, P("N21") };
        var hw = new[] { .7f, .7f };
        MeshObj(parent, "Пол прохода за рамкой", Ribbon("GM_Passage_Floor", pts, Under(hw), .6f), Mats["Floor"]);
        Walls(parent, "Проход", "GM_Passage", pts, hw, q => q.y - 1, q => q.y + 12, 2.5f, Mats["Wall Z4"], true, true);
        var frame = BuildFrame(parent);
        frame.transform.localPosition = Hollow + Vector3.right * 3.8f;
        frame.transform.localRotation = Quaternion.Euler(0, 90, 0);
        PrefabUtility.SaveAsPrefabAssetAndConnect(frame, PrefabDir + "/Stone frame.prefab", InteractionMode.AutomatedAction);
    }

    static GameObject BuildFrame(Transform parent)
    {
        // Opening 1.4 × 2.4 m; local Z is the passage axis, local X across.
        var go = new GameObject("S5 Каменная рамка");
        go.transform.SetParent(parent, false);
        Put(go.transform, "G01_Stone_frame", "Рамка G01 (проём 1,4 × 2,4 м)", Vector3.zero, Face(Vector3.forward), Vector3.one, Vector3.zero, Mats["Frame"]);
        return go;
    }

    static void BuildLedge(Transform parent, GameObject boulder)
    {
        var ids = Ids("N21", "N25");
        var pts = ids.Select(P).ToArray();
        var hw = ids.Select(HW).ToArray();
        // The floor runs 1 m past N25 under the rock that closes the ledge: no gap at its end.
        var floorPts = pts.Concat(new[] { pts[pts.Length - 1] + Flat(pts[pts.Length - 1] - pts[pts.Length - 2]).normalized * 1f }).ToArray();
        var floorHw = hw.Concat(new[] { hw[hw.Length - 1] }).ToArray();
        MeshObj(parent, "Полка", Ribbon("GM_Ledge_Floor", floorPts, Under(floorHw), floorHw, 3f), Mats["Floor"]);
        // The north wall (ridge face) of the ledge continues as the gorge's left wall: see BuildGorge.
        // Outer rim: low stones every ~1.4 m, a big boulder every ~6 m and at every bend.
        var loff = Frame(pts);
        var right = pts.Select((q, i) => q - loff[i] * hw[i]).ToArray();
        var rim = Group(parent, "Борт: камни и валуны");
        float acc = 0, nextBig = 4;
        int stones = 0, bigs = 0;
        for (int i = 0; i + 1 < right.Length; i++)
        {
            Vector3 a = right[i], b = right[i + 1];
            float len = Flat(b - a).magnitude;
            Vector3 inward = loff[i].normalized;
            for (float s = .7f; s < len; s += 1.4f)
            {
                Vector3 p = Vector3.Lerp(a, b, s / len);
                acc += 1.4f;
                if (acc >= nextBig)
                {
                    nextBig = acc + Rand(5.5f, 6.5f);
                    var size = new Vector3(Rand(1.3f, 1.8f), Rand(1.55f, 1.9f), Rand(1.1f, 1.5f));
                    Rock(rim, boulder, "Валун", p - inward * .15f + Vector3.up * size.y * .42f, size);
                    bigs++;
                }
                else
                {
                    var size = new Vector3(Rand(.5f, .75f), Rand(.3f, .6f), Rand(.45f, .65f));
                    Rock(rim, boulder, "Камень борта", p + inward * .08f + Vector3.up * size.y * .38f, size);
                    stones++;
                }
            }
        }
        for (int i = 1; i + 1 < right.Length; i++)
        {
            Vector3 along = Flat(pts[i + 1] - pts[i - 1]).normalized, inward = loff[i].normalized;
            foreach (float s in new[] { -1.4f, 1.4f })
            {
                var size = new Vector3(Rand(1.4f, 1.8f), Rand(1.55f, 1.9f), Rand(1.2f, 1.5f));
                Rock(rim, boulder, "Валун на изгибе", right[i] + along * s - inward * .2f + Vector3.up * (size.y * .42f), size);
                bigs++;
            }
        }
        // East end of the ledge closed by rock: kit cliffs on the faces that are seen.
        RockMass(parent, "Скала в конце полки", new Vector3(43.15f, 13.5f, 23.15f), new Vector3(7.1f, 18f, 5.1f), Mats["Rock"], "WSE", true);
        RockMass(parent, "Плечо хребта у входа в горло", new Vector3(45.1f, 17.5f, 27.9f), new Vector3(7f, 26f, 4.4f), Mats["Rock"], "S", true);
        // Temporary floor under the white abyss (the fall reset comes later).
        Box(parent, "Облако бездны — временный пол блокаута", new Vector3(22, -30.5f, -11.5f), new Vector3(58, 1, 67), Quaternion.identity, Mats["Cloud"], 0);
        // The cliff under the ledge: kit cliffs upside down, the flat (former bottom) side just under the floor, their
        // fronts on the outer edge, hanging down into the cloud.
        var under = Group(parent, "Обрыв под полкой (A01, вверх ногами)");
        var cb = KitP["A01_Cliff"].b;
        for (int i = 0; i + 1 < floorPts.Length; i++)
        {
            Vector3 a = floorPts[i] - LeftOf(Flat(floorPts[i + 1] - floorPts[i]).normalized) * floorHw[i];
            Vector3 b = floorPts[i + 1] - LeftOf(Flat(floorPts[i + 1] - floorPts[i]).normalized) * floorHw[i + 1];
            Vector3 d = Flat(b - a);
            float len = d.magnitude;
            d /= len;
            var outward = -LeftOf(d);
            var rot = Quaternion.FromToRotation(d, (b - a).normalized) * Face(outward) * Quaternion.Euler(0, 0, 180);
            float drop = Mathf.Min(a.y, b.y) + 33f;
            var scale = new Vector3((len + 2.4f) / cb.size.x * (i % 2 == 0 ? 1 : -1), drop / cb.size.y, Rand(.8f, 1f));
            Put(under, "A01_Cliff", $"Обрыв под полкой {i + 1}", (a + b) * .5f + outward * .03f + Vector3.down * .12f, rot, scale,
                new Vector3(cb.center.x, cb.min.y, 0), Mats["Rock"]);
        }
        // A02 rim blocks on the straight runs: the arm reaches out over the abyss with its top at the floor, the hump at
        // its end (the corner part under the walkway is left out).
        foreach (var (from, to, at) in new[] { ("N21", "N22", .55f), ("N23", "N24", .45f) })
        {
            Vector3 pa = P(from), pb = P(to), d = Flat(pb - pa).normalized;
            float half = Mathf.Lerp(HW(from), HW(to), at);
            var edgePoint = Vector3.Lerp(pa, pb, at) - LeftOf(d) * half;
            var rot = Quaternion.FromToRotation(d, (pb - pa).normalized) * Face(LeftOf(d));
            var rim2 = Put(parent, "A02_Ledge_rim", $"Борт A02 у {from}", edgePoint + Vector3.down * .05f, rot, Vector3.one * .8f, Vector3.zero, Mats["Rock"]);
            foreach (var tr in rim2.GetComponentsInChildren<Transform>(true))
                if (tr.name.StartsWith("A02_corner")) tr.gameObject.SetActive(false);
        }
        // A03 overhangs: a console out of the ridge wall over the ledge (and past its edge), never lower than 3.6 m.
        foreach (var (from, to, at) in new[] { ("N21", "N22", .45f), ("N23", "N24", .55f) })
        {
            Vector3 pa = P(from), pb = P(to), p = Vector3.Lerp(pa, pb, at), d = Flat(pb - pa).normalized;
            float half = Mathf.Lerp(HW(from), HW(to), at);
            const float s = .6f;
            // The console's lowest point is its elbow at the wall line, 2.9 m over the pivot at scale 1.
            Put(parent, "A03_Overhang", $"Навес A03 над полкой у {from}", p + LeftOf(d) * half + Vector3.up * (3.6f - 2.9f * s),
                Face(-LeftOf(d)), Vector3.one * s, Vector3.zero, Mats["Rock"]);
        }
        log.AppendLine($"ledge: {stones} stones, {bigs} boulders");
    }

    static void BuildGorge(Transform parent)
    {
        var ids = Ids("N25", "N30");
        var pts = ids.Select(P).ToArray();
        var hw = new[] { .9f, .85f, .8f, .8f, .9f, 1f };
        float[] above = { 24, 22, 18, 12, 8, 6 };
        MeshObj(parent, "Пол горла", Ribbon("GM_Gorge_Floor", pts, Under(hw), .8f), Mats["Floor"]);
        Walls(parent, "Горло", "GM_Gorge", pts, hw, q => q.y - 3, q => q.y, 8f, Mats["Wall Z6"], false, true, above);
        // One left wall from the ledge start to the mouth: the ridge face over the ledge turns into the gorge's left
        // wall with a clean corner at N25 (separate walls left a slanted wedge there that flung the player).
        var lids = Ids("N21", "N30");
        var lpts = lids.Select(P).ToArray();
        var lhw = new[] { HW("N21"), HW("N22"), HW("N23"), HW("N24"), .9f, .85f, .8f, .8f, .9f, 1f };
        float[] labove = { 30, 30, 30, 30, 26, 22, 18, 12, 8, 6 };
        var loff = Frame(lpts);
        var ledge = lpts.Select((q, i) => q + loff[i] * lhw[i]).ToArray();
        WallBoxes(parent, "Стена хребта: полка и горло слева", "GM_Ridge_Wall", ledge, loff,
            lpts.Select((q, i) => q.y - (i < 4 ? 3.5f : 3f)).ToArray(), lpts.Select((q, i) => q.y + labove[i]).ToArray(),
            // Thin along the ledge (the ridge box stands behind it), thick along the gorge (it is the ridge there):
            // a thick box before the corner would reach into the gorge.
            new[] { 2.5f, 2.5f, 2.5f, 2.5f, 8f, 8f, 8f, 8f, 8f }, Mats["Wall Z5"]);
        // Chockstones wedged between the gorge walls high over the path.
        foreach (var (from, to, at, height) in new[] { ("N26", "N27", .5f, 5.2f), ("N27", "N28", .4f, 6.4f) })
        {
            Vector3 pa = P(from), pb = P(to), p = Vector3.Lerp(pa, pb, at), d = Flat(pb - pa).normalized;
            Fit(parent, Boulders[rng.Next(Boulders.Length)], $"Застрявший камень {from}–{to}", p + Vector3.up * (height + .7f), new Vector3(3.2f, 1.4f, 1.6f), Quaternion.LookRotation(d) * Quaternion.Euler(Jit(6), Jit(10), Jit(8)), Mats["Rock"]);
        }
        Slab(parent, "Перемычка устья (проём 2 × 4 м)", P("N30") + new Vector3(.05f, 5f, -.8f), new Vector3(8f, 2f, 2.4f), Quaternion.Euler(0, -7f, 0), Mats["Rock"]);
    }

    static void BuildPlatform(Transform parent, GameObject boulder)
    {
        // A04: the summit step (its plateau top is the floor, from the mouth up to the lip) on its root down to the cloud.
        var lip = new Vector3(PlatX, LipH, LipZ);
        float tilt = Mathf.Atan2(LipH - PlatH0, LipZ - PlatZ0) * Mathf.Rad2Deg;
        var rot = Quaternion.Euler(-tilt, 0, 0) * Face(Vector3.forward);
        Put(parent, "A04_Summit", "Площадка — ступень вершины A04", lip, rot, Vector3.one, Vector3.zero);
        Put(parent, "A04_Root", "Гора под площадкой A04", lip, rot, Vector3.one, Vector3.zero);
        var rocks = Group(parent, "Валуны площадки");
        foreach (var dx in new[] { -6.1f, -4.6f, -3.55f, 3.55f, 4.6f, 6.1f })
        {
            var size = new Vector3(Rand(1.5f, 1.9f), Rand(1.6f, 2.1f), Rand(1.3f, 1.7f));
            Rock(rocks, boulder, "Валун у кромки", new Vector3(PlatX + dx, LipH - .05f + size.y * .4f, LipZ - .4f), size);
        }
        for (float z = PlatZ0 + 1.5f; z < LipZ - 1; z += 1.9f)
            foreach (var dx in new[] { -6.5f, 6.5f })
            {
                var size = new Vector3(Rand(1.4f, 1.8f), Rand(1.6f, 2.2f), Rand(1.6f, 2.0f));
                float floor = Mathf.Lerp(PlatH0, LipH, (z - PlatZ0) / (LipZ - PlatZ0));
                Rock(rocks, boulder, "Валун у края площадки", new Vector3(PlatX + dx, floor + size.y * .4f, z), size);
            }
    }

    static void BuildRidge(Transform parent)
    {
        // Crags on the ridge crest: they break its straight skyline seen from the platform and from far away.
        var crags = Group(parent, "Глыбы на гребне");
        for (float x = -6; x < 33; x += Rand(4.5f, 7.5f))
        {
            var size = new Vector3(Rand(5, 9), Rand(6, 12), Rand(5, 9));
            Fit(crags, Boulders[rng.Next(Boulders.Length)], "Глыба", new Vector3(x, 17.5f + size.y * .5f, Rand(34, 45)), size, Quaternion.Euler(Jit(8), Rand(0, 360), Jit(8)), Mats["Ridge"]);
        }
        for (float z = 26; z < 48; z += Rand(5, 8))
        {
            var size = new Vector3(Rand(4, 7), Rand(6, 12), Rand(4, 7));
            Fit(crags, Boulders[rng.Next(Boulders.Length)], "Глыба восток", new Vector3(Rand(53, 57), 19.5f + size.y * .5f, z), size, Quaternion.Euler(Jit(8), Rand(0, 360), Jit(8)), Mats["Ridge"]);
        }
        // Faces hidden behind the ledge and gorge walls are left out.
        RockMass(parent, "Хребет-ширма, запад", new Vector3(13.75f, -219.5f, 39.65f), new Vector3(43.5f, 481f, 17.3f), Mats["Ridge"], "NW", true);
        RockMass(parent, "Хребет-ширма, восток", new Vector3(54.5f, -218.5f, 35.15f), new Vector3(8f, 483f, 26.3f), Mats["Ridge"], "NESW", true);
        RockMass(parent, "Основание под горлом", new Vector3(42.5f, -225.4f, 38.65f), new Vector3(23f, 469.2f, 19.3f), Mats["Ridge"], "SW", false);
    }

    static GameObject BuildPanorama(Transform parent, GameObject peak, GameObject godModel)
    {
        var center = Platform;
        var sea = MeshObj(parent, "Облачное море", Quad("GM_Cloud_Sea", center + Vector3.up * -450, 40000), Mats["Cloud"], false);
        var seaCollider = sea.AddComponent<BoxCollider>();
        seaCollider.center = center + Vector3.up * -450.5f;
        seaCollider.size = new Vector3(40000, 1, 40000);
        var peaks = Group(parent, "Бескрайние вершины");
        int count = 0;
        var rings = new[] { (700f, 1500f, 14, -300f, 250f), (1500f, 3500f, 20, -250f, 500f), (3500f, 7500f, 22, -100f, 900f) };
        foreach (var (rMin, rMax, n, topMin, topMax) in rings)
            for (int i = 0; i < n; i++)
            {
                float ang = (i + Rand(-.35f, .35f)) / n * 360f;
                float r = Rand(rMin, rMax);
                float top = Rand(topMin, topMax), bottom = -720;
                // Broad massifs, but never over the platform and, in the near rings, never across the god in the north.
                float h = top - bottom, w = Mathf.Min(h * Rand(1.6f, 2.4f), (r - 300) * 1.6f);
                if (rMax <= 3500)
                {
                    float half = Mathf.Atan2(w * .6f, r) * Mathf.Rad2Deg, clear = 16 + half;
                    float delta = Mathf.DeltaAngle(0, ang);
                    if (Mathf.Abs(delta) < clear) ang = (delta < 0 ? -1 : 1) * clear;
                }
                var pos = center + new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad) * r, bottom, Mathf.Cos(ang * Mathf.Deg2Rad) * r);
                string kit = Peaks[rng.Next(Peaks.Length)];
                var kb = KitP[kit].b;
                Put(peaks, kit, "Вершина " + (++count), pos, Quaternion.Euler(Jit(3), Rand(0, 360), Jit(3)),
                    Div(new Vector3(w, h, w * Rand(.8f, 1.2f)), kb.size), new Vector3(kb.center.x, kb.min.y, kb.center.z));
            }
        if (!godModel) { log.AppendLine("ERROR god model not imported"); return null; }
        var group = new GameObject("MG01 — бог (масштаб и место выставить после блокинга)").transform;
        group.SetParent(parent, false);
        group.localPosition = godPose?.pos ?? Platform + new Vector3(0, -400, 1610);
        group.localRotation = godPose?.rot ?? Quaternion.Euler(0, 180, 0);
        group.localScale = godPose?.scale ?? Vector3.one * 100;
        var god = (GameObject)PrefabUtility.InstantiatePrefab(godModel, group);
        god.name = "MG01";
        // Support peaks under the hands that touch the ground, and the talk point in front of the face.
        var body = god.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.name.Contains("Body"));
        if (body)
        {
            var toGroup = group.worldToLocalMatrix * body.transform.localToWorldMatrix;
            var verts = body.sharedMesh.vertices.Select(v => toGroup.MultiplyPoint3x4(v)).ToArray();
            float minY = verts.Min(v => v.y);
            var contacts = verts.Where(v => v.y < minY + .06f).ToList();
            var clusters = new List<List<Vector3>>();
            foreach (var v in contacts)
            {
                var c = clusters.FirstOrDefault(cl => Flat(cl.Aggregate(Vector3.zero, (s, x) => s + x) / cl.Count - v).magnitude < 3f);
                if (c == null) clusters.Add(new List<Vector3> { v }); else c.Add(v);
            }
            int idx = 0;
            foreach (var cl in clusters.OrderByDescending(c => c.Count).Take(3))
            {
                var c = cl.Aggregate(Vector3.zero, (s, x) => s + x) / cl.Count;
                string kit = Peaks[idx % Peaks.Length];
                var kb = KitP[kit].b;
                Put(group, kit, "Опорная вершина под кистью " + (++idx), new Vector3(c.x, minY - 3.2f, c.z), Quaternion.Euler(0, Rand(0, 360), 0),
                    Div(new Vector3(3.6f, 3.2f, 3.6f), kb.size), new Vector3(kb.center.x, kb.min.y, kb.center.z));
            }
            float top = verts.Max(v => v.y);
            var hat = verts.First(v => v.y >= top - 1e-3f);
            // The hat tip leans aside: the face is found as the band of vertices under the cap near the body's axis.
            var axis = verts.Where(v => v.y > top - 6f && v.y < top - 3.5f).ToArray();
            float axisX = axis.Length > 0 ? axis.Average(v => v.x) : hat.x;
            var head = verts.Where(v => v.y < top - 1.6f && v.y > top - 3.6f && Mathf.Abs(v.x - axisX) < 1.2f).ToArray();
            float front = head.Length > 0 ? head.Max(v => v.z) : hat.z + 1;
            float headX = head.Length > 0 ? head.Average(v => v.x) : axisX;
            var h = new GameObject("H — точка разговора перед лицом (черновик)").transform;
            h.SetParent(group, false);
            h.localPosition = new Vector3(headX, top - 2.6f, front + 1.0f);
            Icon(h.gameObject, 5);
            log.AppendLine($"god: contacts {contacts.Count} in {clusters.Count} clusters; height {top - minY:F1} units");
        }
        return god;
    }

    static void BuildMarkers(Transform parent)
    {
        var spawn = Marker(parent, "Появление (S1: лицом к стене)", P("N00"), 3);
        spawn.localRotation = Quaternion.Euler(0, 180, 0);
        Marker(parent, "CP1", P("N21"), 3);
        Marker(parent, "CP2", P("N23"), 3);
        Marker(parent, "CP3", new Vector3(PlatX + 3, PlatH0 + .1f, PlatZ0 + 3.2f), 3);
        Marker(parent, "M — устье", P("N30"), 4);
        var e = Marker(parent, "E — линия края (2,5 м до кромки)", P("N32"), 6);
        Marker(e, "E — от", new Vector3(-6.5f, 0, 0), 6, false);
        Marker(e, "E — до", new Vector3(6.5f, 0, 0), 6, false);
        Marker(parent, "Кромка (открыта середина 6 м)", new Vector3(PlatX, LipH, LipZ), 6);
        Marker(parent, "D — причал ладони", new Vector3(PlatX, LipH, LipZ + .5f), 4);
        var beats = Group(parent, "Странные моменты");
        Marker(beats, "S1 Пробуждение лицом к стене", new Vector3(-34.5f, 1, -31.5f), 2);
        Marker(beats, "S2 Дыхание горы (весь путь)", new Vector3(-28f, 2, -6f), 2);
        Marker(beats, "S3 Третий угол: пустая ниша, висящая плита", P("N13") + Vector3.up, 2);
        Marker(beats, "S4 Лестница вниз и горловина", (P("N16") + P("N17")) * .5f + Vector3.up, 2);
        Marker(beats, "S5 Каменная рамка", Hollow + new Vector3(3.8f, 1, 0), 2);
        Marker(beats, "S6 Кусты по ветру", P("N23") + Vector3.up * 2, 2);
        Marker(beats, "S7 Тишина перед выходом", P("N28") + Vector3.up, 2);
    }

    static Light BuildSun(Transform root)
    {
        var go = new GameObject("Солнце");
        go.transform.SetParent(root, false);
        go.transform.localRotation = Quaternion.Euler(32, 20, 0);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, .95f, .9f);
        light.intensity = 1.3f;
        light.shadows = LightShadows.Soft;
        return light;
    }

    static GameObject BuildPlayer(Scene scene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        if (!prefab) { log.AppendLine("ERROR player prefab missing"); return null; }
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        player.name = "Игрок для прогона блокаута (при подключении убрать)";
        player.transform.SetPositionAndRotation(LevelOrigin + P("N00") + Vector3.up * .05f, Quaternion.Euler(0, 180, 0));
        var cam = player.GetComponentInChildren<Camera>();
        if (cam)
        {
            cam.farClipPlane = 12000;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = Heights.sky;
            PrefabUtility.RecordPrefabInstancePropertyModifications(cam);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
        return player;
    }

    static GodMountainsAtmosphere.Air Air(string name, string fog, float density, string sky, string ambient, float skyHidden) => new GodMountainsAtmosphere.Air
    { name = name, fog = Hex(fog), density = density, sky = Hex(sky), ambient = Hex(ambient), skyHidden = skyHidden };

    static readonly GodMountainsAtmosphere.Air Heights = Air("Высота", "#ABA0C7", .00027f, "#B9AFD6", "#8F87A8", 0);

    static void BuildAtmosphere(Transform root, Transform route, GameObject player)
    {
        var go = new GameObject("Воздух по маршруту");
        go.transform.SetParent(root, false);
        var atm = go.AddComponent<GodMountainsAtmosphere>();
        var cam = player ? player.GetComponentInChildren<Camera>() : null;
        atm.viewer = cam ? cam.transform : null;
        atm.viewCamera = cam;
        atm.sky = SkyMaterial();
        atm.route = Nodes.Select(n => route.Find(n.id)).ToArray();
        atm.profiles = new[]
        {
            // Inside only a strip of sky is seen straight up; in the cloud none.
            Air("Нутро", "#463E5C", .075f, "#463E5C", "#A39BC8", .8f),
            Air("Облако у рамки", "#E9E5EF", .22f, "#E9E5EF", "#C9C3D6", 1),
            Air("Облако у верха полки", "#E9E5EF", .08f, "#EDE9F2", "#D4CEDF", 1),
            Heights,
        };
        float S(string id)
        {
            float s = 0;
            for (int i = 1; i < Nodes.Length; i++)
            {
                s += (P(Nodes[i].id) - P(Nodes[i - 1].id)).magnitude;
                if (Nodes[i].id == id) return s;
            }
            return s;
        }
        float frame = S("N20") + 3.8f;
        atm.keys = new[]
        {
            new GodMountainsAtmosphere.Key { metres = 0, air = 0 },
            new GodMountainsAtmosphere.Key { metres = frame, air = 0 },
            new GodMountainsAtmosphere.Key { metres = frame + 3, air = 1 },
            new GodMountainsAtmosphere.Key { metres = S("N25"), air = 2 },
            new GodMountainsAtmosphere.Key { metres = S("N26"), air = 2 },
            new GodMountainsAtmosphere.Key { metres = S("N28"), air = 3 },
        };
        log.AppendLine($"air keys: frame {frame:F1} m, N25 {S("N25"):F1}, N26 {S("N26"):F1}, N28 {S("N28"):F1}, total {S("N32"):F1}");
    }

    static Material SkyMaterial()
    {
        // The game's pixel sky with the mountains' lavender: soft clouds, a pale warm sun behind the player, faint moons.
        var shader = Shader.Find("Hukaton/Fantasy Day Sky");
        if (!shader) { log.AppendLine("WARN sky shader missing: solid sky"); return null; }
        var template = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/FantasyDaySky.mat");
        var m = template ? new Material(template) : new Material(shader);
        void C(string id, string hex) { if (m.HasProperty(id)) m.SetColor(id, Hex(hex)); }
        void F(string id, float v) { if (m.HasProperty(id)) m.SetFloat(id, v); }
        C("_ZenithColor", "#8F86BC"); C("_SkyColor", "#A79DCF"); C("_HorizonColor", "#CFC6E2"); C("_GroundColor", "#ABA0C7");
        C("_CloudLight", "#F6F0FA"); C("_CloudShadow", "#A99FC9"); C("_SunTint", "#FFF0D8"); C("_SunEmber", "#FFC9A0");
        C("_GalaxyColor", "#E3D9FF"); C("_MoonColor", "#F2EEFA");
        F("_Exposure", 1); F("_CloudCoverage", .6f); F("_CloudScale", 2.6f); F("_CloudSteps", 4); F("_CloudSoftness", .06f);
        F("_SunRadius", 3); F("_SunHalo", .25f); F("_SunCorona", .4f); F("_SunSpeed", .3f);
        F("_GalaxyVisibility", .1f); F("_StarVisibility", .08f); F("_MoonOpacity", .3f);
        F("_FogStrength", 1); F("_FogStart", 4); F("_FogHeight", 18); F("_FogFalloff", 1);
        AssetDatabase.CreateAsset(m, $"{MatDir}/GM Sky.mat");
        return m;
    }

    static void ApplyHeightsAirToSceneSettings()
    {
        // Saved settings = the clear heights, so the scene is easy to edit; in Play the route air takes over at once.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Heights.fog;
        RenderSettings.fogDensity = Heights.density;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Heights.ambient * 1.35f;
        RenderSettings.ambientEquatorColor = Heights.ambient;
        RenderSettings.ambientGroundColor = Heights.ambient * .5f;
    }

    // ------------------------------------------------------------------ god import

    static GameObject ImportGod()
    {
        string fbx = GodDir + "/MG01.fbx";
        string project = Directory.GetCurrentDirectory();
        string source = Path.GetFullPath(Path.Combine(project, "..", "output", "mountain-god-model-v3"));
        string[] textures = { "T_MG01_Body_BaseColor.png", "T_MG01_Cloth_BaseColor.png", "T_MG01_Leaves_BaseColor.png" };
        if (!File.Exists(Path.Combine(project, fbx)))
        {
            File.Copy(Path.Combine(source, "MG01.fbx"), Path.Combine(project, fbx));
            foreach (var t in textures) File.Copy(Path.Combine(source, "textures", t), Path.Combine(project, GodDir, "Textures", t));
            AssetDatabase.Refresh();
            log.AppendLine("god: copied from " + source);
        }
        var maps = new Dictionary<string, Texture2D>();
        foreach (var t in textures)
        {
            string path = GodDir + "/Textures/" + t;
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 2048;
            ti.alphaIsTransparency = t.Contains("Leaves");
            ti.SaveAndReimport();
            maps[t] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        var body = GodMat("M_MG01_Body", maps[textures[0]], false);
        var cloth = GodMat("M_MG01_Cloth", maps[textures[1]], false);
        var leaves = GodMat("M_MG01_Leaves", maps[textures[2]], true);
        var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
        mi.globalScale = 1;
        mi.useFileScale = true;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.None;
        mi.animationType = ModelImporterAnimationType.None;
        mi.importAnimation = false;
        mi.importCameras = false;
        mi.importLights = false;
        mi.isReadable = false;
        mi.addCollider = false;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_MG01_Body"), body);
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_MG01_Cloth"), cloth);
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_MG01_Leaves"), leaves);
        mi.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
    }

    static Material GodMat(string name, Texture2D map, bool leaves)
    {
        string path = GodDir + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        Matte(m, Color.white);
        m.SetTexture("_BaseMap", map);
        m.SetTexture("_MainTex", map);
        if (leaves)
        {
            m.SetFloat("_AlphaClip", 1);
            m.SetFloat("_Cutoff", .5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0);
            m.renderQueue = 2450;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ materials and shared prefabs

    static Texture2D Tex(string name, bool cutout)
    {
        string path = $"{TexDir}/{name}.png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null) { log.AppendLine("ERROR texture missing " + path); return null; }
        ti.textureType = TextureImporterType.Default;
        ti.filterMode = FilterMode.Point;
        ti.wrapMode = cutout ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        ti.mipmapEnabled = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 256;
        ti.alphaIsTransparency = cutout;
        ti.mipMapsPreserveCoverage = cutout;
        ti.alphaTestReferenceValue = .5f;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void MakeMaterials()
    {
        Material M(string name, Texture2D tex, string tint, float tiling, bool cutout = false)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Matte(m, Hex(tint));
            if (tex)
            {
                m.SetTexture("_BaseMap", tex);
                m.SetTexture("_MainTex", tex);
                m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                m.SetTextureScale("_MainTex", new Vector2(tiling, tiling));
            }
            if (cutout)
            {
                m.SetFloat("_AlphaClip", 1);
                m.SetFloat("_Cutoff", .5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0);
                m.doubleSidedGI = true;
                m.renderQueue = 2450;
            }
            AssetDatabase.CreateAsset(m, $"{MatDir}/GM {name}.mat");
            return m;
        }
        // UVs are in metres: one 256 px tile covers 4 m (32 hard pixels per metre, like the approved cave).
        const float tile = .25f;
        var rockSide = M("Rock Side", Tex("T_GM_RockSide", false), "#FFFFFF", tile);
        var rockTop = M("Rock Top", Tex("T_GM_RockTop", false), "#FFFFFF", tile);
        // Inside the mountain: the same stone, colder and darker (one stroke language everywhere).
        var innerSide = M("Inner Side", Tex("T_GM_RockSide", false), "#ABA3C2", tile);
        var innerTop = M("Inner Top", Tex("T_GM_RockTop", false), "#B5ADC9", tile);
        var path = M("Path", Tex("T_GM_Path", false), "#FFFFFF", tile);
        var pathInner = M("Path Inner", Tex("T_GM_Path", false), "#A29BB2", tile);
        var carved = M("Carved", Tex("T_GM_Carved", false), "#FFFFFF", tile);
        var frame = M("Frame", Tex("T_GM_Carved", false), "#C2B9CF", tile);
        var plate = M("Plate", Tex("T_GM_InnerTop", false), "#C9C1D3", tile);
        // The kit's far massifs have UVs in metres too: one tile per 60 m.
        var far = M("Far", Tex("T_GM_Far", false), "#FFFFFF", 1 / 60f);
        var cloud = M("Cloud", Tex("T_GM_Cloud", false), "#FFFFFF", .006f);
        var foliage = M("Foliage", Tex("T_GM_Foliage", true), "#FFFFFF", 1, true);
        var crack = M("Crack", null, "#1E1A24", 1);
        var hole = M("Hole", null, "#2A2420", 1);
        var litter = M("Leaf litter", null, "#5C5A47", 1);
        void L(string name, Material top, Material side) => Mats[name] = new Look { top = top, side = side };
        L("Floor", path, rockSide);
        L("Floor Inner", pathInner, innerSide);
        L("Wall Z1", innerTop, innerSide);
        L("Wall Z2", innerTop, innerSide);
        L("Wall Z3", innerTop, innerSide);
        L("Wall Z4", rockTop, rockSide);
        L("Wall Z5", rockTop, rockSide);
        L("Wall Z6", rockTop, rockSide);
        L("Roof", innerTop, innerSide);
        L("Post", innerTop, innerSide);
        L("Steps", carved, carved);
        L("Frame", frame, frame);
        L("Plate", plate, plate);
        L("Crack", crack, crack);
        L("Hole", hole, hole);
        L("Leaf litter", litter, litter);
        L("Boulder", rockTop, rockSide);
        L("Rock", rockTop, rockSide);
        L("Ridge", rockTop, rockSide);
        L("Peak", far, far);
        L("Cloud", cloud, cloud);
        L("Foliage", foliage, foliage);
    }

    static void Matte(Material m, Color color)
    {
        m.SetColor("_BaseColor", color);
        m.SetColor("_Color", color);
        m.SetFloat("_Smoothness", 0);
        m.SetFloat("_Metallic", 0);
        m.SetFloat("_SpecularHighlights", 0);
        m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetFloat("_EnvironmentReflections", 0);
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
    }

    static void Assign(GameObject go, Mesh mesh, Look look)
    {
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mesh.subMeshCount > 1 ? new[] { look.top, look.side } : new[] { look.side };
    }

    static void BuildLightShafts(Transform parent)
    {
        // Cold light through the seams of the roof (Z0, Z1) and white light from the open top of the hollow (Z4):
        // additive shafts (the tutorial cave's godray shader) and weak lamps without shadows under them.
        var shader = Shader.Find("Hukaton/Cave Godray");
        if (!shader) { log.AppendLine("WARN godray shader missing: no light shafts"); return; }
        var streaks = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/TutorialCave/Textures/T_CV_Godray.png");
        Material Shaft(string name, string colour, float intensity)
        {
            var m = new Material(shader);
            if (streaks) m.SetTexture("_BaseMap", streaks);
            m.SetColor("_Color", Hex(colour));
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_NearFade", 2.5f);
            m.SetFloat("_EdgeSoftness", 3f);
            m.SetFloat("_Scroll", .01f);
            AssetDatabase.CreateAsset(m, $"{MatDir}/GM {name}.mat");
            return m;
        }
        var cold = Shaft("Light shaft cold", "#8C9DFF", .35f);
        var white = Shaft("Light shaft white", "#F2EEFF", .22f);
        int n = 0;
        void Beam(string name, Vector3 top, Vector2 topSize, Vector3 bottom, Vector2 bottomSize, float yaw, Material m, string lamp, float lampIntensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = SaveMesh(ShaftMesh($"GM_Shaft_{++n}", top, topSize, bottom, bottomSize, yaw));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var l = new GameObject("Свет под лучом").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = bottom + Vector3.up * 1.2f;
            l.type = LightType.Point;
            l.color = Hex(lamp);
            l.intensity = lampIntensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }
        // S1: the brightest spot of the pocket is by its only way out, so the player turns to it.
        Beam("Луч у выхода из кармана", new Vector3(-34.8f, 5.6f, -25.9f), new Vector2(1.5f, .28f), new Vector3(-34.6f, 0, -24.4f), new Vector2(1.2f, .9f), 4, cold, "#9AA8FF", 1.6f, 6);
        // Z1: the seams get lighter along the way.
        var seams = new[] { ("N02", "N03", .45f, .9f), ("N03", "N04", .6f, 1.2f), ("N05", "N06", .5f, 1.6f) };
        foreach (var (a, b, t, power) in seams)
        {
            var p = Vector3.Lerp(P(a), P(b), t);
            float yaw = Quaternion.LookRotation(Flat(P(b) - P(a))).eulerAngles.y;
            Beam($"Луч в щели {a}–{b}", p + Vector3.up * 5.1f, new Vector2(1.4f, .18f), p + new Vector3(.3f, 0, .5f), new Vector2(1f, .6f), yaw, cold, "#9AA8FF", power, 5);
        }
        // Z4: the hollow is open to the cloud above.
        Beam("Свет сверху в карман с рамкой", Hollow + Vector3.up * 13f, new Vector2(4.6f, 4.2f), Hollow + new Vector3(.3f, 0, .2f), new Vector2(4.2f, 3.8f), 15, white, "#F4F0FF", 2.2f, 8);
        // Faint cold light from the roof seams all the way inside (Z0–Z3), high on alternate walls: it rakes across the
        // walls' planes and hollows so they read in the dark.
        int lamps = 0;
        var inner = Ids("N00", "N19");
        float next = 2f, walked = 0;
        for (int i = 0; i + 1 < inner.Length; i++)
        {
            Vector3 a = P(inner[i]), b = P(inner[i + 1]), d = Flat(b - a);
            float len = d.magnitude;
            d /= Mathf.Max(len, 1e-4f);
            while (next <= walked + len)
            {
                float t = (next - walked) / len;
                float hw = Mathf.Lerp(HW(inner[i]), HW(inner[i + 1]), t);
                var at = Vector3.Lerp(a, b, t) + LeftOf(d) * ((lamps % 2 == 0 ? 1 : -1) * Mathf.Max(hw - .25f, .3f)) + Vector3.up * 3.3f;
                var l = new GameObject("Холодный свет из щели " + (++lamps)).AddComponent<Light>();
                l.transform.SetParent(parent, false);
                l.transform.localPosition = at;
                l.type = LightType.Point;
                l.color = Hex(lamps % 3 == 0 ? "#C9B8FF" : "#9FB0FF");
                l.intensity = 1.5f;
                l.range = 7f;
                l.shadows = LightShadows.None;
                next += 6.5f;
            }
            walked += len;
        }
        log.AppendLine($"light shafts: {n}, inner lamps: {lamps}");
    }

    /// <summary>A light shaft: a box-like volume from a top rectangle to a bottom rectangle, open ends, smooth normals
    /// (the godray shader fades the faces seen edge-on).</summary>
    static Mesh ShaftMesh(string name, Vector3 top, Vector2 topSize, Vector3 bottom, Vector2 bottomSize, float yaw)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        Vector3[] Rect(Vector3 c, Vector2 s) => new[] { c + rot * new Vector3(-s.x, 0, -s.y) * .5f, c + rot * new Vector3(s.x, 0, -s.y) * .5f, c + rot * new Vector3(s.x, 0, s.y) * .5f, c + rot * new Vector3(-s.x, 0, s.y) * .5f };
        var a = Rect(top, topSize);
        var b = Rect(bottom, bottomSize);
        var fb = new FB();
        for (int k = 0; k < 4; k++)
        {
            int k1 = (k + 1) % 4;
            fb.Quad(b[k], b[k1], a[k1], a[k], new Vector2(k / 4f, 0), new Vector2((k + 1) / 4f, 0), new Vector2((k + 1) / 4f, 1), new Vector2(k / 4f, 1));
        }
        return fb.ToMesh(name);
    }

    static void BuildPlants(Transform parent, GameObject shrubPrefab, GameObject rosettePrefab)
    {
        // Foliage is passable (no colliders). Rare, in sheltered spots at the foot of walls, never in the middle of the path.
        int shrubs = 0, rosettes = 0;
        void Put(GameObject prefab, string name, Vector3 at, Quaternion rot, float scale)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.localPosition = at;
            go.transform.localRotation = rot;
            go.transform.localScale = Vector3.one * scale;
            if (prefab == shrubPrefab) shrubs++; else rosettes++;
        }
        Vector3 WallFoot(string a, string b, float t, bool left, float inset)
        {
            Vector3 pa = P(a), pb = P(b), p = Vector3.Lerp(pa, pb, t), d = Flat(pb - pa).normalized;
            float hw = Mathf.Lerp(HW(a), HW(b), t);
            return p + LeftOf(d) * (left ? 1 : -1) * (hw - inset) + Vector3.up * .01f;
        }
        Quaternion Yaw() => Quaternion.Euler(0, Rand(0, 360), 0);
        // Z1 slot: two rosettes in the cracks where light falls.
        Put(rosettePrefab, "Розетка в щели 1", WallFoot("N02", "N03", .55f, false, .18f), Yaw(), .8f);
        Put(rosettePrefab, "Розетка в щели 2", WallFoot("N04", "N05", .4f, true, .18f), Yaw(), .7f);
        // Z4 hollow: under the open sky.
        Put(rosettePrefab, "Розетка в кармане 1", Hollow + new Vector3(-1.2f, .01f, 2.3f), Yaw(), 1f);
        Put(rosettePrefab, "Розетка в кармане 2", Hollow + new Vector3(.6f, .01f, -2.5f), Yaw(), .85f);
        // Z5 ledge: S6 — all shrubs bent east, the way the player goes, against the ridge wall.
        float[] at = { .3f, .75f };
        string[] ids = Ids("N21", "N25");
        for (int i = 0; i + 1 < ids.Length; i++)
        {
            var d = Flat(P(ids[i + 1]) - P(ids[i])).normalized;
            var pos = WallFoot(ids[i], ids[i + 1], at[i % 2], true, .3f);
            Put(shrubPrefab, $"Куст A07 на полке {i + 1} (по ветру)", pos, Quaternion.LookRotation(d) * Quaternion.Euler(0, Jit(12), 0), Rand(.95f, 1.2f));
            Put(rosettePrefab, $"Розетка на полке {i + 1}", WallFoot(ids[i], ids[i + 1], at[(i + 1) % 2], true, .2f), Yaw(), Rand(.8f, 1.1f));
        }
        // Z6 gorge and Z7 platform.
        Put(rosettePrefab, "Розетка в горле", WallFoot("N26", "N27", .5f, false, .18f), Yaw(), .75f);
        Put(rosettePrefab, "Розетка у устья", WallFoot("N29", "N30", .6f, true, .2f), Yaw(), .8f);
        foreach (var (x, z) in new[] { (-5.4f, 51.4f), (5.6f, 54.8f), (-4.9f, 60.2f) })
            Put(rosettePrefab, "Розетка на площадке", new Vector3(PlatX + x, Mathf.Lerp(PlatH0, LipH, (z - PlatZ0) / (LipZ - PlatZ0)) + .01f, z), Yaw(), Rand(.9f, 1.2f));
        Put(shrubPrefab, "Куст A07 у края площадки", new Vector3(PlatX + 5.2f, Mathf.Lerp(PlatH0, LipH, .25f) + .01f, PlatZ0 + 3.5f), Quaternion.Euler(0, -20, 0), 1.1f);
        log.AppendLine($"plants: {shrubs} shrubs, {rosettes} rosettes");
    }

    // ------------------------------------------------------------------ scene helpers

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Transform Marker(Transform parent, string name, Vector3 local, int icon, bool withIcon = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        if (withIcon) Icon(go, icon);
        return go.transform;
    }

    static void Icon(GameObject go, int index)
    {
        var content = EditorGUIUtility.IconContent("sv_label_" + Mathf.Clamp(index, 0, 7));
        if (content != null && content.image) EditorGUIUtility.SetIconForObject(go, (Texture2D)content.image);
    }

    /// <summary>A crooked, faceted block with a mesh collider equal to what is drawn. jitter &lt; 0: by size; 0: exact box.
    /// Corners never come closer to the walkway than they were (see Protect).</summary>
    static GameObject Box(Transform parent, string name, Vector3 local, Vector3 size, Quaternion rot, Look mat, float jitter = -1, int midRings = -1, bool growX = false, bool strict = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = rot;
        if (jitter < 0) jitter = Mathf.Clamp(Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .15f, .04f, .5f);
        if (midRings < 0) midRings = size.y > 5 ? 1 : 0;
        var t = go.transform;
        var mesh = SaveMesh(Block($"GM_Block_{meshCount:0000}", size, v => levelRoot.InverseTransformPoint(t.TransformPoint(v)), jitter, midRings, growX, strict));
        Assign(go, mesh, mat);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    static void Rock(Transform parent, GameObject boulder, string name, Vector3 local, Vector3 size)
        => Fit(parent, Boulders[rng.Next(Boulders.Length)], name, local, size, Quaternion.Euler(Jit(6), Rand(0, 360), Jit(6)));

    // ------------------------------------------------------------------ kit

    static readonly Vector3 PlateSize = new Vector3(2.2f, .45f, .35f);

    static void ImportKit()
    {
        KitP.Clear();
        var remap = new Dictionary<string, Material>
        {
            { "GM_Top", Mats["Rock"].top }, { "GM_Side", Mats["Rock"].side }, { "GM_FarTop", Mats["Peak"].top }, { "GM_FarSide", Mats["Peak"].side },
            { "GM_Leaf", Mats["Foliage"].side }, { "GM_Crack", Mats["Crack"].side },
        };
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
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
            string name = Path.GetFileNameWithoutExtension(path);
            bool plant = name.StartsWith("A07") || name.StartsWith("A08"), far = name.StartsWith("A09");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = name.Replace('_', ' ');
            int colliders = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                // Painted cracks are decals: no collider, no shadow. Plants are passable; far massifs out of reach.
                if (mf.name.EndsWith("_crack")) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; continue; }
                if (plant || far) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = true;
                colliders++;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{go.name}.prefab");
            Object.DestroyImmediate(go);
            var kit = new Kit { prefab = prefab };
            KitBounds(kit);
            KitP[name] = kit;
            var b = kit.b;
            log.AppendLine($"kit {name}: {b.size.x:F2} × {b.size.y:F2} × {b.size.z:F2}, min ({b.min.x:F2}; {b.min.y:F2}; {b.min.z:F2}), colliders {colliders}");
        }
        // Which way the kit came in: the walls' flat front (Blender -Y) is the bounds side next to zero.
        var wb = KitP["A01_Wall_A"].b;
        kitFix = Mathf.Abs(wb.max.z) < Mathf.Abs(wb.min.z) ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
        log.AppendLine($"kit front: {(Mathf.Abs(wb.max.z) < Mathf.Abs(wb.min.z) ? "+Z" : "-Z")}");
    }

    static void KitBounds(Kit kit)
    {
        var root = kit.prefab.transform.worldToLocalMatrix;
        var pts = new List<Vector3>();
        foreach (var mf in kit.prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh || mf.name.EndsWith("_crack")) continue;
            var m = root * mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices) pts.Add(m.MultiplyPoint3x4(v));
        }
        var b = new Bounds(pts[0], Vector3.zero);
        foreach (var p in pts) b.Encapsulate(p);
        kit.b = b;
        var top = pts.Where(p => p.y > b.max.y - .6f).ToList();
        kit.topCentre = new Vector3(top.Average(p => p.x), b.max.y, top.Average(p => p.z));
    }

    /// <summary>Rotation that turns a kit asset's front (Blender -Y) toward `f`, upright.</summary>
    static Quaternion Face(Vector3 f) => Quaternion.LookRotation(Flat(f).normalized, Vector3.up) * kitFix;

    /// <summary>A kit instance: `anchor` (prefab space, before scaling) lands on `at` (parent space).</summary>
    static GameObject Put(Transform parent, string kit, string name, Vector3 at, Quaternion rot, Vector3 scale, Vector3 anchor, Look look = null)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(KitP[kit].prefab, parent);
        go.name = name;
        Place(go.transform, at, rot, scale, anchor);
        if (look != null) Paint(go, look);
        return go;
    }

    static void Place(Transform t, Vector3 at, Quaternion rot, Vector3 scale, Vector3 anchor)
    {
        t.localRotation = rot;
        t.localScale = scale;
        t.localPosition = at - rot * Vector3.Scale(scale, anchor);
    }

    /// <summary>A kit asset stretched into a box: centre and size along `rot`'s own axes; its front looks along `rot`'s z.</summary>
    static GameObject Fit(Transform parent, string kit, string name, Vector3 centre, Vector3 size, Quaternion rot, Look look = null)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(KitP[kit].prefab, parent);
        go.name = name;
        FitTo(go.transform, kit, centre, size, rot);
        if (look != null) Paint(go, look);
        return go;
    }

    static void FitTo(Transform t, string kit, Vector3 centre, Vector3 size, Quaternion rot)
    {
        var b = KitP[kit].b;
        Place(t, centre, rot * kitFix, Div(size, b.size), b.center);
    }

    static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);

    /// <summary>Swap the kit's own rock materials for a zone's (inner walls are darker and colder).</summary>
    static void Paint(GameObject go, Look look)
    {
        Material top = Mats["Rock"].top, side = Mats["Rock"].side;
        if (look.top == top && look.side == side) return;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var ms = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i] == top) { ms[i] = look.top; changed = true; }
                else if (ms[i] == side) { ms[i] = look.side; changed = true; }
            }
            if (changed) r.sharedMaterials = ms;
        }
    }

    /// <summary>Heavy roof slabs (kit), one per ~5 m of width, each a little crooked.</summary>
    static void Slab(Transform parent, string name, Vector3 centre, Vector3 size, Quaternion rot, Look look)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt(size.x / 5.2f));
        float each = size.x / n;
        for (int i = 0; i < n; i++)
        {
            var off = rot * new Vector3(-size.x * .5f + each * (i + .5f), n > 1 ? Jit(.12f) : 0, 0);
            var piece = new Vector3(each + (n > 1 ? .35f : 0), size.y, size.z * (n > 1 ? Rand(.92f, 1.04f) : 1));
            var tilt = n > 1 ? Quaternion.Euler(Jit(1.5f), Jit(2.5f), Jit(1.5f)) : Quaternion.identity;
            Fit(parent, i % 2 == 0 ? "Roof_slab_A" : "Roof_slab_B", n > 1 ? $"{name}.{i + 1}" : name, centre + off, piece, rot * tilt, look);
        }
    }

    static readonly (string kit, float w)[] WallKits = { ("A01_Wall_A", 12.4f), ("A01_Wall_B", 13.1f), ("A01_Wall_C", 7.8f) };

    /// <summary>One run of A01 kit walls along a straight line: `outward` points into the rock (away from the path), the
    /// pieces' flat fronts lie on the line (`back` m behind it), from y0 to y1. Neighbours overlap 0.6 m, every other one
    /// a few cm further back so coplanar fronts never flicker; some are mirrored.</summary>
    static void Row(Transform group, string name, Vector3 start, Vector3 dir, Vector3 outward, float length, float y0, float y1, float thick, float back, Look look, ref int piece)
    {
        float h = y1 - y0;
        bool cliff = h > 17f;
        int k = Mathf.Max(1, Mathf.RoundToInt(length / (cliff ? 13f : 9.5f)));
        float each = length / k;
        for (int j = 0; j < k; j++)
        {
            float s0 = j * each - (j > 0 ? .6f : 0), s1 = (j + 1) * each + (j < k - 1 ? .6f : 0);
            string kit = "A01_Cliff";
            if (!cliff)
            {
                var fits = WallKits.Where(w => (s1 - s0) / w.w > .55f && (s1 - s0) / w.w < 1.45f).ToArray();
                kit = fits.Length > 0 ? fits[(piece + rng.Next(fits.Length)) % fits.Length].kit
                    : WallKits.OrderBy(w => Mathf.Abs(Mathf.Log((s1 - s0) / w.w))).First().kit;
            }
            var b = KitP[kit].b;
            float sx = (s1 - s0) / b.size.x * (rng.Next(2) == 0 ? 1 : -1), sy = h / b.size.y, sz = Mathf.Clamp(thick / b.size.z, .6f, 2f);
            float recess = back + ((piece & 1) == 0 ? 0 : .04f) + .015f * (piece % 3);
            var front = start + dir * ((s0 + s1) * .5f) + outward * recess;
            Put(group, kit, $"{name}.{j + 1}", new Vector3(front.x, y0, front.z), Face(-outward), new Vector3(sx, sy, sz),
                new Vector3(b.center.x, b.min.y, 0), look);
            piece++;
        }
    }

    /// <summary>A block of mountain made of kit cliffs: tiers of A01 cliffs on the listed faces (N, E, S, W) down
    /// 60 m from the top, the top tier set back; boulders on the crest; below, the A04 root down to the cloud.</summary>
    static void RockMass(Transform parent, string name, Vector3 centre, Vector3 size, Look look, string faces, bool cap)
    {
        var g = Group(parent, name);
        var h = size * .5f;
        float top = centre.y + h.y, bottom = centre.y - h.y, low = Mathf.Max(bottom, top - 60f);
        var all = new[]
        {
            ('S', centre + new Vector3(-h.x, 0, -h.z), Vector3.right, Vector3.forward, size.x, size.z),
            ('E', centre + new Vector3(h.x, 0, -h.z), Vector3.forward, Vector3.left, size.z, size.x),
            ('N', centre + new Vector3(h.x, 0, h.z), Vector3.left, Vector3.back, size.x, size.z),
            ('W', centre + new Vector3(-h.x, 0, h.z), Vector3.back, Vector3.right, size.z, size.x),
        };
        int piece = 0;
        foreach (var (face, start, dir, inward, length, depth) in all)
        {
            if (faces.IndexOf(face) < 0) continue;
            float y1 = top;
            for (int tier = 0; tier < 4 && y1 > low + 1; tier++)
            {
                float y0 = Mathf.Max(bottom - 1, y1 - Rand(17f, 23f));
                if (y0 < low && y1 - low < 8) y0 = Mathf.Max(bottom - 1, y1 - 8);
                float back = tier == 0 && y1 - y0 > 6 ? Rand(.6f, 1.6f) : Rand(0, .3f);
                Row(g, $"{name} {face}{tier + 1}", new Vector3(start.x, 0, start.z), dir, inward, length, y0, y1, Mathf.Min(depth * .5f + 1, 9f), back, look, ref piece);
                y1 = y0 + Rand(1f, 2.5f);
            }
        }
        if (cap)
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt(size.x / 9f)), nz = Mathf.Max(1, Mathf.RoundToInt(size.z / 9f));
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    var cs = new Vector3(size.x / nx * Rand(1f, 1.15f), Rand(3f, 6f), size.z / nz * Rand(1f, 1.15f));
                    cs.x = Mathf.Min(cs.x, size.x); cs.z = Mathf.Min(cs.z, size.z);
                    var c = new Vector3(centre.x - h.x + (i + .5f) * size.x / nx, top - 2.5f + cs.y * .5f, centre.z - h.z + (k + .5f) * size.z / nz);
                    c.x = Mathf.Clamp(c.x, centre.x - h.x + cs.x * .5f, centre.x + h.x - cs.x * .5f);
                    c.z = Mathf.Clamp(c.z, centre.z - h.z + cs.z * .5f, centre.z + h.z - cs.z * .5f);
                    Fit(g, Boulders[rng.Next(Boulders.Length)], $"{name}: гребень", c, cs, Quaternion.Euler(Jit(4), (rng.Next(2) * 180) + Jit(6), Jit(4)), look);
                }
        }
        if (bottom < low - 5)
        {
            // The mountain body under the faces: the root's top on the block's footprint, its foot in the cloud.
            var rk = KitP["A04_Root"];
            var scale = new Vector3(size.x / 20f, (low + 3 - bottom) / rk.b.size.y, size.z / 15f);
            Put(g, "A04_Root", $"{name}: тело горы", new Vector3(centre.x, low + 3, centre.z), kitFix, scale, rk.topCentre, look);
        }
    }

    static GameObject MeshObj(Transform parent, string name, Mesh mesh, Look mat, bool collider = true)
    {
        mesh = SaveMesh(mesh);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Assign(go, mesh, mat);
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    static Mesh SaveMesh(Mesh mesh)
    {
        if (AssetDatabase.Contains(mesh)) return mesh;
        AssetDatabase.CreateAsset(mesh, $"{MeshDir}/{mesh.name}.asset");
        meshCount++;
        return mesh;
    }

    /// <summary>Both side walls of a corridor along its edges: straight box segments, each with its own box collider
    /// equal to what is drawn (convex boxes keep the character controller steady along walls and in corners).</summary>
    static void Walls(Transform parent, string label, string meshName, Vector3[] pts, float[] hw, Func<Vector3, float> bottom, Func<Vector3, float> top,
        float thick, Look mat, bool left, bool right, float[] aboveOverride = null)
    {
        var off = Frame(pts);
        float[] tops = aboveOverride != null ? pts.Select((p, i) => p.y + aboveOverride[Mathf.Min(i, aboveOverride.Length - 1)]).ToArray() : pts.Select(top).ToArray();
        float[] bottoms = pts.Select(bottom).ToArray();
        if (left)
        {
            var edge = pts.Select((p, i) => p + off[i] * hw[i]).ToArray();
            WallBoxes(parent, label + ": стена слева", meshName + "_L", edge, off, bottoms, tops, thick, mat);
        }
        if (right)
        {
            var edge = pts.Select((p, i) => p - off[i] * hw[i]).ToArray();
            WallBoxes(parent, label + ": стена справа", meshName + "_R", edge, off.Select(o => -o).ToArray(), bottoms, tops, thick, mat);
        }
    }

    static void WallBoxes(Transform parent, string label, string meshName, Vector3[] edge, Vector3[] outDir, float[] bottom, float[] top, float thick, Look mat)
        => WallBoxes(parent, label, meshName, edge, outDir, bottom, top, Enumerable.Repeat(thick, edge.Length).ToArray(), mat);

    static void WallBoxes(Transform parent, string label, string meshName, Vector3[] edge, Vector3[] outDir, float[] bottom, float[] top, float[] thicks, Look mat)
    {
        // A01 kit walls along the corridor edge: per segment one or more pieces stretched to fit, their flat fronts on
        // the edge (nothing reaches over the path below 3.4 m), a second tier of cliffs set back where the wall is
        // taller. A segment grows past its ends only toward an outer corner (where the next segment turns toward the
        // path): there the growth stays behind the neighbour's front; at inner corners the pieces meet at the corner.
        var group = Group(parent, label);
        Vector3 Lvl(Vector3 local) => levelRoot.InverseTransformPoint(group.TransformPoint(local));
        float groupY = Lvl(Vector3.zero).y;
        int n = edge.Length;
        var dirs = new Vector3[n - 1];
        var outs = new Vector3[n - 1];
        var lens = new float[n - 1];
        for (int i = 0; i + 1 < n; i++)
        {
            var d = Flat(edge[i + 1] - edge[i]);
            lens[i] = d.magnitude;
            dirs[i] = lens[i] > 1e-4f ? d / lens[i] : Vector3.forward;
            var o = Flat(outDir[i] + outDir[i + 1]);
            outs[i] = (o - dirs[i] * Vector3.Dot(o, dirs[i])).normalized;
        }
        int piece = 0;
        for (int i = 0; i + 1 < n; i++)
        {
            if (lens[i] < .05f) continue;
            float thick = thicks[Mathf.Min(i, thicks.Length - 1)];
            float depth = Mathf.Clamp(thick, 2.8f, 9f);
            float grow0 = 0, grow1 = 0;
            if (i > 0 && lens[i - 1] > .05f && Vector3.Dot(dirs[i], outs[i - 1]) < -.02f)
                grow0 = Mathf.Min(depth * Mathf.Tan(Mathf.Acos(Mathf.Clamp(Vector3.Dot(dirs[i - 1], dirs[i]), -1, 1))), 3f) + .5f;
            if (i + 2 < n && lens[i + 1] > .05f && Vector3.Dot(dirs[i], outs[i + 1]) > .02f)
                grow1 = Mathf.Min(depth * Mathf.Tan(Mathf.Acos(Mathf.Clamp(Vector3.Dot(dirs[i], dirs[i + 1]), -1, 1))), 3f) + .5f;
            float floor = Mathf.Max(Walk(Lvl(edge[i] - outs[i] * .3f)).floor, Walk(Lvl(edge[i + 1] - outs[i] * .3f)).floor) - groupY;
            float lo = Mathf.Min(bottom[i], bottom[i + 1]), hi = Mathf.Max(top[i], top[i + 1]);
            float t1 = Mathf.Min(hi, floor + Rand(11.5f, 13.5f));
            if (hi - t1 < 3f) t1 = hi;
            var start = edge[i] - dirs[i] * grow0;
            float length = lens[i] + grow0 + grow1;
            Row(group, $"{label} {i + 1}", start, dirs[i], outs[i], length, lo, t1, thick, 0, mat, ref piece);
            if (t1 < hi)
            {
                float shift = Rand(0, 2.5f);
                Row(group, $"{label} {i + 1} верх", start - dirs[i] * shift, dirs[i], outs[i], length + shift, t1 - Rand(1f, 2.5f), hi, thick, Rand(.6f, 1.6f), mat, ref piece);
            }
        }
    }

    /// <summary>Floor half widths reaching 1.3 m under the walls: the floor's own edge never touches the player, and the
    /// chipped, stepped feet of the kit walls never open a view under the floor.</summary>
    static float[] Under(float[] hw, float extra = 1.3f) => hw.Select(w => w + extra).ToArray();

    static void Post(Transform parent, string name, Vector3 at, Vector3 dir, bool left, float hw, float height)
    {
        var side = LeftOf(dir) * (left ? 1 : -1);
        var c = at + side * (hw + .8f) + Vector3.up * (height * .5f - 1);
        Fit(parent, "A01_Wall_C", name, c, new Vector3(1.6f, height, 1.6f), Quaternion.LookRotation(-side), Mats["Post"]);
    }

    static void OuterPost(Transform parent, string name, Vector3 before, Vector3 at, Vector3 after, float hw, float height)
    {
        Vector3 d1 = Flat(at - before).normalized, d2 = Flat(after - at).normalized;
        float turn = d1.x * d2.z - d1.z * d2.x; // > 0: left turn
        var n = (LeftOf(d1) + LeftOf(d2)).normalized * (turn > 0 ? -1 : 1);
        var c = at + n * (hw + 1.2f) + Vector3.up * (height * .5f - 1);
        Fit(parent, "A01_Wall_C", name, c, new Vector3(2.4f, height, 2.4f), Quaternion.LookRotation(-n), Mats["Post"]);
    }

    static string[] Ids(string from, string to)
    {
        int a = Array.FindIndex(Nodes, n => n.id == from), b = Array.FindIndex(Nodes, n => n.id == to);
        return Nodes.Skip(a).Take(b - a + 1).Select(n => n.id).ToArray();
    }

    static Vector3 V(float x, float z) => new Vector3(x, 0, z);
    static float[] Fill(int n, float v) => Enumerable.Repeat(v, n).ToArray();
    static float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static float Jit(float a) => Rand(-a, a);
    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ------------------------------------------------------------------ geometry

    /// <summary>Faceted mesh: every triangle has its own vertices (flat PSX shading). Submesh 0: faces looking up
    /// (path, lichen tops), submesh 1: the rest. UVs in metres, planar per triangle.</summary>
    sealed class MB
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> top = new List<int>();
        readonly List<int> side = new List<int>();
        public Vector2 uvOffset;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Tri(a, b, c, outward);
            Tri(a, c, d, outward);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(n, outward) < 0) { var tmp = b; b = c; c = tmp; n = -n; }
            n.Normalize();
            int i = v.Count;
            var tan = Mathf.Abs(n.y) > .7f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized;
            foreach (var p in new[] { a, b, c })
            {
                v.Add(p);
                uv.Add((Mathf.Abs(n.y) > .7f ? new Vector2(p.x, p.z) : new Vector2(Vector3.Dot(p, tan), p.y)) + uvOffset);
            }
            (n.y > .6f ? top : side).AddRange(new[] { i, i + 1, i + 2 });
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.subMeshCount = 2;
            m.SetTriangles(top, 0);
            m.SetTriangles(side, 1);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    // ------------------------------------------------------------------ crooked rock

    /// <summary>Signed plan distance from the walkway (negative inside) and the floor height there.</summary>
    static (float clearance, float floor) Walk(Vector3 p)
    {
        float best = float.MaxValue, floor = 0;
        for (int i = 0; i + 1 < Nodes.Length; i++)
        {
            Vector3 a = P(Nodes[i].id), b = P(Nodes[i + 1].id), ab = Flat(b - a);
            float t = Mathf.Clamp01(Vector3.Dot(Flat(p - a), ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            var q = a + (b - a) * t;
            float hw = Mathf.Max(.3f, Mathf.Lerp(Nodes[i].w, Nodes[i + 1].w, t) * .5f);
            float c = Flat(p - q).magnitude - hw;
            if (c < best) { best = c; floor = q.y; }
        }
        float hollow = Flat(p - Hollow).magnitude - HollowR;
        if (hollow < best) { best = hollow; floor = Hollow.y; }
        // Pocket room Z0.
        var pocket = new[] { V(-34.2f, -23.4f), V(-30.4f, -25.2f), V(-31.2f, -33.2f), V(-38.6f, -32.6f), V(-39.2f, -25.8f), V(-35.9f, -23.4f) };
        float edge = float.MaxValue;
        bool inside = false;
        for (int i = 0, j = pocket.Length - 1; i < pocket.Length; j = i++)
        {
            Vector3 a = pocket[j], b = pocket[i], ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(Flat(p) - a, ab) / ab.sqrMagnitude);
            edge = Mathf.Min(edge, (Flat(p) - (a + ab * t)).magnitude);
            if ((a.z > p.z) != (b.z > p.z) && p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x) inside = !inside;
        }
        float pc = inside ? -edge : edge;
        if (pc < best) { best = pc; floor = 0; }
        // Summit platform.
        float dx = Mathf.Max(Mathf.Abs(p.x - PlatX) - 7, 0), dz = Mathf.Max(PlatZ0 - p.z, p.z - LipZ, 0);
        float plat = (dx > 0 || dz > 0) ? Mathf.Sqrt(dx * dx + dz * dz) : -Mathf.Min(7 - Mathf.Abs(p.x - PlatX), Mathf.Min(p.z - PlatZ0, LipZ - p.z));
        if (plat < best) { best = plat; floor = Mathf.Lerp(PlatH0, LipH, Mathf.InverseLerp(PlatZ0, LipZ, p.z)); }
        return (best, floor);
    }

    /// <summary>Move a corner by a jitter, but never closer to the walkway than it was (or than 0.8 m), and never up or
    /// down where it is next to the walkway below 4 m over the floor (floors, ceilings and hanging slabs keep their height).</summary>
    static Vector3 Protect(Vector3 orig, Vector3 jitter, Func<Vector3, Vector3> toLevel, bool growX = false, bool strict = true)
    {
        // Ends only grow: neighbours along a wall overlap instead of opening a slit to the sky between them.
        jitter.z = Mathf.Abs(jitter.z) * Mathf.Sign(orig.z);
        if (growX) jitter.x = Mathf.Abs(jitter.x) * Mathf.Sign(orig.x);
        var lo = toLevel(orig);
        var (c0, f0) = Walk(lo);
        const float near = .8f;
        if (c0 < near && lo.y < f0 + 4f && lo.y > f0 - 6f) jitter.y = 0;
        float need = Mathf.Min(c0, near) - 1e-3f;
        // Upper tiers may lean over the path, never below 3.2 m over the floor.
        if (!strict && lo.y > f0 + 3.2f) need -= .6f;
        var tries = growX ? new[] { (1f, 1f), (.5f, .5f), (0f, 1f), (0f, .5f) } : new[] { (1f, 1f), (-1f, 1f), (.5f, .5f), (-.5f, .5f), (0f, 1f), (0f, .5f) };
        foreach (var (kx, kz) in tries)
        {
            var cand = orig + new Vector3(jitter.x * kx, jitter.y, jitter.z * kz);
            if (Walk(toLevel(cand)).clearance >= need) return cand;
        }
        var up = orig + new Vector3(0, jitter.y, 0);
        return Walk(toLevel(up)).clearance >= need ? up : orig;
    }

    /// <summary>A block of rings of four corners (bottom, `mid` tiers, top) around a box of `size`: every corner jittered
    /// through Protect, flat faces, metric UVs with a random offset so neighbours don't repeat one pattern.</summary>
    static Mesh Block(string name, Vector3 size, Func<Vector3, Vector3> toLevel, float jitter, int mid, bool growX = false, bool strict = true)
    {
        var h = size * .5f;
        int rings = 2 + mid;
        var ring = new Vector3[rings][];
        for (int r = 0; r < rings; r++)
        {
            float y = r == 0 ? -h.y : r == rings - 1 ? h.y : Mathf.Lerp(-h.y, h.y, (r + Rand(-.2f, .25f)) / (rings - 1));
            ring[r] = new[] { new Vector3(-h.x, y, -h.z), new Vector3(h.x, y, -h.z), new Vector3(h.x, y, h.z), new Vector3(-h.x, y, h.z) };
            if (jitter <= 0) continue;
            float jy = r == rings - 1 ? jitter * 1.6f : r == 0 ? jitter * .2f : jitter;
            // A top face spans its whole block: where the walkway runs over the block, keep that face flat.
            if (r == rings - 1 && BlockUnderWalkway(ring[r], toLevel)) jy = 0;
            float jx = r == 0 ? jitter * .5f : jitter * (r == rings - 1 ? 1.2f : 1f);
            for (int k = 0; k < 4; k++)
                ring[r][k] = Protect(ring[r][k], new Vector3(Jit(jx), Jit(jy), Jit(jx)), toLevel, growX, strict);
        }
        var mb = new MB { uvOffset = new Vector2(Rand(0, 4), Rand(0, 4)) };
        var outs = new[] { Vector3.back, Vector3.right, Vector3.forward, Vector3.left };
        for (int r = 0; r + 1 < rings; r++)
            for (int k = 0; k < 4; k++)
            {
                int k1 = (k + 1) % 4;
                mb.Quad(ring[r][k], ring[r][k1], ring[r + 1][k1], ring[r + 1][k], outs[k]);
            }
        var tp = ring[rings - 1];
        mb.Quad(tp[0], tp[1], tp[2], tp[3], Vector3.up);
        var bt = ring[0];
        mb.Quad(bt[0], bt[1], bt[2], bt[3], Vector3.down);
        return mb.ToMesh(name);
    }

    static bool BlockUnderWalkway(Vector3[] topCorners, Func<Vector3, Vector3> toLevel)
    {
        // Sample the top face; if the walkway floor passes within 2 m above any sample, the face is under the walkway.
        for (float u = 0; u <= 1.001f; u += .125f)
            for (float v = 0; v <= 1.001f; v += .125f)
            {
                var p = toLevel(Vector3.Lerp(Vector3.Lerp(topCorners[0], topCorners[1], u), Vector3.Lerp(topCorners[3], topCorners[2], u), v));
                var (c, f) = Walk(p);
                if (c < .5f && f > p.y - 1f && f < p.y + 2.5f) return true;
            }
        return false;
    }

    /// <summary>Convex hull of a jittered box with two corners cut: a heavy rock of a few big faces (A05).</summary>
    static Mesh HullRock(string name, int seed)
    {
        var r = new System.Random(seed);
        float J(float a) => ((float)r.NextDouble() * 2 - 1) * a;
        var pts = new List<Vector3>();
        for (int i = 0; i < 8; i++)
            pts.Add(new Vector3(((i & 1) == 0 ? -.5f : .5f) + J(.12f), ((i & 2) == 0 ? -.5f : .42f) + J(.08f), ((i & 4) == 0 ? -.5f : .5f) + J(.12f)));
        // Cut two upper corners: replace each by three points pulled toward the centre along the edges.
        foreach (int ci in new[] { 2 + (r.Next(2)), 6 + r.Next(2) })
        {
            var c = pts[ci];
            float cut = .22f + J(.06f);
            pts.Add(c + new Vector3(-Mathf.Sign(c.x) * cut, 0, 0));
            pts.Add(c + new Vector3(0, -cut * .8f, 0));
            pts.Add(c + new Vector3(0, 0, -Mathf.Sign(c.z) * cut));
            pts[ci] = c * .55f;
        }
        var centre = pts.Aggregate(Vector3.zero, (s, x) => s + x) / pts.Count;
        var mb = new MB { uvOffset = new Vector2(J(2), J(2)) };
        int n = pts.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                for (int k = j + 1; k < n; k++)
                {
                    var nrm = Vector3.Cross(pts[j] - pts[i], pts[k] - pts[i]);
                    if (nrm.sqrMagnitude < 1e-8f) continue;
                    nrm.Normalize();
                    bool pos = false, neg = false;
                    for (int l = 0; l < n && !(pos && neg); l++)
                    {
                        if (l == i || l == j || l == k) continue;
                        float d = Vector3.Dot(nrm, pts[l] - pts[i]);
                        if (d > 1e-5f) pos = true; else if (d < -1e-5f) neg = true;
                    }
                    if (pos && neg) continue;
                    mb.Tri(pts[i], pts[j], pts[k], neg ? nrm : -nrm);
                }
        return mb.ToMesh(name);
    }

    /// <summary>A09 distant massif, unit size: two or three crooked tiers with flat shoulders between them.</summary>
    static Mesh MassifMesh(string name, int seed)
    {
        var r = new System.Random(seed);
        float J(float a) => ((float)r.NextDouble() * 2 - 1) * a;
        const int sides = 7;
        var phase = Enumerable.Range(0, sides).Select(i => (i + J(.3f)) * Mathf.PI * 2 / sides).ToArray();
        var radius = Enumerable.Range(0, sides).Select(i => 1 + J(.18f)).ToArray();
        Vector3[] Ring(float y, float rad, Vector3 off) =>
            Enumerable.Range(0, sides).Select(i => off + new Vector3(Mathf.Cos(phase[i]) * rad * radius[i], y + J(.015f), Mathf.Sin(phase[i]) * rad * radius[i])).ToArray();
        var o1 = new Vector3(J(.06f), 0, J(.06f));
        var o2 = o1 + new Vector3(J(.09f), 0, J(.09f));
        var o3 = o2 + new Vector3(J(.06f), 0, J(.06f));
        float y1 = .58f + J(.05f), y2 = .8f + J(.04f);
        var rings = new List<Vector3[]>
        {
            Ring(0, .5f, Vector3.zero),
            Ring(y1, .44f, o1), Ring(y1 + .012f, .3f, o2),            // broad shoulder
            Ring(y2, .26f, o2), Ring(y2 + .01f, .17f, o3),            // second shoulder
            Ring(.97f, .15f, o3), Ring(1f, .11f, o3),                 // flat summit with a chamfer
        };
        var mb = new MB();
        for (int k = 0; k + 1 < rings.Count; k++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                var outward = Flat(rings[k][i] + rings[k][j]) + Vector3.up * .4f;
                mb.Quad(rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i], outward);
            }
        var topRing = rings[rings.Count - 1];
        var topC = topRing.Aggregate(Vector3.zero, (s, x) => s + x) / sides;
        for (int i = 0; i < sides; i++) mb.Tri(topC, topRing[i], topRing[(i + 1) % sides], Vector3.up);
        return mb.ToMesh(name);
    }

    // ------------------------------------------------------------------ plants (atlas UVs, one material)

    sealed class FB
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = v.Count;
            v.AddRange(new[] { a, b, c, d });
            uv.AddRange(new[] { ua, ub, uc, ud });
            t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>A07 wind shrub, ~1.2 m: three low-poly woody branches bent toward +Z (the wind) with leaf twig cards.</summary>
    static Mesh ShrubMesh(string name, int seed)
    {
        var r = new System.Random(seed);
        float J(float a) => ((float)r.NextDouble() * 2 - 1) * a;
        var fb = new FB();
        // Bark: lower half of the atlas; twig cards: upper right quarter.
        Vector2 b0 = new Vector2(0, 0), b1 = new Vector2(1, .48f);
        foreach (float spread in new[] { -.5f, .1f, .55f })
        {
            var pos = new Vector3(J(.08f), 0, J(.08f));
            var dir = Quaternion.Euler(18 + J(6), spread * 70, 0) * Vector3.up;
            float rad = .055f, len = .42f + J(.06f);
            for (int seg = 0; seg < 3; seg++)
            {
                // Bend more with each segment, toward +Z.
                var next = pos + dir * len;
                var side = Vector3.Cross(dir, Vector3.forward).sqrMagnitude > 1e-4f ? Vector3.Cross(dir, Vector3.forward).normalized : Vector3.right;
                var side2 = Vector3.Cross(dir, side).normalized;
                float r1 = rad * .65f;
                var a = new[] { side, side2, -side, -side2 };
                for (int k = 0; k < 4; k++)
                {
                    var s0 = a[k]; var s1 = a[(k + 1) % 4];
                    float u0 = k / 4f, u1 = (k + 1) / 4f;
                    fb.Quad(pos + s0 * rad, pos + s1 * rad, next + s1 * r1, next + s0 * r1,
                        new Vector2(u0, b0.y), new Vector2(u1, b0.y), new Vector2(u1, b1.y), new Vector2(u0, b1.y));
                }
                if (seg > 0)
                {
                    // A twig card of leaves at the joint: two crossed quads, leaning with the branch.
                    float w = .62f, h = .62f;
                    var up = (dir + Vector3.forward * .4f).normalized;
                    foreach (float yaw in new[] { 0f, 60f, 120f })
                    {
                        var right = Quaternion.AngleAxis(yaw + J(20), up) * side;
                        var p0 = next - right * w * .5f;
                        fb.Quad(p0, p0 + right * w, p0 + right * w + up * h, p0 + up * h,
                            new Vector2(.5f, .5f), new Vector2(1, .5f), new Vector2(1, 1), new Vector2(.5f, 1));
                    }
                }
                pos = next;
                rad = r1;
                len *= .85f;
                dir = (Quaternion.Euler(14 + J(6), 0, J(6)) * dir).normalized;
            }
        }
        return fb.ToMesh(name);
    }

    /// <summary>A08 rosette, ~0.5 m: six leaf cards radiating from one point, rising 35–60°.</summary>
    static Mesh RosetteMesh(string name, int seed)
    {
        var r = new System.Random(seed);
        float J(float a) => ((float)r.NextDouble() * 2 - 1) * a;
        var fb = new FB();
        for (int i = 0; i < 6; i++)
        {
            float yaw = i * 60 + J(15), rise = 35 + (float)r.NextDouble() * 25, len = .42f + J(.08f), w = .2f;
            var dir = Quaternion.Euler(-rise, yaw, 0) * Vector3.forward;
            var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            var b = Vector3.up * .01f - right * w * .5f;
            fb.Quad(b, b + right * w, b + right * w + dir * len, b + dir * len,
                new Vector2(.125f, .5f), new Vector2(.375f, .5f), new Vector2(.375f, 1), new Vector2(.125f, 1));
        }
        return fb.ToMesh(name);
    }

    /// <summary>Per point: the left side offset per metre of half width, mitred at bends.</summary>
    static Vector3[] Frame(IList<Vector3> pts)
    {
        int n = pts.Count;
        var off = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 dPrev = i > 0 ? Flat(pts[i] - pts[i - 1]).normalized : Vector3.zero;
            Vector3 dNext = i < n - 1 ? Flat(pts[i + 1] - pts[i]).normalized : Vector3.zero;
            Vector3 m;
            float scale = 1;
            if (i == 0) m = LeftOf(dNext);
            else if (i == n - 1) m = LeftOf(dPrev);
            else
            {
                m = LeftOf(dPrev) + LeftOf(dNext);
                if (m.sqrMagnitude < 1e-6f) m = LeftOf(dNext);
                m.Normalize();
                scale = 1f / Mathf.Max(.35f, Vector3.Dot(m, LeftOf(dNext)));
            }
            off[i] = m * scale;
        }
        return off;
    }

    static Vector3[] OutwardFrame(Vector3[] pts, Vector3 centroid)
    {
        var off = Frame(pts);
        var mid = (pts[0] + pts[1]) * .5f;
        float sign = Vector3.Dot(off[0], Flat(mid - centroid)) >= 0 ? 1 : -1;
        return off.Select(o => o * sign).ToArray();
    }

    static Mesh Ribbon(string name, Vector3[] pts, float[] hw, float thick) => Ribbon(name, pts, hw, hw, thick);

    static Mesh Ribbon(string name, Vector3[] pts, float[] hwLeft, float[] hwRight, float thick)
    {
        var off = Frame(pts);
        int n = pts.Length;
        var L = new Vector3[n];
        var R = new Vector3[n];
        for (int i = 0; i < n; i++) { L[i] = pts[i] + off[i] * hwLeft[i]; R[i] = pts[i] - off[i] * hwRight[i]; }
        var down = Vector3.down * thick;
        var mb = new MB();
        for (int i = 0; i + 1 < n; i++)
        {
            mb.Quad(L[i], L[i + 1], R[i + 1], R[i], Vector3.up);
            mb.Quad(L[i] + down, L[i + 1] + down, R[i + 1] + down, R[i] + down, Vector3.down);
            var side = off[i] + off[i + 1];
            mb.Quad(L[i], L[i + 1], L[i + 1] + down, L[i] + down, side);
            mb.Quad(R[i], R[i + 1], R[i + 1] + down, R[i] + down, -side);
        }
        var d0 = Flat(pts[1] - pts[0]);
        var d1 = Flat(pts[n - 1] - pts[n - 2]);
        mb.Quad(L[0], R[0], R[0] + down, L[0] + down, -d0);
        mb.Quad(L[n - 1], R[n - 1], R[n - 1] + down, L[n - 1] + down, d1);
        return mb.ToMesh(name);
    }

    static Mesh Wall(string name, Vector3[] edge, Vector3[] outDir, float[] bottom, float[] top, float thick)
    {
        int n = edge.Length;
        var ib = new Vector3[n]; var it = new Vector3[n]; var ob = new Vector3[n]; var ot = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            ib[i] = new Vector3(edge[i].x, bottom[i], edge[i].z);
            it[i] = new Vector3(edge[i].x, top[i], edge[i].z);
            ob[i] = ib[i] + outDir[i] * thick;
            ot[i] = it[i] + outDir[i] * thick;
        }
        var mb = new MB();
        for (int i = 0; i + 1 < n; i++)
        {
            var o = outDir[i] + outDir[i + 1];
            mb.Quad(ib[i], it[i], it[i + 1], ib[i + 1], -o);
            mb.Quad(ob[i], ot[i], ot[i + 1], ob[i + 1], o);
            mb.Quad(it[i], ot[i], ot[i + 1], it[i + 1], Vector3.up);
            mb.Quad(ib[i], ob[i], ob[i + 1], ib[i + 1], Vector3.down);
        }
        var d0 = Flat(edge[1] - edge[0]);
        var d1 = Flat(edge[n - 1] - edge[n - 2]);
        mb.Quad(ib[0], it[0], ot[0], ob[0], -d0);
        mb.Quad(ib[n - 1], it[n - 1], ot[n - 1], ob[n - 1], d1);
        return mb.ToMesh(name);
    }

    static Mesh PolySlab(string name, Vector3[] poly, float topY, float thick)
    {
        var c = poly.Aggregate(Vector3.zero, (s, p) => s + p) / poly.Length;
        var mb = new MB();
        Vector3 Up(Vector3 p) => new Vector3(p.x, topY, p.z);
        Vector3 Dn(Vector3 p) => new Vector3(p.x, topY - thick, p.z);
        for (int i = 0; i < poly.Length; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Length];
            mb.Tri(Up(c), Up(a), Up(b), Vector3.up);
            mb.Tri(Dn(c), Dn(a), Dn(b), Vector3.down);
            var mid = Flat((a + b) * .5f - c);
            mb.Quad(Up(a), Up(b), Dn(b), Dn(a), mid);
        }
        return mb.ToMesh(name);
    }

    static Mesh Quad(string name, Vector3 center, float size)
    {
        var mb = new MB();
        float h = size * .5f;
        mb.Quad(center + new Vector3(-h, 0, -h), center + new Vector3(-h, 0, h), center + new Vector3(h, 0, h), center + new Vector3(h, 0, -h), Vector3.up);
        return mb.ToMesh(name);
    }

    // ------------------------------------------------------------------ verification (also temporary)

    public static string Check()
    {
        var sb = new StringBuilder();
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            // The test player stands on the spawn point; take it out of the physics queries (the scene is not saved).
            foreach (var g in scene.GetRootGameObjects()) if (g.name.StartsWith("Игрок")) g.SetActive(false);
            Physics.SyncTransforms();
            var root = scene.GetRootGameObjects().First(g => g.name == LevelName).transform;
            var route = root.Find(RouteName);
            var pts = Nodes.Select(n => route.Find(n.id).position).ToArray();
            int samples = 0, noFloor = 0, offFloor = 0, blocked = 0;
            var problems = new List<string>();
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Flat(b - a).magnitude / .2f));
                for (int k = 0; k <= steps; k++)
                {
                    var p = Vector3.Lerp(a, b, k / (float)steps);
                    samples++;
                    if (!Physics.Raycast(p + Vector3.up * 2.4f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                    { noFloor++; if (problems.Count < 40) problems.Add($"нет пола {Nodes[i].id}+{k} {Local(root, p)}"); continue; }
                    if (Mathf.Abs(hit.point.y - p.y) > .45f) { offFloor++; if (problems.Count < 40) problems.Add($"пол {hit.point.y - p.y:+0.00;-0.00} м {Nodes[i].id}+{k} ({hit.collider.name})"); }
                    var bottom = hit.point + Vector3.up * .33f;
                    var top = hit.point + Vector3.up * (1.8f - .28f + .05f);
                    // Risers of the carved stairs touch a static capsule near each step edge: the walking controller steps them.
                    var names = Physics.OverlapCapsule(bottom, top, .26f, ~0, QueryTriggerInteraction.Ignore)
                        .Select(c => c.name).Where(n => !n.StartsWith("Ступень") && n != "Пол перед лестницей" && !n.StartsWith("Игрок")).Distinct().ToArray();
                    if (names.Length > 0)
                    {
                        blocked++;
                        if (problems.Count < 40) problems.Add($"капсула упирается {Nodes[i].id}+{k} {Local(root, p)}: {string.Join(", ", names)}");
                    }
                }
            }
            sb.AppendLine($"ПРОХОДИМОСТЬ: проб {samples}, нет пола {noFloor}, пол не на высоте {offFloor}, капсула упирается {blocked}");
            foreach (var p in problems) sb.AppendLine("  " + p);

            var atm = root.GetComponentInChildren<GodMountainsAtmosphere>();
            var godGroup = root.Find("Панорама").Cast<Transform>().First(t => t.name.StartsWith("MG01"));
            var renderers = godGroup.Find("MG01").GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var face = godGroup.Cast<Transform>().First(t => t.name.StartsWith("H "));
            var targets = new[] { face.position, bounds.center, new Vector3(bounds.center.x, bounds.max.y - 30, bounds.center.z),
                new Vector3(bounds.min.x + 80, bounds.center.y, bounds.center.z), new Vector3(bounds.max.x - 80, bounds.center.y, bounds.center.z) };
            int eyes = 0, geoSeen = 0, fogSeen = 0;
            var seen = new List<string>();
            int lastIndex = Array.FindIndex(Nodes, n => n.id == "N28");
            for (int i = 0; i < lastIndex; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Flat(b - a).magnitude));
                for (int k = 0; k < steps; k++)
                {
                    var p = Vector3.Lerp(a, b, k / (float)steps) + Vector3.up * 1.6f;
                    eyes++;
                    foreach (var target in targets)
                    {
                        var d = target - p;
                        if (Physics.Raycast(p, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore)) continue;
                        geoSeen++;
                        var air = atm.Evaluate(p);
                        float through = Mathf.Exp(-Mathf.Pow(air.density * d.magnitude, 2));
                        if (through > .02f) { fogSeen++; if (seen.Count < 20) seen.Add($"{Nodes[i].id}+{k}: сквозь туман {through:P0}"); }
                        else if (seen.Count < 20) seen.Add($"{Nodes[i].id}+{k}: только геометрия открыта, туман закрывает");
                    }
                }
            }
            sb.AppendLine($"СКРЫТНОСТЬ БОГА (N00–N28): точек глаз {eyes}, лучей открыто геометрией {geoSeen}, виден сквозь туман {fogSeen}");
            foreach (var s in seen) sb.AppendLine("  " + s);

            var m = route.Find("N30").position + Vector3.up * 1.6f;
            foreach (var target in targets)
            {
                var d = target - m;
                float yaw = Vector3.SignedAngle(Vector3.forward, Flat(d), Vector3.up);
                float pitch = Mathf.Atan2(d.y, Flat(d).magnitude) * Mathf.Rad2Deg;
                bool hit = Physics.Raycast(m, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
                sb.AppendLine($"из M: поворот {yaw:F0}°, высота {pitch:F0}°, {(hit ? "закрыто" : "открыто")}, {d.magnitude:F0} м");
            }
            int missing = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true)).Count(c => c == null);
            sb.AppendLine($"пустых скриптов: {missing}");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
        return sb.ToString();
    }

    static string Local(Transform root, Vector3 world) { var l = root.InverseTransformPoint(world); return $"({l.x:F1}; {l.z:F1}; h {l.y:F1})"; }

    public static string Shots()
    {
        var sb = new StringBuilder();
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var camGo = new GameObject("shot camera");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        try
        {
            var root = scene.GetRootGameObjects().First(g => g.name == LevelName).transform;
            var route = root.Find(RouteName);
            var atm = root.GetComponentInChildren<GodMountainsAtmosphere>();
            Vector3 N(string id) => route.Find(id).position;
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.targetTexture = rt;
            cam.nearClipPlane = .05f;
            cam.farClipPlane = 12000;
            cam.fieldOfView = 75;
            cam.clearFlags = CameraClearFlags.SolidColor;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge", "shots");
            Directory.CreateDirectory(dir);
            var eye = Vector3.up * 1.6f;
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
                ("17-plan-top", root.TransformPoint(new Vector3(5, 300, 15)), root.TransformPoint(new Vector3(5, 0, 15.01f)), false),
                ("18-reveal-wide", root.TransformPoint(new Vector3(30, 50, 5)), root.TransformPoint(new Vector3(42.4f, 300, 1665)), false),
            };
            foreach (var (name, pos, look, useAir) in list)
            {
                cam.orthographic = name.Contains("plan-top");
                cam.orthographicSize = 54;
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, name.Contains("plan-top") ? Vector3.forward : Vector3.up));
                var air = useAir ? atm.Evaluate(pos) : new GodMountainsAtmosphere.Air { fog = Heights.fog, density = 0, sky = Heights.sky, ambient = Hex("#B5ADC8"), skyHidden = 0 };
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
                sb.AppendLine($"{name}: воздух «{air.name}», плотность {air.density:0.#####}");
            }
        }
        finally
        {
            Object.DestroyImmediate(camGo);
            rt.Release();
            Object.DestroyImmediate(rt);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
        return sb.ToString();
    }
}
