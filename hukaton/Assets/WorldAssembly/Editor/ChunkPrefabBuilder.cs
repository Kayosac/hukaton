using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using Hakaton.OldApiary;
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
            // Collision copies are made anew with the chunks that use them.
            collisionCopies.Clear();
            AssetDatabase.DeleteAsset(CollisionPath);
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
            var stale = new HashSet<string>(AssetDatabase.FindAssets("", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath).Where(p => !AssetDatabase.IsValidFolder(p)));
            foreach (var shape in TileShapes.All)
                foreach (var variant in shape.Variants)
                {
                    var view = Build(shape, variant.Id);
                    catalogue.entries.Add(new ChunkCatalog.Entry { shape = shape.Id, variant = variant.Id, prefab = view });
                    stale.Remove(AssetDatabase.GetAssetPath(view.gameObject));
                    stale.Remove($"{Root}/{shape.Id}/{shape.Id} {variant.Id} data.asset");
                    count++;
                }
            // Variants that are gone (an old scene list) leave no prefabs behind.
            foreach (var path in stale) AssetDatabase.DeleteAsset(path);
            EditorUtility.SetDirty(catalogue);
            EnsureContent();
            AssetDatabase.SaveAssets();
            return $"built {count} chunk prefabs, removed {stale.Count} old assets";
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
            dataPath = meshPath;

            placed.Clear();
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
            groundCollider = ground.AddComponent<MeshCollider>();
            groundCollider.sharedMesh = mesh;
            Physics.SyncTransforms();
            view.worldUv = new[] { ground.GetComponent<MeshFilter>() };

            var obstacles = new List<WorldObstacle>();
            if (terrain.Wet) obstacles.Add(Water(go.transform, terrain, meshPath, name));
            if (shape.Kind == ShapeKind.Maze && terrain.Forest != null)
            {
                ForestWalls(go.transform, terrain, rng);
                if (terrain.Forest.Deadfall && terrain.Forest.Alcove.HasValue) obstacles.Add(DeadfallPiece(go.transform, terrain, rng));
                if (terrain.Forest.Hollow) HollowTrunk(go.transform, terrain);
                view.lookouts = go.GetComponentsInChildren<Lookout>();
            }
            else if (shape.Kind == ShapeKind.Maze) StoneWalls(go.transform, terrain, rng);
            if (terrain.Scene != null) ScenePieces(go.transform, terrain, rng, obstacles);
            if (terrain.Scene != null) Places(view, terrain);
            view.obstacles = obstacles.ToArray();

            Crossings(go.transform, terrain, rng);
            Dress(go.transform, terrain, rng);
            Interactive(view);
            view.navMesh = Bake(go, terrain, name);
            if (view.navMesh) AssetDatabase.AddObjectToAsset(view.navMesh, meshPath);
            Sockets(view, terrain, rng);

            if (shape.Id == Landmarks.StartHill.PartId(1, 1))
            {
                // The top of the start hill, facing north; the beacon stands behind (a prop of the hill's scene).
                var pose = new GameObject("Departure pose").transform;
                pose.SetParent(go.transform, false);
                pose.localPosition = new Vector3(0, GroundY(terrain, new Vector2(8, 8.6f)) + .1f, .6f);
                view.departurePose = pose;
            }

            view.edgeHeights = EdgeHeights(terrain);
            // Roads climb a cliff by its ramp, from its foot to its top.
            if (shape.Kind == ShapeKind.Cliff && terrain.Ramps.Count > 0)
            {
                var ramp = terrain.Ramps[0];
                view.roadGuide = new[] { new Vector3(ramp.A.x - 8, 0, ramp.A.y - 8), new Vector3(ramp.B.x - 8, Plateau, ramp.B.y - 8) };
            }
            CullDetail(go.transform);

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
            mats[(int)Surface.Main] = GroundMat(MainGround(t));
            mats[(int)Surface.Rock] = GroundMat("OA_Ground_Rock");
            mats[(int)Surface.Bank] = GroundMat("OA_Ground_Path");
            mats[(int)Surface.Humus] = GroundMat("OA_Ground_Humus");
            mats[(int)Surface.Bed] = GroundMat("OA_Ground_Bed");
            mats[(int)Surface.Path] = GroundMat("OA_Ground_Path");
            return (mesh, mats);
        }

        static Surface Classify(TileTerrain t, Vector2 p, float h, Vector3 normal)
        {
            if (normal.y < .72f) return Surface.Rock;
            if (t.Wet && h < TileTerrain.WaterLevel + .05f) return Surface.Bed;
            if (t.Wet && h < .08f && t.NearWater(p, .8f)) return Surface.Bank;
            if (t.Shape.Kind == ShapeKind.Canyon && h < -t.CanyonDepth + .4f) return Surface.Bed;
            if (t.OnRamp(p)) return Surface.Path;
            if (t.Forest != null) return t.Forest.IsWall(p) ? Surface.Humus : t.Forest.OnTrail(p) ? Surface.Path : Surface.Main;
            if (t.Shape.Kind == ShapeKind.Maze) return t.InMazeWall(p) ? Surface.Humus : Surface.Path;
            if (t.SceneFloor(p) || t.SceneTrail(p)) return Surface.Path;
            if (t.SceneThicket(p)) return Surface.Humus;
            return Surface.Main;
        }

        /// <summary>One ground everywhere, so chunks never show their borders; variety comes from rock, paths and dressing.</summary>
        static string MainGround(TileTerrain t) => "OA_Ground_Moss";

        const string GroundMaterials = "Assets/WorldAssembly/Materials/Ground";

        /// <summary>
        /// The world's own copy of an Old Apiary ground material on Hakaton/World Ground Lit: same textures, tinted by
        /// biome. Made once; edit it freely afterwards (rebuilding keeps it).
        /// </summary>
        static Material GroundMat(string name)
        {
            string path = $"{GroundMaterials}/{name}.mat";
            if (materials.TryGetValue(path, out var cached)) return cached;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                var original = Mat(name);
                var shader = Shader.Find("Hakaton/World Ground Lit");
                if (!original || !shader) return original;
                Directory.CreateDirectory(GroundMaterials);
                m = new Material(original) { name = name, shader = shader };
                AssetDatabase.CreateAsset(m, path);
            }
            materials[path] = m;
            return m;
        }

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
                    // Only where the water has some depth: shallow rims would show as a band of foam.
                    bool any = t.IsDeep(p0) || t.IsDeep(p0 + new Vector2(Step, 0)) || t.IsDeep(p0 + new Vector2(0, Step)) || t.IsDeep(p0 + new Vector2(Step, Step));
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
            // No baked ice: the Ice card freezes water in a radius as a card-made IcePatch.

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

            var obstacle = root.AddComponent<WorldObstacle>();
            obstacle.kind = ObstacleKind.Lake;
            obstacle.barrier = barrier;
            obstacle.intact = visual;
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
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                Directory.CreateDirectory("Assets/WorldAssembly/Materials");
                var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/OldApiary/Materials/OA_WaterFlow.mat");
                if (!source) return;
                mat = new Material(source);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetFloat("_UseMeshFlow", 1);
            // A thin lip of foam: the banks are gentle, so the default shore foam reads as snow.
            mat.SetFloat("_FoamDepth", .1f);
            mat.SetFloat("_ShoreFoam", .12f);
            // Finer pixels and a fainter caustic web: in shallow pools the default web reads as cracked ice, and near
            // the camera the coarse pixels of the foam flicker.
            mat.SetFloat("_PixelsPerMeter", 24);
            mat.SetFloat("_CausticStrength", 1.1f);
            mat.SetFloat("_CausticWidth", .09f);
            mat.SetFloat("_FlowFoam", .22f);
            EditorUtility.SetDirty(mat);
        }

        // ---------- thickets ----------

        static readonly string[] WallRocks = { "FA_RK08_RocksTallGroup", "FA_RK09_CliffWall", "FA_RK03_BoulderLarge", "FA_RK06_BoulderSplit", "FA_RK05_StoneTall" };

        /// <summary>
        /// Walls of a stone maze: the blocks between corridors (and the walled sides) are old overgrown rock that
        /// nothing burns or crosses — one solid barrier each, dressed with tall rocks, ivy, bushes and a tree here and
        /// there; the corridors stay open.
        /// </summary>
        static void StoneWalls(Transform parent, TileTerrain t, System.Random rng)
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
            var walls = new GameObject("Stone walls").transform;
            walls.SetParent(parent, false);
            var blocks = new List<(Rect r, Transform block)>();
            var rocks = new List<(Transform block, GameObject rock)>();
            foreach (var r in pieces)
            {
                var block = new GameObject("Wall").transform;
                block.SetParent(walls, false);
                block.localPosition = new Vector3(r.center.x - 8, 0, r.center.y - 8);
                blocks.Add((r, block));
                // Rocks on a loose grid inside the block, tall enough to hide what is behind; solid as they look.
                for (float x = r.xMin + 1f; x < r.xMax - .6f; x += 2.1f)
                    for (float z = r.yMin + 1f; z < r.yMax - .6f; z += 2.1f)
                    {
                        var p = new Vector2(x + (float)rng.NextDouble() * .6f - .3f, z + (float)rng.NextDouble() * .6f - .3f);
                        float s = 1.25f + (float)rng.NextDouble() * .6f;
                        rocks.Add((block, Place(block, "Rocks/" + WallRocks[rng.Next(WallRocks.Length)], t, p, (float)rng.NextDouble() * 360, new Vector3(s, s * (1.3f + (float)rng.NextDouble() * .4f), s))));
                    }
                for (int k = 0; k < 3; k++)
                {
                    var p = new Vector2(Mathf.Lerp(r.xMin + .8f, r.xMax - .8f, (float)rng.NextDouble()), Mathf.Lerp(r.yMin + .8f, r.yMax - .8f, (float)rng.NextDouble()));
                    var green = Place(block, "Plants/" + (k == 0 ? "FA_VG14_IvyLong" : Bushes[rng.Next(Bushes.Length)]), t, p, (float)rng.NextDouble() * 360, Vector3.one * (1.2f + (float)rng.NextDouble() * .5f));
                    if (green) green.transform.position += Vector3.up * 2.4f;
                }
                if (r.width > 5 && r.height > 5 && rng.NextDouble() < .6)
                {
                    // Keeps its collider: a card can hit it and wake it up.
                    Place(block, "Trees/" + Trees[rng.Next(Trees.Length)], t, r.center, (float)rng.NextDouble() * 360, Vector3.one * (.8f + (float)rng.NextDouble() * .6f));
                }
            }
            // No invisible boxes: the rocks are solid as they look and keep out of the corridors; where they leave a way
            // in, another rock.
            float Depth(Vector2 p)
            {
                float best = -1;
                foreach (var (r, _) in blocks)
                    best = Mathf.Max(best, Mathf.Min(Mathf.Min(p.x - r.xMin, r.xMax - p.x), Mathf.Min(p.y - r.yMin, r.yMax - p.y)));
                return best;
            }
            foreach (var (_, rock) in rocks) Fit(rock, t, Depth);
            Seal(parent, t, Depth, .6f, null, p =>
            {
                var home = blocks.OrderBy(b => b.r.Contains(p) ? 0 : 1).First().block;
                float s = 1.2f + (float)rng.NextDouble() * .5f;
                return Fit(Place(home, "Rocks/" + WallRocks[rng.Next(WallRocks.Length)], t, p, (float)rng.NextDouble() * 360, new Vector3(s, s * (1.3f + (float)rng.NextDouble() * .4f), s)), t, Depth)
                    ?? Fit(Place(home, "Rocks/FA_RK05_StoneTall", t, p, (float)rng.NextDouble() * 360, new Vector3(.9f, 1.4f, .9f)), t, Depth);
            }, 80, "stone wall");
        }

        // ---------- alive as the Old Apiary ----------

        /// <summary>Every ForestArt piece placed on the chunk being built, with its asset path ("Plants/FA_VG04_GrassLow").</summary>
        static readonly List<(GameObject go, string asset)> placed = new List<(GameObject, string)>();

        /// <summary>What burns and how, by prefab, as the Old Apiary has it: Ash burns away, Char stays black.</summary>
        static readonly Dictionary<string, (BurnStyle style, float seconds)> BurnRules = new Dictionary<string, (BurnStyle, float)>
        {
            ["FA_VG01_FernLow"] = (BurnStyle.Ash, 1.4f), ["FA_VG02_FernTall"] = (BurnStyle.Ash, 1.6f), ["FA_VG03_FernDrooping"] = (BurnStyle.Ash, 1.4f),
            ["FA_VG04_GrassLow"] = (BurnStyle.Ash, 1.1f), ["FA_VG05_GrassTall"] = (BurnStyle.Ash, 1.4f), ["FA_VG06_GrassSparse"] = (BurnStyle.Ash, 1.1f),
            ["FA_VG07_GrassDrooping"] = (BurnStyle.Ash, 1.3f), ["FA_VG08_BushRound"] = (BurnStyle.Ash, 2.2f), ["FA_VG09_BushTallThorny"] = (BurnStyle.Ash, 2.4f),
            ["FA_VG10_ThornThicket"] = (BurnStyle.Char, 3f), ["FA_VG11_BroadLeavesLow"] = (BurnStyle.Ash, 1.2f), ["FA_VG12_BroadLeavesTall"] = (BurnStyle.Ash, 1.5f),
            ["FA_TR07_YoungStraight"] = (BurnStyle.Ash, 4.5f), ["FA_TR08_YoungBow"] = (BurnStyle.Ash, 4.5f), ["FA_TR09_YoungFork"] = (BurnStyle.Ash, 4.5f),
            ["FA_DW01_LogWhole"] = (BurnStyle.Char, 6f), ["FA_DW02_LogSplit"] = (BurnStyle.Char, 5f), ["FA_DW03_LogHollow"] = (BurnStyle.Char, 6f),
            ["FA_DW04_LogRotten"] = (BurnStyle.Char, 5f), ["FA_DW09_SticksSmall"] = (BurnStyle.Ash, 2f), ["FA_DW10_SticksLarge"] = (BurnStyle.Ash, 2.6f),
            ["FA_DW11_BranchForked"] = (BurnStyle.Ash, 2f), ["FA_DW12_BarkSlab"] = (BurnStyle.Ash, 1.6f),
            ["FA_ST01_FenceSection"] = (BurnStyle.Char, 4f), ["FA_ST02_FenceBroken"] = (BurnStyle.Char, 4f), ["FA_ST03_FenceCorner"] = (BurnStyle.Char, 4f),
            ["FA_ST04_Post"] = (BurnStyle.Char, 4f), ["FA_ST05_PostCrooked"] = (BurnStyle.Char, 4f), ["FA_ST06_GateCrooked"] = (BurnStyle.Char, 4f),
            ["FA_ST07_GateRuined"] = (BurnStyle.Char, 4f), ["FA_ST12_Bench"] = (BurnStyle.Char, 5f),
            ["FA_MK01_SignArrow"] = (BurnStyle.Char, 3f), ["FA_MK02_SignFork"] = (BurnStyle.Char, 3f), ["FA_MK03_SignBroken"] = (BurnStyle.Char, 3f),
            ["FA_MK07_ClothShort"] = (BurnStyle.Ash, 1.2f), ["FA_MK08_ClothLong"] = (BurnStyle.Ash, 1.2f), ["FA_MK09_ClothPennants"] = (BurnStyle.Ash, 1.2f),
            ["FA_MK10_CharmTwigDiamond"] = (BurnStyle.Ash, 1f), ["FA_MK11_CharmHerbs"] = (BurnStyle.Ash, 1f), ["FA_MK17_MobileTwigs"] = (BurnStyle.Ash, 1f),
            ["FA_MK13_CageSmall"] = (BurnStyle.Ash, 2f), ["FA_MK14_CageCrooked"] = (BurnStyle.Ash, 2f),
            ["FA_PR11_SackSmall"] = (BurnStyle.Ash, 2.2f), ["FA_PR12_SackLarge"] = (BurnStyle.Ash, 2.8f), ["FA_PR13_CrateClosed"] = (BurnStyle.Ash, 3.4f),
            ["FA_PR14_CrateOpen"] = (BurnStyle.Ash, 3.4f), ["FA_PR15_HerbBundle"] = (BurnStyle.Ash, 1f),
            ["FA_SP04_Beehive"] = (BurnStyle.Ash, 3.6f), ["FA_SP09_Scarecrow"] = (BurnStyle.Ash, 4f),
        };

        /// <summary>Loose things the player and cards push about, with their mass at scale 1 (the Old Apiary's bodies).</summary>
        static readonly Dictionary<string, float> LooseMass = new Dictionary<string, float>
        {
            ["FA_DW01_LogWhole"] = 5, ["FA_DW02_LogSplit"] = 3, ["FA_DW04_LogRotten"] = 3, ["FA_DW09_SticksSmall"] = .4f, ["FA_DW10_SticksLarge"] = 1.5f,
            ["FA_DW11_BranchForked"] = .5f, ["FA_DW12_BarkSlab"] = .3f,
            ["FA_PR02_LanternGround"] = .5f, ["FA_PR04_CandleThick"] = .15f, ["FA_PR05_CandlesDish"] = .3f, ["FA_PR06_BottleSquat"] = .2f,
            ["FA_PR07_BottleTall"] = .2f, ["FA_PR09_PotLow"] = .3f, ["FA_PR10_Pitcher"] = .35f, ["FA_PR11_SackSmall"] = .6f, ["FA_PR12_SackLarge"] = 1.2f,
            ["FA_PR13_CrateClosed"] = 1.6f, ["FA_PR14_CrateOpen"] = 1.3f, ["FA_SP04_Beehive"] = 1.8f, ["FA_MK13_CageSmall"] = .5f, ["FA_MK14_CageCrooked"] = .8f,
        };

        /// <summary>
        /// Makes the chunk's things as alive as the Old Apiary's (its authoring rules): grass, bushes, young trees, wood,
        /// sacks and hives burn (<see cref="Burnable"/>, run by the streamer's fire field); every tree that stands with
        /// its own collider can be awakened (<see cref="AwakenableTree"/>); light loose things get a body and are pushed
        /// about (<see cref="PhysicsProp"/>). Pieces of card obstacles (thorn walls) keep to their obstacle; dressing
        /// that lost its colliders (inside maze and forest walls) only burns; big pieces (a log bridge) stay put.
        /// </summary>
        static void Interactive(ChunkView view)
        {
            var burnables = new List<Burnable>();
            var trees = new List<AwakenableTree>();
            var props = new List<PhysicsProp>();
            foreach (var (go, asset) in placed)
            {
                if (!go || asset.StartsWith("VFX/") || go.GetComponentInParent<WorldObstacle>()) continue;
                string id = asset.Substring(asset.LastIndexOf('/') + 1);
                var colliders = go.GetComponentsInChildren<Collider>();
                var scale = go.transform.lossyScale;
                if (BurnRules.TryGetValue(id, out var burn))
                {
                    var b = go.AddComponent<Burnable>();
                    // What dresses a maze or forest wall only chars: burnt away it would bare the barrier behind.
                    b.style = InWall(go.transform) ? BurnStyle.Char : burn.style;
                    b.seconds = burn.seconds;
                    burnables.Add(b);
                }
                if (asset.StartsWith("Trees/") && colliders.Length > 0) trees.Add(go.AddComponent<AwakenableTree>());
                // What makes a wall stays put: a log that rolled off would open it.
                bool loose = LooseMass.TryGetValue(id, out float mass) && colliders.Length > 0 && !InWall(go.transform)
                    && Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)) <= 1.6f
                    && colliders.All(c => !(c is MeshCollider mesh) || mesh.convex);
                // What stays put is hit as drawn, not by its hull.
                if (!loose) foreach (var c in colliders) if (c is MeshCollider mesh && mesh.convex) mesh.convex = false;
                if (loose)
                {
                    // Set a hair above the ground, so it settles instead of springing out of it.
                    go.transform.position += Vector3.up * .05f;
                    var body = go.AddComponent<Rigidbody>();
                    body.mass = mass * Mathf.Abs(scale.x * scale.y * scale.z);
                    body.angularDamping = .05f;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.collisionDetectionMode = body.mass < 1 ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
                    var prop = go.AddComponent<PhysicsProp>();
                    prop.body = body;
                    props.Add(prop);
                }
            }
            view.burnables = burnables.ToArray();
            view.trees = trees.ToArray();
            view.props = props.ToArray();
        }

        static bool InWall(Transform t)
        {
            for (var p = t.parent; p; p = p.parent)
                if (p.name == "Forest" || p.name == "Stone walls") return true;
            return false;
        }


        // ---------- the giant tangled forest ----------

        /// <summary>Giant trees for the tangle: prefab, trunk radius at scale 1 (its collider), and the scales it grows to.</summary>
        static readonly (string asset, float trunk, float min, float max)[] GiantTrees =
        {
            ("FA_TR14_KnottyGiant", 1.45f, 1.25f, 1.6f),
            ("FA_TR01_ThickCrooked", .95f, 1.8f, 2.3f),
            ("FA_TR06_Hollow", .95f, 1.8f, 2.2f),
            ("FA_TR03_DoubleFork", .65f, 2.2f, 2.7f),
            ("FA_TR12_Split", .6f, 2.2f, 2.6f),
            ("FA_TR02_TallNarrow", .36f, 2.6f, 3.2f),
        };
        static readonly string[] Undergrowth = { "FA_VG02_FernTall", "FA_VG03_FernDrooping", "FA_VG12_BroadLeavesTall", "FA_VG08_BushRound", "FA_VG09_BushTallThorny", "FA_VG02_FernTall" };
        static readonly string[] Fallen = { "FA_DW01_LogWhole", "FA_DW04_LogRotten", "FA_DW02_LogSplit", "FA_DW01_LogWhole" };

        /// <summary>
        /// A cell of the giant tangled forest (Kayospace «Открытый мир v2»): its wall is what stands where the ground
        /// plan calls wall — giant trees two and three times the usual size, root plates, fallen trunks and thorns, each
        /// solid as it looks, with no invisible boxes (<see cref="Seal"/> fills every way in with more of them) — and
        /// dense undergrowth along the lanes; the heart is the greatest tree of all with its shrine, the
        /// reward at its foot. Crowns close over the lanes: under them the air is the forest's (see WorldAtmosphere).
        /// </summary>
        static void ForestWalls(Transform parent, TileTerrain t, System.Random rng)
        {
            var f = t.Forest;
            var root = new GameObject("Forest").transform;
            root.SetParent(parent, false);
            // A giant tree takes the solid pieces round its trunk along (see Own): woken, it leaves its place open.
            var owners = new List<(GameObject go, Vector2 at, float reach)>();
            float Rnd(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            float EdgeRoom(Vector2 p) => Mathf.Min(Mathf.Min(p.x, 16 - p.x), Mathf.Min(p.y, 16 - p.y));
            void Bare(GameObject go) { if (go) foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c); }
            void NoShadow(GameObject go) { if (go) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            // A solid piece of the wall near a giant tree is the tree's: woken, the tree takes it along and its place is open.
            void Own(GameObject go, Vector2 at)
            {
                if (!go) return;
                GameObject owner = null;
                float best = float.MaxValue;
                foreach (var o in owners)
                {
                    float s = Vector2.Distance(at, o.at) - o.reach - .8f;
                    if (o.go && s <= 0 && s < best) { best = s; owner = o.go; }
                }
                if (owner) go.transform.SetParent(owner.transform, true);
            }

            // Grid of points in the tangle, shuffled.
            var points = new List<Vector2>();
            for (float x = .5f; x < 16; x += .5f)
                for (float z = .5f; z < 16; z += .5f)
                    points.Add(new Vector2(x + Rnd(-.2f, .2f), z + Rnd(-.2f, .2f)));
            for (int i = points.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (points[i], points[j]) = (points[j], points[i]); }

            // Giant trees: the biggest that fits at each spot, trunks inside the wall and inside the chunk.
            // They keep their colliders: cards hit them, «Оживление дерева» wakes even the greatest.
            var trunks = new List<(Vector2 at, float radius)>();
            var trees = new GameObject("Giant trees").transform;
            trees.SetParent(root, false);
            GameObject great = null;
            if (f.Heart)
            {
                // Its own collider is the heart's middle (the barrier leaves the trunk out): woken, it leaves the place open.
                great = Place(trees, "Trees/FA_TR14_KnottyGiant", t, f.Hub, Rnd(0, 360), Vector3.one * (ForestLayout.TrunkRadius / 1.45f));
                if (great) TurnClear(great, t, f);
                trunks.Add((f.Hub, ForestLayout.TrunkRadius));
            }
            foreach (var p in points)
            {
                if (trunks.Count >= (f.Heart ? 5 : 7)) break;
                float depth = f.WallDepth(p);
                if (depth < .9f || f.InTrunk(p, 1.5f)) continue;
                foreach (var (asset, trunk, min, max) in GiantTrees)
                {
                    float scale = Rnd(min, max), radius = trunk * scale;
                    if (depth < radius + .3f || EdgeRoom(p) < radius + .4f) continue;
                    if (trunks.Any(q => Vector2.Distance(q.at, p) < q.radius + radius + 1.6f)) continue;
                    if (f.InLog(p, radius + .3f)) continue;
                    // Its roots and low boughs are solid as drawn: they keep out of the lanes (a little over the edge is roots).
                    var tree = Fit(Place(trees, "Trees/" + asset, t, p, Rnd(0, 360), Vector3.one * scale), t, f.WallDepth, .35f);
                    if (!tree) continue;
                    var stands = tree.transform.localPosition;
                    var at = new Vector2(stands.x + 8, stands.z + 8);
                    radius *= tree.transform.localScale.x / scale;
                    trunks.Add((at, radius));
                    owners.Add((tree, at, radius));
                    // Moss and fungus on the big ones.
                    if (radius > 1.4f && rng.NextDouble() < .7)
                    {
                        float a = Rnd(0, Mathf.PI * 2);
                        var side = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * .85f;
                        var fungus = Place(trees, "Plants/FA_VG16_ShelfFungusGroup", null, Vector2.zero, -a * Mathf.Rad2Deg + 90, Vector3.one * Rnd(1.2f, 1.7f));
                        if (fungus) fungus.transform.localPosition = new Vector3(side.x - 8, GroundY(t, side) + Rnd(.9f, 2.2f), side.y - 8);
                        // Solid as it looks; out of the lane, or not at all.
                        fungus = Fit(fungus, t, f.WallDepth);
                        // Goes with its tree when the tree walks off.
                        if (fungus) fungus.transform.SetParent(tree.transform, true);
                    }
                    break;
                }
            }

            // Deadfall: fallen trunks lying along the lanes' sides, root plates and branches deeper in.
            var dead = new GameObject("Deadfall").transform;
            dead.SetParent(root, false);
            var logs = new List<Vector2>();
            foreach (var p in points)
            {
                if (logs.Count >= 5) break;
                float depth = f.WallDepth(p);
                if (depth < .5f || depth > 1.3f || EdgeRoom(p) < 1.5f || f.InLog(p, 1.5f)) continue;
                if (logs.Any(q => Vector2.Distance(q, p) < 3.6f) || trunks.Any(q => Vector2.Distance(q.at, p) < q.radius + .6f)) continue;
                // Along the edge of the lane: across the slope of the wall depth.
                var grad = new Vector2(f.WallDepth(p + Vector2.right * .3f) - f.WallDepth(p - Vector2.right * .3f), f.WallDepth(p + Vector2.up * .3f) - f.WallDepth(p - Vector2.up * .3f));
                if (grad.sqrMagnitude < 1e-4f) continue;
                var along = new Vector2(-grad.y, grad.x).normalized;
                float length = Rnd(3.6f, 5.4f);
                var a = p + along * length * .5f;
                var b = p - along * length * .5f;
                if (f.WallDepth(a) < .25f || f.WallDepth(b) < .25f || EdgeRoom(a) < .4f || EdgeRoom(b) < .4f) continue;
                float yaw = Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg;
                var log = Fit(Place(dead, "Deadwood/" + Fallen[rng.Next(Fallen.Length)], t, p, yaw, new Vector3(Rnd(1.2f, 1.5f), Rnd(1.2f, 1.5f), length / 3.55f)), t, f.WallDepth);
                if (!log) continue;
                Own(log, p);
                logs.Add(p);
            }
            int plates = 0, sticks = 0;
            foreach (var p in points)
            {
                if (plates >= 3 && sticks >= 4) break;
                float depth = f.WallDepth(p);
                if (EdgeRoom(p) < 1.2f || trunks.Any(q => Vector2.Distance(q.at, p) < q.radius + .5f)) continue;
                if (plates < 3 && depth > 1.3f && logs.All(q => Vector2.Distance(q, p) > 2.2f))
                {
                    Own(Fit(Place(dead, "Deadwood/FA_DW08_StumpUprooted", t, p, Rnd(0, 360), Vector3.one * Rnd(1.8f, 2.5f)), t, f.WallDepth), p);
                    logs.Add(p);
                    plates++;
                }
                else if (sticks < 4 && depth > .2f && depth < 1.4f && logs.All(q => Vector2.Distance(q, p) > 1.6f))
                {
                    Own(Fit(Place(dead, "Deadwood/" + (rng.NextDouble() < .5 ? "FA_DW10_SticksLarge" : "FA_DW11_BranchForked"), t, p, Rnd(0, 360), Vector3.one * Rnd(1.2f, 1.6f)), t, f.WallDepth), p);
                    logs.Add(p);
                    sticks++;
                }
            }

            // Undergrowth thick along the lanes, and behind it brambles over everything the lanes leave: the tangle
            // hides what lies beyond and shows where the barrier stands.
            var growth = new GameObject("Undergrowth").transform;
            growth.SetParent(root, false);
            var green = new List<Vector2>();
            foreach (var p in points)
            {
                if (green.Count >= 40) break;
                float depth = f.WallDepth(p);
                if (depth < .05f || depth > 1f || f.InTrunk(p, .3f) || f.InLog(p)) continue;
                if (green.Any(q => Vector2.Distance(q, p) < 1.05f)) continue;
                var plant = Place(growth, "Plants/" + Undergrowth[rng.Next(Undergrowth.Length)], t, p, Rnd(0, 360), Vector3.one * Rnd(1.8f, 2.7f));
                Bare(plant);
                NoShadow(plant);
                green.Add(p);
            }
            var tangle = new List<Vector2>();
            foreach (var p in points)
            {
                if (tangle.Count >= 56) break;
                float depth = f.WallDepth(p);
                if (depth < .6f || f.InTrunk(p, .3f) || f.InLog(p)) continue;
                if (trunks.Any(q => Vector2.Distance(q.at, p) < q.radius + .15f)) continue;
                if (tangle.Any(q => Vector2.Distance(q, p) < 1.35f) || green.Any(q => Vector2.Distance(q, p) < .8f)) continue;
                double kind = rng.NextDouble();
                GameObject piece;
                bool solid = false;
                if (kind < .5)
                {
                    float w = Rnd(1f, 1.5f);
                    piece = Place(growth, "Plants/FA_VG10_ThornThicket", t, p, Rnd(0, 360), new Vector3(w, w * Rnd(1.8f, 2.6f), w));
                    solid = true;
                }
                else if (kind < .7) piece = Place(growth, "Plants/FA_VG09_BushTallThorny", t, p, Rnd(0, 360), Vector3.one * Rnd(1.6f, 2.3f));
                else if (kind < .85) piece = Place(growth, "Plants/FA_VG12_BroadLeavesTall", t, p, Rnd(0, 360), Vector3.one * Rnd(1.6f, 2.4f));
                else
                {
                    // A broken trunk propped across the tangle.
                    piece = Place(growth, "Deadwood/" + Fallen[rng.Next(Fallen.Length)], t, p, Rnd(0, 360), new Vector3(Rnd(1f, 1.3f), Rnd(1f, 1.3f), Rnd(.8f, 1.2f)));
                    if (piece) piece.transform.rotation *= Quaternion.Euler(Rnd(-18, 18), 0, Rnd(-10, 10));
                    solid = true;
                }
                // Thorns and trunks are as solid as they look (and keep out of the lanes); leaves and ferns are not.
                if (solid) Own(piece = Fit(piece, t, f.WallDepth), p); else Bare(piece);
                NoShadow(piece);
                tangle.Add(p);
            }
            // Roots over the lanes' sides, leaves on the lanes, now and then a web between the trunks.
            var floor = new GameObject("Floor").transform;
            floor.SetParent(root, false);
            int roots = 0, leaves = 0;
            foreach (var p in points)
            {
                if (roots >= 5 && leaves >= 5) break;
                if (f.InLog(p, .2f)) continue;
                float depth = f.WallDepth(p);
                if (roots < 5 && depth > -.5f && depth < .3f && !f.InTrunk(p, .2f))
                {
                    var r = Place(floor, "Plants/FA_VG17_MossOnRoot", t, p, Rnd(0, 360), Vector3.one * Rnd(1.3f, 1.9f));
                    Bare(r);
                    roots++;
                }
                else if (leaves < 5 && depth < -.6f && !f.InTrunk(p, .4f))
                {
                    var l = Place(floor, "Decals/" + (rng.NextDouble() < .5 ? "FA_D11_LeavesPile" : "FA_D12_LeavesTrail"), t, p, Rnd(0, 360), Vector3.one * Rnd(1.2f, 1.8f));
                    Bare(l);
                    leaves++;
                }
            }
            if (rng.NextDouble() < .45 && trunks.Count >= 2)
            {
                var (a, ra) = trunks[0];
                var (b, rb) = trunks[1];
                var web = Place(floor, "Decals/FA_D21_WebDrape", null, Vector2.zero, Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg, Vector3.one * 1.6f);
                var mid = (a + b) / 2;
                if (web) web.transform.localPosition = new Vector3(mid.x - 8, GroundY(t, mid) + 2.4f, mid.y - 8);
                Bare(web);
            }

            if (f.Heart)
            {
                // The shrine of the forest: a mask on the great tree, charms hanging round it, fireflies, a hum.
                var shrine = new GameObject("Heart").transform;
                shrine.SetParent(root, false);
                var toward = (f.PocketSpot - f.Hub).normalized;
                var face = f.Hub + toward * (ForestLayout.TrunkRadius - .1f);
                var mask = Place(shrine, "Markers/FA_MK15_WoodMask", null, Vector2.zero, Mathf.Atan2(toward.x, toward.y) * Mathf.Rad2Deg, Vector3.one * 1.8f);
                if (mask) mask.transform.localPosition = new Vector3(face.x - 8, GroundY(t, face) + 1.4f, face.y - 8);
                // The mask and the charms belong to the great tree: they go with it when it wakes.
                if (mask && great) mask.transform.SetParent(great.transform, true);
                for (int k = 0; k < 4; k++)
                {
                    float a = Mathf.Atan2(toward.y, toward.x) + (k - 1.5f) * .9f;
                    var at = f.Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (ForestLayout.TrunkRadius + 3.2f);
                    var charm = Place(shrine, "Markers/" + (k % 2 == 0 ? "FA_MK17_MobileTwigs" : "FA_MK10_CharmTwigDiamond"), null, Vector2.zero, Rnd(0, 360), Vector3.one * 1.3f);
                    if (charm) charm.transform.localPosition = new Vector3(at.x - 8, GroundY(t, at) + Rnd(2.6f, 3.4f), at.y - 8);
                    Bare(charm);
                    if (charm && great) charm.transform.SetParent(great.transform, true);
                }
                if (great) GreatStair(great.transform, root, t, f);
                var flies = Place(shrine, "VFX/Insects/Fireflies/FirefliesGreen", null, Vector2.zero, 0, Vector3.one);
                if (flies) flies.transform.localPosition = new Vector3(f.Hub.x - 8, GroundY(t, f.PocketSpot) + 1.2f, f.Hub.y - 8);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3 + Rnd(-.3f, .3f);
                    var at = f.Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (ForestLayout.TrunkRadius + .3f);
                    Bare(Place(shrine, "Plants/FA_VG17_MossOnRoot", t, at, -a * Mathf.Rad2Deg + 90, Vector3.one * Rnd(1.6f, 2.2f)));
                }
            }
            if (f.Glade)
            {
                // Motes drifting in the light over the glade.
                var motes = Place(root, "VFX/Dust/DustFloaty", null, Vector2.zero, 0, Vector3.one * 1.4f);
                if (motes) motes.transform.localPosition = new Vector3(f.Hub.x - 8, GroundY(t, f.Hub) + 1.6f, f.Hub.y - 8);
            }
            // The wall is what stands in it — giant trees, trunks, root plates, thorns — and nothing invisible: where they
            // leave a way in, more thorns (or a stump, a boulder), each with its own collider.
            var thorns = new GameObject("Wall").transform;
            thorns.SetParent(root, false);
            Seal(parent, t, f.WallDepth, .6f, null, p =>
            {
                double kind = rng.NextDouble();
                GameObject piece;
                if (kind < .75)
                {
                    float w = Rnd(1.1f, 1.5f);
                    piece = Place(thorns, "Plants/FA_VG10_ThornThicket", t, p, Rnd(0, 360), new Vector3(w, w * Rnd(1.8f, 2.4f), w));
                }
                else if (kind < .9) piece = Place(thorns, "Deadwood/FA_DW08_StumpUprooted", t, p, Rnd(0, 360), Vector3.one * Rnd(1.2f, 1.6f));
                else piece = Place(thorns, "Rocks/FA_RK06_BoulderSplit", t, p, Rnd(0, 360), Vector3.one * Rnd(1.1f, 1.5f));
                // Kept out of the lanes; where nothing that size fits, a small thicket.
                piece = Fit(piece, t, f.WallDepth) ?? Fit(Place(thorns, "Plants/FA_VG10_ThornThicket", t, p, Rnd(0, 360), new Vector3(.8f, 1.6f, .8f)), t, f.WallDepth);
                NoShadow(piece);
                Own(piece, p);
                return piece;
            }, 160, "forest wall");
        }


        /// <summary>
        /// The way up the greatest tree to its lookout: giant shelf fungi winding round the trunk, a walkable ramp hidden
        /// in their caps, and a broad cap at the top with the lookout. The whole stair belongs to the tree: woken, it takes
        /// its fungi with it.
        /// </summary>
        // Wide enough that a walker keeps a clear strip round the bend, not a thread the navigation loses.
        // Under the crown, not into it: a walker stands on the lookout with the leaves over his head.
        const float StairRadius = 3.2f, StairRise = 6.4f, StairSweepDeg = 290f, StairWidth = 2f;
        const int StairSteps = 26;

        /// <summary>Where the great tree's stair starts round its trunk: its foot faces the first way in, a little to the side.</summary>
        static float StairStart(ForestLayout f)
        {
            for (int s = 0; s < 4; s++)
                if (f.Open[s])
                {
                    var d = ForestLayout.Middle[s] - f.Hub;
                    return Mathf.Atan2(d.y, d.x) + 50 * Mathf.Deg2Rad;
                }
            return 0;
        }

        /// <summary>
        /// The great tree is solid as drawn — its roots, boughs and crown — so it is turned until the way up its stair
        /// and the lookout at the top keep a walker's room clear of it (tried every 5°; the clearest wins).
        /// </summary>
        static void TurnClear(GameObject tree, TileTerrain t, ForestLayout f)
        {
            var chunk = tree.transform;
            while (chunk.parent) chunk = chunk.parent;
            float a0 = StairStart(f), sweep = StairSweepDeg * Mathf.Deg2Rad;
            float h0 = GroundY(t, f.Hub + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * StairRadius) - .05f;
            var way = new List<Vector3>();
            for (int k = 0; k < StairSteps; k++)
            {
                float a = a0 + sweep * (k + .5f) / StairSteps, floor = h0 + StairRise * (k + .5f) / StairSteps;
                // The foot of the stair is stepped onto from outside, over the roots by the trunk.
                foreach (float r in k < 2 ? new[] { StairRadius, StairRadius + .5f } : new[] { StairRadius - .5f, StairRadius, StairRadius + .5f })
                    way.Add(new Vector3(f.Hub.x + Mathf.Cos(a) * r - 8, floor, f.Hub.y + Mathf.Sin(a) * r - 8));
            }
            float top = a0 + sweep + .3f;
            for (int k = -1; k <= 1; k++)
                way.Add(new Vector3(f.Hub.x + Mathf.Cos(top + k * .15f) * (StairRadius + .2f) - 8, h0 + StairRise, f.Hub.y + Mathf.Sin(top + k * .15f) * (StairRadius + .2f) - 8));
            float bestYaw = 0;
            int best = int.MaxValue;
            for (int k = 0; k < 72 && best > 0; k++)
            {
                tree.transform.localRotation = Quaternion.Euler(0, k * 5, 0);
                Physics.SyncTransforms();
                int hits = 0;
                foreach (var w in way)
                {
                    var feet = chunk.TransformPoint(w);
                    int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * .45f, feet + Vector3.up * 1.6f, .32f, touchHits, ~(1 << 4), QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                        if (touchHits[i].transform.IsChildOf(tree.transform)) { hits++; break; }
                }
                if (hits < best) { best = hits; bestYaw = k * 5; }
            }
            tree.transform.localRotation = Quaternion.Euler(0, bestYaw, 0);
            Physics.SyncTransforms();
            if (best == 0) return;
            var where = new List<string>();
            for (int i = 0; i < way.Count; i++)
            {
                var feet = chunk.TransformPoint(way[i]);
                int steps = way.Count - 3;
                int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * .45f, feet + Vector3.up * 1.6f, .32f, touchHits, ~(1 << 4), QueryTriggerInteraction.Ignore);
                for (int j = 0; j < n; j++)
                    if (touchHits[j].transform.IsChildOf(tree.transform)) { where.Add($"{(i < steps ? "step " + (i + 2) / 3 : "top")} ({touchHits[j].name})"); break; }
            }
            Debug.LogWarning($"[World] {chunk.name}: the great tree still stands in its stair's way at {best} of {way.Count} places: {string.Join(", ", where)}");
        }

        static void GreatStair(Transform tree, Transform root, TileTerrain t, ForestLayout f)
        {
            const float radius = StairRadius, rise = StairRise, sweepDeg = StairSweepDeg, width = StairWidth;
            const int steps = StairSteps;
            var stair = new GameObject("Stair").transform;
            stair.SetParent(root, false);
            float a0 = StairStart(f);
            float sweep = sweepDeg * Mathf.Deg2Rad;
            Vector2 At(float a, float r) => f.Hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            float h0 = GroundY(t, At(a0, radius)) - .05f;
            for (int k = 0; k < steps; k++)
            {
                float a = a0 + sweep * k / steps, b = a0 + sweep * (k + 1) / steps;
                Vector2 pa = At(a, radius), pb = At(b, radius);
                float ha = h0 + rise * k / steps, hb = h0 + rise * (k + 1) / steps;
                var from = new Vector3(pa.x - 8, ha, pa.y - 8);
                var to = new Vector3(pb.x - 8, hb, pb.y - 8);
                var step = new GameObject("Step").transform;
                step.SetParent(stair, false);
                var forward = to - from;
                step.localRotation = Quaternion.LookRotation(forward, Vector3.up);
                step.localPosition = (from + to) / 2 - step.localRotation * Vector3.up * .125f;
                var box = step.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(width, .25f, forward.magnitude + .2f);
                // Every step is a cap you can see (nothing to stand on that is not there).
                {
                    // A cap from the trunk out over the step: its shelf sticks out of the bark the way the prefab grows it.
                    float m = (a + b) / 2 + sweep / steps * .5f;
                    var bark = At(m, ForestLayout.TrunkRadius - .1f);
                    var inward = -new Vector2(Mathf.Cos(m), Mathf.Sin(m));
                    var cap = Place(stair, "Plants/FA_VG15_ShelfFungus", null, Vector2.zero, Mathf.Atan2(inward.x, inward.y) * Mathf.Rad2Deg, new Vector3(5.5f, 3.6f, 6f));
                    if (cap)
                    {
                        // Solid as drawn too: the shelf over the edge of the step holds a rope and a foot.
                        cap.transform.localPosition = new Vector3(bark.x - 8, (ha + hb) / 2 - .33f, bark.y - 8);
                    }
                }
            }
            // The top: a broad cap to stand on and look out from.
            float top = a0 + sweep + .3f;
            var platform = At(top, radius + .2f);
            var deck = new GameObject("Lookout cap").transform;
            deck.SetParent(stair, false);
            deck.localPosition = new Vector3(platform.x - 8, h0 + rise - .15f, platform.y - 8);
            // Within the broad caps you see: no ledge of air round them.
            var floor = deck.gameObject.AddComponent<BoxCollider>();
            floor.size = new Vector3(2.6f, .3f, 2.6f);
            var topInward = -new Vector2(Mathf.Cos(top), Mathf.Sin(top));
            var group = Place(stair, "Plants/FA_VG16_ShelfFungusGroup", null, Vector2.zero, Mathf.Atan2(topInward.x, topInward.y) * Mathf.Rad2Deg, new Vector3(8, 4, 8));
            if (group)
            {
                var bark = At(top, ForestLayout.TrunkRadius - .1f);
                group.transform.localPosition = new Vector3(bark.x - 8, h0 + rise - .5f, bark.y - 8);
            }
            var lantern = Place(stair, "Props/FA_PR02_LanternGround", null, Vector2.zero, 0, Vector3.one);
            if (lantern)
            {
                var spot = At(top + .25f, radius + .9f);
                lantern.transform.localPosition = new Vector3(spot.x - 8, h0 + rise, spot.y - 8);
            }
            var lookout = new GameObject("Lookout");
            lookout.transform.SetParent(stair, false);
            lookout.transform.localPosition = new Vector3(platform.x - 8, h0 + rise, platform.y - 8);
            lookout.AddComponent<Lookout>().revealRadius = 650;
            stair.SetParent(tree, true);
        }

        /// <summary>
        /// A giant hollow trunk lying along a lane, half sunk in the ground: the lane runs through it. It keeps its own
        /// shape as its collider, so inside it is open and its walls are solid.
        /// </summary>
        static void HollowTrunk(Transform parent, TileTerrain t)
        {
            var f = t.Forest;
            const float scale = 4.6f;
            float low = float.MaxValue;
            for (float k = -.5f; k <= .5f; k += .1f) low = Mathf.Min(low, GroundY(t, f.LogAt + f.LogDir * f.LogLength * k));
            var log = Place(parent, "Deadwood/FA_DW03_LogHollow", null, Vector2.zero, Mathf.Atan2(f.LogDir.x, f.LogDir.y) * Mathf.Rad2Deg, new Vector3(scale, scale, f.LogLength / 2.86f));
            if (!log) return;
            log.name = "Hollow trunk";
            // Sunk so the hollow's floor is the ground: inside, the walk is on the lane itself.
            log.transform.localPosition = new Vector3(f.LogAt.x - 8, low - .97f, f.LogAt.y - 8);
            // Its drawn shape is its collider (Shape): inside it is open, its walls are solid.
        }

        /// <summary>
        /// Deadfall closing the blind alcove of a forest cell: trunks piled across its mouth, a card obstacle like a
        /// thorn thicket — Fire burns it to ashes and the way to the reward at the alcove's end is open.
        /// </summary>
        static WorldObstacle DeadfallPiece(Transform parent, TileTerrain t, System.Random rng)
        {
            var f = t.Forest;
            float Rnd(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            var root = new GameObject("Deadfall");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(f.DeadfallAt.x - 8, GroundY(t, f.DeadfallAt), f.DeadfallAt.y - 8);
            root.transform.localRotation = Quaternion.Euler(0, Mathf.Atan2(f.DeadfallDir.x, f.DeadfallDir.y) * Mathf.Rad2Deg, 0);
            var live = new GameObject("Deadfall pile").transform;
            live.SetParent(root.transform, false);
            var burnt = new GameObject("Ashes").transform;
            burnt.SetParent(root.transform, false);
            GameObject Piece(Transform under, string asset, Vector3 at, Vector3 euler, Vector3 size)
            {
                var go = Place(under, asset, null, Vector2.zero, 0, size);
                if (!go) return null;
                go.transform.localPosition = at;
                go.transform.localRotation = Quaternion.Euler(euler);
                // The pile is solid as it looks (its pieces' own colliders, merged below); the ashes are not.
                if (under != live) foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                return go;
            }
            // Trunks across the mouth, one on another, crossing a little.
            for (int k = 0; k < 4; k++)
                Piece(live, "Deadwood/" + Fallen[rng.Next(Fallen.Length)], new Vector3(Rnd(-.3f, .3f), .1f + k * .55f, Rnd(-.45f, .45f)),
                    new Vector3(Rnd(-6, 6), 90 + Rnd(-22, 22), Rnd(-8, 8)), new Vector3(Rnd(1.2f, 1.45f), Rnd(1.2f, 1.45f), Rnd(1f, 1.15f)));
            Piece(live, "Deadwood/FA_DW08_StumpUprooted", new Vector3(Rnd(-1.4f, 1.4f), 0, Rnd(.3f, .7f)), new Vector3(0, Rnd(0, 360), 0), Vector3.one * 1.7f);
            for (int k = 0; k < 3; k++)
                Piece(live, "Deadwood/" + (k % 2 == 0 ? "FA_DW10_SticksLarge" : "FA_DW11_BranchForked"), new Vector3(Rnd(-1.5f, 1.5f), Rnd(.4f, 2f), Rnd(-.6f, .6f)),
                    new Vector3(Rnd(-30, 30), Rnd(0, 360), Rnd(-30, 30)), Vector3.one * Rnd(1.3f, 1.7f));
            // What is left after the fire: a low black heap.
            for (int k = 0; k < 3; k++)
                Piece(burnt, "Plants/FA_VG10_ThornThicketBurnt", new Vector3(-1.2f + k * 1.2f, 0, Rnd(-.3f, .3f)), new Vector3(0, Rnd(0, 360), 0), new Vector3(1.2f, .45f, 1.2f));
            Piece(burnt, "Deadwood/FA_DW09_SticksSmall", new Vector3(Rnd(-.8f, .8f), 0, Rnd(-.3f, .3f)), new Vector3(0, Rnd(0, 360), 0), Vector3.one * 1.4f);
            burnt.gameObject.SetActive(false);
            // Where the pile leaves a gap into the alcove, more trunks and branches across it (they burn with it).
            var chunk = parent;
            while (chunk.parent) chunk = chunk.parent;
            bool Behind(Vector2 p) => f.InAlcove(p) && Vector2.Dot(p - f.DeadfallAt, f.DeadfallDir) > .9f;
            float AlcoveWall(Vector2 p) => f.InAlcove(p) && Vector2.Dot(p - f.DeadfallAt, f.DeadfallDir) > -.6f ? 1 : f.WallDepth(p);
            Seal(chunk, t, f.WallDepth, .6f, Behind, p =>
            {
                var go = Place(live, "Deadwood/" + (rng.NextDouble() < .6 ? Fallen[rng.Next(Fallen.Length)] : "FA_DW08_StumpUprooted"), t, p, Rnd(0, 360), Vector3.one * Rnd(1.1f, 1.4f));
                return Fit(go, t, AlcoveWall) ?? Fit(Place(live, "Deadwood/FA_DW10_SticksLarge", t, p, Rnd(0, 360), Vector3.one * 1.3f), t, AlcoveWall);
            }, 30, "deadfall");
            var barrier = SolidFromParts(live.gameObject, "deadfall");
            var obstacle = root.AddComponent<WorldObstacle>();
            obstacle.kind = ObstacleKind.Thorns;
            obstacle.barrier = barrier;
            obstacle.intact = live.gameObject;
            obstacle.changed = burnt.gameObject;
            obstacle.tint = new Color(1f, .62f, .3f);
            Feedback(obstacle, "1c053b046a88e4e7b8c543f06e64b7e2", "40b80550388374f72a36150a39fb3d81", "a7ef1d25e7e664753b75a51a4803f109", "3070caddd61f843dd8ba16ca7c10784a");
            return obstacle;
        }

        /// <summary>A block of thorns over a rectangle (canonical metres): a wall until Fire burns it.</summary>
        static WorldObstacle ThornPiece(Transform parent, Rect r, System.Random rng, TileTerrain t = null)
        {
            var root = new GameObject("Thorns");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(r.center.x - 8, 0, r.center.y - 8);
            var live = new GameObject("Live thorns");
            live.transform.SetParent(root.transform, false);
            var burnt = new GameObject("Burnt thorns");
            burnt.transform.SetParent(root.transform, false);
            // Kept inside the piece so corridors stay open; tall enough to hide what is behind.
            for (float x = r.xMin + .9f; x < r.xMax - .5f; x += 1.5f)
                for (float z = r.yMin + .8f; z < r.yMax - .5f; z += 1.2f)
                {
                    var p = new Vector2(x + (float)rng.NextDouble() * .6f - .3f, z + (float)rng.NextDouble() * .6f - .3f);
                    float yaw = (float)rng.NextDouble() * 360, s = .85f + (float)rng.NextDouble() * .45f;
                    var a = Place(live.transform, "Plants/FA_VG10_ThornThicket", null, Vector2.zero, yaw, new Vector3(s, s * 1.9f, s));
                    if (a) a.transform.position = root.transform.position + new Vector3(p.x - r.center.x, 0, p.y - r.center.y);
                    var b = Place(burnt.transform, "Plants/FA_VG10_ThornThicketBurnt", null, Vector2.zero, yaw, new Vector3(s, s * .6f, s));
                    if (b) b.transform.position = root.transform.position + new Vector3(p.x - r.center.x, 0, p.y - r.center.y);
                }
            foreach (var c in burnt.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            // The thicket is solid as it looks: its bushes' own colliders; where they leave a way through, more bushes.
            if (t != null)
            {
                var chunk = parent;
                while (chunk.parent) chunk = chunk.parent;
                float Depth(Vector2 p) => Mathf.Min(Mathf.Min(p.x - r.xMin, r.xMax - p.x), Mathf.Min(p.y - r.yMin, r.yMax - p.y));
                float thick = Mathf.Min(r.width, r.height) / 2;
                Seal(chunk, t, Depth, Mathf.Min(.6f, thick * .7f), null, p =>
                {
                    float yaw = (float)rng.NextDouble() * 360, s = .9f + (float)rng.NextDouble() * .4f;
                    var a = Place(live.transform, "Plants/FA_VG10_ThornThicket", t, p, yaw, new Vector3(s, s * 1.9f, s));
                    var b = Place(burnt.transform, "Plants/FA_VG10_ThornThicketBurnt", t, p, yaw, new Vector3(s, s * .6f, s));
                    if (b) foreach (var c in b.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                    return a;
                }, 30, "thorns");
            }
            var barrier = SolidFromParts(live, "thorns");
            burnt.SetActive(false);
            var obstacle = root.AddComponent<WorldObstacle>();
            obstacle.kind = ObstacleKind.Thorns;
            obstacle.barrier = barrier;
            obstacle.intact = live;
            obstacle.changed = burnt;
            obstacle.tint = new Color(1f, .62f, .3f);
            Feedback(obstacle, "1c053b046a88e4e7b8c543f06e64b7e2", "40b80550388374f72a36150a39fb3d81", "a7ef1d25e7e664753b75a51a4803f109", "3070caddd61f843dd8ba16ca7c10784a");
            return obstacle;
        }

        // ---------- scenes ----------

        static readonly string[] RidgeStones = { "FA_RK03_BoulderLarge", "FA_RK06_BoulderSplit", "FA_RK08_RocksTallGroup", "FA_RK02_BoulderMedium", "FA_RK05_StoneTall" };

        /// <summary>Set pieces of a plain or plateau-top scene: props, rocks along its ridges, its thicket.</summary>
        static void ScenePieces(Transform parent, TileTerrain t, System.Random rng, List<WorldObstacle> obstacles)
        {
            var scene = t.Scene;
            var group = new GameObject("Scene " + scene.Id).transform;
            group.SetParent(parent, false);
            foreach (var prop in scene.Props)
            {
                // A landmark's things go to the part they stand in.
                if (!t.InPart(prop.At)) continue;
                var go = Place(group, prop.Asset, t, t.Local(prop.At), prop.Yaw, prop.Scale);
                if (!go) continue;
                // Effects over water (mist, a spring's bubbles) sit on its surface, not on the bed.
                if (prop.Asset.StartsWith("VFX/") && t.IsWater(t.Local(prop.At)))
                {
                    var at = go.transform.localPosition;
                    go.transform.localPosition = new Vector3(at.x, TileTerrain.WaterLevel, at.z);
                }
                if (prop.Lift != 0) go.transform.position += Vector3.up * prop.Lift;
                // Decoration is still solid if it looks solid (a stone circle, a ruined wall): only soft things stay soft.
            }
            // Same stones in every part of a landmark: each part keeps those standing on it.
            var stones = new System.Random(scene.Id.GetHashCode());
            foreach (var (index, spacing) in scene.RidgeRocks)
            {
                var ridge = scene.Ridges[index];
                for (int i = 0; i + 1 < ridge.Line.Length; i++)
                {
                    var a = ridge.Line[i];
                    var ab = ridge.Line[i + 1] - a;
                    int n = Mathf.Max(1, Mathf.RoundToInt(ab.magnitude / spacing));
                    for (int k = 0; k < n; k++)
                    {
                        var p = a + ab * ((k + .3f + (float)stones.NextDouble() * .4f) / n);
                        float s = Mathf.Lerp(.75f, 1.15f, (float)stones.NextDouble()) * Mathf.Clamp(ridge.Half / .85f, .7f, 1.1f);
                        float yaw = (float)stones.NextDouble() * 360;
                        string stoneName = RidgeStones[stones.Next(RidgeStones.Length)];
                        // The pass stays open.
                        if (scene.Gaps.Any(g => Vector2.Distance(p, g.Centre) < g.Radius + 1.1f)) continue;
                        if (!t.InPart(p)) continue;
                        var stone = Place(group, "Rocks/" + stoneName, t, t.Local(p), yaw, Vector3.one * s);
                        if (stone) stone.transform.position += Vector3.down * .15f;
                    }
                }
            }
            foreach (var (at, yaw, length) in scene.Walkways)
            {
                if (!t.InPart(at)) continue;
                // A whole trunk across a gully: resting a little sunk at both rims, with a flat walkway on top.
                var along = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                var dir = new Vector2(along.x, along.z);
                var local = t.Local(at);
                float rest = Mathf.Min(GroundY(t, local + dir * length * .45f), GroundY(t, local - dir * length * .45f)) - .85f;
                // You walk on the trunk itself: its own collider is flat on top.
                var log = Place(group, "Deadwood/FA_DW01_LogWhole", null, Vector2.zero, yaw, new Vector3(2.4f, 2.2f, length / 3.9f));
                if (log) log.transform.localPosition = new Vector3(local.x - 8, rest, local.y - 8);
            }
            foreach (var (line, deck) in scene.Boardwalks)
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    // Plank sections 2 m long on posts, level at the deck height, end to end along the line; each part
                    // keeps the sections that start in it.
                    var a = line[i];
                    var ab = line[i + 1] - a;
                    int n = Mathf.Max(1, Mathf.CeilToInt(ab.magnitude / 2));
                    float yaw = Mathf.Atan2(ab.x, ab.y) * Mathf.Rad2Deg;
                    for (int k = 0; k < n; k++)
                    {
                        var from = a + ab * k / n;
                        if (!t.InPart(from + ab / n * .5f)) continue;
                        var local = t.Local(from);
                        var board = Place(group, "Structures/FA_ST10_Boardwalk", null, Vector2.zero, yaw, new Vector3(1, 1, ab.magnitude / n / 2));
                        if (board) board.transform.localPosition = new Vector3(local.x - 8, deck - .25f, local.y - 8);
                    }
                }
            if (scene.Thicket.HasValue && t.InPart(scene.Thicket.Value.center))
            {
                var r = scene.Thicket.Value;
                obstacles.Add(ThornPiece(group, new Rect(r.position - t.Offset, r.size), rng, t));
            }
        }

        static readonly Dictionary<string, (string clip, float volume, float min, float max)> SoundCues = new Dictionary<string, (string, float, float, float)>
        {
            ["campfire"] = ("Assets/Audio/Sfx/impacts/fire_crackle_loop.wav", .5f, 1.5f, 14),
            ["fire-small"] = ("Assets/Plugins/Retro Arsenal/Sound/Fire/retro_fire_tiny_loop.wav", .3f, 1.5f, 11),
            ["bubbles"] = ("Assets/Plugins/Retro Arsenal/Sound/Misc/retro_bubble_loop.wav", .22f, 1.5f, 10),
            ["hum"] = ("Assets/Audio/Sfx/backroom/magic_hum_loop.wav", .3f, 2, 13),
        };

        const string CueFolder = "Assets/WorldAssembly/Audio";

        /// <summary>The world's own looping cue for a place that sounds, made once (edit it freely afterwards).</summary>
        static SfxCue Cue(string id)
        {
            string path = $"{CueFolder}/world_{id}.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
            if (cue) return cue;
            if (!SoundCues.TryGetValue(id, out var spec)) return null;
            Directory.CreateDirectory(CueFolder);
            cue = ScriptableObject.CreateInstance<SfxCue>();
            cue.clips = new[] { AssetDatabase.LoadAssetAtPath<AudioClip>(spec.clip) };
            cue.volume = spec.volume;
            cue.pitch = new Vector2(.97f, 1.03f);
            cue.spatialBlend = 1;
            cue.minDistance = spec.min;
            cue.maxDistance = spec.max;
            cue.cooldown = 0;
            AssetDatabase.CreateAsset(cue, path);
            return cue;
        }

        /// <summary>The scene's places that sound and its lookouts, in this part.</summary>
        static void Places(ChunkView view, TileTerrain t)
        {
            var scene = t.Scene;
            var group = new GameObject("Places").transform;
            group.SetParent(view.transform, false);
            var ambient = new List<AmbientSpot>();
            foreach (var (at, cue, lift) in scene.Sounds)
            {
                if (!t.InPart(at)) continue;
                var local = t.Local(at);
                var go = new GameObject("Sound " + cue);
                go.transform.SetParent(group, false);
                float y = t.IsWater(local) ? TileTerrain.WaterLevel : GroundY(t, local);
                go.transform.localPosition = new Vector3(local.x - 8, y + lift, local.y - 8);
                var spot = go.AddComponent<AmbientSpot>();
                spot.cue = Cue(cue);
                ambient.Add(spot);
            }
            view.ambient = ambient.ToArray();
            var lookouts = new List<Lookout>();
            foreach (var (at, radius) in scene.Lookouts)
            {
                if (!t.InPart(at)) continue;
                var local = t.Local(at);
                var go = new GameObject("Lookout");
                go.transform.SetParent(group, false);
                go.transform.localPosition = new Vector3(local.x - 8, GroundY(t, local), local.y - 8);
                var lookout = go.AddComponent<Lookout>();
                lookout.revealRadius = radius;
                lookouts.Add(lookout);
            }
            view.lookouts = lookouts.ToArray();
        }

        /// <summary>Height of whatever stands under a canonical point in the chunk being built (a deck over water), or the ground.</summary>
        static float SurfaceY(Transform chunk, TileTerrain t, Vector2 p)
        {
            Physics.SyncTransforms();
            var from = chunk.TransformPoint(new Vector3(p.x - 8, 40, p.y - 8));
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 80, ~(1 << 4), QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(chunk)) best = Mathf.Max(best, chunk.InverseTransformPoint(hit.point).y);
            return float.IsNegativeInfinity(best) ? GroundY(t, p) : best;
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
                    // You walk on the trunk itself: its own collider is flat on top.
                    var log = Place(parent, "Deadwood/FA_DW01_LogWhole", null, Vector2.zero, 90, new Vector3(1.5f, 1.2f, 2.15f));
                    if (log) log.transform.localPosition = new Vector3(-3.8f, -.15f, 0);
                    break;
                }
            }
        }

        // ---------- dressing ----------

        static readonly string[] Trees = { "FA_TR01_ThickCrooked", "FA_TR02_TallNarrow", "FA_TR03_DoubleFork", "FA_TR04_Leaning", "FA_TR05_Zigzag", "FA_TR06_Hollow", "FA_TR07_YoungStraight", "FA_TR08_YoungBow", "FA_TR09_YoungFork", "FA_TR10_DeadBare", "FA_TR12_Split", "FA_TR14_KnottyGiant" };
        static readonly string[] Grass = { "FA_VG04_GrassLow", "FA_VG05_GrassTall", "FA_VG06_GrassSparse", "FA_VG07_GrassDrooping", "FA_VG01_FernLow", "FA_VG11_BroadLeavesLow" };
        static readonly string[] Ferns = { "FA_VG01_FernLow", "FA_VG02_FernTall", "FA_VG03_FernDrooping", "FA_VG12_BroadLeavesTall", "FA_VG08_BushRound" };
        static readonly string[] Boulders = { "FA_RK01_BoulderSmall", "FA_RK02_BoulderMedium", "FA_RK03_BoulderLarge", "FA_RK05_StoneTall", "FA_RK06_BoulderSplit", "FA_RK07_RocksLowGroup" };
        static readonly string[] Bushes = { "FA_VG08_BushRound", "FA_VG09_BushTallThorny", "FA_VG12_BroadLeavesTall", "FA_VG02_FernTall", "FA_VG08_BushRound" };
        static readonly string[] Litter = { "FA_DW05_StumpLow", "FA_DW09_SticksSmall", "FA_DW11_BranchForked", "FA_DW04_LogRotten", "FA_DW07_StumpHollow" };
        static readonly string[] Pebbles = { "FA_RK01_BoulderSmall", "FA_RK07_RocksLowGroup", "FA_RK04_BoulderFlat" };
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
                    if (folder != "Plants" && NearLink(p, 2.2f)) continue;
                    float s = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
                    var placedGo = Place(dressing, folder + "/" + names[rng.Next(names.Length)], t, p, (float)rng.NextDouble() * 360, Vector3.one * s);
                    // Nothing on ground that falls away under it: trees and stones would hang over the slope.
                    if (placedGo && lastFall > (folder == "Trees" ? .25f : folder == "Plants" ? .5f : .35f))
                    {
                        Object.DestroyImmediate(placedGo);
                        continue;
                    }
                    placed.Add(p);
                    // Grass and bushes cast no shadows: many small draws for little.
                    if (placedGo && folder == "Plants")
                        foreach (var r in placedGo.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            bool High(Vector2 p) => t.Height(p) > Plateau * .8f;
            bool Low(Vector2 p) => t.Height(p) < Plateau * .2f;
            switch (t.Shape.Kind)
            {
                case ShapeKind.Plain:
                case ShapeKind.PlateauTop:
                case ShapeKind.Landmark:
                {
                    var scene = t.Scene;
                    Func<Vector2, bool> open = p => !t.SceneBusy(p) && !t.SceneTrail(p);
                    Scatter("Plants", Grass, scene.Grass, 1, 1.1f, .8f, 1.3f, open);
                    if (scene.Ferns > 0) Scatter("Plants", Ferns, scene.Ferns, 1.2f, 1.3f, .8f, 1.2f, open);
                    // Undergrowth and litter so no corner of a scene is bare: bushes without colliders, a few stumps and stones.
                    Scatter("Plants", Bushes, scene.Bushes, 1.2f, 1.9f, 1f, 1.7f, open);
                    // Things with colliders keep clear of the edges, where neighbours' navigation links land.
                    Scatter("Deadwood", Litter, 2, 2.6f, 2.4f, .8f, 1.1f, open);
                    Scatter("Rocks", Pebbles, 2, 2.6f, 2.4f, .7f, 1f, open);
                    // Trees only near the edges' corners, clear of the scene's lanes.
                    if (scene.Trees > 0) Scatter("Trees", Trees, scene.Trees, 1.8f, 3.4f, .65f, 1.45f, open);
                    break;
                }
                case ShapeKind.Cliff:
                    Scatter("Plants", Grass, 10, 1, 1.2f, .7f, 1.2f);
                    Scatter("Plants", Bushes, 4, 1.2f, 1.8f, .9f, 1.5f);
                    // A saddle's low band is narrow: nothing solid on it.
                    if (!t.IsSaddle) Scatter("Rocks", Boulders, 3, 2.4f, 3, .7f, 1.4f, Low);
                    Scatter("Trees", Trees, 2, 2, 3.4f, .65f, 1.4f, High);
                    if (!t.IsSaddle) Scatter("Trees", Trees, 2, 2, 3.4f, .65f, 1.4f, Low);
                    if (t.Variant == "ledge") Ledge(dressing, t);
                    if (t.HasFalls) Falls(dressing, t);
                    CliffFace(dressing, t, rng);
                    break;
                case ShapeKind.River:
                    Scatter("Plants", Ferns, 8, 1, 1.2f, .8f, 1.3f);
                    Scatter("Plants", Grass, 8, 1, 1, .9f, 1.3f);
                    Scatter("Plants", Bushes, 3, 1.2f, 1.8f, .9f, 1.5f, p => t.WaterDistance(p) > 1);
                    Scatter("Trees", Trees, 2, 2, 3.6f, .65f, 1.4f, p => t.WaterDistance(p) > 1.6f);
                    break;
                case ShapeKind.Canyon:
                    Scatter("Plants", Grass, 8, 1, 1.3f, .7f, 1.2f, p => t.Height(p) > -.2f);
                    // On the floor only along the walls: the floor is the way through.
                    if (t.Ramps.Count == 0) Scatter("Rocks", Boulders, 2, 1.5f, 2.5f, .6f, .9f, p => t.Height(p) < -t.CanyonDepth + .3f && t.DistanceToCentre(p) > TileTerrain.FloorHalf - .7f);
                    Scatter("Trees", Trees, 2, 2, 3.6f, .65f, 1.35f, p => t.Height(p) > -.2f && t.DistanceToCentre(p) > TileTerrain.RimHalf + 1.4f);
                    break;
                case ShapeKind.Maze when t.Forest != null:
                    // The lanes stay clear to walk; a little growth at their sides, a meadow in a glade.
                    Scatter("Plants", Ferns, 5, 1, 1.4f, .7f, 1.1f, p => t.Forest.WallDepth(p) > -.9f);
                    Scatter("Plants", Grass, t.Forest.Glade ? 22 : 6, 1, 1f, .8f, 1.3f);
                    break;
                case ShapeKind.Maze:
                    Scatter("Deadwood", Deadwood, 1, 6.5f, 2, .7f, .9f);
                    Scatter("Plants", Grass, 6, 1, 1.1f, .7f, 1.1f);
                    break;
            }
        }

        /// <summary>
        /// Everything on a chunk but its ground and water vanishes together once the chunk is small on screen (about
        /// 70 m away, deep in the fog): the camera reaches far for the silhouettes, and the loaded chunks out there
        /// would be thousands of draws for nothing. Their ground stays, so the land runs on into the fog.
        /// </summary>
        static void CullDetail(Transform chunk)
        {
            var renderers = chunk.GetComponentsInChildren<Renderer>(true)
                .Where(r => !r.transform.IsChildOf(chunk.Find("Ground")) && !(chunk.Find("Water") && r.transform.IsChildOf(chunk.Find("Water"))))
                .ToArray();
            if (renderers.Length == 0) return;
            var group = chunk.gameObject.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(.2f, renderers) });
            group.localReferencePoint = new Vector3(0, 2, 0);
            group.size = 16;
        }

        static readonly string[] FaceRocks = { "FA_RK09_CliffWall", "FA_RK10_CliffCornerInner", "FA_RK11_CliffCornerOuter", "FA_RK12_CliffLedge", "FA_RK08_RocksTallGroup" };

        /// <summary>
        /// Rock slabs set into a cliff face, turned to face out of it, and scree at its foot: the face stops reading as
        /// one smooth wall. The slabs have no colliders, so they give no hold the face itself does not.
        /// </summary>
        static void CliffFace(Transform parent, TileTerrain t, System.Random rng)
        {
            var spots = new List<(Vector2 p, Vector2 outward)>();
            for (float x = 1.5f; x < 15; x += .75f)
                for (float z = 1.5f; z < 15; z += .75f)
                {
                    var p = new Vector2(x, z);
                    float h = t.Height(p);
                    if (h < Plateau * .3f || h > Plateau * .7f || t.OnRamp(p)) continue;
                    var gradient = new Vector2(t.Height(p + Vector2.right * .3f) - t.Height(p - Vector2.right * .3f), t.Height(p + Vector2.up * .3f) - t.Height(p - Vector2.up * .3f)) / .6f;
                    if (gradient.magnitude < 1.5f) continue;
                    // The waterfall's strip of the face stays bare.
                    if (t.HasFalls && Mathf.Abs(Vector2.Dot(p - t.FallsTop, new Vector2(-t.FallsDown.y, t.FallsDown.x))) < 2.2f) continue;
                    spots.Add((p, -gradient.normalized));
                }
            var chosen = new List<Vector2>();
            foreach (var (p, outward) in spots.OrderBy(_ => rng.NextDouble()))
            {
                if (chosen.Count >= 7 || chosen.Any(q => Vector2.Distance(p, q) < 2.6f)) continue;
                chosen.Add(p);
                float yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg + (float)rng.NextDouble() * 30 - 15;
                float s = 1.1f + (float)rng.NextDouble() * .7f;
                var rock = Place(parent, "Rocks/" + FaceRocks[rng.Next(FaceRocks.Length)], null, Vector2.zero, yaw, new Vector3(s, s * (1 + (float)rng.NextDouble() * .5f), s));
                if (!rock) continue;
                // Set into the face: a little behind the surface, at the height of the spot.
                var at = p - outward * .5f;
                rock.transform.localPosition = new Vector3(at.x - 8, t.Height(p) - 1.2f * s, at.y - 8);
                // Scree below it.
                var foot = p + outward * 1.6f;
                if (!t.IsSaddle && t.Height(foot) < Plateau * .25f && !t.OnRamp(foot) && !NearLink(foot, 1.6f) && t.WaterDistance(foot) > .5f && rng.NextDouble() < .6)
                    Place(parent, "Rocks/" + (rng.NextDouble() < .5 ? "FA_RK07_RocksLowGroup" : "FA_RK01_BoulderSmall"), t, foot, (float)rng.NextDouble() * 360, Vector3.one * (.8f + (float)rng.NextDouble() * .5f));
            }
        }

        /// <summary>Water over the edge of a straight cliff into the pool at its foot: the fall, spray and reeds.</summary>
        static void Falls(Transform parent, TileTerrain t)
        {
            // The effect pours out along its local back (its edge emitter is turned that way).
            float yaw = Mathf.Atan2(t.FallsDown.x, t.FallsDown.y) * Mathf.Rad2Deg + 180;
            var top = Place(parent, "VFX/Liquids/Water/WaterFallBig", null, Vector2.zero, yaw, Vector3.one);
            if (top) top.transform.localPosition = new Vector3(t.FallsTop.x - 8, GroundY(t, t.FallsTop) + .1f, t.FallsTop.y - 8);
            var foot = t.FallsTop + t.FallsDown * 2.2f;
            var splash = Place(parent, "VFX/Liquids/Water/RippleSplash", null, Vector2.zero, 0, Vector3.one * 1.2f);
            if (splash) splash.transform.localPosition = new Vector3(foot.x - 8, TileTerrain.WaterLevel + .05f, foot.y - 8);
            var mist = Place(parent, "VFX/Smoke/White/SmokeFlatWhite", null, Vector2.zero, 0, Vector3.one);
            if (mist) mist.transform.localPosition = new Vector3(foot.x - 8, TileTerrain.WaterLevel + .2f, foot.y - 8);
            var side = new Vector2(-t.FallsDown.y, t.FallsDown.x);
            foreach (float s in new[] { -1f, 1f })
            {
                var at = t.FallsPool + side * s * (TileTerrain.FallsBank - .2f);
                var reeds = Place(parent, "Plants/FA_VG05_GrassTall", t, at, s * 50, Vector3.one * 1.3f);
                if (reeds) foreach (var c in reeds.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            // A mossy rock to each side of the fall on top.
            foreach (float s in new[] { -1.3f, 1.3f })
            {
                var at = t.FallsTop + side * s - t.FallsDown * .3f;
                var rock = Place(parent, "Rocks/FA_RK06_BoulderSplit", t, at, s * 70, Vector3.one * .7f);
                if (rock) rock.transform.position += Vector3.down * .2f;
            }
        }

        /// <summary>A rock shelf halfway up a straight cliff: Impulse I twice.</summary>
        static void Ledge(Transform parent, TileTerrain t)
        {
            var shelf = new GameObject("Ledge");
            shelf.transform.SetParent(parent, false);
            shelf.transform.localPosition = new Vector3(4.5f - 8, 0, 7.2f - 8);
            // The rock's own collider is the shelf.
            var rock = Place(shelf.transform, "Rocks/FA_RK13_StepFlat", null, Vector2.zero, 0, new Vector3(2.4f, 1.6f, 2.4f));
            if (rock) rock.transform.localPosition = new Vector3(0, Plateau * .5f - .8f, 0);
        }

        static bool Free(TileTerrain t, Vector2 p)
        {
            if (t.IsWater(p) || t.OnRamp(p) || t.InMazeWall(p)) return false;
            if (t.Wet && t.WaterDistance(p) < .3f) return false;
            if (t.SceneThicket(p)) return false;
            if (t.Shape.Kind == ShapeKind.River && t.DistanceToCentre(p) < TileTerrain.BankHalf + .6f) return false;
            if (t.Shape.Kind == ShapeKind.Canyon && t.DistanceToCentre(p) < TileTerrain.RimHalf + .4f && t.DistanceToCentre(p) > TileTerrain.FloorHalf - .3f) return false;
            if (t.Shape.Kind == ShapeKind.Maze && t.Forest == null && Mathf.Abs(p.x - 8) < 1.6f && Mathf.Abs(p.y - 8) < 1.6f) return false;
            if (t.Forest != null && (t.Forest.InTrunk(p, 1.1f) || t.Forest.InLog(p, .3f))) return false;
            // Steep ground (cliff faces) stays bare.
            float h = t.Height(p);
            float slope = Mathf.Max(Mathf.Abs(t.Height(p + Vector2.right * .5f) - h), Mathf.Abs(t.Height(p + Vector2.up * .5f) - h)) / .5f;
            return slope < .5f;
        }

        /// <summary>The ground collider of the chunk being built: things are set on the mesh as built, not on the height function.</summary>
        static MeshCollider groundCollider;
        /// <summary>The chunk's data asset (ground mesh): meshes made while dressing go into it.</summary>
        static string dataPath;

        // ---------- walls without invisible boxes ----------

        /// <summary>The player's capsule radius (FirstPersonPlayer): narrower gaps stop him.</summary>
        const float PlayerRadius = .28f;
        const float SealCell = .25f;
        static readonly Collider[] sealHits = new Collider[24];
        static readonly (int di, int dj)[] Steps4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        /// <summary>
        /// Whether the player could stand at a canonical point: nothing solid but the ground (and water, triggers)
        /// within his radius at his feet, waist and head.
        /// </summary>
        static bool Standable(Transform chunk, TileTerrain t, Vector2 p)
        {
            float y = GroundY(t, p);
            for (int k = 0; k < 3; k++)
            {
                var at = chunk.TransformPoint(new Vector3(p.x - 8, y + .45f + k * .55f, p.y - 8));
                int n = Physics.OverlapSphereNonAlloc(at, PlayerRadius, sealHits, ~(1 << 4), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (sealHits[i] != groundCollider && sealHits[i].transform.IsChildOf(chunk)) return false;
            }
            return true;
        }

        /// <summary>
        /// Walls of visible things only (no invisible boxes): from the lanes — <paramref name="depth"/> below 0 and not
        /// <paramref name="shut"/> — the player must not get <paramref name="leakDepth"/> into a wall, nor into a shut
        /// place. Wherever he could, <paramref name="fill"/> stands one more visible piece with its own collider at the
        /// mouth of the gap, and the check runs again. Returns the pieces added; −1 when it gave up.
        /// </summary>
        static int Seal(Transform chunk, TileTerrain t, Func<Vector2, float> depth, float leakDepth, Func<Vector2, bool> shut, Func<Vector2, GameObject> fill, int max, string what)
        {
            int n = Mathf.RoundToInt(TileTerrain.Size / SealCell);
            var free = new bool[n, n];
            var deep = new float[n, n];
            var closed = new bool[n, n];
            Vector2 At(int i, int j) => new Vector2((i + .5f) * SealCell, (j + .5f) * SealCell);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    deep[i, j] = depth(At(i, j));
                    closed[i, j] = shut != null && shut(At(i, j));
                }
            void Measure(int i0, int j0, int i1, int j1)
            {
                Physics.SyncTransforms();
                for (int i = Mathf.Max(0, i0); i <= Mathf.Min(n - 1, i1); i++)
                    for (int j = Mathf.Max(0, j0); j <= Mathf.Min(n - 1, j1); j++)
                        free[i, j] = Standable(chunk, t, At(i, j));
            }
            Measure(0, 0, n - 1, n - 1);
            var from = new int[n * n];
            var queue = new Queue<int>();
            for (int added = 0; ; added++)
            {
                for (int k = 0; k < from.Length; k++) from[k] = -2;
                queue.Clear();
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (free[i, j] && deep[i, j] < 0 && !closed[i, j]) { from[i * n + j] = -1; queue.Enqueue(i * n + j); }
                int leak = -1;
                while (queue.Count > 0 && leak < 0)
                {
                    int c = queue.Dequeue();
                    foreach (var (di, dj) in Steps4)
                    {
                        int i = c / n + di, j = c % n + dj;
                        if (i < 0 || j < 0 || i >= n || j >= n || !free[i, j] || from[i * n + j] != -2) continue;
                        from[i * n + j] = c;
                        if (deep[i, j] >= leakDepth || closed[i, j]) { leak = i * n + j; break; }
                        queue.Enqueue(i * n + j);
                    }
                }
                if (leak < 0) return added;
                if (added >= max)
                {
                    Debug.LogWarning($"[World] {chunk.name}: {what} still open at {At(leak / n, leak % n)} after {added} pieces");
                    return -1;
                }
                // The mouth of the gap: walking back from the leak toward the lane, the last cell still in the wall.
                int mouth = leak;
                for (int c = from[leak]; c >= 0; c = from[c])
                {
                    if (deep[c / n, c % n] < .3f && !closed[c / n, c % n]) break;
                    mouth = c;
                }
                var piece = fill(At(mouth / n, mouth % n));
                if (!piece) return -1;
                int r = Mathf.CeilToInt(2.5f / SealCell);
                Measure(mouth / n - r, mouth % n - r, mouth / n + r, mouth % n + r);
            }
        }

        /// <summary>
        /// Keeps a solid piece out of the lanes: where its collider pokes more than <paramref name="allowance"/> out of
        /// the wall (<paramref name="depth"/> below 0) at body height, it moves deeper into the wall, then shrinks;
        /// failing that it goes. Returns the piece, or null.
        /// </summary>
        static readonly Collider[] touchHits = new Collider[32];

        /// <summary>Is <paramref name="c"/>'s surface within <paramref name="radius"/> of a point?</summary>
        static bool Touches(Collider c, Vector3 point, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(point, radius, touchHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (touchHits[i] == c) return true;
            return false;
        }

        static GameObject Fit(GameObject go, TileTerrain t, Func<Vector2, float> depth, float allowance = .15f)
        {
            if (!go) return null;
            var chunk = go.transform;
            while (chunk.parent) chunk = chunk.parent;
            Vector2 Canon(Vector3 world) { var l = chunk.InverseTransformPoint(world); return new Vector2(l.x + 8, l.z + 8); }
            float GroundAt(Vector2 p) => chunk.TransformPoint(new Vector3(p.x - 8, GroundY(t, p), p.y - 8)).y;
            bool Out()
            {
                Physics.SyncTransforms();
                foreach (var c in go.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger) continue;
                    // A hollow mesh (a rock, a trunk as drawn) has no inside to test: its surface near the point counts.
                    bool hollow = c is MeshCollider mc && !mc.convex;
                    var b = c.bounds;
                    for (float x = b.min.x; x <= b.max.x + .01f; x += .25f)
                        for (float z = b.min.z; z <= b.max.z + .01f; z += .25f)
                        {
                            var p = Canon(new Vector3(x, 0, z));
                            if (depth(p) >= -allowance) continue;
                            float y = GroundAt(p);
                            for (int k = 0; k < 4; k++)
                            {
                                var q = new Vector3(x, y + .35f + k * .45f, z);
                                if (q.y < b.min.y - .15f || q.y > b.max.y + .15f) continue;
                                if (hollow ? Touches(c, q, .13f) : (c.ClosestPoint(q) - q).sqrMagnitude < .0025f) return true;
                            }
                        }
                }
                return false;
            }
            if (!Out()) return go;
            var start = go.transform.position;
            var scale = go.transform.localScale;
            var p0 = Canon(start);
            float lift = start.y - GroundAt(p0);
            var grad = new Vector2(depth(p0 + Vector2.right * .3f) - depth(p0 - Vector2.right * .3f), depth(p0 + Vector2.up * .3f) - depth(p0 - Vector2.up * .3f));
            if (grad.sqrMagnitude > 1e-8f) grad.Normalize();
            foreach (float shrink in new[] { 1f, .8f, .65f })
                foreach (float d in new[] { 0f, .3f, .6f, .9f, 1.2f })
                {
                    var p = p0 + grad * d;
                    var w = chunk.TransformPoint(new Vector3(p.x - 8, 0, p.y - 8));
                    go.transform.position = new Vector3(w.x, GroundAt(p) + lift * shrink, w.z);
                    go.transform.localScale = scale * shrink;
                    if (!Out()) return go;
                }
            Object.DestroyImmediate(go);
            return null;
        }

        /// <summary>
        /// One collider for an obstacle made of pieces (a card barrier that must stay one collider): the pieces' own
        /// colliders, merged into a mesh on <paramref name="root"/> and taken off the pieces. It stands exactly where
        /// the pieces are, and goes with them.
        /// </summary>
        static MeshCollider SolidFromParts(GameObject root, string meshName)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var toRoot = root.transform.worldToLocalMatrix;
            void Box(Matrix4x4 m, Vector3 centre, Vector3 size)
            {
                int b = verts.Count;
                for (int k = 0; k < 8; k++)
                {
                    var corner = centre + Vector3.Scale(size * .5f, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    verts.Add(toRoot.MultiplyPoint3x4(m.MultiplyPoint3x4(corner)));
                }
                int[] faces = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
                foreach (int f in faces) tris.Add(b + f);
            }
            foreach (var c in root.GetComponentsInChildren<Collider>(true).ToList())
            {
                if (c.isTrigger) continue;
                var m = c.transform.localToWorldMatrix;
                switch (c)
                {
                    case BoxCollider box: Box(m, box.center, box.size); break;
                    case CapsuleCollider cap:
                    {
                        var size = Vector3.one * cap.radius * 2;
                        size[cap.direction] = Mathf.Max(cap.height, cap.radius * 2);
                        Box(m, cap.center, size);
                        break;
                    }
                    case SphereCollider sphere: Box(m, sphere.center, Vector3.one * sphere.radius * 2); break;
                    case MeshCollider mc when mc.sharedMesh:
                    {
                        int b = verts.Count;
                        foreach (var v in mc.sharedMesh.vertices) verts.Add(toRoot.MultiplyPoint3x4(m.MultiplyPoint3x4(v)));
                        foreach (int i in mc.sharedMesh.triangles) tris.Add(b + i);
                        break;
                    }
                }
                Object.DestroyImmediate(c);
            }
            var mesh = new Mesh { name = meshName, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            if (!string.IsNullOrEmpty(dataPath)) AssetDatabase.AddObjectToAsset(mesh, dataPath);
            var collider = root.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return collider;
        }
        /// <summary>How far the ground falls away under the footprint of the last thing placed on it, m.</summary>
        static float lastFall;

        /// <summary>Height of the built ground mesh at a canonical point (the mesh's vertices wander a little from the height function).</summary>
        static float GroundY(TileTerrain t, Vector2 p)
        {
            if (groundCollider)
            {
                var from = groundCollider.transform.TransformPoint(new Vector3(p.x - 8, 80, p.y - 8));
                if (groundCollider.Raycast(new Ray(from, Vector3.down), out var hit, 200)) return groundCollider.transform.InverseTransformPoint(hit.point).y;
            }
            return t.Height(p);
        }

        /// <summary>
        /// A ForestArt prefab on the chunk. With terrain: set on the lowest point of the ground under its footprint (its
        /// colliders, or a little of its renderers for plants), so no edge hangs in the air; <see cref="lastFall"/> says
        /// how far the ground falls away under it, for callers that would rather not place it on such ground.
        /// </summary>
        static GameObject Place(Transform parent, string asset, TileTerrain t, Vector2 p, float yaw, Vector3 scale)
        {
            string path = asset.StartsWith("VFX/") ? $"Assets/Plugins/Retro Arsenal/Prefabs/Environment/{asset.Substring(4)}.prefab" : $"Assets/ForestArt/Prefabs/{asset}.prefab";
            if (!prefabs.TryGetValue(path, out var prefab))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                prefabs[path] = prefab;
                if (!prefab) Debug.LogWarning("[World] missing prefab " + path);
            }
            lastFall = 0;
            if (!prefab) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            placed.Add((go, asset));
            go.transform.localPosition = new Vector3(p.x - 8, 0, p.y - 8);
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = scale;
            if (t == null) { Shape(go, asset); return go; }
            // Chunk-local: the parent may be a nested group at the chunk origin.
            var chunk = parent;
            while (chunk.parent) chunk = chunk.parent;
            go.transform.position = chunk.TransformPoint(new Vector3(p.x - 8, 0, p.y - 8));
            Physics.SyncTransforms();
            float radius = Footprint(go);
            float low = GroundY(t, p), high = low;
            for (int k = 0; k < 8 && radius > .05f; k++)
            {
                float a = k * Mathf.PI / 4;
                var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                float h = GroundY(t, q);
                low = Mathf.Min(low, h);
                high = Mathf.Max(high, h);
            }
            lastFall = high - low;
            if (Lies(asset))
            {
                // Things that lie on the ground follow its slope: set flush on the ground's mean normal under them.
                var normal = GroundNormal(t, p, Mathf.Max(radius, .3f));
                go.transform.rotation = chunk.rotation * Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, yaw, 0);
                go.transform.position = chunk.TransformPoint(new Vector3(p.x - 8, GroundY(t, p) - .06f, p.y - 8));
            }
            else go.transform.position = chunk.TransformPoint(new Vector3(p.x - 8, low - .04f, p.y - 8));
            // Set by the prefab's own colliders (its trunk, its footprint); from here on it is hit where it is drawn.
            Shape(go, asset);
            return go;
        }

        // ---------- what you see is what you hit ----------

        /// <summary>Readable copies of the ForestArt meshes (their models are not readable): the colliders of the chunks.</summary>
        const string CollisionPath = "Assets/WorldAssembly/Collision meshes.asset";
        static readonly Dictionary<Mesh, Mesh> collisionCopies = new Dictionary<Mesh, Mesh>();

        /// <summary>
        /// Everything solid is hit exactly where it is drawn (Kayospace: colliders as close as can be to what the player
        /// sees — a rope holds on a branch, a card stops on a rock's edge, nothing stands in the air): each of its meshes
        /// is its own collider, crowns and branches too, no boxes or capsules standing in for them. Light loose things
        /// get the convex hull of each part, so they can have a body. Soft things (<see cref="ChunkLooks.Soft"/>: grass,
        /// ferns, leaves, bushes, ivy, moss, cobwebs, cloth, decals) are walked through as they look: no collider.
        /// </summary>
        static void Shape(GameObject go, string asset)
        {
            if (!go || asset.StartsWith("VFX/")) return;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger) Object.DestroyImmediate(c);
            if (ChunkLooks.Soft(asset)) return;
            bool convex = LooseMass.ContainsKey(asset.Substring(asset.LastIndexOf('/') + 1));
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh || !filter.GetComponent<MeshRenderer>()) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = CollisionCopy(filter.sharedMesh);
                collider.convex = convex;
            }
            Physics.SyncTransforms();
        }

        /// <summary>A closed shape: every edge (its seams welded) shared by two faces.</summary>
        static bool Closed(Vector3[] vertices, List<int> triangles)
        {
            var id = new int[vertices.Length];
            var welded = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * 1000);
                if (!welded.TryGetValue(key, out int k)) welded[key] = k = welded.Count;
                id[i] = k;
            }
            var edges = new Dictionary<(int, int), int>();
            for (int i = 0; i + 2 < triangles.Count; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = id[triangles[i + e]], b = id[triangles[i + (e + 1) % 3]];
                    var edge = a < b ? (a, b) : (b, a);
                    edges.TryGetValue(edge, out int count);
                    edges[edge] = count + 1;
                }
            return edges.Values.All(count => count % 2 == 0);
        }

        /// <summary>A readable copy of a mesh's shape (all its parts as one), shared by every chunk that uses it.</summary>
        static Mesh CollisionCopy(Mesh source)
        {
            if (collisionCopies.TryGetValue(source, out var copy) && copy) return copy;
            copy = new Mesh { name = source.name + " collision", indexFormat = source.vertexCount > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            copy.vertices = source.vertices;
            var triangles = new List<int>();
            for (int s = 0; s < source.subMeshCount; s++) triangles.AddRange(source.GetTriangles(s));
            // An open shape (leaf sheets of a crown, a moss cap on a rock) is drawn from both sides: it is hit from both
            // sides too, so nothing passes through it from behind (casts and rays see front faces only).
            if (!Closed(copy.vertices, triangles))
                for (int i = 0, n = triangles.Count; i + 2 < n; i += 3)
                {
                    triangles.Add(triangles[i]);
                    triangles.Add(triangles[i + 2]);
                    triangles.Add(triangles[i + 1]);
                }
            copy.triangles = triangles.ToArray();
            copy.RecalculateBounds();
            if (!AssetDatabase.LoadAssetAtPath<Mesh>(CollisionPath)) AssetDatabase.CreateAsset(copy, CollisionPath);
            else AssetDatabase.AddObjectToAsset(copy, CollisionPath);
            collisionCopies[source] = copy;
            return copy;
        }

        /// <summary>Lying things follow the ground; standing ones (trees, posts, walls, houses, signs) stay upright.</summary>
        static bool Lies(string asset) =>
            asset.StartsWith("Rocks/") || asset.StartsWith("Deadwood/") || asset.StartsWith("Props/") || asset.StartsWith("Decals/") ||
            asset.StartsWith("Plants/") || asset == "Special/FA_SP11_BrokenWagon" || asset == "Markers/FA_MK16_StoneCircle";

        /// <summary>Mean normal of the built ground round a point.</summary>
        static Vector3 GroundNormal(TileTerrain t, Vector2 p, float radius)
        {
            var sum = Vector3.zero;
            for (int k = 0; k < 5; k++)
            {
                var q = k == 0 ? p : p + new Vector2(Mathf.Cos(k * Mathf.PI / 2), Mathf.Sin(k * Mathf.PI / 2)) * radius;
                if (!groundCollider) break;
                var from = groundCollider.transform.TransformPoint(new Vector3(q.x - 8, 80, q.y - 8));
                if (groundCollider.Raycast(new Ray(from, Vector3.down), out var hit, 200)) sum += groundCollider.transform.InverseTransformDirection(hit.normal);
            }
            return sum.sqrMagnitude > 1e-6f ? sum.normalized : Vector3.up;
        }

        /// <summary>Radius of what stands on the ground: the colliders' footprint, else a little of the renderers'.</summary>
        static float Footprint(GameObject go)
        {
            var colliders = go.GetComponentsInChildren<Collider>();
            Bounds? b = null;
            foreach (var c in colliders) { if (b == null) b = c.bounds; else { var x = b.Value; x.Encapsulate(c.bounds); b = x; } }
            if (b != null) return Mathf.Clamp(Mathf.Min(b.Value.extents.x, b.Value.extents.z) * .85f, .1f, 2.2f);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; } }
            return b != null ? Mathf.Clamp(Mathf.Min(b.Value.extents.x, b.Value.extents.z) * .4f, .1f, .6f) : .1f;
        }

        static readonly Vector2[] CornerAt = { new Vector2(0, 16), new Vector2(16, 16), new Vector2(16, 0), new Vector2(0, 0) };

        /// <summary>Near the middle of an edge segment, where the links to neighbouring chunks land.</summary>
        static bool NearLink(Vector2 p, float distance)
        {
            for (int side = 0; side < 4; side++)
                for (int seg = 0; seg < 3; seg++)
                    if (Vector2.Distance(p, Vector2.Lerp(CornerAt[side], CornerAt[(side + 1) & 3], (seg + .5f) / 3)) < distance + .9f) return true;
            return false;
        }

        /// <summary>
        /// Ground height next to the middle of each edge segment (canonical side × 3 + segment, 0.9 m inside): where
        /// the streamer's navigation links between chunks land. Landmark parts meet their neighbours above ground level.
        /// </summary>
        static float[] EdgeHeights(TileTerrain t)
        {
            var result = new float[12];
            for (int side = 0; side < 4; side++)
                for (int seg = 0; seg < 3; seg++)
                {
                    var p = Vector2.Lerp(CornerAt[side], CornerAt[(side + 1) & 3], (seg + .5f) / 3);
                    p += (new Vector2(8, 8) - p).normalized * .9f;
                    result[side * 3 + seg] = GroundY(t, p);
                }
            return result;
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
                    if (!Free(t, p)) continue;
                    candidates.Add(p);
                }
            // Only where the chunk's own navigation is: never inside a rock or under a wall.
            if (view.navMesh)
            {
                var far = new Vector3(-20000, 0, -20000);
                var instance = NavMesh.AddNavMeshData(view.navMesh, far, Quaternion.identity);
                candidates = candidates.Where(p => NavMesh.SamplePosition(far + new Vector3(p.x - 8, GroundY(t, p), p.y - 8), out var hit, .3f, NavMesh.AllAreas)).ToList();
                NavMesh.RemoveNavMeshData(instance);
            }
            // Finds: on the best ground the shape has — the top of a cliff, the floor of a canyon, the corridor of a maze.
            Func<Vector2, bool> prefer = t.Shape.Kind switch
            {
                ShapeKind.Cliff => p => t.Height(p) > Plateau * .8f,
                ShapeKind.Canyon => p => t.Height(p) < -t.CanyonDepth + .3f,
                _ => p => true,
            };
            // A glade keeps its finds in the open.
            if (t.Forest != null && t.Forest.Glade) prefer = p => Vector2.Distance(p, t.Forest.Hub) < ForestLayout.GladeRadius - 1;
            // Behind the deadfall lies only the alcove's own reward.
            if (t.Forest != null && t.Forest.Deadfall) candidates = candidates.Where(p => !t.Forest.InAlcove(p)).ToList();
            var finds = Spread(candidates.Where(prefer).ToList(), 4, rng);
            if (finds.Count == 0) finds = Spread(candidates, 3, rng);
            // Scenes put their finds where the scene points: the top of the knoll, the floor of the ruin.
            if (t.Scene != null)
            {
                var own = t.Scene.Finds.Where(t.InPart).Select(t.Local).ToList();
                // Landmark parts without a spot of their own keep the spread ones.
                if (own.Count > 0 || t.Shape.Kind != ShapeKind.Landmark) finds = own;
            }
            if (t.Variant == "ledge") finds.Insert(0, new Vector2(4.5f, 7.2f));
            float? FindHeight(Vector2 p, int i)
            {
                if (t.Variant == "ledge" && i == 0) return Plateau * .5f + .05f;
                if (t.Scene != null && t.Scene.OnBoardwalk(p + t.Offset, 1.2f)) return SurfaceY(view.transform, t, p);
                return null;
            }
            view.findSockets = finds.Select((p, i) => Socket(group, "Find " + i, t, p, FindHeight(p, i))).ToArray();

            if (t.Variant == "island") view.pocketSocket = Socket(group, "Pocket", t, new Vector2(8, 8), null);
            if (t.Shape.Kind == ShapeKind.Maze) view.pocketSocket = Socket(group, "Pocket", t, t.Forest != null ? t.Forest.PocketSpot : new Vector2(8, 8), null);
            if (t.Scene?.Pocket != null && t.InPart(t.Scene.Pocket.Value)) view.pocketSocket = Socket(group, "Pocket", t, t.Local(t.Scene.Pocket.Value), null);

            Func<Vector2, bool> home = t.Shape.Kind switch
            {
                ShapeKind.Canyon => p => t.Height(p) < -t.CanyonDepth + .3f,
                ShapeKind.Cliff => p => true,
                _ => p => true,
            };
            var creatures = Spread(candidates.Where(home).ToList(), 2, rng);
            if (t.Shape.Kind == ShapeKind.Maze) creatures = new List<Vector2> { new Vector2(8, 8) };
            // In the forest they wait at the heart's tree, in the blind alcove, or where the lanes meet.
            if (t.Forest != null) creatures = new List<Vector2> { t.Forest.Heart || t.Forest.Deadfall ? t.Forest.GuardSpot : t.Forest.Alcove ?? t.Forest.Hub };
            if (t.Scene != null)
            {
                var own = t.Scene.Creatures.Where(t.InPart).Select(t.Local).ToList();
                if (own.Count > 0 || t.Shape.Kind != ShapeKind.Landmark) creatures = own;
            }
            view.creatureSockets = creatures.Select((p, i) => Socket(group, "Creature " + i, t, p, null)).ToArray();
        }

        static Transform Socket(Transform parent, string name, TileTerrain t, Vector2 p, float? height)
        {
            var s = new GameObject(name).transform;
            s.SetParent(parent, false);
            s.localPosition = new Vector3(p.x - 8, (height ?? GroundY(t, p)) + .15f, p.y - 8);
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
            // Tree crowns are solid as drawn, but nobody walks over them: no navigation up there (where a crown hangs
            // low, the ground under it is still too low to stand on).
            for (int i = 0; i < sources.Count; i++)
                if (sources[i].component is Collider c && c.name.EndsWith("_Crown"))
                {
                    var source = sources[i];
                    source.area = 1;
                    sources[i] = source;
                }
            // Water is not walkable for creatures: one box per run of deep cells along each row, so dry ground inside
            // a river's bend stays walkable.
            if (t.Wet)
            {
                int n = Mathf.RoundToInt(TileTerrain.Size / Step);
                for (int j = 0; j < n; j++)
                {
                    int run = -1;
                    for (int i = 0; i <= n; i++)
                    {
                        bool wet = i < n && t.IsWater(new Vector2((i + .5f) * Step, (j + .5f) * Step));
                        if (wet && run < 0) run = i;
                        if (wet || run < 0) continue;
                        float x0 = run * Step, x1 = i * Step, z = (j + .5f) * Step;
                        sources.Add(new NavMeshBuildSource
                        {
                            shape = NavMeshBuildSourceShape.ModifierBox,
                            // Up to just over the water: decks over it stay walkable.
                            transform = Matrix4x4.Translate(new Vector3((x0 + x1) / 2 - 8, -1.75f, z - 8)),
                            size = new Vector3(x1 - x0 + .1f, 3.6f, Step + .1f),
                            area = 1,
                        });
                        run = -1;
                    }
                }
            }
            var settings = NavMesh.GetSettingsByID(0);
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data) data.name = name + " navmesh";
            return data;
        }

        // ---------- content ----------

        /// <summary>The chime a lookout plays when it shows the far landmarks (made once).</summary>
        static SfxCue LookoutCue()
        {
            string path = $"{CueFolder}/world_lookout.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
            if (cue) return cue;
            Directory.CreateDirectory(CueFolder);
            cue = ScriptableObject.CreateInstance<SfxCue>();
            cue.clips = new[] { AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Plugins/Retro Arsenal/Sound/Misc/retro_bling.wav") };
            cue.volume = .55f;
            cue.pitch = new Vector2(1, 1);
            cue.spatialBlend = 0;
            cue.cooldown = 1;
            AssetDatabase.CreateAsset(cue, path);
            return cue;
        }

        /// <summary>Asset ids of the common and the special find prefab (was Assets/Game/Prefabs/Trophy …).</summary>
        const string CommonFind = "e63587611541a43df91765f109770ab8", SpecialFind = "3ce5eb435103343309e6c134c49adce5";

        /// <summary>A prefab by file name: in Assets/Game/Prefabs, or wherever else it was moved.</summary>
        static GameObject Prefab(string file)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Prefabs/{file}.prefab");
            if (go) return go;
            foreach (var guid in AssetDatabase.FindAssets($"\"{file}\" t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == file) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        /// <summary>The content asset: loot by id and every creature the themes name, by id ("bee-swarm" → Bee swarm.prefab).</summary>
        static void EnsureContent()
        {
            var content = AssetDatabase.LoadAssetAtPath<WorldContent>(ContentPath);
            bool created = !content;
            if (created) content = ScriptableObject.CreateInstance<WorldContent>();
            // Loot by asset id: the find prefabs keep it wherever they are moved or renamed.
            WorldLoot Loot(string guid) => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid))?.GetComponent<WorldLoot>();
            WorldCreature[] Creatures(params string[] files) => files.Select(f => Prefab(f)?.GetComponent<WorldCreature>()).Where(c => c).ToArray();
            content.loot.Clear();
            content.loot.Add(new WorldContent.LootEntry { id = "find", prefab = Loot(CommonFind) });
            content.loot.Add(new WorldContent.LootEntry { id = "find-hard", prefab = Loot(SpecialFind) });
            content.loot.Add(new WorldContent.LootEntry { id = "crystal", prefab = Loot(SpecialFind) });
            if (content.loot.Any(e => !e.prefab)) Debug.LogWarning("[World] a find prefab is missing: " + string.Join(", ", content.loot.Where(e => !e.prefab).Select(e => e.id)));
            const string sparkle = "Assets/Plugins/Retro Arsenal/Prefabs/Environment/Sparkle/Mini/";
            content.hardGlow = AssetDatabase.LoadAssetAtPath<GameObject>(sparkle + "SparkleMiniYellowLoop.prefab");
            content.crystalGlow = AssetDatabase.LoadAssetAtPath<GameObject>(sparkle + "SparkleMiniPurpleLoop.prefab");
            content.lookoutCue = LookoutCue();
            content.roadMaterial = GroundMat("OA_Ground_Path");
            content.creatures.Clear();
            foreach (var id in CreatureThemes.AllCreatures())
            {
                string file = char.ToUpperInvariant(id[0]) + id.Substring(1).Replace('-', ' ');
                var prefabs = Creatures(file);
                if (prefabs.Length == 0) Debug.LogWarning("[World] no creature prefab " + file);
                content.creatures.Add(new WorldContent.CreatureEntry { role = id, prefabs = prefabs });
            }
            if (created) AssetDatabase.CreateAsset(content, ContentPath);
            else EditorUtility.SetDirty(content);
        }
    }

}
