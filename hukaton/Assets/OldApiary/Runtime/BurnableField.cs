using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.OldApiary
{
    /// <summary>
    /// Runs fire across the level's <see cref="Burnable"/>s. For every outing it registers them as card zones so Fire and
    /// Lightning find them, passes flames on to neighbours, thorn thickets and card zones (powder barrel, tar, branch
    /// bridge, gas, Scarecrow: <see cref="CardZoneRegistry.IgniteBox"/>), chars them or burns them away, and when the
    /// outing ends puts every one back as it was. Thickets burnt by a card light the kindling around them too.
    /// A streamed world (the endless world's chunks) brings its burnables and thickets in and takes them out as chunks
    /// load and unload: <see cref="Join(Burnable)"/>, <see cref="Leave(Burnable)"/>, <see cref="JoinThorns"/>.
    /// </summary>
    public sealed class BurnableField : MonoBehaviour
    {
        public ExpeditionWorld world;
        [Tooltip("Everything of the level that can burn (filled by the authoring pass; edit freely).")]
        public Burnable[] burnables;

        [Header("Fire")]
        [Tooltip("How close a Fire or Lightning hit must land to a burnable's look to set it alight, m.")]
        [Min(0)] public float catchReach = .5f;
        [Tooltip("Flames jump to burnables whose look is this close, m.")]
        [Min(0)] public float spreadReach = .9f;
        [Tooltip("Share of its burn time after which a burnable lights its neighbours.")]
        [Range(0, 1)] public float spreadAt = .5f;
        [Tooltip("Thorn thickets this close to a burning thing catch fire too, m.")]
        [Min(0)] public float thornReach = 1.2f;
        [Tooltip("Зоны карт (пороховая бочка, смола, ветвь-мост, газ, Чучело) так близко к горящему загораются от него, м.")]
        [Min(0)] public float zoneReach = .3f;
        [Tooltip("At most this many burn at once; the rest catch fire in turn.")]
        [Min(1)] public int maxBurning = 40;
        [Tooltip("Ash only: how low the burning thing sinks before it is gone (share of its height).")]
        [Range(0, 1)] public float ashHeight = .2f;

        [Header("Feedback")]
        public VfxBurst flare;
        public VfxBurst flames;
        public SfxCue igniteSound, burnLoop;
        public Color tint = new Color(1f, .62f, .3f);
        public Color hot = new Color(1f, .55f, .2f), charred = new Color(.12f, .1f, .09f);

        sealed class Fire
        {
            public Burnable burnable;
            public float time;
            public bool spread;
            public VfxBurst fx;
        }

        sealed class Original
        {
            public Vector3 scale;
            public bool kinematic;
            public Collider[] colliders;
        }

        readonly List<Fire> fires = new List<Fire>();
        readonly Queue<(Burnable burnable, Vector3 point)> waiting = new Queue<(Burnable, Vector3)>();
        readonly Dictionary<Burnable, Original> touched = new Dictionary<Burnable, Original>();
        readonly Dictionary<WorldObstacle, Bounds> thorns = new Dictionary<WorldObstacle, Bounds>();
        readonly HashSet<WorldObstacle> thornsLit = new HashSet<WorldObstacle>();
        readonly Dictionary<Vector2Int, List<Burnable>> grid = new Dictionary<Vector2Int, List<Burnable>>();
        readonly List<Burnable> near = new List<Burnable>();
        readonly List<Burnable> streamed = new List<Burnable>();
        readonly List<WorldObstacle> streamedThorns = new List<WorldObstacle>();
        MaterialPropertyBlock block;
        bool registered;
        const float Cell = 2f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>Burning or burnt this outing.</summary>
        public int Lit => touched.Count;
        public int Burning => fires.Count;

        void OnEnable() { if (world) world.OutingCleared += RestoreAll; }
        void OnDisable() { if (world) world.OutingCleared -= RestoreAll; RestoreAll(); }

        void Update()
        {
            if (!world) return;
            if (!world.Running)
            {
                if (registered || touched.Count > 0) RestoreAll();
                return;
            }
            if (!registered) Register();
            CatchFromThorns();
            Advance(Time.deltaTime);
        }

        void Register()
        {
            registered = true;
            grid.Clear();
            if (burnables != null) foreach (var b in burnables) Enter(b);
            foreach (var b in streamed) Enter(b);
            thorns.Clear(); thornsLit.Clear();
            if (world.obstacles != null)
                foreach (var o in world.obstacles)
                    if (o && o.kind == ObstacleKind.Thorns && o.barrier && o.transform.IsChildOf(transform.root))
                        thorns[o] = o.barrier.bounds;
            foreach (var o in streamedThorns) if (o && o.barrier) thorns[o] = o.barrier.bounds;
        }

        void Enter(Burnable b)
        {
            if (!b) return;
            b.Forget();
            world.Zones.Add(b);
            var key = Key(b);
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Burnable>();
            list.Add(b);
        }

        static Vector2Int Key(Burnable b)
        {
            var c = b.Look.center;
            return new Vector2Int(Mathf.FloorToInt(c.x / Cell), Mathf.FloorToInt(c.z / Cell));
        }

        // ---------- streamed worlds ----------

        /// <summary>A burnable of a chunk that has just loaded: it can catch fire from now on.</summary>
        public void Join(Burnable b)
        {
            if (!b || streamed.Contains(b)) return;
            b.field = this;
            streamed.Add(b);
            if (registered) Enter(b);
        }

        /// <summary>A burnable of a chunk about to unload: its fire goes out with it (the chunk remembers what burnt).</summary>
        public void Leave(Burnable b)
        {
            if (!b || !streamed.Remove(b)) return;
            if (world) world.Zones.Remove(b);
            if (grid.TryGetValue(Key(b), out var list)) list.Remove(b);
            int i = fires.FindIndex(f => f.burnable == b);
            if (i >= 0) { if (fires[i].fx) fires[i].fx.Release(.2f); fires.RemoveAt(i); }
            touched.Remove(b);
        }

        /// <summary>A thorn thicket of a loaded chunk: burning things nearby light it, and it lights them.</summary>
        public void JoinThorns(WorldObstacle thicket)
        {
            if (!thicket || thicket.kind != ObstacleKind.Thorns || streamedThorns.Contains(thicket)) return;
            streamedThorns.Add(thicket);
            if (registered && thicket.barrier) thorns[thicket] = thicket.barrier.bounds;
        }

        public void LeaveThorns(WorldObstacle thicket)
        {
            if (!thicket) return;
            streamedThorns.Remove(thicket);
            thorns.Remove(thicket);
            thornsLit.Remove(thicket);
        }

        /// <summary>Burning or burnt this outing.</summary>
        public bool Touched(Burnable b) => b && touched.ContainsKey(b);

        /// <summary>A burnable that burnt before its chunk unloaded comes back burnt: charred, or gone to ash.</summary>
        public void Burnt(Burnable b)
        {
            if (!b || touched.ContainsKey(b)) return;
            Remember(b);
            block ??= new MaterialPropertyBlock();
            foreach (var r in b.Renderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColor, charred);
                r.SetPropertyBlock(block);
            }
            Finish(new Fire { burnable = b });
        }

        /// <summary>Sets a burnable alight. False when the outing is not running or it already burns or burnt.</summary>
        public bool Ignite(Burnable burnable, Vector3 point)
        {
            if (!registered || !world || !world.Running || !burnable || touched.ContainsKey(burnable)) return false;
            Remember(burnable);
            if (fires.Count >= maxBurning) waiting.Enqueue((burnable, point));
            else Light(burnable, point);
            return true;
        }

        /// <summary>Rain puts a burning thing out (it stays as charred as it got).</summary>
        public bool Douse(Burnable burnable)
        {
            int i = fires.FindIndex(f => f.burnable == burnable);
            if (i < 0) return false;
            if (fires[i].fx) fires[i].fx.Release(.4f);
            fires.RemoveAt(i);
            return true;
        }

        void Remember(Burnable b)
        {
            // Drawn in its chunk's merged look until now: from here on by its own renderers (they char and shrink).
            MergedLooks.Unmerge(b.gameObject);
            touched[b] = new Original
            {
                scale = b.transform.localScale,
                kinematic = b.Body && b.Body.isKinematic,
                colliders = b.GetComponentsInChildren<Collider>()
            };
        }

        void Light(Burnable b, Vector3 point)
        {
            var look = b.Look;
            if (igniteSound && fires.Count < 6) world.Play(igniteSound, point, .7f);
            if (flare) world.Spawn(flare, point, Quaternion.identity, tint, .8f);
            float size = Mathf.Max(look.size.x, look.size.z);
            var fx = !flames ? null : world.Spawn(flames, new Vector3(look.center.x, look.min.y, look.center.z), Quaternion.identity, tint,
                Mathf.Clamp(size / 2.5f, .3f, 1.6f));
            if (fx) fx.HoldFor(b.seconds);
            fires.Add(new Fire { burnable = b, fx = fx });
        }

        void Advance(float dt)
        {
            block ??= new MaterialPropertyBlock();
            Vector3 centre = Vector3.zero;
            for (int i = fires.Count - 1; i >= 0; i--)
            {
                var f = fires[i];
                var b = f.burnable;
                if (!b) { fires.RemoveAt(i); continue; }
                f.time += dt;
                float k = Mathf.Clamp01(f.time / b.seconds);
                Color colour = Color.Lerp(hot, charred, k) * (1 + Mathf.Sin(f.time * 40) * .15f * (1 - k));
                foreach (var r in b.Renderers)
                {
                    if (!r) continue;
                    r.GetPropertyBlock(block);
                    block.SetColor(BaseColor, colour);
                    r.SetPropertyBlock(block);
                }
                if (b.style == BurnStyle.Ash && touched.TryGetValue(b, out var o))
                {
                    var s = o.scale;
                    b.transform.localScale = new Vector3(s.x, s.y * Mathf.Lerp(1, ashHeight, k * k * k), s.z);
                }
                if (!f.spread && k >= spreadAt) { f.spread = true; Spread(b); }
                centre += b.Look.center;
                if (k >= 1) { Finish(f); fires.RemoveAt(i); }
            }
            while (waiting.Count > 0 && fires.Count < maxBurning)
            {
                var (b, p) = waiting.Dequeue();
                if (b) Light(b, p);
            }
            if (world.sfx && burnLoop)
            {
                if (fires.Count > 0) world.sfx.Loop(this, burnLoop, centre / Mathf.Max(1, fires.Count), Mathf.Min(1, fires.Count / 4f));
                else world.sfx.StopLoop(this);
            }
        }

        void Spread(Burnable b)
        {
            var look = b.Look;
            near.Clear();
            int reach = Mathf.CeilToInt((Mathf.Max(look.extents.x, look.extents.z) + spreadReach) / Cell);
            int cx = Mathf.FloorToInt(look.center.x / Cell), cz = Mathf.FloorToInt(look.center.z / Cell);
            for (int x = cx - reach; x <= cx + reach; x++)
                for (int z = cz - reach; z <= cz + reach; z++)
                    if (grid.TryGetValue(new Vector2Int(x, z), out var list)) near.AddRange(list);
            foreach (var other in near)
                if (other && other != b && !touched.ContainsKey(other) && other.isActiveAndEnabled && Gap(look, other.Look) <= spreadReach)
                    Ignite(other, Vector3.Lerp(look.center, other.Look.center, .5f));
            foreach (var kv in thorns)
                if (!kv.Key.Resolved && Gap(look, kv.Value) <= thornReach) kv.Key.React(CardAction.Fire, look.center);
            // Огонь горящего поджигает и зоны карт рядом — бочку, смолу, ветвь-мост, газ, Чучело (любой огонь игры
            // поджигает бочку). Горючие этого поля поджигает оно само, выше.
            world.Zones.IgniteBox(look, zoneReach, IgnitionSource.Fire, zone => zone is Burnable);
        }

        void CatchFromThorns()
        {
            foreach (var kv in thorns)
            {
                if (!kv.Key.Resolved || !thornsLit.Add(kv.Key)) continue;
                if (burnables != null)
                    foreach (var b in burnables)
                        if (b && !touched.ContainsKey(b) && Gap(kv.Value, b.Look) <= thornReach) Ignite(b, kv.Value.center);
                foreach (var b in streamed)
                    if (b && !touched.ContainsKey(b) && Gap(kv.Value, b.Look) <= thornReach) Ignite(b, kv.Value.center);
            }
        }

        void Finish(Fire f)
        {
            var b = f.burnable;
            if (f.fx) f.fx.Release(.6f);
            if (b.style != BurnStyle.Ash) return;
            foreach (var r in b.Renderers) if (r) r.enabled = false;
            if (touched.TryGetValue(b, out var o)) foreach (var c in o.colliders) if (c) c.enabled = false;
            if (b.Body) b.Body.isKinematic = true;
            // A young tree burnt to ash is not there to be awakened any more.
            if (b.GetComponent<AwakenableTree>()) world.HideForOuting(b.gameObject);
        }

        void RestoreAll()
        {
            foreach (var f in fires) if (f.fx) f.fx.Release(.2f);
            fires.Clear();
            waiting.Clear();
            foreach (var kv in touched)
            {
                var b = kv.Key;
                if (!b) continue;
                var o = kv.Value;
                b.transform.localScale = o.scale;
                foreach (var r in b.Renderers) { if (!r) continue; r.enabled = true; r.SetPropertyBlock(null); }
                foreach (var c in o.colliders) if (c) c.enabled = true;
                if (b.Body) b.Body.isKinematic = o.kinematic;
                b.Forget();
            }
            touched.Clear();
            thornsLit.Clear();
            if (world && world.sfx) world.sfx.StopLoop(this);
            registered = false;
        }

        /// <summary>Distance between two boxes (0 when they touch or overlap).</summary>
        static float Gap(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float dy = Mathf.Max(0, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
            float dz = Mathf.Max(0, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
