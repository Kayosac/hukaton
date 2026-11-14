using System.Collections.Generic;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using Hakaton.Expeditions.Events;
using Hakaton.Feel;
using Hakaton.OldApiary;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// The endless world of the outing (Kayospace «Открытый мир v2»): a new <see cref="WorldMap"/> each outing, chunk
    /// prefabs loaded around the player and unloaded behind, each with its own baked navigation joined to its
    /// neighbours by links. What happened in a chunk is remembered when it unloads (loot taken, creatures hurt or
    /// dead, ice and burnt thickets, moved props, burnt grass and wood, trees that came alive, things lying around) and
    /// restored when it loads again. Chunks are as alive as the Old Apiary: what burns there burns here (through
    /// <see cref="fire"/>), its trees can be awakened, its loose props pushed about.
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
        [Tooltip("Runs fire across the loaded chunks' burnables, as the Old Apiary's own field does there.")]
        public BurnableField fire;

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
        /// <summary>Treasure chests in the loaded chunks (tests, debug).</summary>
        public int ChestCount { get { int n = 0; foreach (var e in loaded.Values) if (e.chest) n++; return n; } }
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
            public readonly List<(int index, TarPuddleZone tar)> tar = new List<(int, TarPuddleZone)>();
            public TreasureChest chest;
            public TreasureChest sunken;
            public BeastBrawl brawl;
            public Oddity oddity;
        }

        sealed class Memory
        {
            public readonly HashSet<int> takenLoot = new HashSet<int>();
            public readonly Dictionary<int, (bool alive, int health, Vector3 position)> creatures = new Dictionary<int, (bool, int, Vector3)>();
            public readonly HashSet<int> resolved = new HashSet<int>();
            public readonly Dictionary<int, (Vector3 position, Quaternion rotation)> props = new Dictionary<int, (Vector3, Quaternion)>();
            public readonly HashSet<int> burnt = new HashSet<int>();
            public readonly HashSet<int> awakened = new HashSet<int>();
            /// <summary>Natural tar pools burnt out or washed away: they do not come back.</summary>
            public readonly HashSet<int> tarGone = new HashSet<int>();
            public readonly List<GameObject> loose = new List<GameObject>();
            public bool chestOpened;
            public bool sunkenOpened;
            /// <summary>A mimic as it was left: shut, on guard where it last hid, killed or opened.</summary>
            public ChestMimic.Snapshot? mimic;
            public BeastBrawl.Save brawl;
            public Oddity.State? oddity;
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
            roadLines.Clear();
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
            // WebGL runs on one thread: one chunk a frame keeps the walk smooth (a chunk costs about 1–3 ms in the
            // editor, several times that in a browser).
            Stream(player.position, Application.platform == RuntimePlatform.WebGLPlayer ? 1 : loadsPerFrame);
            Ambience();
            LookOut();
        }

        // ---------- roads ----------

        [Tooltip("Width of the worn roads between landmarks, m.")]
        [Min(.5f)] public float roadWidth = 1.7f;
        public int RoadsPainted { get; private set; }

        /// <summary>
        /// The roads through a chunk, laid on its ground as it stands: a strip of path ground following a smooth line
        /// from edge middle to edge middle (where chunks join) through a wandering point inside. Where something stands
        /// on the road, the ground is steep or under water, the strip breaks. Landmarks keep their own trails.
        /// </summary>
        void PaintRoads(Loaded entry, ChunkCoord coord)
        {
            if (!content || !content.roadMaterial || entry.plan.Shape.Kind == ShapeKind.Landmark) return;
            var roads = Map.RoadsThrough(coord);
            if (roads.Count == 0) return;
            var ground = entry.view.worldUv.Length > 0 && entry.view.worldUv[0] ? entry.view.worldUv[0].GetComponent<MeshCollider>() : null;
            if (!ground) return;
            Physics.SyncTransforms();
            float baseY = entry.view.transform.position.y;
            foreach (var road in roads)
            {
                int k = road.Cells.IndexOf(coord);
                var mesh = RoadStrip(road, k, ground, baseY);
                if (!mesh) continue;
                var go = new GameObject("Road");
                go.transform.SetParent(entry.view.transform, false);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = content.roadMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                entry.meshes.Add(mesh);
                RoadsPainted++;
            }
        }

        sealed class RoadLine
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            /// <summary>Per cell of the road: the control points from where it comes in to where it goes out.</summary>
            public int[] In, Out;
        }

        readonly Dictionary<int, RoadLine> roadLines = new Dictionary<int, RoadLine>();

        /// <summary>
        /// Control points of a road in world XZ: the edge middles where it passes from chunk to chunk and, inside each
        /// chunk, a wandering point — the middle at its ends and over river crossings, the two ends of a ramp.
        /// </summary>
        RoadLine Line(Road road)
        {
            if (roadLines.TryGetValue(road.Id, out var line)) return line;
            line = new RoadLine { In = new int[road.Cells.Count], Out = new int[road.Cells.Count] };
            var o = new Vector2(Origin.x, Origin.z);
            int last = road.Cells.Count - 1;
            for (int k = 0; k <= last; k++)
            {
                var c = road.Cells[k];
                var centre = o + new Vector2(c.X, c.Y) * Size;
                if (k > 0)
                {
                    var p = road.Cells[k - 1];
                    line.Points.Add(o + new Vector2(p.X + c.X, p.Y + c.Y) * Size / 2);
                    line.Out[k - 1] = line.In[k] = line.Points.Count - 1;
                }
                else line.In[k] = 0;
                var plan = Map.Chunk(c);
                var prefab = catalog ? catalog.Find(plan.Shape.Id, plan.Variant.Id, ChunkCatalog.Land(plan.Biome, plan.Zone)) : null;
                if (k == 0 || k == last || plan.Shape.Kind == ShapeKind.River) line.Points.Add(centre);
                else if (prefab && prefab.roadGuide.Length >= 2)
                {
                    // Up the ramp: its end nearer to where the road comes in first.
                    var turn = Quaternion.Euler(0, 90 * plan.Rotation, 0);
                    var a = turn * prefab.roadGuide[0];
                    var b = turn * prefab.roadGuide[prefab.roadGuide.Length - 1];
                    Vector2 pa = centre + new Vector2(a.x, a.z), pb = centre + new Vector2(b.x, b.z);
                    var entry = line.Points[line.Points.Count - 1];
                    if (Vector2.Distance(entry, pb) < Vector2.Distance(entry, pa)) (pa, pb) = (pb, pa);
                    line.Points.Add(pa);
                    line.Points.Add(pb);
                }
                else
                {
                    float jx = (float)Hash.Unit(Seed, road.Id, c.X, c.Y) * 2 - 1, jy = (float)Hash.Unit(Seed, road.Id + 1, c.X, c.Y) * 2 - 1;
                    line.Points.Add(centre + new Vector2(jx, jy) * 2.6f);
                }
                if (k == last) line.Out[k] = line.Points.Count - 1;
            }
            roadLines[road.Id] = line;
            return line;
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return .5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
        }

        readonly List<Vector2> roadLine = new List<Vector2>();
        readonly List<Vector3> roadVertices = new List<Vector3>();
        readonly List<Vector2> roadUv = new List<Vector2>();
        readonly List<int> roadTriangles = new List<int>();

        Mesh RoadStrip(Road road, int k, MeshCollider ground, float baseY)
        {
            if (k < 0) return null;
            // The line over this chunk: from the edge middle where it comes in to the one where it goes out.
            var line = Line(road);
            var pts = line.Points;
            Vector2 At(int i) => pts[Mathf.Clamp(i, 0, pts.Count - 1)];
            roadLine.Clear();
            for (int seg = line.In[k]; seg < line.Out[k]; seg++)
            {
                Vector2 p0 = At(seg - 1), p1 = At(seg), p2 = At(seg + 1), p3 = At(seg + 2);
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / .5f));
                for (int s = roadLine.Count == 0 ? 0 : 1; s <= n; s++) roadLine.Add(CatmullRom(p0, p1, p2, p3, s / (float)n));
            }
            if (roadLine.Count < 2) return null;
            roadVertices.Clear(); roadUv.Clear(); roadTriangles.Clear();
            bool open = false;
            float waterY = baseY - .25f;
            for (int i = 0; i < roadLine.Count; i++)
            {
                var p = roadLine[i];
                var along = (roadLine[Mathf.Min(i + 1, roadLine.Count - 1)] - roadLine[Mathf.Max(i - 1, 0)]).normalized;
                var across = new Vector2(-along.y, along.x);
                float w = roadWidth * (.85f + .3f * (float)Hash.Unit(Seed, road.Id + 2, Mathf.RoundToInt(p.x * 2), Mathf.RoundToInt(p.y * 2))) / 2;
                bool ok = true;
                var row = new Vector3[3];
                for (int j = 0; j < 3 && ok; j++)
                {
                    var q = p + across * (j - 1) * w;
                    var from = new Vector3(q.x, baseY + 40, q.y);
                    if (!ground.Raycast(new Ray(from, Vector3.down), out var hit, 80) || hit.normal.y < .8f || hit.point.y < waterY) { ok = false; break; }
                    // Anything standing on the road (a rock, a fence, a bridge) breaks it.
                    if (Physics.Raycast(from, Vector3.down, out var any, 80, ~(1 << 4), QueryTriggerInteraction.Ignore) && any.collider != ground && any.point.y > hit.point.y + .1f) { ok = false; break; }
                    row[j] = hit.point + hit.normal * .045f;
                }
                if (!ok) { open = false; continue; }
                int b = roadVertices.Count;
                for (int j = 0; j < 3; j++) { roadVertices.Add(row[j]); roadUv.Add(new Vector2(row[j].x, row[j].z) / 4f); }
                if (open) roadTriangles.AddRange(new[] { b - 3, b - 2, b, b - 2, b + 1, b, b - 2, b - 1, b + 1, b - 1, b + 2, b + 1 });
                open = true;
            }
            if (roadTriangles.Count == 0) return null;
            var mesh = new Mesh { name = "Road" };
            mesh.SetVertices(roadVertices);
            mesh.SetUVs(0, roadUv);
            mesh.SetTriangles(roadTriangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- places that sound, lookouts ----------

        [Tooltip("At most this many places sound at once (each takes one of the sound player's sources).")]
        [Min(0)] public int ambientVoices = 2;
        readonly List<AmbientSpot> sounding = new List<AmbientSpot>();
        readonly List<(float distance, AmbientSpot spot)> heard = new List<(float, AmbientSpot)>();
        FarSilhouettes silhouettes;

        /// <summary>The nearest few places in earshot loop through the expedition's sound player; the rest are quiet.</summary>
        void Ambience()
        {
            var sfx = world ? world.sfx : null;
            if (!sfx) return;
            heard.Clear();
            var at = player.position;
            foreach (var entry in loaded.Values)
                foreach (var spot in entry.view.ambient)
                {
                    if (!spot || !spot.cue) continue;
                    float d = Vector3.Distance(at, spot.transform.position);
                    if (d < spot.cue.maxDistance) heard.Add((d, spot));
                }
            heard.Sort((a, b) => a.distance.CompareTo(b.distance));
            for (int i = sounding.Count - 1; i >= 0; i--)
            {
                var spot = sounding[i];
                int rank = heard.FindIndex(h => h.spot == spot);
                if (spot && rank >= 0 && rank < ambientVoices) continue;
                if (spot) sfx.StopLoop(spot);
                sounding.RemoveAt(i);
            }
            for (int i = 0; i < heard.Count && i < ambientVoices; i++)
            {
                var spot = heard[i].spot;
                // Fades in over the last few metres of its reach instead of starting at full volume.
                float edge = Mathf.Clamp01((spot.cue.maxDistance - heard[i].distance) / 4);
                sfx.Loop(spot, spot.cue, spot.transform.position, spot.volume * edge);
                if (!sounding.Contains(spot)) sounding.Add(spot);
            }
        }

        void Quiet(ChunkView view)
        {
            var sfx = world ? world.sfx : null;
            foreach (var spot in view.ambient)
            {
                if (!spot) continue;
                if (sfx) sfx.StopLoop(spot);
                sounding.Remove(spot);
            }
        }

        /// <summary>Standing by a lookout shows the far landmarks for a while (and chimes once when it starts).</summary>
        void LookOut()
        {
            if (!silhouettes) silhouettes = GetComponent<FarSilhouettes>();
            if (!silhouettes) return;
            var at = player.position;
            foreach (var entry in loaded.Values)
                foreach (var lookout in entry.view.lookouts)
                {
                    if (!lookout) continue;
                    var d = at - lookout.transform.position;
                    if (Mathf.Abs(d.y) > 3) continue;
                    d.y = 0;
                    if (d.sqrMagnitude > lookout.reach * lookout.reach) continue;
                    bool fresh = !silhouettes.Revealed;
                    silhouettes.Reveal(lookout.revealRadius, lookout.seconds);
                    if (fresh && world)
                    {
                        if (world.sfx && content && content.lookoutCue) world.sfx.Play(content.lookoutCue);
                        world.Announce("С высоты видны дальние ориентиры", 3);
                    }
                    return;
                }
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
            for (int i = 0; i < wanted.Count && i < budget; i++)
            {
                clock.Restart();
                Load(wanted[i]);
                Note(ref LoadTimes, (float)clock.Elapsed.TotalMilliseconds);
            }

            leaving.Clear();
            foreach (var c in loaded.Keys) if (Distance(c, centre) > unloadRadius) leaving.Add(c);
            foreach (var c in leaving)
            {
                clock.Restart();
                Unload(c, remember: true);
                Note(ref UnloadTimes, (float)clock.Elapsed.TotalMilliseconds);
            }
        }

        /// <summary>Running cost of streaming, for measuring (count, total and worst in ms).</summary>
        public struct Timing
        {
            public int Count;
            public float Total, Worst;
            public float Average => Count > 0 ? Total / Count : 0;
            public override string ToString() => $"{Count} × avg {Average:0.0} ms, worst {Worst:0.0} ms";
        }

        public Timing LoadTimes, UnloadTimes;
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

        static void Note(ref Timing timing, float ms)
        {
            timing.Count++;
            timing.Total += ms;
            timing.Worst = Mathf.Max(timing.Worst, ms);
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
            var prefab = catalog ? catalog.Find(plan.Shape.Id, plan.Variant.Id, ChunkCatalog.Land(plan.Biome, plan.Zone)) : null;
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
                if (fire) fire.JoinThorns(o);
            }
            for (int i = 0; i < view.props.Length; i++)
            {
                var p = view.props[i];
                if (!p) continue;
                // A prop that fell out of the world comes back where it was made.
                if (mem != null && mem.props.TryGetValue(i, out var pose) && pose.position.y > position.y - 20)
                {
                    p.body.position = pose.position;
                    p.body.rotation = pose.rotation;
                    p.transform.SetPositionAndRotation(pose.position, pose.rotation);
                }
                world.props.Add(p);
            }
            for (int i = 0; i < view.trees.Length; i++)
                if (view.trees[i] && mem != null && mem.awakened.Contains(i))
                {
                    MergedLooks.Unmerge(view.trees[i].gameObject);
                    view.trees[i].gameObject.SetActive(false);
                }
            if (fire)
                for (int i = 0; i < view.burnables.Length; i++)
                {
                    var b = view.burnables[i];
                    if (!b) continue;
                    fire.Join(b);
                    if (mem != null && mem.burnt.Contains(i)) fire.Burnt(b);
                }

            SwampPieces(entry, mem);
            SpawnLoot(entry, mem);
            SpawnChest(entry, mem);
            SpawnSunken(entry, mem);
            SpawnWisp(entry);
            SpawnEvent(entry, mem);
            SpawnCreatures(entry, mem);
            PaintRoads(entry, coord);
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

        /// <summary>
        /// The rotten swamp's live pieces: gas vents join the outing's card zones; natural tar pools are laid as tar
        /// puddles (the «Лужа смолы» rules), except those that burnt out or were washed away before.
        /// </summary>
        void SwampPieces(Loaded entry, Memory mem)
        {
            var view = entry.view;
            foreach (var vent in view.gasVents) if (vent) vent.OnSpawned(world);
            if (!content || !content.tarPuddle) return;
            for (int i = 0; i < view.tarSpots.Length; i++)
            {
                var spot = view.tarSpots[i];
                if (!spot || (mem != null && mem.tarGone.Contains(i))) continue;
                var tar = world.SpawnForOuting(content.tarPuddle, spot.transform.position, spot.transform.rotation, view.transform);
                if (!tar) continue;
                tar.Setup(spot.radius, content.tarSlow, content.tarBurnSeconds, content.tarBurnDamage);
                entry.tar.Add((i, tar));
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
                    // A valuable reward goes to the place the scene made for it (its first find spot); ordinary finds anywhere.
                    bool valuable = plan.Loot[i] != "find";
                    var socket = valuable ? sockets[0] : sockets[(Pick(plan.Coord, i) + (uint)i) % (uint)sockets.Length];
                    var loot = PlaceLoot(plan.Loot[i], socket, Pick(plan.Coord, 60 + i));
                    if (loot) entry.loot.Add((i, loot));
                }
            if (plan.PocketReward != null && entry.view.pocketSocket && (mem == null || !mem.takenLoot.Contains(PocketIndex)))
            {
                var loot = PlaceLoot(plan.PocketReward, entry.view.pocketSocket, Pick(plan.Coord, 59));
                if (loot) entry.loot.Add((PocketIndex, loot));
            }
        }

        readonly List<int> freeSockets = new List<int>();
        readonly RaycastHit[] chestHits = new RaycastHit[8];

        /// <summary>
        /// A treasure chest, maybe: the ring's table rolls whether the chunk has one and which (richer further out),
        /// from the seed and the chunk only. It stands on a find spot no find of this chunk uses; the start has none.
        /// An opened chest stays opened and empty when its chunk comes back.
        /// </summary>
        /// <summary>
        /// A chest on the bed of the chunk's deepest water (some chunks with deep water): the ring's richest chest,
        /// paying <see cref="WorldContent.sunkenRiches"/> times over; reached with «Притяжение» (<see cref="SunkenChest"/>).
        /// </summary>
        void SpawnSunken(Loaded entry, Memory mem)
        {
            var plan = entry.plan;
            var c = plan.Coord;
            if (!content || !entry.view.hasSunkenSpot || WorldRules.IsStartArea(c)) return;
            if (Hash.Unit(Seed, Salt.ChestPresence, c.X, c.Y, 7) >= content.sunkenChance) return;
            var table = content.ChestTable(plan.Ring);
            if (!table) return;
            TreasureChest prefab = null;
            foreach (var e in table.entries)
                if (e.prefab && e.prefab.tier && !e.prefab.GetComponent<ChestMimic>() && (!prefab || e.prefab.tier.rank > prefab.tier.rank)) prefab = e.prefab;
            if (!prefab) return;
            var at = entry.view.transform.TransformPoint(entry.view.sunkenSpot);
            float yaw = (float)Hash.Unit(Seed, Salt.ChestSpot, c.X, c.Y, 7) * 360;
            var chest = Instantiate(prefab, at, Quaternion.Euler(0, yaw, 0), entry.view.transform);
            chest.name = prefab.name + " (sunken)";
            chest.world = world;
            if (mem != null && mem.sunkenOpened) chest.SetEmpty();
            else chest.ResetForOuting();
            chest.CoinMultiplier = content.sunkenRiches;
            var body = chest.GetComponent<Rigidbody>();
            if (!body) body = chest.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.mass = 2;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var sunken = chest.gameObject.AddComponent<SunkenChest>();
            sunken.level = entry.view.transform.position.y - .3f;
            sunken.player = player;
            if (content.sunkenBubbles && !chest.Opened)
            {
                sunken.bubbles = Instantiate(content.sunkenBubbles, at + Vector3.up * .4f, Quaternion.identity, chest.transform);
                sunken.bubbles.name = "Bubbles";
            }
            entry.sunken = chest;
        }

        /// <summary>
        /// A will-o'-the-wisp over some swamp chunks: it leads to the chunk's treasure (a find, its chest, its sunken
        /// chest) or into one of its bog windows, by the seed.
        /// </summary>
        void SpawnWisp(Loaded entry)
        {
            var plan = entry.plan;
            var c = plan.Coord;
            if (!content || !content.wisp || plan.Biome != Biome.Swamp || plan.Shape.Kind == ShapeKind.Cliff) return;
            if (Hash.Unit(Seed, Salt.Event + 9, c.X, c.Y) >= .3) return;
            Vector3? treasure = null, bog = null;
            if (entry.sunken && !entry.sunken.Opened) treasure = entry.sunken.transform.position;
            else if (entry.chest && !entry.chest.Opened) treasure = entry.chest.transform.position;
            else foreach (var (_, find) in entry.loot) if (find && find.Available) { treasure = find.transform.position; break; }
            foreach (var o in entry.view.obstacles)
                if (o && o.GetComponent<BogMire>()) { bog = o.HasSurface ? o.SurfaceBounds.center : o.transform.position; break; }
            bool lure = Hash.Unit(Seed, Salt.Event + 10, c.X, c.Y) < .45;
            var target = lure && bog.HasValue ? bog : treasure ?? bog;
            if (!target.HasValue) return;
            double a = Hash.Unit(Seed, Salt.Event + 11, c.X, c.Y) * Mathf.PI * 2;
            var start = target.Value + new Vector3((float)System.Math.Cos(a), 0, (float)System.Math.Sin(a)) * 11 + Vector3.up * 1.3f;
            var wisp = Instantiate(content.wisp, start, Quaternion.identity, entry.view.transform);
            wisp.name = content.wisp.name;
            wisp.Lead(world, player, start, target.Value);
        }

        void SpawnChest(Loaded entry, Memory mem)
        {
            var plan = entry.plan;
            var c = plan.Coord;
            if (!content || WorldRules.IsStartArea(c)) return;
            var table = content.ChestTable(plan.Ring);
            if (!table) return;
            var prefab = table.Roll(Hash.Unit(Seed, Salt.ChestPresence, c.X, c.Y), Hash.Unit(Seed, Salt.ChestTier, c.X, c.Y));
            if (!prefab) return;
            int spot = ChestSpot(entry);
            if (spot < 0) return;
            var socket = entry.view.findSockets[spot];

            // On the ground under the spot (sockets sit .15 m above it); not on a branch or a roof overhead.
            Physics.SyncTransforms();
            var at = socket.position + Vector3.down * .15f;
            int count = Physics.RaycastNonAlloc(socket.position + Vector3.up * 2, Vector3.down, chestHits, 6, ~((1 << 2) | (1 << 4)), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int h = 0; h < count; h++)
            {
                var hit = chestHits[h];
                if (hit.point.y > socket.position.y + .5f || hit.distance >= best) continue;
                best = hit.distance;
                at = hit.point;
            }
            // The lock (the chest's -Z) toward the middle of the chunk, where the player comes from, a little askew.
            var toMiddle = entry.view.transform.position - at;
            toMiddle.y = 0;
            float skew = (float)(Hash.Unit(Seed, Salt.ChestSpot, c.X, c.Y, 1) - .5) * 40;
            var facing = toMiddle.sqrMagnitude > .25f ? Quaternion.LookRotation(-toMiddle) * Quaternion.Euler(0, skew, 0) : Quaternion.Euler(0, skew * 9, 0);
            var chest = Instantiate(prefab, at, facing, entry.view.transform);
            chest.name = prefab.name;
            chest.world = world;
            var mimic = chest.GetComponent<ChestMimic>();
            if (mimic && mem != null && mem.mimic.HasValue)
            {
                chest.ResetForOuting();
                mimic.Restore(mem.mimic.Value);
            }
            else if (mem != null && mem.chestOpened) chest.SetEmpty();
            else chest.ResetForOuting();
            entry.chest = chest;
        }

        /// <summary>
        /// Find spots no find of this chunk uses (taken or not, so the choice never changes), as SpawnLoot picks them;
        /// in socket order.
        /// </summary>
        void FreeSockets(Loaded entry)
        {
            var plan = entry.plan;
            var sockets = entry.view.findSockets;
            int n = sockets.Length;
            freeSockets.Clear();
            for (int s = 0; s < n; s++) if (sockets[s]) freeSockets.Add(s);
            for (int i = 0; i < plan.Loot.Count && n > 0; i++)
                freeSockets.Remove(plan.Loot[i] != "find" ? 0 : (int)((Pick(plan.Coord, i) + (uint)i) % (uint)n));
        }

        /// <summary>The find spot the chunk's chest takes, whether the chest is there or not: −1 for none free.</summary>
        int ChestSpot(Loaded entry)
        {
            var c = entry.plan.Coord;
            FreeSockets(entry);
            if (freeSockets.Count == 0) return -1;
            return freeSockets[(int)(Hash.Of(Seed, Salt.ChestSpot, c.X, c.Y) % (uint)freeSockets.Count)];
        }

        /// <summary>
        /// Where a payphone or a television stands: a find spot no find and no chest uses; else, when the scene has too
        /// few, a creature spot; else the navigation nearest the middle of the chunk.
        /// </summary>
        bool OdditySpot(Loaded entry, out Vector3 spot)
        {
            var c = entry.plan.Coord;
            uint pick = Hash.Of(Seed, Salt.EventSpot, c.X, c.Y);
            FreeSockets(entry);
            if (entry.chest)
                for (int i = freeSockets.Count - 1; i >= 0; i--)
                    if (Vector3.Distance(entry.view.findSockets[freeSockets[i]].position, entry.chest.transform.position) < 2.5f) freeSockets.RemoveAt(i);
            if (freeSockets.Count > 0)
            {
                spot = entry.view.findSockets[freeSockets[(int)(pick % (uint)freeSockets.Count)]].position;
                return true;
            }
            var creatureSockets = entry.view.creatureSockets;
            if (creatureSockets.Length > 0 && creatureSockets[(int)(pick % (uint)creatureSockets.Length)])
            {
                // A step aside, so the chunk's beasts gather round it rather than inside it.
                var socket = creatureSockets[(int)(pick % (uint)creatureSockets.Length)];
                spot = socket.position + socket.right * 1.6f;
                if (NavMesh.SamplePosition(spot, out var near, 1.5f, NavMesh.AllAreas)) { spot = near.position; return true; }
                spot = socket.position;
                return true;
            }
            if (NavMesh.SamplePosition(entry.view.transform.position, out var hit, 5, NavMesh.AllAreas)) { spot = hit.position; return true; }
            spot = default;
            return false;
        }

        /// <summary>
        /// The chunk's small event (<see cref="WorldEvents"/>), if its plan has one. A payphone or a television stands on
        /// a free spot (<see cref="OdditySpot"/>), facing the middle of the chunk; a brawl takes the creature spot nearest
        /// the middle, its packs and variant from the seed. Remembered events come back as left.
        /// </summary>
        void SpawnEvent(Loaded entry, Memory mem)
        {
            var plan = entry.plan;
            if (plan.Event == null || !content) return;
            var c = plan.Coord;
            var middle = entry.view.transform.position;
            if (plan.Event == WorldEvents.Brawl)
            {
                if (!content.brawl) return;
                var sockets = entry.view.creatureSockets;
                Vector3 at = middle;
                float best = float.MaxValue;
                foreach (var s in sockets)
                {
                    if (!s) continue;
                    float d = (s.position - middle).sqrMagnitude;
                    if (d < best) { best = d; at = s.position; }
                }
                if (NavMesh.SamplePosition(at, out var hit, 3, NavMesh.AllAreas)) at = hit.position;
                var brawl = Instantiate(content.brawl, at, Quaternion.identity, entry.view.transform);
                brawl.name = content.brawl.name;
                brawl.world = world;
                brawl.variant = Hash.Unit(Seed, Salt.EventKind + 2, c.X, c.Y) < .5 ? BeastBrawl.Variant.AlreadyFighting : BeastBrawl.Variant.Calm;
                if (mem != null && mem.brawl != null) brawl.Restore(mem.brawl);
                else brawl.Spawn((int)Hash.Of(Seed, Salt.EventSeed, c.X, c.Y));
                entry.brawl = brawl;
                return;
            }

            var prefab = plan.Event == WorldEvents.Payphone ? content.payphone : plan.Event == WorldEvents.Television ? content.television : null;
            if (!prefab) { Debug.LogWarning($"[World] no prefab for the event {plan.Event}"); return; }
            if (!OdditySpot(entry, out var spot))
            {
                Debug.LogWarning($"[World] no room for the {plan.Event} on {plan}");
                return;
            }
            var toMiddle = middle - spot;
            toMiddle.y = 0;
            var facing = toMiddle.sqrMagnitude > .25f ? Quaternion.LookRotation(toMiddle) : Quaternion.Euler(0, (float)(Hash.Unit(Seed, Salt.EventSpot, c.X, c.Y, 1) * 360), 0);
            var oddity = Instantiate(prefab, spot, facing, entry.view.transform);
            oddity.name = prefab.name;
            oddity.world = world;
            if (mem != null && mem.oddity.HasValue) oddity.Restore(mem.oddity.Value);
            entry.oddity = oddity;
        }

        /// <param name="pick">Seeds which item a pooled find turns out to be: the same for the same spot of the same world.</param>
        WorldLoot PlaceLoot(string id, Transform socket, uint pick)
        {
            var prefab = content ? content.Loot(id) : null;
            if (!prefab) return null;
            var loot = Instantiate(prefab, socket.position, socket.rotation, socket);
            loot.world = world;
            loot.PickFromPool(new System.Random((int)(pick & 0x7fffffff)));
            loot.guaranteed = true;
            loot.spawnPoints = null;
            world.finds.Add(loot);
            // Rewards worth a detour sparkle, so they are seen from where the way to them starts; the glow goes with them.
            var glow = content.Glow(id);
            if (glow) Instantiate(glow, loot.transform.position + Vector3.up * .5f, Quaternion.identity, loot.transform);
            return loot;
        }

        /// <summary>
        /// The chunk's creature groups: each group round one of the chunk's creature sockets, the leader on it and the
        /// rest in a loose ring on the navigation. Remembered creatures come back as left; dead ones stay dead.
        /// </summary>
        void SpawnCreatures(Loaded entry, Memory mem)
        {
            var plan = entry.plan;
            var sockets = entry.view.creatureSockets;
            if (plan.Spawns.Count == 0 || sockets.Length == 0 || !content) return;
            int k = 0;
            // Ambushers of this chunk each take their own clump of cover.
            var cover = new List<Vector3>();
            for (int g = 0; g < plan.Spawns.Count; g++)
            {
                var spawn = plan.Spawns[g];
                var prefab = content.Creature(spawn.Creature, Pick(plan.Coord, 50 + g));
                var socket = sockets[(Pick(plan.Coord, 60) + (uint)g) % (uint)sockets.Length];
                float turn = Pick(plan.Coord, 70 + g) % 628 / 100f;
                // More groups than spots (the calm ones come in threes): the later ones settle a few metres off.
                int lap = g / sockets.Length;
                var centre = socket.position;
                if (lap > 0)
                {
                    float a = turn + lap * 2.1f;
                    var aside = centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (2.6f + .8f * lap);
                    if (NavMesh.SamplePosition(aside, out var spot, 2f, NavMesh.AllAreas)) centre = spot.position + Vector3.up * .15f;
                }
                var taken = new List<Vector3>();
                var group = new List<WorldCreature>();
                for (int m = 0; m < spawn.Count; m++, k++)
                {
                    bool remembered = false;
                    (bool alive, int health, Vector3 position) state = default;
                    if (mem != null && mem.creatures.TryGetValue(k, out state))
                    {
                        remembered = true;
                        if (!state.alive) continue;
                    }
                    if (!prefab) continue;
                    var at = Roomy(centre, taken, turn + m * 2.4f, m > 0 ? 1.4f + .45f * m : 0) ?? centre;
                    taken.Add(at);
                    var creature = Instantiate(prefab, at, socket.rotation, entry.view.transform);
                    creature.world = world;
                    creature.spawnPoints = null;
                    if (remembered) creature.Resume(state.health, state.position);
                    else creature.ResetForOuting(null);
                    // An ambusher (mantis man) settles in the nearest reeds round its spot.
                    // A leech goes under the nearest water instead.
                    if (creature.ambush.inWater) creature.HideInWater(entry.view.obstacles, cover, !remembered);
                    else if (creature.ambush.enabled) creature.HideIn(entry.view.burnables, cover, !remembered);
                    // One hiding among copies of its disguise (the rat pumpkin's patch) lays them out round its spot.
                    if (creature.ambush.companion) creature.LayOutCompanions(entry.view.transform, new System.Random((int)(Pick(plan.Coord, 90 + k) & 0x7fffffff)), taken);
                    entry.creatures.Add((k, creature));
                    world.creatures.Add(creature);
                    group.Add(creature);
                }
                // Squad creatures (giant dung beetles) walk a route round their spot together.
                if (prefab && prefab.patrol.enabled && group.Count > 0) PatrolSquad.Form(group, centre, (int)(Pick(plan.Coord, 80 + g) & 0x7fffffff));
            }
        }

        static readonly Collider[] spawnHits = new Collider[8];

        /// <summary>
        /// A spot for one of a group on the navigation round <paramref name="centre"/> where its body has room: no other
        /// of the group within a metre, nothing solid (a low bough, a rock) where it stands — else a body pushed out of
        /// another flies up and lands in a tree. Null when none is found.
        /// </summary>
        static Vector3? Roomy(Vector3 centre, List<Vector3> taken, float angle, float radius)
        {
            for (int k = 0; k < 14; k++)
            {
                float a = angle + k * .9f, r = radius + (k > 0 ? .5f + k * .2f : 0);
                var probe = centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                if (!NavMesh.SamplePosition(probe, out var hit, 1.6f, NavMesh.AllAreas)) continue;
                var at = hit.position + Vector3.up * .15f;
                bool apart = true;
                foreach (var other in taken) if ((other - at).sqrMagnitude < 1) { apart = false; break; }
                if (!apart) continue;
                int n = Physics.OverlapCapsuleNonAlloc(at + Vector3.up * .45f, at + Vector3.up * 1.4f, .35f, spawnHits, ~(1 << 4), QueryTriggerInteraction.Ignore);
                bool clear = true;
                for (int i = 0; i < n; i++) if (!spawnHits[i].attachedRigidbody || spawnHits[i].attachedRigidbody.isKinematic) { clear = false; break; }
                if (clear) return at;
            }
            return null;
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
                // Tar that caught fire is as good as burnt out.
                foreach (var (index, tar) in entry.tar) if (!tar || tar.Phase != TarPhase.Tar) mem.tarGone.Add(index);
                if (entry.chest && entry.chest.Opened) mem.chestOpened = true;
                if (entry.sunken && entry.sunken.Opened) mem.sunkenOpened = true;
                if (entry.chest && entry.chest.TryGetComponent<ChestMimic>(out var mimic)) mem.mimic = mimic.Capture();
                if (entry.brawl) mem.brawl = entry.brawl.Capture();
                if (entry.oddity) mem.oddity = entry.oddity.Capture();
                foreach (var (index, creature) in entry.creatures)
                    mem.creatures[index] = creature && creature.gameObject.activeSelf && creature.Alive
                        ? (true, creature.Health, creature.transform.position)
                        : (false, 0, Vector3.zero);
                for (int i = 0; i < entry.view.obstacles.Length; i++)
                    if (entry.view.obstacles[i] && entry.view.obstacles[i].Resolved) mem.resolved.Add(i);
                for (int i = 0; i < entry.view.props.Length; i++)
                    if (entry.view.props[i]) mem.props[i] = (entry.view.props[i].transform.position, entry.view.props[i].transform.rotation);
                for (int i = 0; i < entry.view.burnables.Length; i++)
                    if (fire && fire.Touched(entry.view.burnables[i])) mem.burnt.Add(i);
                // A tree that came alive walks on as an ally; its chunk keeps the empty place.
                for (int i = 0; i < entry.view.trees.Length; i++)
                    if (entry.view.trees[i] && !entry.view.trees[i].gameObject.activeSelf) mem.awakened.Add(i);
                HideLoose(coord, mem);
            }
            if (fire)
            {
                foreach (var b in entry.view.burnables) fire.Leave(b);
                foreach (var o in entry.view.obstacles) fire.LeaveThorns(o);
            }
            if (world)
            {
                foreach (var (_, loot) in entry.loot) world.finds.Remove(loot);
                foreach (var (_, creature) in entry.creatures) world.creatures.Remove(creature);
                if (entry.brawl) foreach (var beast in entry.brawl.Beasts) world.creatures.Remove(beast);
                foreach (var o in entry.view.obstacles) world.obstacles.Remove(o);
                foreach (var p in entry.view.props) world.props.Remove(p);
            }
            foreach (var side in Sides.All) Unjoin(coord, side);
            Quiet(entry.view);
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
                // Allies (awakened trees) go with the player, even while under the ground: never left behind in a chunk.
                if (child.GetComponent<ICombatant>() is ICombatant fighter && fighter.Faction == Faction.Player) continue;
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
        /// Where a link lands on one side of an edge: on the ground the chunk's prefab measured there (landmarks meet
        /// their neighbours wherever their hill or crater has it; every edge rolls a little); the region's level for
        /// prefabs built before edge heights.
        /// </summary>
        float LinkHeight(Loaded chunk, Side side, int segment, int level)
        {
            var plan = chunk.plan;
            var heights = chunk.view ? chunk.view.edgeHeights : null;
            if (heights == null || heights.Length != 12) return Height(level);
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
