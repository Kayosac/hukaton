using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Expeditions;
using Hakaton.Feel;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Generator tool for the endless world (Kayospace «Открытый мир v2», «генератор-инструмент»): builds one prefab
    /// per chunk shape and variant from <see cref="TileTerrain"/> — crooked low-poly ground with Old Apiary ground
    /// materials, water and thickets as card obstacles, bridges, logs and ForestArt dressing, sockets for finds and
    /// creatures, and the chunk's own navigation — then fills the chunk catalogue. The prefabs are the source of truth:
    /// edit them by hand afterwards; rebuilding overwrites them.
    /// </summary>
    public static class ChunkPrefabBuilder
    {
        const string Root = "Assets/WorldAssembly/Chunks";
        const string CataloguePath = "Assets/WorldAssembly/Chunk catalogue.asset";
        const string ContentPath = "Assets/WorldAssembly/World content.asset";
        const float Step = .5f;
        const float Plateau = 6f, CanyonDepth = 5f;

        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

        [MenuItem("Hakaton/World/Rebuild chunk prefabs")]
        static void Menu()
        {
            if (EditorUtility.DisplayDialog("Rebuild chunk prefabs", "Every chunk prefab in " + Root + " will be generated anew; hand edits are lost.", "Rebuild", "Cancel"))
                Debug.Log(BuildAll());
        }

        public static string BuildAll()
        {
            materials.Clear();
            prefabs.Clear();
            Directory.CreateDirectory(Root);
            RiverMaterial();
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>(CataloguePath);
            if (!catalogue)
            {
                catalogue = ScriptableObject.CreateInstance<ChunkCatalog>();
                AssetDatabase.CreateAsset(catalogue, CataloguePath);
            }
            catalogue.entries.Clear();
            int count = 0;
            foreach (var shape in TileShapes.All)
                foreach (var variant in shape.Variants)
                {
                    var view = Build(shape, variant.Id);
                    catalogue.entries.Add(new ChunkCatalog.Entry { shape = shape.Id, variant = variant.Id, prefab = view });
                    count++;
                }
            EditorUtility.SetDirty(catalogue);
            EnsureContent();
            AssetDatabase.SaveAssets();
            return $"built {count} chunk prefabs";
        }

        // ---------- one chunk ----------

        static ChunkView Build(TileShape shape, string variant)
        {
            var terrain = new TileTerrain(shape, variant, Plateau, CanyonDepth);
            string folder = $"{Root}/{shape.Id}";
            Directory.CreateDirectory(folder);
            string name = $"{shape.Id} {variant}";
            string meshPath = $"{folder}/{name} data.asset";
            AssetDatabase.DeleteAsset(meshPath);

            var go = new GameObject(name);
            var view = go.AddComponent<ChunkView>();
            view.shapeId = shape.Id;
            view.variantId = variant;
            var rng = new System.Random(name.GetHashCode());

            var ground = new GameObject("Ground");
            ground.transform.SetParent(go.transform, false);
            var (mesh, mats) = Ground(terrain);
            mesh.name = name + " ground";
            // The ground mesh is the main asset; water, ice and navigation are added to it.
            AssetDatabase.CreateAsset(mesh, meshPath);
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            ground.AddComponent<MeshRenderer>().sharedMaterials = mats;
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;
            view.worldUv = new[] { ground.GetComponent<MeshFilter>() };

            var obstacles = new List<WorldObstacle>();
            if (shape.Kind == ShapeKind.River || (shape.Kind == ShapeKind.Plain && variant == "pond"))
                obstacles.Add(Water(go.transform, terrain, meshPath, name));
            if (shape.Kind == ShapeKind.Maze) obstacles.AddRange(Thickets(go.transform, terrain, rng));
            view.obstacles = obstacles.ToArray();

            Crossings(go.transform, terrain, rng);
            Dress(go.transform, terrain, rng);
            Sockets(view, terrain, rng);

            if (shape.Kind == ShapeKind.Start)
            {
                var pose = new GameObject("Departure pose").transform;
                pose.SetParent(go.transform, false);
                pose.localPosition = new Vector3(0, terrain.Height(new Vector2(8, 8.6f)) + .1f, .6f);
                view.departurePose = pose;
                // The beacon stands behind the player's start, seen from far away through the fog.
                Place(go.transform, "Structures/FA_ST04_Post", terrain, new Vector2(7.3f, 6.6f), 0, new Vector3(1.4f, 9f, 1.4f));
                var lantern = Place(go.transform, "Props/FA_PR01_LanternHanging", terrain, new Vector2(7.3f, 6.6f), 0, Vector3.one * 2.2f);
                if (lantern) lantern.transform.localPosition += Vector3.up * 13f;
                Place(go.transform, "Markers/FA_MK16_StoneCircle", terrain, new Vector2(8, 8), 0, Vector3.one);
            }

            view.navMesh = Bake(go, terrain, name);
            if (view.navMesh) AssetDatabase.AddObjectToAsset(view.navMesh, meshPath);

            string prefabPath = $"{folder}/{name}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return saved.GetComponent<ChunkView>();
        }

        // ---------- ground ----------

        enum Surface { Main, Rock, Bank, Bed, Path, Humus, Count }

        static (Mesh, Material[]) Ground(TileTerrain t)
        {
            int n = Mathf.RoundToInt(TileTerrain.Size / Step);
            var pos = new Vector3[(n + 1) * (n + 1)];
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    var p = new Vector2(i * Step, j * Step);
                    // Crooked: interior vertices wander a little; edge vertices stay exact.
                    float fade = TileTerrain.EdgeFade(p, 1.5f);
                    var q = p + new Vector2(t.Noise(p, 1.3f, 21), t.Noise(p, 1.3f, 22)) * .14f * fade;
                    pos[i * (n + 1) + j] = new Vector3(q.x - 8, t.Height(q), q.y - 8);
                }
            var verts = new List<Vector3>();
            var subs = new List<int>[(int)Surface.Count];
            for (int s = 0; s < subs.Length; s++) subs[s] = new List<int>();
            void Tri(int a, int b, int c)
            {
                Vector3 A = pos[a], B = pos[b], C = pos[c];
                var normal = Vector3.Cross(B - A, C - A).normalized;
                var centre = (A + B + C) / 3;
                var p = new Vector2(centre.x + 8, centre.z + 8);
                var kind = Classify(t, p, centre.y, normal);
                int index = verts.Count;
                verts.Add(A); verts.Add(B); verts.Add(C);
                subs[(int)kind].Add(index); subs[(int)kind].Add(index + 1); subs[(int)kind].Add(index + 2);
            }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    int a = i * (n + 1) + j, b = (i + 1) * (n + 1) + j, c = (i + 1) * (n + 1) + j + 1, d = i * (n + 1) + j + 1;
                    // Alternate the diagonal for a less regular look; winding keeps normals up.
                    if (((i + j) & 1) == 0) { Tri(a, d, c); Tri(a, c, b); }
                    else { Tri(a, d, b); Tri(d, c, b); }
                }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.subMeshCount = subs.Length;
            for (int s = 0; s < subs.Length; s++) mesh.SetTriangles(subs[s], s);
            mesh.SetUVs(0, verts.Select(v => new Vector2(v.x, v.z) / 4f).ToList());
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var mats = new Material[subs.Length];
            mats[(int)Surface.Main] = Mat(MainGround(t));
            mats[(int)Surface.Rock] = Mat("OA_Ground_Rock");
            mats[(int)Surface.Bank] = Mat("OA_Ground_Path");
            mats[(int)Surface.Humus] = Mat("OA_Ground_Humus");
            mats[(int)Surface.Bed] = Mat("OA_Ground_Bed");
            mats[(int)Surface.Path] = Mat("OA_Ground_Path");
            return (mesh, mats);
        }

        static Surface Classify(TileTerrain t, Vector2 p, float h, Vector3 normal)
        {
            if (normal.y < .72f) return Surface.Rock;
            bool wet = t.Shape.Kind == ShapeKind.River || (t.Shape.Kind == ShapeKind.Plain && t.Variant == "pond");
            if (wet && h < TileTerrain.WaterLevel + .05f) return Surface.Bed;
            if (wet && h < .08f && (t.Shape.Kind != ShapeKind.River || t.DistanceToCentre(p) < TileTerrain.BankHalf + .8f || t.Shape.Id == "river-end")) return Surface.Bank;
            if (t.Shape.Kind == ShapeKind.Canyon && h < -t.CanyonDepth + .4f) return Surface.Bed;
            if (t.OnRamp(p)) return Surface.Path;
            if (t.Shape.Kind == ShapeKind.Maze) return t.InMazeWall(p) ? Surface.Humus : Surface.Path;
            if (t.Shape.Kind == ShapeKind.Start && Vector2.Distance(p, new Vector2(8, 8)) < 7.2f) return Surface.Path;
            return Surface.Main;
        }

        /// <summary>One ground everywhere, so chunks never show their borders; variety comes from rock, paths and dressing.</summary>
        static string MainGround(TileTerrain t) => "OA_Ground_Moss";

        static Material Mat(string name)
        {
            if (materials.TryGetValue(name, out var m)) return m;
            string path = name.StartsWith("Assets/") ? name : $"Assets/OldApiary/Materials/{name}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) Debug.LogWarning("[World] missing material " + path);
            materials[name] = m;
            return m;
        }

        // ---------- water ----------

        static WorldObstacle Water(Transform parent, TileTerrain t, string assetPath, string name)
        {
            // Water surface: grid cells with water under any corner, flat at the water level; flow along the river.
            int n = Mathf.RoundToInt(TileTerrain.Size / Step);
            var verts = new List<Vector3>();
            var flow = new List<Vector2>();
            var tris = new List<int>();
            float y = TileTerrain.WaterLevel;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    var p0 = new Vector2(i * Step, j * Step);
                    bool any = t.IsWater(p0) || t.IsWater(p0 + new Vector2(Step, 0)) || t.IsWater(p0 + new Vector2(0, Step)) || t.IsWater(p0 + new Vector2(Step, Step));
                    if (!any) continue;
                    int b = verts.Count;
                    verts.Add(new Vector3(p0.x - 8, y, p0.y - 8));
                    verts.Add(new Vector3(p0.x - 8, y, p0.y + Step - 8));
                    verts.Add(new Vector3(p0.x + Step - 8, y, p0.y + Step - 8));
                    verts.Add(new Vector3(p0.x + Step - 8, y, p0.y - 8));
                    var dir = FlowAt(t, p0 + Vector2.one * Step / 2);
                    for (int k = 0; k < 4; k++) flow.Add(dir);
                    tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                }
            var water = new Mesh { name = name + " water" };
            water.SetVertices(verts);
            water.SetTriangles(tris, 0);
            water.SetUVs(0, flow);
            water.RecalculateNormals();
            water.RecalculateBounds();
            AssetDatabase.AddObjectToAsset(water, assetPath);
            var ice = new Mesh { name = name + " ice" };
            ice.SetVertices(verts.Select(v => v + Vector3.up * .12f).ToList());
            ice.SetTriangles(tris, 0);
            ice.SetUVs(0, verts.Select(v => new Vector2(v.x, v.z) / 3f).ToList());
            ice.RecalculateNormals();
            ice.RecalculateBounds();
            AssetDatabase.AddObjectToAsset(ice, assetPath);

            var root = new GameObject("Water");
            root.transform.SetParent(parent, false);
            var bounds = water.bounds;
            var barrier = root.AddComponent<BoxCollider>();
            barrier.isTrigger = true;
            barrier.center = new Vector3(bounds.center.x, -1.0f, bounds.center.z);
            barrier.size = new Vector3(bounds.size.x + .2f, 2.6f, bounds.size.z + .2f);

            var surface = new GameObject("Surface (Water layer)") { layer = 4 };
            surface.transform.SetParent(root.transform, false);
            surface.AddComponent<MeshCollider>().sharedMesh = water;

            var visual = new GameObject("Water mesh");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = water;
            visual.AddComponent<MeshRenderer>().sharedMaterial = Mat("Assets/WorldAssembly/Materials/River water.mat");

            var frozen = new GameObject("Ice");
            frozen.transform.SetParent(root.transform, false);
            frozen.AddComponent<MeshFilter>().sharedMesh = ice;
            frozen.AddComponent<MeshRenderer>().sharedMaterial = Mat("Assets/Expedition/Materials/Ice.mat");
            frozen.AddComponent<MeshCollider>().sharedMesh = ice;
            frozen.SetActive(false);

            var obstacle = root.AddComponent<WorldObstacle>();
            obstacle.kind = ObstacleKind.Lake;
            obstacle.barrier = barrier;
            obstacle.intact = visual;
            obstacle.changed = frozen;
            obstacle.waterSurface = surface.GetComponent<Collider>();
            obstacle.tint = new Color(.55f, .85f, 1f);
            Feedback(obstacle, "883ca243c76bf4c7788012001a4f876b", null, "aedb344531d8648dc9cce21ac4fd52e3", null);
            return obstacle;
        }

        static Vector2 FlowAt(TileTerrain t, Vector2 p)
        {
            if (t.Shape.Kind != ShapeKind.River || t.Centreline.Count == 0) return Vector2.zero;
            if (t.Shape.Id == "river-end" && Vector2.Distance(p, new Vector2(8, 8)) < TileTerrain.LakeRadius) return Vector2.zero;
            var line = t.Centreline[0];
            float best = float.MaxValue;
            Vector2 dir = Vector2.zero;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                float d = Vector2.Distance(p, (line[i] + line[i + 1]) / 2);
                if (d < best) { best = d; dir = (line[i + 1] - line[i]).normalized; }
            }
            return dir;
        }

        static void Feedback(WorldObstacle obstacle, string react, string loop, string reactSound, string loopSound)
        {
            var so = new SerializedObject(obstacle);
            void Set(string field, string guid, bool vfx)
            {
                if (guid == null) return;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) return;
                Object asset = vfx ? AssetDatabase.LoadAssetAtPath<VfxBurst>(path) : AssetDatabase.LoadAssetAtPath<SfxCue>(path);
                so.FindProperty(field).objectReferenceValue = asset;
            }
            Set("reactVfx", react, true);
            Set("loopVfx", loop, true);
            Set("reactSound", reactSound, false);
            Set("loopSound", loopSound, false);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Flowing water for rivers: the Old Apiary flowing water, with the flow taken from the mesh.</summary>
        static void RiverMaterial()
        {
            const string path = "Assets/WorldAssembly/Materials/River water.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path)) return;
            Directory.CreateDirectory("Assets/WorldAssembly/Materials");
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/OldApiary/Materials/OA_WaterFlow.mat");
            if (!source) return;
            var mat = new Material(source);
            mat.SetFloat("_UseMeshFlow", 1);
            AssetDatabase.CreateAsset(mat, path);
        }

        // ---------- thickets ----------

        static IEnumerable<WorldObstacle> Thickets(Transform parent, TileTerrain t, System.Random rng)
        {
            float c0 = 8 - TileTerrain.Corridor, c1 = 8 + TileTerrain.Corridor;
            var pieces = new List<Rect>
            {
                Rect.MinMaxRect(0, 0, c0, c0), Rect.MinMaxRect(c1, 0, 16, c0), Rect.MinMaxRect(0, c1, c0, 16), Rect.MinMaxRect(c1, c1, 16, 16),
            };
            if (t.Shape.Mids[0] != Mid.MazeOpen) pieces.Add(Rect.MinMaxRect(c0, c1, c1, 16));
            if (t.Shape.Mids[1] != Mid.MazeOpen) pieces.Add(Rect.MinMaxRect(c1, c0, 16, c1));
            if (t.Shape.Mids[2] != Mid.MazeOpen) pieces.Add(Rect.MinMaxRect(c0, 0, c1, c0));
            if (t.Shape.Mids[3] != Mid.MazeOpen) pieces.Add(Rect.MinMaxRect(0, c0, c0, c1));
            foreach (var r in pieces)
            {
                var root = new GameObject("Thorns");
                root.transform.SetParent(parent, false);
                root.transform.localPosition = new Vector3(r.center.x - 8, 0, r.center.y - 8);
                var barrierGo = new GameObject("Barrier");
                barrierGo.transform.SetParent(root.transform, false);
                var barrier = barrierGo.AddComponent<BoxCollider>();
                barrier.center = new Vector3(0, 1.3f, 0);
                barrier.size = new Vector3(r.width, 2.6f, r.height);
                var live = new GameObject("Live thorns");
                live.transform.SetParent(root.transform, false);
                var burnt = new GameObject("Burnt thorns");
                burnt.transform.SetParent(root.transform, false);
                for (float x = r.xMin + .8f; x < r.xMax; x += 1.6f)
                    for (float z = r.yMin + .7f; z < r.yMax; z += 1.3f)
                    {
                        var p = new Vector2(x + (float)rng.NextDouble() * .5f - .25f, z + (float)rng.NextDouble() * .4f - .2f);
                        float yaw = (float)rng.NextDouble() * 360, s = 1.3f + (float)rng.NextDouble() * .5f;
                        var a = Place(live.transform, "Plants/FA_VG10_ThornThicket", null, Vector2.zero, yaw, new Vector3(s, s * 1.5f, s * 1.4f));
                        if (a) a.transform.position = root.transform.position + new Vector3(p.x - r.center.x, 0, p.y - r.center.y);
                        var b = Place(burnt.transform, "Plants/FA_VG10_ThornThicketBurnt", null, Vector2.zero, yaw, new Vector3(s, s * .6f, s * 1.4f));
                        if (b) b.transform.position = root.transform.position + new Vector3(p.x - r.center.x, 0, p.y - r.center.y);
                    }
                foreach (var c in live.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                foreach (var c in burnt.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                burnt.SetActive(false);
                var obstacle = root.AddComponent<WorldObstacle>();
                obstacle.kind = ObstacleKind.Thorns;
                obstacle.barrier = barrier;
                obstacle.intact = live;
                obstacle.changed = burnt;
                obstacle.tint = new Color(1f, .62f, .3f);
                Feedback(obstacle, "1c053b046a88e4e7b8c543f06e64b7e2", "40b80550388374f72a36150a39fb3d81", "a7ef1d25e7e664753b75a51a4803f109", "3070caddd61f843dd8ba16ca7c10784a");
                yield return obstacle;
            }
        }

        // ---------- bridges, logs, fords ----------

        static void Crossings(Transform parent, TileTerrain t, System.Random rng)
        {
            switch (t.Shape.Id + "/" + t.Variant)
            {
                case "river-straight/bridge":
                {
                    var bridge = Place(parent, "Structures/FA_ST08_Bridge", null, Vector2.zero, 90, new Vector3(1.6f, 1, 2.05f));
                    if (bridge) bridge.transform.localPosition = new Vector3(-4.1f, .02f, 0);
                    break;
                }
                case "river-straight/ford":
                case "river-bend/ford":
                {
                    var centre = t.Shape.Id == "river-bend" ? new Vector2(16 - 8 * .7071f, 16 - 8 * .7071f) : new Vector2(8, 8);
                    for (int i = -2; i <= 2; i++)
                    {
                        var p = centre + (t.Shape.Id == "river-bend" ? new Vector2(.7071f, .7071f) : Vector2.right) * i * 1.25f;
                        var stone = Place(parent, "Rocks/FA_RK04_BoulderFlat", t, p, (float)rng.NextDouble() * 360, Vector3.one * 1.4f);
                        if (stone) stone.transform.localPosition = new Vector3(p.x - 8, TileTerrain.WaterLevel - .15f, p.y - 8);
                    }
                    break;
                }
                case "canyon-straight/log":
                {
                    var log = Place(parent, "Deadwood/FA_DW01_LogWhole", null, Vector2.zero, 90, new Vector3(1.5f, 1.2f, 2.15f));
                    if (log) log.transform.localPosition = new Vector3(-3.8f, -.15f, 0);
                    // A flat walkway on top so the player does not slide off the round log.
                    var walk = new GameObject("Log walkway");
                    walk.transform.SetParent(parent, false);
                    var box = walk.AddComponent<BoxCollider>();
                    box.center = new Vector3(0, .55f, 0);
                    box.size = new Vector3(8.4f, .2f, 1.1f);
                    break;
                }
            }
        }

        // ---------- dressing ----------

        static readonly string[] Trees = { "FA_TR01_ThickCrooked", "FA_TR02_TallNarrow", "FA_TR03_DoubleFork", "FA_TR04_Leaning", "FA_TR05_Zigzag", "FA_TR06_Hollow", "FA_TR07_YoungStraight", "FA_TR08_YoungBow", "FA_TR09_YoungFork", "FA_TR10_DeadBare", "FA_TR12_Split", "FA_TR14_KnottyGiant" };
        static readonly string[] Grass = { "FA_VG04_GrassLow", "FA_VG05_GrassTall", "FA_VG06_GrassSparse", "FA_VG07_GrassDrooping", "FA_VG01_FernLow", "FA_VG11_BroadLeavesLow" };
        static readonly string[] Ferns = { "FA_VG01_FernLow", "FA_VG02_FernTall", "FA_VG03_FernDrooping", "FA_VG12_BroadLeavesTall", "FA_VG08_BushRound" };
        static readonly string[] Boulders = { "FA_RK01_BoulderSmall", "FA_RK02_BoulderMedium", "FA_RK03_BoulderLarge", "FA_RK05_StoneTall", "FA_RK06_BoulderSplit", "FA_RK07_RocksLowGroup" };
        static readonly string[] Deadwood = { "FA_DW01_LogWhole", "FA_DW04_LogRotten", "FA_DW05_StumpLow", "FA_DW06_StumpTall", "FA_DW09_SticksSmall", "FA_DW11_BranchForked" };

        static void Dress(Transform parent, TileTerrain t, System.Random rng)
        {
            var dressing = new GameObject("Dressing").transform;
            dressing.SetParent(parent, false);
            void Scatter(string folder, string[] names, int count, float edge, float minSpacing, float scaleMin, float scaleMax, Func<Vector2, bool> where = null)
            {
                var placed = new List<Vector2>();
                for (int attempt = 0; attempt < count * 12 && placed.Count < count; attempt++)
                {
                    var p = new Vector2(edge + (float)rng.NextDouble() * (16 - 2 * edge), edge + (float)rng.NextDouble() * (16 - 2 * edge));
                    if (!Free(t, p) || (where != null && !where(p)) || placed.Any(q => Vector2.Distance(p, q) < minSpacing)) continue;
                    placed.Add(p);
                    float s = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
                    Place(dressing, folder + "/" + names[rng.Next(names.Length)], t, p, (float)rng.NextDouble() * 360, Vector3.one * s);
                }
            }
            bool High(Vector2 p) => t.Height(p) > Plateau * .8f;
            bool Low(Vector2 p) => t.Height(p) < Plateau * .2f;
            switch (t.Shape.Kind)
            {
                case ShapeKind.Plain:
                    if (t.Variant == "woods") { Scatter("Trees", Trees, 7, 2.6f, 3.2f, .8f, 1.1f); Scatter("Plants", Ferns, 10, 1, 1.2f, .8f, 1.3f); Scatter("Deadwood", Deadwood, 3, 2, 2, .8f, 1.1f); }
                    else if (t.Variant == "rocks") { Scatter("Rocks", Boulders, 6, 2.2f, 2.4f, .9f, 1.6f); Scatter("Plants", Grass, 8, 1, 1.2f, .8f, 1.2f); }
                    else if (t.Variant == "pond") { Scatter("Plants", Grass, 12, 1, 1, .9f, 1.4f); Scatter("Trees", Trees, 1, 3, 3, .9f, 1f); }
                    else { Scatter("Plants", Grass, 14, 1, 1, .8f, 1.3f); Scatter("Rocks", Boulders, 1, 3, 3, .6f, .9f); }
                    break;
                case ShapeKind.PlateauTop:
                    if (t.Variant == "rocks") Scatter("Rocks", Boulders, 5, 2.4f, 2.6f, 1f, 1.8f);
                    Scatter("Plants", Grass, 8, 1, 1.2f, .7f, 1.1f);
                    break;
                case ShapeKind.Cliff:
                    Scatter("Plants", Grass, 6, 1, 1.4f, .7f, 1.1f);
                    Scatter("Rocks", Boulders, 2, 2.4f, 3, .7f, 1.2f, Low);
                    Scatter("Trees", Trees, 1, 3, 4, .8f, 1f, High);
                    if (t.Variant == "ledge") Ledge(dressing, t);
                    break;
                case ShapeKind.River:
                    Scatter("Plants", Ferns, 7, 1, 1.2f, .8f, 1.2f);
                    Scatter("Plants", Grass, 6, 1, 1, .9f, 1.3f);
                    break;
                case ShapeKind.Canyon:
                    Scatter("Plants", Grass, 6, 1, 1.4f, .7f, 1.1f, p => t.Height(p) > -.2f);
                    Scatter("Rocks", Boulders, 2, 1.5f, 2.5f, .6f, 1f, p => t.Height(p) < -t.CanyonDepth + .3f);
                    break;
                case ShapeKind.Maze:
                    Scatter("Deadwood", Deadwood, 1, 6.5f, 2, .7f, .9f);
                    break;
            }
        }

        /// <summary>A rock shelf halfway up a straight cliff: Impulse I twice.</summary>
        static void Ledge(Transform parent, TileTerrain t)
        {
            var shelf = new GameObject("Ledge");
            shelf.transform.SetParent(parent, false);
            shelf.transform.localPosition = new Vector3(4.5f - 8, 0, 7.2f - 8);
            var box = shelf.AddComponent<BoxCollider>();
            box.center = new Vector3(0, Plateau * .5f - .4f, 0);
            box.size = new Vector3(3.2f, .8f, 2.2f);
            var rock = Place(shelf.transform, "Rocks/FA_RK13_StepFlat", null, Vector2.zero, 0, new Vector3(2.4f, 1.6f, 2.4f));
            if (rock) rock.transform.localPosition = new Vector3(0, Plateau * .5f - .8f, 0);
        }

        static bool Free(TileTerrain t, Vector2 p)
        {
            if (t.IsWater(p) || t.OnRamp(p) || t.InMazeWall(p)) return false;
            if (t.Shape.Kind == ShapeKind.River && t.DistanceToCentre(p) < TileTerrain.BankHalf + .6f) return false;
            if (t.Shape.Kind == ShapeKind.Canyon && t.DistanceToCentre(p) < TileTerrain.RimHalf + .4f && t.DistanceToCentre(p) > TileTerrain.FloorHalf - .3f) return false;
            if (t.Shape.Kind == ShapeKind.Start && Vector2.Distance(p, new Vector2(8, 8)) < 8) return false;
            if (t.Shape.Kind == ShapeKind.Maze && Mathf.Abs(p.x - 8) < 1.6f && Mathf.Abs(p.y - 8) < 1.6f) return false;
            // Steep ground (cliff faces) stays bare.
            float h = t.Height(p);
            float slope = Mathf.Max(Mathf.Abs(t.Height(p + Vector2.right * .5f) - h), Mathf.Abs(t.Height(p + Vector2.up * .5f) - h)) / .5f;
            return slope < .5f;
        }

        static GameObject Place(Transform parent, string asset, TileTerrain t, Vector2 p, float yaw, Vector3 scale)
        {
            string path = $"Assets/ForestArt/Prefabs/{asset}.prefab";
            if (!prefabs.TryGetValue(path, out var prefab))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                prefabs[path] = prefab;
                if (!prefab) Debug.LogWarning("[World] missing prefab " + path);
            }
            if (!prefab) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            float y = t != null ? t.Height(p) : 0;
            go.transform.localPosition = new Vector3(p.x - 8, y, p.y - 8);
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = scale;
            if (t != null)
            {
                // Chunk-local: the parent may be a nested group at the chunk origin.
                var chunk = parent;
                while (chunk.parent) chunk = chunk.parent;
                go.transform.position = chunk.TransformPoint(new Vector3(p.x - 8, y - .05f, p.y - 8));
            }
            return go;
        }

        // ---------- sockets ----------

        static void Sockets(ChunkView view, TileTerrain t, System.Random rng)
        {
            var group = new GameObject("Sockets").transform;
            group.SetParent(view.transform, false);
            var candidates = new List<Vector2>();
            for (float x = 1.5f; x <= 14.5f; x += 1f)
                for (float z = 1.5f; z <= 14.5f; z += 1f)
                {
                    var p = new Vector2(x, z);
                    if (!Free(t, p) && !(t.Shape.Kind == ShapeKind.Start && Vector2.Distance(p, new Vector2(8, 8)) > 2)) continue;
                    if (t.Shape.Kind == ShapeKind.Start) continue;
                    candidates.Add(p);
                }
            // Finds: on the best ground the shape has — the top of a cliff, the floor of a canyon, the corridor of a maze.
            Func<Vector2, bool> prefer = t.Shape.Kind switch
            {
                ShapeKind.Cliff => p => t.Height(p) > Plateau * .8f,
                ShapeKind.Canyon => p => t.Height(p) < -t.CanyonDepth + .3f,
                _ => p => true,
            };
            var finds = Spread(candidates.Where(prefer).ToList(), 4, rng);
            if (finds.Count == 0) finds = Spread(candidates, 3, rng);
            if (t.Variant == "ledge") finds.Insert(0, new Vector2(4.5f, 7.2f));
            view.findSockets = finds.Select((p, i) => Socket(group, "Find " + i, t, p, t.Variant == "ledge" && i == 0 ? Plateau * .5f + .05f : (float?)null)).ToArray();

            if (t.Variant == "island") view.pocketSocket = Socket(group, "Pocket", t, new Vector2(8, 8), null);
            if (t.Shape.Kind == ShapeKind.Maze) view.pocketSocket = Socket(group, "Pocket", t, new Vector2(8, 8), null);

            Func<Vector2, bool> home = t.Shape.Kind switch
            {
                ShapeKind.Canyon => p => t.Height(p) < -t.CanyonDepth + .3f,
                ShapeKind.Cliff => p => true,
                _ => p => true,
            };
            var creatures = Spread(candidates.Where(home).ToList(), 2, rng);
            if (t.Shape.Kind == ShapeKind.Maze) creatures = new List<Vector2> { new Vector2(8, 8) };
            view.creatureSockets = creatures.Select((p, i) => Socket(group, "Creature " + i, t, p, null)).ToArray();
        }

        static Transform Socket(Transform parent, string name, TileTerrain t, Vector2 p, float? height)
        {
            var s = new GameObject(name).transform;
            s.SetParent(parent, false);
            s.localPosition = new Vector3(p.x - 8, (height ?? t.Height(p)) + .15f, p.y - 8);
            return s;
        }

        /// <summary>Up to <paramref name="count"/> points far apart from each other (farthest-point sampling).</summary>
        static List<Vector2> Spread(List<Vector2> pool, int count, System.Random rng)
        {
            var result = new List<Vector2>();
            if (pool.Count == 0) return result;
            result.Add(pool[rng.Next(pool.Count)]);
            while (result.Count < count && result.Count < pool.Count)
            {
                Vector2 best = pool[0];
                float bestD = -1;
                foreach (var p in pool)
                {
                    float d = result.Min(q => Vector2.Distance(p, q));
                    if (d > bestD) { bestD = d; best = p; }
                }
                if (bestD < 2.5f) break;
                result.Add(best);
            }
            return result;
        }

        // ---------- navigation ----------

        static NavMeshData Bake(GameObject root, TileTerrain t, string name)
        {
            var triggers = root.GetComponentsInChildren<Collider>(true).Where(c => c.isTrigger && c.enabled).ToList();
            foreach (var c in triggers) c.enabled = false;
            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(new Vector3(0, 2, 0), new Vector3(16, 24, 16));
            NavMeshBuilder.CollectSources(bounds, ~(1 << 4), NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            foreach (var c in triggers) c.enabled = true;
            // Water is not walkable for creatures.
            foreach (var obstacle in root.GetComponentsInChildren<WorldObstacle>())
            {
                if (obstacle.kind != ObstacleKind.Lake || !obstacle.intact) continue;
                var mesh = obstacle.intact.GetComponent<MeshFilter>().sharedMesh;
                var b = mesh.bounds;
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.ModifierBox,
                    transform = Matrix4x4.Translate(new Vector3(b.center.x, -1f, b.center.z)),
                    size = new Vector3(b.size.x, 3.4f, b.size.z),
                    area = 1,
                });
            }
            var settings = NavMesh.GetSettingsByID(0);
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data) data.name = name + " navmesh";
            return data;
        }

        // ---------- content ----------

        static void EnsureContent()
        {
            var content = AssetDatabase.LoadAssetAtPath<WorldContent>(ContentPath);
            if (content) return;
            content = ScriptableObject.CreateInstance<WorldContent>();
            WorldLoot Loot(string file) => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Prefabs/{file}.prefab")?.GetComponent<WorldLoot>();
            WorldCreature[] Creatures(params string[] files) => files.Select(f => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Prefabs/{f}.prefab")?.GetComponent<WorldCreature>()).Where(c => c).ToArray();
            content.loot.Add(new WorldContent.LootEntry { id = "find", prefab = Loot("Trophy common_find") });
            content.loot.Add(new WorldContent.LootEntry { id = "find-hard", prefab = Loot("Trophy special_find") });
            content.loot.Add(new WorldContent.LootEntry { id = "crystal", prefab = Loot("Trophy special_find") });
            content.creatures.Add(new WorldContent.CreatureEntry { role = "peaceful", prefabs = Creatures("Purple mushroom", "Kind little worm", "Rooster baseball", "Snail frog mount", "Teen leshy skater", "Zombie beekeeper") });
            content.creatures.Add(new WorldContent.CreatureEntry { role = "small-hostile", prefabs = Creatures("Bee swarm", "Rat pumpkin") });
            content.creatures.Add(new WorldContent.CreatureEntry { role = "hostile", prefabs = Creatures("Goblin dog", "Enraged bee mutant") });
            content.creatures.Add(new WorldContent.CreatureEntry { role = "thicket", prefabs = Creatures("Hostile bush") });
            content.creatures.Add(new WorldContent.CreatureEntry { role = "territorial", prefabs = Creatures("Budding troll") });
            content.creatures.Add(new WorldContent.CreatureEntry { role = "brute", prefabs = Creatures("Sinister living tree", "Drunken dwarf robot") });
            AssetDatabase.CreateAsset(content, ContentPath);
        }
    }

}
