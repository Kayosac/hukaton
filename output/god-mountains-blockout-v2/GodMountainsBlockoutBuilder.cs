// TEMPORARY authoring tool for the god-mountains blockout. Run once through the editor bridge, verify, then delete
// this file and its .meta (AGENTS.md: no builders left as the project's source of truth).
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

static class GodMountainsBlockoutBuilder
{
    const string Root = "Assets/GodMountains";
    const string BlockoutDir = Root + "/Blockout";
    const string MeshDir = BlockoutDir + "/Meshes";
    const string MatDir = BlockoutDir + "/Materials";
    const string PrefabDir = BlockoutDir + "/Prefabs";
    const string TexDir = BlockoutDir + "/Textures";
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
    static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
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
        if (AssetDatabase.IsValidFolder(BlockoutDir)) AssetDatabase.DeleteAsset(BlockoutDir);
        foreach (var dir in new[] { Root, Root + "/Scenes", BlockoutDir, MeshDir, MatDir, PrefabDir, TexDir, Root + "/God", GodDir, GodDir + "/Textures", GodDir + "/Materials" })
            EnsureFolder(dir);
        var godModel = ImportGod();
        MakeMaterials();
        var boulder = MakeBoulderPrefab();
        var peak = MakePeakPrefab();

        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var root = new GameObject(LevelName).transform;
            root.position = LevelOrigin;
            var route = Group(root, RouteName);
            foreach (var n in Nodes) Marker(route, n.id, new Vector3(n.x, n.h, n.z), 1);
            var geo = Group(root, "Блокаут");
            BuildPocketAndSlot(Group(geo, "Z0–Z1 Карман и щель"));
            BuildCorners(Group(geo, "Z2 Повтор"), boulder);
            BuildDescent(Group(geo, "Z3 Спуск и горловина"));
            BuildThreshold(Group(geo, "Z4 Порог"));
            BuildLedge(Group(geo, "Z5 Белая полка"), boulder);
            BuildGorge(Group(geo, "Z6 Горло"));
            BuildPlatform(Group(geo, "Z7 Площадка"), boulder);
            BuildRidge(Group(geo, "Хребет и опоры"));
            var god = BuildPanorama(Group(root, "Панорама"), peak, godModel);
            BuildMarkers(Group(root, "Метки"));
            var sun = BuildSun(root);
            var player = BuildPlayer(scene);
            BuildAtmosphere(root, route, player);
            RenderSettings.sun = sun;
            RenderSettings.skybox = null;
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
        var floorMat = Mats["Floor"];
        var poly = new[] { V(-38.6f, -32.6f), V(-31.2f, -33.2f), V(-30.4f, -25.2f), V(-34.2f, -23.4f), V(-35.9f, -23.4f), V(-39.2f, -25.8f) };
        var centroid = V(-35f, -28.4f);
        var under = poly.Select(v => centroid + (v - centroid) * (1 + .6f / Flat(v - centroid).magnitude)).ToArray();
        MeshObj(parent, "Пол кармана", PolySlab("GM_Pocket_Floor", under, 0, .6f), floorMat);
        var wallPts = new[] { V(-34.2f, -23.4f), V(-30.4f, -25.2f), V(-31.2f, -33.2f), V(-38.6f, -32.6f), V(-39.2f, -25.8f), V(-35.9f, -23.4f) };
        var outs = OutwardFrame(wallPts, centroid);
        WallBoxes(parent, "Стены кармана", "GM_Pocket_Wall", wallPts, outs, Fill(wallPts.Length, -1), Fill(wallPts.Length, 14), 2.5f, Mats["Wall Z1"]);
        // Roof of the pocket: four heavy slabs, light comes through the seams.
        float[] zs = { -32.2f, -29.6f, -27.0f, -24.6f };
        float[] ys = { 4.8f, 5.6f, 5.1f, 6.0f };
        for (int i = 0; i < zs.Length; i++)
            Box(parent, "Свод кармана " + (i + 1), new Vector3(-34.8f, ys[i] + .45f, zs[i]), new Vector3(10.5f, .9f, 2.35f),
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
                Box(parent, $"Плита крыши {i + 1}.{k + 1}", c + Vector3.up * (4.95f + Rand(0, 1f)), new Vector3(w, .9f, len - .22f),
                    Quaternion.LookRotation(d.normalized) * Quaternion.Euler(Jit(3), Jit(3), Jit(3)), Mats["Roof"]);
            }
        }
        Post(parent, "Стык N01 слева", P("N01"), Flat(P("N02") - P("N01")).normalized, true, HW("N01"), 14);
        Post(parent, "Стык N01 справа", P("N01"), Flat(P("N02") - P("N01")).normalized, false, HW("N01"), 14);
        OuterPost(parent, "Стык N06", P("N05"), P("N06"), P("N07"), HW("N06"), 14);
    }

    static void BuildCorners(Transform parent, GameObject boulder)
    {
        var module = BuildCornerModule(parent, boulder);
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
        var hole = Box(third.transform, "К — пустая ниша, лунка", shrub.localPosition + new Vector3(0, -.47f, 0), new Vector3(.55f, .06f, .35f), shrub.localRotation, Mats["Hole"]);
        Object.DestroyImmediate(hole.GetComponent<BoxCollider>());
        plate.localPosition = new Vector3(.25f, .12f + 2.2f + .225f, 2f);
        plate.localRotation = Quaternion.Euler(6, 12, -5);
        PrefabUtility.SaveAsPrefabAssetAndConnect(third, PrefabDir + "/Corner module third.prefab", InteractionMode.AutomatedAction);
        module.name = "Угол 1";
        log.AppendLine("corners: prefab + variant");
    }

    static GameObject BuildCornerModule(Transform parent, GameObject boulder)
    {
        var go = new GameObject("Угол");
        go.transform.SetParent(parent, false);
        var t = go.transform;
        Vector3[] p = { new Vector3(0, 0, 0), new Vector3(0, .25f, 4), new Vector3(3, .45f, 6), new Vector3(3.5f, .7f, 10) };
        float[] hw = { .8f, .8f, .8f, .8f };
        MeshObj(t, "Пол", Ribbon("GM_Corner_Floor", p, Under(hw), .6f), Mats["Floor"]);
        Walls(t, "Угол", "GM_Corner", p, hw, q => q.y - 1, q => q.y + 13, 2.5f, Mats["Wall Z2"], true, true);
        var d0 = Flat(p[1] - p[0]).normalized;
        Post(t, "Стык слева", p[0], d0, true, .8f, 13);
        Post(t, "Стык справа", p[0], d0, false, .8f, 13);
        var off = Frame(p);
        Vector3 leftCorner = p[1] + off[1] * hw[1];
        Vector3 along2 = Flat(p[2] - p[1]).normalized, inward2 = -LeftOf(along2);
        var shrub = Box(t, "К — куст A07", leftCorner + along2 * .6f + inward2 * .25f + Vector3.up * .5f, new Vector3(.6f, 1f, .4f), Quaternion.LookRotation(inward2), Mats["Shrub"]);
        shrub.name = "К — куст A07";
        // A bush is foliage: passable, like tree crowns (it stood 5 cm from the wall and wedged the player).
        Object.DestroyImmediate(shrub.GetComponent<BoxCollider>());
        Box(t, "П — плита A06", new Vector3(.6f, .12f + .225f, 2f), new Vector3(.35f, .45f, 2.2f), Quaternion.Euler(0, 2, 0), Mats["Plate"]);
        Vector3 crackBase = leftCorner + along2 * 1.5f + inward2 * .03f;
        var crack1 = Box(t, "Т — трещина «Г» (вертикаль)", crackBase + Vector3.up * 1.6f, new Vector3(.12f, 2f, .05f), Quaternion.LookRotation(inward2), Mats["Crack"]);
        var crack2 = Box(t, "Т — трещина «Г» (перекладина)", crackBase + along2 * .39f + Vector3.up * 2.55f, new Vector3(.9f, .12f, .05f), Quaternion.LookRotation(inward2), Mats["Crack"]);
        Object.DestroyImmediate(crack1.GetComponent<BoxCollider>());
        Object.DestroyImmediate(crack2.GetComponent<BoxCollider>());
        Vector3 along3 = Flat(p[3] - p[2]).normalized, right3 = -LeftOf(along3);
        Vector3 wallPoint = p[2] - off[2] * hw[2] + along3 * .9f;
        var b = (GameObject)PrefabUtility.InstantiatePrefab(boulder, t);
        b.name = "В — валун A05";
        // Pressed into the right wall (0.3 m of it sticks out), faces along the wall: no wedge between rock and wall.
        b.transform.localPosition = wallPoint + right3 * (.35f - .3f) + Vector3.up * .38f;
        b.transform.localScale = new Vector3(.7f, .85f, .9f);
        b.transform.localRotation = Quaternion.LookRotation(along3);
        return go;
    }

    static void BuildDescent(Transform parent)
    {
        var ids = new[] { "N15", "N16", "N17", "N18", "N19" };
        var pts = ids.Select(P).ToArray();
        var hw = ids.Select(HW).ToArray();
        MeshObj(parent, "Пол перед лестницей", Ribbon("GM_Descent_Top", new[] { P("N15"), P("N16") }, Under(new[] { HW("N15"), HW("N16") }), .6f), Mats["Floor"]);
        MeshObj(parent, "Пол горловины и пандуса", Ribbon("GM_Descent_Bottom", new[] { P("N17"), P("N18"), P("N19") }, Under(new[] { HW("N17"), HW("N18"), HW("N19") }), .6f), Mats["Floor"]);
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
            Box(steps, "Ступень " + (i + 1), new Vector3(c.x, (top - 1f) * .5f, c.z), new Vector3(w, top + 1f, tread + .02f), Quaternion.LookRotation(dir), Mats["Steps"]);
        }
        // Choke ceiling at 2.1 m over the middle of N17–N18: 1.2 m of flat floor before and after it, so the head clears
        // it both coming down the stairs and coming down the ramp.
        Vector3 c0 = P("N17"), c1 = P("N18"), cd = Flat(c1 - c0);
        float from = 1.2f, to = cd.magnitude - 1.2f;
        Box(parent, "Потолок горловины (2,1 м)", c0 + cd.normalized * ((from + to) * .5f) + Vector3.up * (2.1f + .6f), new Vector3(4f, 1.2f, to - from), Quaternion.LookRotation(cd.normalized), Mats["Roof"]);
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
            disk[i] = new Vector3(Hollow.x + Mathf.Cos(a) * (HollowR + .3f), 0, Hollow.z + Mathf.Sin(a) * (HollowR + .3f));
        }
        MeshObj(parent, "Пол кармана с рамкой", PolySlab("GM_Hollow_Floor", disk, Hollow.y, .6f), Mats["Floor"]);
        float west = Mathf.Atan2(P("N19").z - Hollow.z, P("N19").x - Hollow.x) * Mathf.Rad2Deg;
        var ring = Group(parent, "Стена кармана");
        for (int k = 0; k < 24; k++)
        {
            float ang = 7.5f + 15 * k;
            if (Mathf.Abs(Mathf.DeltaAngle(ang, west)) < 20 || Mathf.Abs(Mathf.DeltaAngle(ang, 0)) < 17) continue;
            float r = HollowR + .8f, rad = ang * Mathf.Deg2Rad;
            var c = new Vector3(Hollow.x + Mathf.Cos(rad) * r, Hollow.y + 5f, Hollow.z + Mathf.Sin(rad) * r);
            Box(ring, "Камень " + k, c, new Vector3(1.35f, 14f, 1.6f), Quaternion.LookRotation(new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad))), Mats["Wall Z4"]);
        }
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
        Box(go.transform, "Столб левый", new Vector3(-.92f, 1.4f, 0), new Vector3(.45f, 2.8f, .6f), Quaternion.Euler(0, 0, 2.5f), Mats["Frame"]);
        Box(go.transform, "Столб правый", new Vector3(.93f, 1.35f, 0), new Vector3(.45f, 2.7f, .6f), Quaternion.Euler(0, 4, -2f), Mats["Frame"]);
        Box(go.transform, "Перемычка", new Vector3(0, 2.65f, 0), new Vector3(2.5f, .5f, .62f), Quaternion.Euler(0, 0, -3.5f), Mats["Frame"]);
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
        // East end of the ledge closed by rock.
        Box(parent, "Скала в конце полки", new Vector3(43.15f, 13.5f, 23.15f), new Vector3(7.1f, 18f, 5.1f), Quaternion.identity, Mats["Rock"]);
        Box(parent, "Плечо хребта у входа в горло", new Vector3(45.1f, 17.5f, 27.9f), new Vector3(7f, 26f, 4.4f), Quaternion.identity, Mats["Rock"]);
        // Temporary floor under the white abyss (the fall reset comes later).
        Box(parent, "Облако бездны — временный пол блокаута", new Vector3(22, -30.5f, -11.5f), new Vector3(58, 1, 67), Quaternion.identity, Mats["Cloud"]);
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
        Box(parent, "Перемычка устья (проём 2 × 4 м)", P("N30") + new Vector3(.05f, 5f, -.8f), new Vector3(8f, 2f, 2.4f), Quaternion.Euler(0, -7f, 0), Mats["Roof"]);
    }

    static void BuildPlatform(Transform parent, GameObject boulder)
    {
        var a = new Vector3(PlatX, PlatH0, PlatZ0);
        var b = new Vector3(PlatX, LipH, LipZ);
        MeshObj(parent, "Площадка (ступень вершины A04)", Ribbon("GM_Platform_Top", new[] { a, b }, new[] { 7f, 7f }, 1.5f), Mats["Floor"]);
        Box(parent, "Обрыв под площадкой", new Vector3(PlatX, (-460f + PlatH0 - .6f) * .5f, (PlatZ0 + LipZ) * .5f), new Vector3(13.9f, 460f + PlatH0 - .6f, 13.9f), Quaternion.identity, Mats["Rock"]);
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
        Box(parent, "Хребет-ширма, запад", new Vector3(13.75f, -219.5f, 39.65f), new Vector3(43.5f, 481f, 17.3f), Quaternion.identity, Mats["Ridge"]);
        Box(parent, "Хребет-ширма, восток", new Vector3(54.5f, -218.5f, 35.15f), new Vector3(8f, 483f, 26.3f), Quaternion.identity, Mats["Ridge"]);
        Box(parent, "Основание под горлом", new Vector3(42.5f, -225.4f, 38.65f), new Vector3(23f, 469.2f, 19.3f), Quaternion.identity, Mats["Ridge"]);
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
                if (Mathf.Abs(Mathf.DeltaAngle(ang, 0)) < 22 && rMax <= 3500) ang += 44 * (Rand(0, 1) < .5f ? 1 : -1);
                float r = Rand(rMin, rMax);
                float top = Rand(topMin, topMax), bottom = -720;
                float h = top - bottom, w = h * Rand(.9f, 1.5f);
                var pos = center + new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad) * r, bottom, Mathf.Cos(ang * Mathf.Deg2Rad) * r);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(peak, peaks);
                go.name = "Вершина " + (++count);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(Jit(3), Rand(0, 360), Jit(3));
                go.transform.localScale = new Vector3(w, h, w * Rand(.8f, 1.2f));
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
                var go = (GameObject)PrefabUtility.InstantiatePrefab(peak, group);
                go.name = "Опорная вершина под кистью " + (++idx);
                go.transform.localPosition = new Vector3(c.x, minY - 3.2f, c.z);
                go.transform.localRotation = Quaternion.Euler(0, Rand(0, 360), 0);
                go.transform.localScale = new Vector3(3.6f, 3.2f, 3.6f);
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
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Heights.sky;
            PrefabUtility.RecordPrefabInstancePropertyModifications(cam);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
        return player;
    }

    static GodMountainsAtmosphere.Air Air(string name, string fog, float density, string sky, string ambient) => new GodMountainsAtmosphere.Air
    { name = name, fog = Hex(fog), density = density, sky = Hex(sky), ambient = Hex(ambient) };

    static readonly GodMountainsAtmosphere.Air Heights = Air("Высота", "#ABA0C7", .00027f, "#B9AFD6", "#8F87A8");

    static void BuildAtmosphere(Transform root, Transform route, GameObject player)
    {
        var go = new GameObject("Воздух по маршруту");
        go.transform.SetParent(root, false);
        var atm = go.AddComponent<GodMountainsAtmosphere>();
        var cam = player ? player.GetComponentInChildren<Camera>() : null;
        atm.viewer = cam ? cam.transform : null;
        atm.viewCamera = cam;
        atm.route = Nodes.Select(n => route.Find(n.id)).ToArray();
        atm.profiles = new[]
        {
            Air("Нутро", "#2E2A38", .12f, "#2E2A38", "#4A4560"),
            Air("Облако у рамки", "#E9E5EF", .22f, "#E9E5EF", "#C9C3D6"),
            Air("Облако у верха полки", "#E9E5EF", .08f, "#EDE9F2", "#D4CEDF"),
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

    static void ApplyHeightsAirToSceneSettings()
    {
        // Saved settings = the clear heights, so the scene is easy to edit; in Play the route air takes over at once.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Heights.fog;
        RenderSettings.fogDensity = Heights.density;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Heights.ambient;
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

    static void MakeMaterials()
    {
        var grid = GridTexture();
        void M(string name, string hex, bool useGrid)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Matte(m, Hex(hex));
            if (useGrid)
            {
                m.SetTexture("_BaseMap", grid);
                m.SetTexture("_MainTex", grid);
                m.SetTextureScale("_BaseMap", new Vector2(.5f, .5f));
            }
            AssetDatabase.CreateAsset(m, $"{MatDir}/GM {name}.mat");
            Mats[name] = m;
        }
        M("Floor", "#B9A891", true);
        M("Wall Z1", "#5E566E", true);
        M("Wall Z2", "#6B5E7D", true);
        M("Wall Z3", "#6E5463", true);
        M("Wall Z4", "#7A6070", true);
        M("Wall Z5", "#6F6680", true);
        M("Wall Z6", "#5D6B66", true);
        M("Roof", "#4A4356", false);
        M("Steps", "#A8977F", false);
        M("Shrub", "#6E7F4F", false);
        M("Plate", "#8C8496", false);
        M("Crack", "#1E1A24", false);
        M("Hole", "#2A2420", false);
        M("Frame", "#2D2836", false);
        M("Boulder", "#857A8F", false);
        M("Rock", "#6C6178", false);
        M("Ridge", "#675C74", false);
        M("Peak", "#8E86A6", false);
        M("Cloud", "#EEEAF5", false);
        M("Post", "#5E566E", false);
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

    static Texture2D GridTexture()
    {
        // 128 px = 2 m: thin dark lines every metre, fainter every half metre.
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float v = 1f;
                bool metre = x % 64 < 2 || y % 64 < 2;
                bool half = x % 32 == 0 || y % 32 == 0;
                if (metre) v = .78f; else if (half) v = .92f;
                tex.SetPixel(x, y, new Color(v, v, v, 1));
            }
        string path = TexDir + "/GM Grid.png";
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.filterMode = FilterMode.Bilinear;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 4;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static GameObject MakeBoulderPrefab()
    {
        var mesh = SaveMesh(Rock("GM_Boulder", 1, .22f));
        var go = new GameObject("Валун");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = Mats["Boulder"];
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/Boulder.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject MakePeakPrefab()
    {
        var mesh = SaveMesh(PeakMesh("GM_Peak"));
        var go = new GameObject("Вершина");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = Mats["Peak"];
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/Peak.prefab");
        Object.DestroyImmediate(go);
        return prefab;
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

    static GameObject Box(Transform parent, string name, Vector3 local, Vector3 size, Quaternion rot, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = rot;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static void Rock(Transform parent, GameObject boulder, string name, Vector3 local, Vector3 size)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(boulder, parent);
        go.name = name;
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(Jit(6), Rand(0, 360), Jit(6));
        go.transform.localScale = size;
    }

    static GameObject MeshObj(Transform parent, string name, Mesh mesh, Material mat, bool collider = true)
    {
        mesh = SaveMesh(mesh);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
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
        float thick, Material mat, bool left, bool right, float[] aboveOverride = null)
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

    static void WallBoxes(Transform parent, string label, string meshName, Vector3[] edge, Vector3[] outDir, float[] bottom, float[] top, float thick, Material mat)
        => WallBoxes(parent, label, meshName, edge, outDir, bottom, top, Enumerable.Repeat(thick, edge.Length).ToArray(), mat);

    static void WallBoxes(Transform parent, string label, string meshName, Vector3[] edge, Vector3[] outDir, float[] bottom, float[] top, float[] thicks, Material mat)
    {
        var group = Group(parent, label);
        for (int i = 0; i + 1 < edge.Length; i++)
        {
            Vector3 a = edge[i], b = edge[i + 1], along = Flat(b - a);
            float len = along.magnitude;
            if (len < .05f) continue;
            along /= len;
            var outward = Flat(outDir[i] + outDir[i + 1]);
            outward = (outward - along * Vector3.Dot(outward, along)).normalized;
            float lo = Mathf.Min(bottom[i], bottom[i + 1]), hi = Mathf.Max(top[i], top[i + 1]);
            float thick = thicks[Mathf.Min(i, thicks.Length - 1)];
            var go = new GameObject($"{label} {i + 1}");
            go.transform.SetParent(group, false);
            var mid = (a + b) * .5f;
            go.transform.localPosition = new Vector3(mid.x, (lo + hi) * .5f, mid.z) + outward * (thick * .5f);
            go.transform.localRotation = Quaternion.LookRotation(along);
            var size = new Vector3(thick, hi - lo, len);
            var mesh = SaveMesh(BoxMesh($"{meshName}_{i + 1}", size));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var box = go.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = size;
        }
    }

    static Mesh BoxMesh(string name, Vector3 size)
    {
        var h = size * .5f;
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++) c[i] = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
        var mb = new MB();
        mb.Quad(c[0], c[1], c[3], c[2], Vector3.back);
        mb.Quad(c[4], c[5], c[7], c[6], Vector3.forward);
        mb.Quad(c[0], c[2], c[6], c[4], Vector3.left);
        mb.Quad(c[1], c[3], c[7], c[5], Vector3.right);
        mb.Quad(c[2], c[3], c[7], c[6], Vector3.up);
        mb.Quad(c[0], c[1], c[5], c[4], Vector3.down);
        return mb.ToMesh(name);
    }

    /// <summary>Floor half widths reaching 0.6 m under the walls, so the floor's own edge never touches the player.</summary>
    static float[] Under(float[] hw, float extra = .6f) => hw.Select(w => w + extra).ToArray();

    static void Post(Transform parent, string name, Vector3 at, Vector3 dir, bool left, float hw, float height)
    {
        var side = LeftOf(dir) * (left ? 1 : -1);
        var c = at + side * (hw + .7f) + Vector3.up * (height * .5f - 1);
        Box(parent, name, c, new Vector3(1.4f, height, 1.4f), Quaternion.LookRotation(dir), Mats["Post"]);
    }

    static void OuterPost(Transform parent, string name, Vector3 before, Vector3 at, Vector3 after, float hw, float height)
    {
        Vector3 d1 = Flat(at - before).normalized, d2 = Flat(after - at).normalized;
        float turn = d1.x * d2.z - d1.z * d2.x; // > 0: left turn
        var n = (LeftOf(d1) + LeftOf(d2)).normalized * (turn > 0 ? -1 : 1);
        var c = at + n * (hw + 1.2f) + Vector3.up * (height * .5f - 1);
        Box(parent, name, c, new Vector3(2.4f, height, 2.4f), Quaternion.LookRotation(n), Mats["Post"]);
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

    sealed class MB
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(n, outward) < 0) { var tmp = b; b = d; d = tmp; n = -n; }
            n.Normalize();
            int i = v.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                v.Add(p);
                if (Mathf.Abs(n.y) > .7f) uv.Add(new Vector2(p.x, p.z));
                else
                {
                    var tan = Vector3.Cross(Vector3.up, n).normalized;
                    uv.Add(new Vector2(Vector3.Dot(p, tan), p.y));
                }
            }
            t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward) => Quad(a, b, c, c, outward);

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
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

    static Mesh Rock(string name, float size, float jitter)
    {
        // A crooked box: 8 corners pushed about, flat faces.
        var r = new System.Random(7);
        float J() => ((float)r.NextDouble() * 2 - 1) * jitter;
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = new Vector3(((i & 1) == 0 ? -.5f : .5f) + J(), ((i & 2) == 0 ? -.5f : .5f) + J() * .6f, ((i & 4) == 0 ? -.5f : .5f) + J()) * size;
        c[2] += new Vector3(.1f, -.08f, .05f); c[7] += new Vector3(-.12f, -.1f, -.06f);
        var mb = new MB();
        mb.Quad(c[0], c[1], c[3], c[2], Vector3.back);
        mb.Quad(c[4], c[5], c[7], c[6], Vector3.forward);
        mb.Quad(c[0], c[2], c[6], c[4], Vector3.left);
        mb.Quad(c[1], c[3], c[7], c[5], Vector3.right);
        mb.Quad(c[2], c[3], c[7], c[6], Vector3.up);
        mb.Quad(c[0], c[1], c[5], c[4], Vector3.down);
        return mb.ToMesh(name);
    }

    static Mesh PeakMesh(string name)
    {
        // Unit peak: base y 0, crooked flat-ish top at y 1, six sides.
        var r = new System.Random(3);
        float J(float a) => ((float)r.NextDouble() * 2 - 1) * a;
        var baseRing = new Vector3[6];
        var topRing = new Vector3[6];
        var topOffset = new Vector3(.07f, 0, -.04f);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3 + J(.15f);
            baseRing[i] = new Vector3(Mathf.Cos(a) * (.5f + J(.06f)), 0, Mathf.Sin(a) * (.5f + J(.06f)));
            topRing[i] = topOffset + new Vector3(Mathf.Cos(a) * (.13f + J(.03f)), 1 - J(.05f) * (i % 2), Mathf.Sin(a) * (.13f + J(.03f)));
        }
        var mb = new MB();
        var topCenter = topRing.Aggregate(Vector3.zero, (s, p) => s + p) / 6;
        for (int i = 0; i < 6; i++)
        {
            int j = (i + 1) % 6;
            var outward = Flat((baseRing[i] + baseRing[j]) * .5f);
            mb.Quad(baseRing[i], topRing[i], topRing[j], baseRing[j], outward + Vector3.up * .3f);
            mb.Tri(topCenter, topRing[i], topRing[j], Vector3.up);
        }
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
                var air = useAir ? atm.Evaluate(pos) : new GodMountainsAtmosphere.Air { fog = Heights.fog, density = 0, sky = Heights.sky, ambient = Hex("#B5ADC8") };
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
