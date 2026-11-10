using System.Collections.Generic;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// The endless world of the outing (Kayospace «Открытый мир v2»): a new <see cref="WorldMap"/> each outing, chunk
    /// prefabs loaded around the player and unloaded behind, each with its own baked navigation joined to its
    /// neighbours by links. What happened in a chunk is remembered when it unloads (loot taken, creatures hurt or
    /// dead, ice and burnt thickets, moved props, things lying around) and restored when it loads again.
    /// </summary>
    public sealed class WorldStreamer : MonoBehaviour, IOutingWorld
    {
        [Header("Content")]
        public ChunkCatalog catalog;
        public WorldContent content;
        [Tooltip("Loaded chunks live here.")]
        public Transform root;
        [Tooltip("Whose position decides what is loaded (the player).")]
        public Transform player;

        [Header("Streaming")]
        [Min(16)] public float loadRadius = 72;
        [Min(16)] public float unloadRadius = 104;
        [Min(1)] public int loadsPerFrame = 2;
        [Tooltip("0: a new random world every outing. Any other value: always this world (testing).")]
        public int fixedSeed;

        public WorldMap Map { get; private set; }
        public int Seed { get; private set; }
        public bool Active { get; private set; }
        public int LoadedCount => loaded.Count;
        public bool IsLoaded(ChunkCoord c) => loaded.ContainsKey(c);
        /// <summary>The expedition of the current outing (null between outings).</summary>
        public ExpeditionWorld World => Active ? world : null;
        /// <summary>Changes whenever a chunk loads or unloads.</summary>
        public int Version { get; private set; }

        sealed class Loaded
        {
            public ChunkPlan plan;
            public ChunkView view;
            public NavMeshDataInstance nav;
            public readonly List<Mesh> meshes = new List<Mesh>();
            public readonly List<(int index, WorldLoot loot)> loot = new List<(int, WorldLoot)>();
            public readonly List<(int index, WorldCreature creature)> creatures = new List<(int, WorldCreature)>();
        }

        sealed class Memory
        {
            public readonly HashSet<int> takenLoot = new HashSet<int>();
            public readonly Dictionary<int, (bool alive, int health, Vector3 position)> creatures = new Dictionary<int, (bool, int, Vector3)>();
            public readonly HashSet<int> resolved = new HashSet<int>();
            public readonly Dictionary<int, (Vector3 position, Quaternion rotation)> props = new Dictionary<int, (Vector3, Quaternion)>();
            public readonly List<GameObject> loose = new List<GameObject>();
        }

        const int PocketIndex = 1000;

        readonly Dictionary<ChunkCoord, Loaded> loaded = new Dictionary<ChunkCoord, Loaded>();
        readonly Dictionary<ChunkCoord, Memory> memory = new Dictionary<ChunkCoord, Memory>();
        readonly Dictionary<(ChunkCoord, Side), List<NavMeshLinkInstance>> links = new Dictionary<(ChunkCoord, Side), List<NavMeshLinkInstance>>();
        readonly List<ChunkCoord> wanted = new List<ChunkCoord>();
        readonly List<ChunkCoord> leaving = new List<ChunkCoord>();
        ExpeditionWorld world;

        float Size => Map.Rules.ChunkSize;
        /// <summary>The world is laid out around the chunk root (rotation is ignored).</summary>
        Vector3 Origin => root ? root.position : transform.position;

        // ---------- IOutingWorld ----------

        public Pose BeginOuting(ExpeditionWorld expedition)
        {
            EndOuting();
            world = expedition;
            Seed = fixedSeed != 0 ? fixedSeed : new System.Random().Next(1, int.MaxValue);
            Map = new WorldMap(Seed);
            Active = true;
            world.finds.Clear();
            world.obstacles.Clear();
            world.creatures.Clear();
            world.props.Clear();
            // Everything around the start loads behind the departure fade.
            Stream(Origin, int.MaxValue);
            var start = loaded.TryGetValue(new ChunkCoord(0, 0), out var s) && s.view.departurePose ? s.view.departurePose : null;
            return start ? new Pose(start.position, start.rotation) : new Pose(Origin + Vector3.up * 2, Quaternion.identity);
        }

        public void EndOuting()
        {
            if (!Active && loaded.Count == 0) return;
            foreach (var coord in new List<ChunkCoord>(loaded.Keys)) Unload(coord, remember: false);
            foreach (var m in memory.Values)
                foreach (var go in m.loose)
                    if (go) Destroy(go);
            memory.Clear();
            links.Clear();
            Active = false;
            Map = null;
            if (world)
            {
                world.finds.Clear();
                world.obstacles.Clear();
                world.creatures.Clear();
                world.props.Clear();
            }
        }

        void OnDisable() { if (Active) EndOuting(); }

        void Update()
        {
            if (!Active || !player) return;
            Stream(player.position, loadsPerFrame);
        }

        // ---------- streaming ----------

        public ChunkCoord CoordAt(Vector3 position) =>
            new ChunkCoord(Mathf.RoundToInt((position.x - Origin.x) / Size), Mathf.RoundToInt((position.z - Origin.z) / Size));

        void Stream(Vector3 centre, int budget)
        {
            var here = CoordAt(centre);
            int reach = Mathf.CeilToInt(loadRadius / Size) + 1;
            wanted.Clear();
            for (int dx = -reach; dx <= reach; dx++)
                for (int dy = -reach; dy <= reach; dy++)
                {
                    var c = new ChunkCoord(here.X + dx, here.Y + dy);
                    if (loaded.ContainsKey(c) || Distance(c, centre) > loadRadius) continue;
                    wanted.Add(c);
                }
            wanted.Sort((a, b) => Distance(a, centre).CompareTo(Distance(b, centre)));
            for (int i = 0; i < wanted.Count && i < budget; i++) Load(wanted[i]);

            leaving.Clear();
            foreach (var c in loaded.Keys) if (Distance(c, centre) > unloadRadius) leaving.Add(c);
            foreach (var c in leaving) Unload(c, remember: true);
        }

        float Distance(ChunkCoord c, Vector3 p)
        {
            float dx = c.X * Size + Origin.x - p.x, dz = c.Y * Size + Origin.z - p.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        uint Pick(ChunkCoord c, int salt) => Hash.Of(Seed, 900 + salt, c.X, c.Y);

        void Load(ChunkCoord coord)
        {
            var plan = Map.Chunk(coord);
            var prefab = catalog ? catalog.Find(plan.Shape.Id, plan.Variant.Id) : null;
            if (!prefab)
            {
                Debug.LogWarning($"[World] no prefab for {plan.Shape.Id}/{plan.Variant.Id}");
                prefab = catalog ? catalog.Find("plain", "grove") : null;
                if (!prefab) return;
            }
            var position = Origin + new Vector3(coord.X * Size, plan.BaseLevel * Map.Rules.PlateauHeight, coord.Y * Size);
            var rotation = Quaternion.Euler(0, 90 * plan.Rotation, 0);
            var view = Instantiate(prefab, position, rotation, root);
            view.name = $"{plan.Shape.Id}/{plan.Variant.Id} {coord}";
            var entry = new Loaded { plan = plan, view = view };
            loaded[coord] = entry;
            Version++;
            memory.TryGetValue(coord, out var mem);

            foreach (var filter in view.worldUv) if (filter) entry.meshes.Add(WorldSpaceUv(filter));
            if (view.navMesh) entry.nav = NavMesh.AddNavMeshData(view.navMesh, position, rotation);

            for (int i = 0; i < view.obstacles.Length; i++)
            {
                var o = view.obstacles[i];
                if (!o) continue;
                o.world = world;
                if (mem != null && mem.resolved.Contains(i)) o.ResolveAtOnce(); else o.ResetForOuting();
                world.obstacles.Add(o);
            }
            for (int i = 0; i < view.props.Length; i++)
            {
                var p = view.props[i];
                if (!p) continue;
                if (mem != null && mem.props.TryGetValue(i, out var pose)) { p.body.position = pose.position; p.body.rotation = pose.rotation; p.transform.SetPositionAndRotation(pose.position, pose.rotation); }
                world.props.Add(p);
            }

            SpawnLoot(entry, mem);
            SpawnCreatures(entry, mem);
            if (mem != null)
            {
                foreach (var go in mem.loose) if (go) go.SetActive(true);
                mem.loose.Clear();
            }
            foreach (var side in Sides.All)
            {
                var other = coord.Step(side);
                if (loaded.ContainsKey(other)) Join(coord, side);
            }
        }

        void SpawnLoot(Loaded entry, Memory mem)
        {
            var plan = entry.plan;
            var sockets = entry.view.findSockets;
            if (sockets.Length > 0)
                for (int i = 0; i < plan.Loot.Count; i++)
                {
                    if (mem != null && mem.takenLoot.Contains(i)) continue;
                    var socket = sockets[(Pick(plan.Coord, i) + (uint)i) % (uint)sockets.Length];
                    var loot = PlaceLoot(plan.Loot[i], socket);
                    if (loot) entry.loot.Add((i, loot));
                }
            if (plan.PocketReward != null && entry.view.pocketSocket && (mem == null || !mem.takenLoot.Contains(PocketIndex)))
            {
                var loot = PlaceLoot(plan.PocketReward, entry.view.pocketSocket);
                if (loot) entry.loot.Add((PocketIndex, loot));
            }
        }

        WorldLoot PlaceLoot(string id, Transform socket)
        {
            var prefab = content ? content.Loot(id) : null;
            if (!prefab) return null;
            var loot = Instantiate(prefab, socket.position, socket.rotation, socket);
            loot.world = world;
            loot.guaranteed = true;
            loot.spawnPoints = null;
            world.finds.Add(loot);
            return loot;
        }

        void SpawnCreatures(Loaded entry, Memory mem)
        {
            var plan = entry.plan;
            var sockets = entry.view.creatureSockets;
            if (plan.Creature == null || sockets.Length == 0 || !content) return;
            for (int k = 0; k < plan.CreatureCount; k++)
            {
                bool remembered = false;
                (bool alive, int health, Vector3 position) state = default;
                if (mem != null && mem.creatures.TryGetValue(k, out state))
                {
                    remembered = true;
                    if (!state.alive) continue;
                }
                var prefab = content.Creature(plan.Creature, Pick(plan.Coord, 50 + k));
                if (!prefab) continue;
                var socket = sockets[(Pick(plan.Coord, 60) + (uint)k) % (uint)sockets.Length];
                var creature = Instantiate(prefab, socket.position, socket.rotation, entry.view.transform);
                creature.world = world;
                creature.spawnPoints = null;
                if (remembered) creature.Resume(state.health, state.position);
                else creature.ResetForOuting(null);
                entry.creatures.Add((k, creature));
                world.creatures.Add(creature);
            }
        }

        void Unload(ChunkCoord coord, bool remember)
        {
            if (!loaded.TryGetValue(coord, out var entry)) return;
            loaded.Remove(coord);
            Version++;
            if (remember && world)
            {
                if (!memory.TryGetValue(coord, out var mem)) memory[coord] = mem = new Memory();
                foreach (var (index, loot) in entry.loot) if (!loot || loot.Taken) mem.takenLoot.Add(index);
                foreach (var (index, creature) in entry.creatures)
                    mem.creatures[index] = creature && creature.gameObject.activeSelf && creature.Alive
                        ? (true, creature.Health, creature.transform.position)
                        : (false, 0, Vector3.zero);
                for (int i = 0; i < entry.view.obstacles.Length; i++)
                    if (entry.view.obstacles[i] && entry.view.obstacles[i].Resolved) mem.resolved.Add(i);
                for (int i = 0; i < entry.view.props.Length; i++)
                    if (entry.view.props[i]) mem.props[i] = (entry.view.props[i].transform.position, entry.view.props[i].transform.rotation);
                HideLoose(coord, mem);
            }
            if (world)
            {
                foreach (var (_, loot) in entry.loot) world.finds.Remove(loot);
                foreach (var (_, creature) in entry.creatures) world.creatures.Remove(creature);
                foreach (var o in entry.view.obstacles) world.obstacles.Remove(o);
                foreach (var p in entry.view.props) world.props.Remove(p);
            }
            foreach (var side in Sides.All) Unjoin(coord, side);
            entry.nav.Remove();
            foreach (var mesh in entry.meshes) Destroy(mesh);
            Destroy(entry.view.gameObject);
        }

        /// <summary>Trophies and other loose things lying in an unloading chunk wait, hidden, for it to come back.</summary>
        void HideLoose(ChunkCoord coord, Memory mem)
        {
            if (!world.spawnedObjects) return;
            float half = Size / 2;
            foreach (Transform child in world.spawnedObjects)
            {
                if (!child.gameObject.activeSelf || child.GetComponent<VfxBurst>()) continue;
                var p = child.position;
                if (Mathf.Abs(p.x - Origin.x - coord.X * Size) > half || Mathf.Abs(p.z - Origin.z - coord.Y * Size) > half) continue;
                child.gameObject.SetActive(false);
                mem.loose.Add(child.gameObject);
            }
        }

        // ---------- navigation between chunks ----------

        void Join(ChunkCoord coord, Side side)
        {
            // One owner per edge: the chunk to the south or west.
            if (side == Side.South || side == Side.West) { Join(coord.Step(side), side.Opposite()); return; }
            var key = (coord, side);
            if (links.ContainsKey(key)) return;
            var a = loaded[coord].plan;
            var b = loaded[coord.Step(side)].plan;
            var list = new List<NavMeshLinkInstance>();
            Vector3 normal = side == Side.North ? Vector3.forward : Vector3.right;
            for (int segment = 0; segment < 3; segment++)
            {
                int ra = a.Port(side, segment), rb = b.Port(side.Opposite(), 2 - segment);
                if (ra < 0 || rb < 0) continue;
                if (a.Gate(side, segment) != Need.Walk || b.Gate(side.Opposite(), 2 - segment) != Need.Walk) continue;
                int level = a.AbsoluteLevel(ra);
                if (level != b.AbsoluteLevel(rb)) continue;
                var mid = SegmentMiddle(a.Coord, side, segment) + Vector3.up * Origin.y;
                var data = new NavMeshLinkData
                {
                    startPosition = mid - normal * .9f + Vector3.up * LinkHeight(loaded[coord], side, segment, level),
                    endPosition = mid + normal * .9f + Vector3.up * LinkHeight(loaded[coord.Step(side)], side.Opposite(), 2 - segment, level),
                    width = Size / 3 * .8f,
                    bidirectional = true,
                    costModifier = -1,
                    area = 0,
                    agentTypeID = 0,
                };
                list.Add(NavMesh.AddLink(data));
            }
            links[key] = list;
        }

        void Unjoin(ChunkCoord coord, Side side)
        {
            var key = side == Side.South || side == Side.West ? (coord.Step(side), side.Opposite()) : (coord, side);
            if (!links.TryGetValue(key, out var list)) return;
            foreach (var link in list) NavMesh.RemoveLink(link);
            links.Remove(key);
        }

        float Height(int level) => level >= 0 ? level * Map.Rules.PlateauHeight : -Map.Rules.CanyonDepth;

        /// <summary>
        /// Where a link lands on one side of an edge: the region's level, except on a landmark part, whose ground
        /// meets its neighbours wherever its hill or crater has it (the heights its prefab measured).
        /// </summary>
        float LinkHeight(Loaded chunk, Side side, int segment, int level)
        {
            var plan = chunk.plan;
            var heights = chunk.view ? chunk.view.edgeHeights : null;
            if (plan.Shape.Kind != ShapeKind.Landmark || heights == null || heights.Length != 12) return Height(level);
            int canonical = ((int)side - plan.Rotation) & 3;
            return plan.BaseLevel * Map.Rules.PlateauHeight + heights[canonical * 3 + segment];
        }

        /// <summary>World position (height 0) of the middle of an edge segment; segments run clockwise.</summary>
        Vector3 SegmentMiddle(ChunkCoord c, Side side, int segment)
        {
            float h = Size / 2, x = c.X * Size + Origin.x, z = c.Y * Size + Origin.z;
            Vector3 from, to;
            switch (side)
            {
                case Side.North: from = new Vector3(x - h, 0, z + h); to = new Vector3(x + h, 0, z + h); break;
                case Side.East: from = new Vector3(x + h, 0, z + h); to = new Vector3(x + h, 0, z - h); break;
                case Side.South: from = new Vector3(x + h, 0, z - h); to = new Vector3(x - h, 0, z - h); break;
                default: from = new Vector3(x - h, 0, z - h); to = new Vector3(x - h, 0, z + h); break;
            }
            return Vector3.Lerp(from, to, (segment + .5f) / 3f);
        }

        // ---------- ground textures ----------

        /// <summary>
        /// Gives a placed ground mesh world-space UVs (4 m texture tiles), projected per triangle on its dominant axis,
        /// so ground textures run on across chunks whatever their rotation. Returns the copy to destroy on unload.
        /// </summary>
        static Mesh WorldSpaceUv(MeshFilter filter)
        {
            var source = filter.sharedMesh;
            if (!source) return null;
            var mesh = Instantiate(source);
            var vertices = mesh.vertices;
            var uv = new Vector2[vertices.Length];
            var toWorld = filter.transform.localToWorldMatrix;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tris = mesh.GetTriangles(sub);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = toWorld.MultiplyPoint3x4(vertices[tris[t]]), b = toWorld.MultiplyPoint3x4(vertices[tris[t + 1]]), c = toWorld.MultiplyPoint3x4(vertices[tris[t + 2]]);
                    var n = Vector3.Cross(b - a, c - a);
                    n = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                    for (int k = 0; k < 3; k++)
                    {
                        var p = toWorld.MultiplyPoint3x4(vertices[tris[t + k]]);
                        uv[tris[t + k]] = (n.y >= n.x && n.y >= n.z ? new Vector2(p.x, p.z) : n.x >= n.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y)) / 4f;
                    }
                }
            }
            mesh.uv = uv;
            filter.sharedMesh = mesh;
            return mesh;
        }
    }
}
