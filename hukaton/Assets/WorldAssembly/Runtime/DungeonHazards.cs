using System.Collections;
using System.Collections.Generic;
using Hakaton.Dungeons;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// A dungeon's floor modifiers (Kayospace «Данжи»: модификатор этажа, 0–1 на этаж). Each level rolls on its own
    /// (<see cref="DungeonKit.modifierChance"/>):
    /// <list type="bullet">
    /// <item><b>Acid</b> — glowing puddles on the floor of corridors and chambers; standing in one poisons the player
    /// (the ready «Яд», refreshed while standing).</item>
    /// <item><b>Collapses</b> — heaps of fallen earth against the walls (never across the way: a lane stays), and now and
    /// then the ceiling comes down a few steps ahead of the player: dust first, then chunks that hurt whoever is under
    /// them. The chunks stay where they fall.</item>
    /// </list>
    /// Laid once with the dungeon (same seed, same hazards) and kept with it.
    /// </summary>
    public sealed class DungeonHazards : MonoBehaviour
    {
        public enum Modifier { None, Acid, Collapse }

        [Tooltip("The player stands in a puddle when his feet are this far inside its edge, m.")]
        public float acidReach = .85f;
        [Tooltip("Poison is put on again this often while standing in acid, s.")]
        public float acidEvery = .5f;
        [Tooltip("A fall comes down this far ahead of the player, m: from and to.")]
        public Vector2 fallAhead = new Vector2(2.5f, 5.5f);
        [Tooltip("Dust before the chunks, s.")]
        public float fallWarning = 1.1f;
        [Tooltip("Hurts the player within this distance of where the fall lands, m.")]
        public float fallReach = 1.5f;
        [Tooltip("At most this many fallen chunks lie in the dungeon; older ones go.")]
        public int keepChunks = 80;

        DungeonBuilder dungeon;
        ExpeditionWorld world;
        readonly List<Modifier> levels = new List<Modifier>();
        readonly List<(Vector3 at, float radius)> acid = new List<(Vector3, float)>();
        readonly Queue<GameObject> chunks = new Queue<GameObject>();
        Transform root;
        float nextPoison, nextFall;
        System.Random random;

        public IReadOnlyList<Modifier> Levels => levels;
        public int AcidPuddles => acid.Count;
        public int Falls { get; private set; }
        /// <summary>A fall is under way (dust is showing, chunks about to drop).</summary>
        public bool Falling { get; private set; }

        public static DungeonHazards Lay(DungeonBuilder dungeon, ExpeditionWorld world, int seed)
        {
            var h = dungeon.gameObject.AddComponent<DungeonHazards>();
            h.dungeon = dungeon;
            h.world = world;
            h.random = new System.Random(seed ^ 0x2f6b1d3);
            h.root = new GameObject("Hazards").transform;
            h.root.SetParent(dungeon.transform, false);
            h.Roll();
            h.Place();
            return h;
        }

        // ---------- laying ----------

        void Roll()
        {
            var kit = dungeon.kit;
            for (int l = 0; l < Mathf.Max(1, dungeon.Plan.Levels); l++)
            {
                var m = Modifier.None;
                if (random.NextDouble() < kit.modifierChance)
                {
                    bool canAcid = kit.acidPuddles != null && kit.acidPuddles.Length > 0;
                    bool canCollapse = kit.rubble != null && kit.rubble.Length > 0;
                    if (canAcid && canCollapse) m = random.NextDouble() < .5 ? Modifier.Acid : Modifier.Collapse;
                    else if (canAcid) m = Modifier.Acid;
                    else if (canCollapse) m = Modifier.Collapse;
                }
                levels.Add(m);
            }
        }

        /// <summary>The level of a floor height (0 at the entrance, one per <see cref="DungeonKit.levelDrop"/> m down).</summary>
        public int LevelOf(float floor) => Mathf.Clamp(Mathf.RoundToInt(-floor / Mathf.Max(1, dungeon.kit.levelDrop)), 0, levels.Count - 1);

        public Modifier At(int level) => level >= 0 && level < levels.Count ? levels[level] : Modifier.None;

        /// <summary>Debug, tests: gives <paramref name="level"/> the modifier <paramref name="m"/> and lays its hazards now.</summary>
        public void Force(int level, Modifier m)
        {
            if (level < 0 || level >= levels.Count) return;
            levels[level] = m;
            Place(level);
        }

        void Place(int onlyLevel = -1)
        {
            var plan = dungeon.Plan;
            var kit = dungeon.kit;
            var markers = new List<Vector3>();
            foreach (var (_, _, at) in dungeon.Markers) if (at) markers.Add(at.position);
            bool NearMarker(Vector3 p, float r)
            {
                foreach (var m in markers) if ((m - p).sqrMagnitude < r * r) return true;
                return false;
            }
            // Eligible cells per level, in a stable order.
            var byLevel = new Dictionary<int, List<Cell>>();
            foreach (var kv in plan.Cells)
            {
                var info = kv.Value;
                if (info.Kind == CellKind.Ramp || info.Kind == CellKind.Passage) continue;
                // The entrance chamber stays clean: the player arrives there.
                if (info.Kind == CellKind.Room && info.Room == plan.Entrance.Index) continue;
                int level = LevelOf(info.Floor);
                if (At(level) == Modifier.None || (onlyLevel >= 0 && level != onlyLevel)) continue;
                if (!byLevel.TryGetValue(level, out var list)) byLevel[level] = list = new List<Cell>();
                list.Add(kv.Key);
            }
            foreach (var kv in byLevel)
            {
                var modifier = At(kv.Key);
                var cells = kv.Value;
                cells.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
                var left = new List<Cell>();
                int laid = 0;
                foreach (var c in cells)
                {
                    bool corridor = plan.Cells[c].Kind == CellKind.Corridor;
                    float chance = modifier == Modifier.Acid ? (corridor ? kit.acidPerCorridorCell : kit.acidPerRoomCell)
                        : (corridor ? kit.rubblePerCorridorCell : kit.rubblePerRoomWall * 2);
                    if (random.NextDouble() < chance && Lay(c, modifier, NearMarker)) laid++;
                    else left.Add(c);
                }
                // A modified level always shows it: a few at least.
                int least = modifier == Modifier.Acid ? 3 : 2;
                while (laid < least && left.Count > 0)
                {
                    int i = random.Next(left.Count);
                    if (Lay(left[i], modifier, NearMarker)) laid++;
                    left.RemoveAt(i);
                }
            }
        }

        bool Lay(Cell c, Modifier modifier, System.Func<Vector3, float, bool> nearMarker)
        {
            var plan = dungeon.Plan;
            var info = plan.Cells[c];
            const float S = DungeonPlan.CellSize;
            bool corridor = info.Kind == CellKind.Corridor;
            var centre = dungeon.Built.TransformPoint(DungeonGeometry.Center(c, info.Floor));
            if (modifier == Modifier.Acid)
            {
                float spread = corridor ? .8f : 1.2f;
                var at = centre + dungeon.Built.rotation * new Vector3(Jitter(spread), 0, Jitter(spread));
                if (nearMarker(at, 2.5f)) return false;
                Puddle(at);
                return true;
            }
            var walls = new List<Dir>();
            foreach (var d in Dirs.All) if (!plan.IsOpen(c, d)) walls.Add(d);
            if (walls.Count == 0) return false;
            var side = walls[random.Next(walls.Count)];
            var spot = centre + dungeon.Built.rotation * (DungeonGeometry.Offset(side) * (S / 2 - .45f) + DungeonGeometry.Offset(side.Right()) * Jitter(.5f));
            if (nearMarker(spot, 2.8f)) return false;
            // Its back (−Z) to the wall, its slope towards the way.
            Heap(spot, dungeon.Built.rotation * Quaternion.Euler(0, DungeonGeometry.Yaw(side.Opposite()) + Jitter(12), 0));
            return true;
        }

        float Jitter(float half) => (float)(random.NextDouble() * 2 - 1) * half;

        void Puddle(Vector3 at)
        {
            var kit = dungeon.kit;
            var prefab = kit.acidPuddles[random.Next(kit.acidPuddles.Length)];
            if (!prefab) return;
            var p = Instantiate(prefab, at + Vector3.up * .01f, Quaternion.Euler(0, (float)random.NextDouble() * 360, 0), root);
            p.name = prefab.name;
            float radius = 1f;
            foreach (var r in p.GetComponentsInChildren<Renderer>())
            {
                radius = Mathf.Max(radius, Mathf.Max(r.bounds.extents.x, r.bounds.extents.z));
                // The liquid takes the glowing acid material; the crust keeps its own.
                if (kit.acidMaterial && r.name.EndsWith("_Acid")) r.sharedMaterial = kit.acidMaterial;
            }
            var glow = new GameObject("Acid Glow").AddComponent<Light>();
            glow.transform.SetParent(p.transform, false);
            glow.transform.localPosition = Vector3.up * .5f;
            glow.type = LightType.Point; glow.color = kit.acidGlow; glow.range = 3.2f; glow.intensity = 1.4f; glow.shadows = LightShadows.None;
            acid.Add((at, Mathf.Max(.4f, radius - acidReach * .5f)));
        }

        void Heap(Vector3 at, Quaternion facing)
        {
            var kit = dungeon.kit;
            var prefab = kit.rubble[random.Next(kit.rubble.Length)];
            if (!prefab) return;
            var h = Instantiate(prefab, at, facing, root);
            h.name = prefab.name;
            // The creatures walk round it.
            var obstacle = h.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0, .6f, .75f);
            obstacle.size = new Vector3(2.1f, 1.2f, 1.5f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }

        // ---------- while the player is below ----------

        void Update()
        {
            if (!world || !world.Running || !world.motor || !dungeon || dungeon.Plan == null) return;
            var feet = world.motor.transform.position;
            var local = dungeon.Built.InverseTransformPoint(feet);
            var cell = new Cell(Mathf.RoundToInt(local.x / DungeonPlan.CellSize), Mathf.RoundToInt(local.z / DungeonPlan.CellSize));
            if (!dungeon.Plan.Cells.TryGetValue(cell, out var info)) return;
            var modifier = At(LevelOf(info.Floor));
            if (modifier == Modifier.Acid) Acid(feet);
            else if (modifier == Modifier.Collapse && info.Kind != CellKind.Passage) Collapse(feet, cell, info);
        }

        void Acid(Vector3 feet)
        {
            if (Time.time < nextPoison) return;
            foreach (var (at, radius) in acid)
            {
                var d = feet - at;
                if (Mathf.Abs(d.y) > 1.2f || new Vector2(d.x, d.z).sqrMagnitude > radius * radius) continue;
                nextPoison = Time.time + acidEvery;
                if (world.statuses) world.statuses.Apply(PlayerStatusKind.Poison);
                return;
            }
        }

        void Collapse(Vector3 feet, Cell cell, CellInfo info)
        {
            if (Falling) return;
            if (nextFall <= 0) { nextFall = Time.time + Random.Range(dungeon.kit.collapseEvery.x, dungeon.kit.collapseEvery.y) * .5f; return; }
            if (Time.time < nextFall) return;
            // A few steps ahead, else somewhere round the player (a corridor's turn, a wall ahead).
            var forward = world.motor.transform.forward; forward.y = 0;
            forward = forward.sqrMagnitude > .01f ? forward.normalized : Vector3.forward;
            NavMeshHit hit = default;
            bool found = false;
            for (int i = 0; i < 6 && !found; i++)
            {
                var dir = i == 0 ? forward : Quaternion.Euler(0, Random.Range(-160f, 160f), 0) * forward;
                var want = feet + dir * Random.Range(fallAhead.x, fallAhead.y);
                found = NavMesh.SamplePosition(want, out hit, 1.5f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - feet.y) < 1.5f
                        && Vector3.Distance(hit.position, feet) > 1.8f;
            }
            if (!found) { nextFall = Time.time + .5f; return; }
            nextFall = Time.time + Random.Range(dungeon.kit.collapseEvery.x, dungeon.kit.collapseEvery.y);
            float ceiling = info.Floor + Mathf.Max(1, info.Rows) * 4f;
            var top = dungeon.Built.TransformPoint(new Vector3(0, ceiling, 0)).y - .6f;
            StartCoroutine(Fall(hit.position, top));
        }

        IEnumerator Fall(Vector3 spot, float top)
        {
            Falling = true;
            Falls++;
            var kit = dungeon.kit;
            var dust = kit.collapseDust ? kit.collapseDust.GetComponent<VfxBurst>() : null;
            var above = new Vector3(spot.x, top, spot.z);
            // A rumble and dust from the ceiling: time to step aside.
            VfxBurst.Spawn(dust, above, Quaternion.Euler(90, 0, 0), root, new Color(.55f, .42f, .32f), 1.3f);
            if (world.feel) world.feel.AddTrauma(.18f);
            yield return new WaitForSeconds(fallWarning * .5f);
            VfxBurst.Spawn(dust, above, Quaternion.Euler(90, 0, 0), root, new Color(.55f, .42f, .32f), 1.6f);
            yield return new WaitForSeconds(fallWarning * .5f);
            if (kit.debris)
            {
                int n = Random.Range(3, 6);
                for (int i = 0; i < n; i++)
                {
                    var source = kit.debris.transform.GetChild(Random.Range(0, kit.debris.transform.childCount));
                    var chunk = Instantiate(source.gameObject, above + new Vector3(Random.Range(-.7f, .7f), Random.Range(-.3f, 0), Random.Range(-.7f, .7f)), Random.rotation, root);
                    chunk.name = "Fallen " + source.name;
                    foreach (var col in chunk.GetComponentsInChildren<Collider>()) DestroyImmediate(col);
                    var mesh = chunk.GetComponentInChildren<MeshFilter>();
                    if (mesh)
                    {
                        var mc = mesh.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mesh.sharedMesh;
                        mc.convex = true;
                    }
                    var body = chunk.AddComponent<Rigidbody>();
                    body.mass = 3;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.linearVelocity = new Vector3(Random.Range(-.6f, .6f), -2f, Random.Range(-.6f, .6f));
                    body.angularVelocity = Random.insideUnitSphere * 6;
                    chunks.Enqueue(chunk);
                    while (chunks.Count > keepChunks) { var old = chunks.Dequeue(); if (old) Destroy(old); }
                }
            }
            // About when they reach the floor.
            float drop = Mathf.Max(.2f, top - spot.y);
            yield return new WaitForSeconds(Mathf.Sqrt(2 * drop / 9.81f) * .85f);
            VfxBurst.Spawn(dust, spot + Vector3.up * .2f, Quaternion.identity, root, new Color(.5f, .4f, .3f), 1.8f);
            if (world.feel) world.feel.AddTrauma(.35f);
            var feet = world.motor.transform.position;
            var d = feet - spot;
            if (Mathf.Abs(d.y) < 2.2f && new Vector2(d.x, d.z).magnitude < fallReach && kit.collapseHalfHearts > 0)
                world.HurtPlayer(kit.collapseHalfHearts, Vector3.zero, spot + Vector3.up * 3, DamageKind.Other);
            Falling = false;
        }
    }
}
