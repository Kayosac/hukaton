using System.Collections.Generic;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// The life of the water round the player (Kayospace «Биом: Гнилая топь»): banks of ground fog wander over the
    /// swamp (walk into one and the world goes white), fish splash in deep water, frogs croak along the shores (and
    /// fall silent when something hostile is near), frogs plop into the water as the player comes up, dragonflies and
    /// midges hang over the swamp. All of it cheap: a few fog discs, one-shot splashes, two particle clouds.
    /// </summary>
    public sealed class SwampLife : MonoBehaviour
    {
        [Header("Ground fog")]
        [Tooltip("How many fog banks wander round the player in the swamp.")]
        [Min(0)] public int banks = 7;
        public Vector2 bankRadius = new Vector2(8, 13);
        [Tooltip("How fast the banks drift, m/s.")]
        [Min(0)] public float bankSpeed = .45f;
        [Tooltip("How far out banks live, m.")]
        [Min(10)] public float bankRange = 70;

        [Header("Life")]
        public Vector2 fishEvery = new Vector2(3, 8);
        public Vector2 croakEvery = new Vector2(1.2f, 3.5f);
        [Tooltip("Frogs keep quiet while a hostile creature is this near the player, m.")]
        [Min(0)] public float hushRadius = 16;

        /// <summary>The swamp's water surface over a chunk's base (TileTerrain.WaterLevel).</summary>
        const float WaterLevel = -.3f;
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int SeedId = Shader.PropertyToID("_Seed");

        sealed class Bank
        {
            public Transform transform;
            public MeshRenderer renderer;
            public Vector2 position;
            public float radius, level, fade, seed;
            public bool dying;
        }

        WorldStreamer streamer;
        WorldAtmosphere air;
        readonly List<Bank> live = new List<Bank>();
        Mesh disc;
        MaterialPropertyBlock block;
        Transform holder;
        float nextFish, nextCroak, nextPlop, hush, nextBank;
        Vector2 wind;
        Vector3 lastPlayer;
        ParticleSystem[] dragonflies, midges;
        Transform dragonflyRoot, midgeRoot;

        public float Mist { get; private set; }

        /// <summary>Fog banks round the player now, and where one of them is (for tests).</summary>
        public int Banks => live.Count;
        public Vector3 ThickestBank()
        {
            Bank best = null;
            foreach (var b in live) if (!b.dying && (best == null || b.fade > best.fade)) best = b;
            return best == null ? Vector3.zero : new Vector3(best.position.x, best.level, best.position.y);
        }

        public Vector3 BankCentre(int i) => live.Count == 0 ? Vector3.zero : new Vector3(live[i % live.Count].position.x, live[i % live.Count].level, live[i % live.Count].position.y);

        void Awake()
        {
            streamer = GetComponent<WorldStreamer>();
            air = GetComponent<WorldAtmosphere>();
            float a = Random.value * Mathf.PI * 2;
            wind = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        void OnDisable() => Clear();

        void Clear()
        {
            foreach (var b in live) if (b.transform) Destroy(b.transform.gameObject);
            live.Clear();
            Mist = 0;
            Cloud(dragonflies, 0);
            Cloud(midges, 0);
        }

        void LateUpdate()
        {
            var content = streamer ? streamer.content : null;
            if (!streamer || !streamer.Active || !streamer.player || !content)
            {
                if (live.Count > 0 || Mist > 0) Clear();
                return;
            }
            float swamp = air ? air.Strength(WorldAtmosphere.Swamp) : 0;
            Fog(content);
            Fauna(content, swamp);
            if (!dragonflyRoot && content.dragonflies) dragonflies = Spawn(content.dragonflies, "Dragonflies", out dragonflyRoot);
            if (!midgeRoot && content.midges) midges = Spawn(content.midges, "Midges", out midgeRoot);
            var cam = streamer.World && streamer.World.viewCamera ? streamer.World.viewCamera.transform.position : streamer.player.position;
            if (dragonflyRoot) dragonflyRoot.position = cam + Vector3.down * .9f;
            if (midgeRoot) midgeRoot.position = cam + Vector3.down * .3f;
            Cloud(dragonflies, swamp);
            Cloud(midges, swamp);
        }

        // ---------- ground fog ----------

        void Fog(WorldContent content)
        {
            if (!content.groundFog) return;
            float dt = Time.deltaTime;
            var player = streamer.player.position;
            var map = streamer.Map;
            float size = map.Rules.ChunkSize, plateau = map.Rules.PlateauHeight;
            var origin = streamer.root ? streamer.root.position : streamer.transform.position;
            bool InSwamp(Vector2 p, out float level)
            {
                var c = streamer.CoordAt(new Vector3(p.x, player.y, p.y));
                var plan = map.Chunk(c);
                level = origin.y + plan.BaseLevel * plateau + WaterLevel;
                return plan.Biome == Biome.Swamp && plan.Shape.Kind != ShapeKind.Cliff;
            }
            // The wind turns slowly; the banks go with it, each wandering a little of its own.
            float turn = Mathf.PerlinNoise(Time.time * .01f, 3.1f) - .5f;
            wind = Quaternion.Euler(0, 0, turn * 4 * dt) * wind;
            // New banks, a few at a time, out in the swamp round the player.
            int alive = 0;
            foreach (var b in live) if (!b.dying) alive++;
            if (alive < banks && Time.time >= nextBank)
            {
                nextBank = Time.time + .5f;
                float a = Random.value * Mathf.PI * 2, d = Random.Range(15f, bankRange * .8f);
                var p = new Vector2(player.x + Mathf.Cos(a) * d, player.z + Mathf.Sin(a) * d);
                if (InSwamp(p, out float level)) live.Add(NewBank(content, p, level));
            }
            float mist = 0;
            var cam = streamer.World && streamer.World.viewCamera ? streamer.World.viewCamera.transform.position : player;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var b = live[i];
                float wander = (Mathf.PerlinNoise(b.seed, Time.time * .05f) - .5f) * 2;
                var dir = (Vector2)(Quaternion.Euler(0, 0, wander * 70) * wind);
                b.position += dir * bankSpeed * dt;
                // Off the swamp, onto another level or too far out: it thins away.
                if (!InSwamp(b.position, out float level) || Mathf.Abs(level - b.level) > .5f || Vector2.Distance(b.position, new Vector2(player.x, player.z)) > bankRange) b.dying = true;
                b.fade = Mathf.MoveTowards(b.fade, b.dying ? 0 : 1, dt / 5);
                if (b.dying && b.fade <= 0)
                {
                    Destroy(b.transform.gameObject);
                    live.RemoveAt(i);
                    continue;
                }
                b.transform.position = new Vector3(b.position.x, b.level, b.position.y);
                block.SetFloat(FadeId, b.fade);
                block.SetFloat(SeedId, b.seed);
                b.renderer.SetPropertyBlock(block);
                // Inside a bank the world is white: the air thickens round the camera.
                float flat = Vector2.Distance(new Vector2(cam.x, cam.z), b.position) / (b.radius * .8f);
                float above = cam.y - b.level;
                float inside = Mathf.Clamp01(1.15f - flat) * Mathf.Clamp01((3.3f - above) / 1f) * b.fade;
                mist = Mathf.Max(mist, inside);
            }
            Mist = mist;
            if (air) air.Hold(WorldAtmosphere.Mist, mist);
        }

        Bank NewBank(WorldContent content, Vector2 position, float level)
        {
            if (!holder)
            {
                holder = new GameObject("Ground fog").transform;
                holder.SetParent(transform, false);
            }
            disc ??= Disc();
            block ??= new MaterialPropertyBlock();
            var go = new GameObject("Fog bank");
            go.transform.SetParent(holder, false);
            float radius = Random.Range(bankRadius.x, bankRadius.y);
            go.transform.localScale = new Vector3(radius, 1, radius);
            go.AddComponent<MeshFilter>().sharedMesh = disc;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = content.groundFog;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return new Bank { transform = go.transform, renderer = r, position = position, radius = radius, level = level, seed = Random.Range(0f, 100f) };
        }

        /// <summary>Layers of discs over the water: alpha 1 at the middle, 0 at the rim (vertex colour); UV1.x the layer's height share.</summary>
        static Mesh Disc()
        {
            const int segments = 28;
            float[] heights = { .12f, .6f, 1.1f, 1.65f, 2.25f };
            var v = new List<Vector3>();
            var c = new List<Color>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            foreach (float h in heights)
            {
                int centre = v.Count;
                float top = h / 2.25f;
                v.Add(new Vector3(0, h, 0)); c.Add(new Color(1, 1, 1, 1)); uv.Add(new Vector2(top, 0));
                for (int ring = 1; ring <= 2; ring++)
                    for (int s = 0; s < segments; s++)
                    {
                        float a = s * Mathf.PI * 2 / segments, r = ring == 1 ? .55f : 1f;
                        v.Add(new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r));
                        c.Add(new Color(1, 1, 1, ring == 1 ? .75f : 0));
                        uv.Add(new Vector2(top, 0));
                    }
                for (int s = 0; s < segments; s++)
                {
                    int a0 = centre + 1 + s, a1 = centre + 1 + (s + 1) % segments;
                    int b0 = a0 + segments, b1 = a1 + segments;
                    tris.AddRange(new[] { centre, a1, a0, a0, a1, b1, a0, b1, b0 });
                }
            }
            var mesh = new Mesh { name = "Fog bank" };
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetUVs(1, uv);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(new Vector3(0, 1.2f, 0), new Vector3(2.2f, 2.6f, 2.2f));
            return mesh;
        }

        // ---------- fish, frogs, dragonflies ----------

        void Fauna(WorldContent content, float swamp)
        {
            var world = streamer.World;
            if (!world || !world.Running) return;
            var player = streamer.player.position;
            float now = Time.time;
            // Frogs fall silent while something hostile is near.
            foreach (var c in world.creatures)
                if (c && c.Alive && c.isActiveAndEnabled && (c.transform.position - player).sqrMagnitude < hushRadius * hushRadius)
                {
                    hush = now + 4;
                    break;
                }
            if (now >= nextFish)
            {
                nextFish = now + Random.Range(fishEvery.x, fishEvery.y) / Mathf.Lerp(1, 1.6f, swamp);
                for (int k = 0; k < 4; k++)
                    if (Water(player, 6, 24, out var at, out float depth) && depth > .35f)
                    {
                        Splash(world, content.fishSplash, content.fishCue, at, Random.Range(.8f, 1.2f));
                        break;
                    }
            }
            if (now >= nextCroak && now >= hush)
            {
                nextCroak = now + Random.Range(croakEvery.x, croakEvery.y) / Mathf.Lerp(.5f, 1.4f, swamp);
                for (int k = 0; k < 4; k++)
                    if (Water(player, 7, 26, out var at, out float depth) && depth < .35f)
                    {
                        if (content.frogCue) world.Play(content.frogCue, at, 1, Random.Range(.85f, 1.2f));
                        break;
                    }
            }
            // A frog jumps into the water as the player comes up to the shore.
            var moved = player - lastPlayer;
            lastPlayer = player;
            moved.y = 0;
            if (now >= nextPlop && moved.sqrMagnitude > .0001f)
            {
                nextPlop = now + .4f;
                var ahead = player + moved.normalized * Random.Range(2f, 4f) + new Vector3(Random.Range(-1.5f, 1.5f), 0, Random.Range(-1.5f, 1.5f));
                if (Random.value < .3f && Water(ahead, 0, 0, out var at, out float depth) && depth > .03f && depth < .3f)
                {
                    Splash(world, content.fishSplash, content.plopCue, at, .45f);
                    nextPlop = now + 3;
                }
            }
        }

        static readonly RaycastHit[] hits = new RaycastHit[6];

        /// <summary>A point of open water (not a bog) round <paramref name="centre"/>; its depth to the bed.</summary>
        bool Water(Vector3 centre, float from, float to, out Vector3 at, out float depth)
        {
            at = default;
            depth = 0;
            var p = centre;
            if (to > 0)
            {
                float a = Random.value * Mathf.PI * 2, d = Random.Range(from, to);
                p += new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            }
            if (!Physics.Raycast(p + Vector3.up * 12, Vector3.down, out var surface, 30, 1 << 4, QueryTriggerInteraction.Collide)) return false;
            if (surface.collider.GetComponentInParent<BogMire>()) return false;
            at = surface.point;
            depth = Physics.Raycast(at + Vector3.up * .01f, Vector3.down, out var bed, 6, ~((1 << 4) | (1 << 2)), QueryTriggerInteraction.Ignore) ? bed.distance : 6;
            return true;
        }

        static void Splash(ExpeditionWorld world, VfxBurst prefab, SfxCue cue, Vector3 at, float scale)
        {
            if (prefab) VfxBurst.Spawn(prefab, at + Vector3.up * .02f, Quaternion.identity, world.spawnedObjects, Color.white, scale);
            if (cue) world.Play(cue, at, Mathf.Clamp01(scale + .2f), Random.Range(.9f, 1.15f) / Mathf.Max(.5f, scale));
        }

        ParticleSystem[] Spawn(GameObject prefab, string name, out Transform root)
        {
            if (!holder)
            {
                holder = new GameObject("Ground fog").transform;
                holder.SetParent(transform, false);
            }
            var go = Instantiate(prefab, holder);
            go.name = name;
            root = go.transform;
            var systems = go.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            }
            return systems;
        }

        static void Cloud(ParticleSystem[] systems, float k)
        {
            if (systems == null) return;
            foreach (var ps in systems)
            {
                if (!ps) continue;
                if (k > .05f && !ps.isEmitting) ps.Play(false);
                else if (k <= .05f && ps.isEmitting) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }
}
