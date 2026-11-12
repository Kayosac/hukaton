using System.Collections.Generic;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// The rain of the endless world (<see cref="WorldWeather"/>): fronts that the outing's wind drives across the land
    /// under low grey clouds with rain hanging from them, so a shower is seen coming, passes over and goes on. It is
    /// heard; far out the heart of a front is a thunderstorm, lightning strikes out of its clouds and the thunder comes
    /// after the flash. The rain is the «Дождевая туча» card's rain: whoever stands under it in the open is wet
    /// (<see cref="WetStatus"/>, the 30 s count; the player's badge), a burning creature goes out and every washable
    /// zone it falls on is washed (<see cref="IWashable"/>: tar, lit or not; burning grass and wood). Under a roof, a
    /// fallen trunk or a ledge it does not reach. On the summits it is snow, which only looks.
    /// Its assets are the world content's (Weather); <see cref="WorldAtmosphere"/> takes the air from it.
    /// </summary>
    [RequireComponent(typeof(WorldStreamer))]
    public sealed class WorldRain : MonoBehaviour
    {
        [Tooltip("How often the rain wets bodies and washes zones, s.")]
        [Min(.05f)] public float tickSeconds = .25f;

        [Header("Clouds")]
        [Tooltip("Height of the cloud deck over the lowest ground, m (the summits are three levels up).")]
        public float cloudHeight = 32;
        [Tooltip("One cloud per cell of this size, m; neighbours merge into a deck.")]
        [Min(8)] public float cloudCell = 30;
        [Tooltip("Clouds are kept this far round the camera, m.")]
        [Min(40)] public float cloudReach = 220;
        [Tooltip("Seconds for a cloud to gather or melt away.")]
        [Min(.1f)] public float cloudGrow = 3;
        public Color cloudColour = new Color(.55f, .58f, .63f), stormColour = new Color(.3f, .32f, .37f);

        [Header("Rain veils")]
        [Tooltip("Rain hanging from the clouds is drawn under this many of them, the nearest (the veil fades out near the camera, where its own rain falls).")]
        [Min(0)] public int maxVeils = 40;
        public Color veilColour = new Color(.3f, .33f, .38f, .55f), stormVeilColour = new Color(.2f, .22f, .26f, .7f);

        [Header("Thunderstorm")]
        [Tooltip("Seconds between strikes at the height of a storm and at its edge.")]
        public Vector2 strikeEvery = new Vector2(5, 15);
        [Tooltip("Lightning strikes this far from the camera, m.")]
        public Vector2 strikeDistance = new Vector2(45, 190);
        [Min(.1f)] public float boltWidth = 1.6f;
        [Min(0)] public float boltJitter = 7;
        [Tooltip("Speed of sound for the thunder's delay, m/s.")]
        [Min(1)] public float soundSpeed = 340;

        WorldStreamer streamer;
        WorldContent content;
        Hakaton.Feel.SfxPlayer sfx;
        bool active;
        float began, offset, nextTick, nextStrike, flashAt = -99, flashPower;
        Vector2 wind;
        Transform holder;
        readonly List<ICombatant> fighters = new List<ICombatant>();
        readonly List<IWashable> washables = new List<IWashable>();
        readonly RaycastHit[] hits = new RaycastHit[8];
        readonly List<(float at, Vector3 where, float volume)> thunders = new List<(float, Vector3, float)>();

        sealed class Cloud
        {
            public Transform transform;
            public MeshRenderer[] renderers;
            public Transform[] puffs;
            public float grow, want, rain, storm, size, thick, lift, yaw, ground;
            public Vector2 home;
            public bool seen, cold, hung;
            public Transform veil;
            public MeshRenderer veilRenderer;
        }

        readonly Dictionary<(int, int), Cloud> clouds = new Dictionary<(int, int), Cloud>();
        readonly Stack<Cloud> spareClouds = new Stack<Cloud>();
        readonly List<(int, int)> gone = new List<(int, int)>();
        readonly List<(float distance, Cloud cloud)> rainy = new List<(float, Cloud)>();
        readonly List<Cloud> stormy = new List<Cloud>();
        MaterialPropertyBlock tint;
        Material cloudMaterial, veilMaterial;
        Mesh veilMesh;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Amount = Shader.PropertyToID("_Amount"), FogDensity = Shader.PropertyToID("_FogDensity");

        /// <summary>Seconds of weather since the outing began (the time <see cref="WorldWeather"/> is asked for).</summary>
        public float Clock => active ? Time.time - began + offset : 0;
        /// <summary>The rain falling round the camera now, eased (0…1; 0 under a roof and in the snow).</summary>
        public float Falling { get; private set; }
        /// <summary>The thunderstorm round the camera now, eased (0…1).</summary>
        public float Pouring { get; private set; }
        /// <summary>Snow falling round the camera now, eased (0…1).</summary>
        public float Snowing { get; private set; }
        /// <summary>Grey clouds overhead or coming near (0…1): the air darkens before the rain.</summary>
        public float Gloom { get; private set; }
        /// <summary>The same for thunder clouds.</summary>
        public float StormGloom { get; private set; }
        /// <summary>A lightning flash lights the land now (0…1).</summary>
        public float Flash => FlashAt(Time.time - flashAt) * flashPower;
        /// <summary>Lightning strikes this outing (tests).</summary>
        public int Strikes { get; private set; }
        /// <summary>Clouds in the sky now (tests).</summary>
        public int Clouds => clouds.Count;
        /// <summary>Rain veils hanging from the clouds now (tests).</summary>
        public int Veils { get; private set; }
        /// <summary>Burning creatures the rain has put out this outing (tests).</summary>
        public int Doused { get; private set; }

        void Awake()
        {
            streamer = GetComponent<WorldStreamer>();
            tint = new MaterialPropertyBlock();
        }

        /// <summary>Sets the weather to <paramref name="seconds"/> after the outing began (tests and photos).</summary>
        public void SetClock(float seconds) => offset = seconds - (Time.time - began);

        Vector3 Origin => streamer.root ? streamer.root.position : transform.position;

        /// <summary>The sky over a world point now.</summary>
        public Sky SkyAt(Vector3 point)
        {
            if (!active) return default;
            var o = Origin;
            return WorldWeather.At(streamer.Map.Rules, streamer.Seed, point.x - o.x, point.z - o.z, Clock);
        }

        /// <summary>On the summits the rain is snow.</summary>
        public bool Cold(Vector3 point) => active && streamer.Map.Chunk(streamer.CoordAt(point)).BaseLevel >= 3;

        /// <summary>Rain falls on this point hard enough to wet and put fire out (open sky not checked).</summary>
        public bool RainsAt(Vector3 point) => active && SkyAt(point).Wets(streamer.Map.Rules.Weather) && !Cold(point);

        /// <summary>Something solid hangs over the point (a roof, a trunk, a ledge): the rain does not reach it.</summary>
        public bool Sheltered(Vector3 point, Transform body = null)
        {
            int n = Physics.RaycastNonAlloc(point, Vector3.up, hits, 60, ~((1 << 2) | (1 << 4)), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = hits[i].collider;
                if (!hit || (body && hit.transform.IsChildOf(body))) continue;
                // A loose thing flying overhead is no roof.
                if (hit.attachedRigidbody && !hit.attachedRigidbody.isKinematic) continue;
                return true;
            }
            return false;
        }

        void LateUpdate()
        {
            bool on = streamer.Active && streamer.player && streamer.World;
            if (on != active)
            {
                if (on) Begin(); else End();
            }
            if (!active) return;
            var world = streamer.World;
            var cam = world.viewCamera ? world.viewCamera.transform.position : streamer.player.position;
            Overhead();
            Air(cam);
            Thunder(world, cam);
            if (Time.time >= nextTick)
            {
                nextTick = Time.time + tickSeconds;
                Soak(world);
            }
        }

        void OnDisable() { if (active) End(); }

        void Begin()
        {
            active = true;
            content = streamer.content;
            sfx = streamer.World.sfx;
            began = Time.time;
            offset = 0;
            Strikes = Doused = 0;
            flashAt = -99;
            var (wx, wy) = WorldWeather.WindOf(streamer.Seed, streamer.Map.Rules.Weather);
            wind = new Vector2((float)wx, (float)wy);
            nextStrike = Time.time + strikeEvery.x;
            if (!holder)
            {
                holder = new GameObject("Rain clouds").transform;
                holder.SetParent(transform, false);
            }
            // Own copies: the fog they take follows the scene's.
            if (!cloudMaterial && content && content.cloudMaterial) cloudMaterial = new Material(content.cloudMaterial) { name = "Rain clouds" };
            if (!veilMaterial && content && content.rainVeil) veilMaterial = new Material(content.rainVeil) { name = "Rain veils" };
            if (!veilMesh) veilMesh = Quad();
        }

        /// <summary>A unit quad standing on its bottom edge, facing +Z.</summary>
        static Mesh Quad()
        {
            var mesh = new Mesh { name = "Rain veil" };
            mesh.vertices = new[] { new Vector3(-.5f, 0, 0), new Vector3(.5f, 0, 0), new Vector3(-.5f, 1, 0), new Vector3(.5f, 1, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        void End()
        {
            active = false;
            if (sfx) { sfx.StopLoop(this); sfx.StopLoop(transform); }
            sfx = null;
            foreach (var cloud in clouds.Values) Release(cloud);
            clouds.Clear();
            thunders.Clear();
            Falling = Pouring = Snowing = Gloom = StormGloom = 0;
        }

        // ---------- the clouds over the fronts ----------

        void Overhead()
        {
            var rules = streamer.Map.Rules;
            var o = Origin;
            float t = Clock;
            var drift = wind * t;
            var cam = streamer.World.viewCamera ? streamer.World.viewCamera.transform.position : streamer.player.position;
            float fog = RenderSettings.fog ? RenderSettings.fogDensity : 0;
            if (cloudMaterial) cloudMaterial.SetFloat(FogDensity, fog);
            if (veilMaterial) veilMaterial.SetFloat(FogDensity, fog);
            // The clouds live in the frame the wind carries: each keeps its cell while it drifts on.
            float ux = cam.x - o.x - drift.x, uy = cam.z - o.z - drift.y;
            int ci = Mathf.FloorToInt(ux / cloudCell), cj = Mathf.FloorToInt(uy / cloudCell), r = Mathf.CeilToInt(cloudReach / cloudCell);
            foreach (var cloud in clouds.Values) cloud.seen = false;
            int seed = streamer.Seed;
            for (int i = ci - r; i <= ci + r; i++)
                for (int j = cj - r; j <= cj + r; j++)
                {
                    float hx = (i + .2f + .6f * (float)Hash.Unit(seed, 131, i, j)) * cloudCell;
                    float hy = (j + .2f + .6f * (float)Hash.Unit(seed, 132, i, j)) * cloudCell;
                    float dx = hx - ux, dy = hy - uy;
                    if (dx * dx + dy * dy > cloudReach * cloudReach) continue;
                    float x = hx + drift.x, y = hy + drift.y;
                    var sky = WorldWeather.At(rules, seed, x, y, t);
                    float want = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.04f, .5f, sky.Rain));
                    clouds.TryGetValue((i, j), out var cloud);
                    if (cloud == null)
                    {
                        if (want <= 0) continue;
                        cloud = Take();
                        clouds[(i, j)] = cloud;
                        cloud.home = new Vector2(hx, hy);
                        cloud.size = cloudCell * (.7f + .35f * (float)Hash.Unit(seed, 133, i, j));
                        cloud.thick = 7 + 5 * (float)Hash.Unit(seed, 134, i, j);
                        // Lumps on the cloud, so it billows rather than lies flat.
                        for (int p = 0; p < cloud.puffs.Length; p++)
                        {
                            float a = (float)Hash.Unit(seed, 137 + p, i, j) * Mathf.PI * 2, off = .35f + .2f * (float)Hash.Unit(seed, 139 + p, i, j);
                            cloud.puffs[p].localPosition = new Vector3(Mathf.Cos(a) * off, .3f + .25f * p, Mathf.Sin(a) * off);
                            cloud.puffs[p].localScale = Vector3.one * (.5f + .15f * (float)Hash.Unit(seed, 141 + p, i, j));
                        }
                        cloud.lift = 5 * (float)Hash.Unit(seed, 135, i, j) - 2.5f;
                        cloud.yaw = 360 * (float)Hash.Unit(seed, 136, i, j);
                        var plan = streamer.Map.Chunk(streamer.CoordAt(new Vector3(o.x + x, 0, o.z + y)));
                        cloud.ground = o.y + plan.BaseLevel * rules.PlateauHeight;
                        cloud.cold = plan.BaseLevel >= 3;
                    }
                    cloud.seen = true;
                    cloud.want = want;
                    cloud.rain = sky.Rain;
                    cloud.storm = sky.Storm;
                }
            float step = Time.deltaTime / cloudGrow;
            float gloom = 0, stormGloom = 0;
            rainy.Clear();
            stormy.Clear();
            gone.Clear();
            foreach (var pair in clouds)
            {
                var cloud = pair.Value;
                if (!cloud.seen) cloud.want = 0;
                cloud.grow = Mathf.MoveTowards(cloud.grow, cloud.want, step);
                if (cloud.grow <= 0 && cloud.want <= 0) { gone.Add(pair.Key); continue; }
                var at = new Vector3(o.x + cloud.home.x + drift.x, o.y + cloudHeight + cloud.lift, o.z + cloud.home.y + drift.y);
                float k = Ease(cloud.grow);
                cloud.transform.position = at;
                cloud.transform.localRotation = Quaternion.Euler(0, cloud.yaw, 0);
                cloud.transform.localScale = new Vector3(cloud.size * Mathf.Lerp(.35f, 1, k), cloud.thick * Mathf.Lerp(.2f, 1, k), cloud.size * Mathf.Lerp(.35f, 1, k));
                tint.Clear();
                tint.SetColor(BaseColor, Color.Lerp(cloudColour, stormColour, cloud.storm));
                foreach (var renderer in cloud.renderers) renderer.SetPropertyBlock(tint);
                float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(cam.x, cam.z));
                float over = k * Mathf.Clamp01(1 - (d - cloud.size * .5f) / 70);
                gloom = Mathf.Max(gloom, over);
                stormGloom = Mathf.Max(stormGloom, over * cloud.storm);
                if (!cloud.cold && cloud.rain > .3f) rainy.Add((d, cloud));
                if (cloud.storm > .3f && d >= strikeDistance.x && d <= strikeDistance.y) stormy.Add(cloud);
            }
            foreach (var key in gone) { Release(clouds[key]); clouds.Remove(key); }
            Gloom = gloom;
            StormGloom = stormGloom;
            Hang(cam);
        }

        static float Ease(float k) => k * k * (3 - 2 * k);

        Cloud Take()
        {
            if (spareClouds.Count > 0)
            {
                var spare = spareClouds.Pop();
                spare.transform.gameObject.SetActive(true);
                spare.grow = 0;
                return spare;
            }
            var cloud = new Cloud { puffs = new Transform[2], renderers = new MeshRenderer[3] };
            var mesh = content ? content.cloudMesh : null;
            (cloud.transform, cloud.renderers[0]) = Renderer("Cloud", mesh, cloudMaterial);
            for (int p = 0; p < cloud.puffs.Length; p++)
            {
                (cloud.puffs[p], cloud.renderers[p + 1]) = Renderer("Puff", mesh, cloudMaterial);
                cloud.puffs[p].SetParent(cloud.transform, false);
            }
            (cloud.veil, cloud.veilRenderer) = Renderer("Rain", veilMesh, veilMaterial);
            cloud.veil.gameObject.SetActive(false);
            return cloud;
        }

        (Transform, MeshRenderer) Renderer(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(holder, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return (go.transform, renderer);
        }

        void Release(Cloud cloud)
        {
            if (!cloud.transform) return;
            cloud.transform.gameObject.SetActive(false);
            if (cloud.veil) cloud.veil.gameObject.SetActive(false);
            spareClouds.Push(cloud);
        }

        // ---------- rain hanging from the clouds ----------

        /// <summary>A veil of rain under the nearest raining clouds, turned to the camera.</summary>
        void Hang(Vector3 cam)
        {
            rainy.Sort((a, b) => a.distance.CompareTo(b.distance));
            int shown = 0;
            for (int i = 0; i < rainy.Count; i++)
            {
                var cloud = rainy[i].cloud;
                if (shown >= maxVeils || !cloud.veil) continue;
                cloud.hung = true;
                shown++;
                var veil = cloud.veil;
                if (!veil.gameObject.activeSelf) veil.gameObject.SetActive(true);
                var top = cloud.transform.position.y - cloud.thick * .25f;
                var at = new Vector3(cloud.transform.position.x, cloud.ground - 1, cloud.transform.position.z);
                var toward = cam - at;
                toward.y = 0;
                veil.SetPositionAndRotation(at, toward.sqrMagnitude > .01f ? Quaternion.LookRotation(toward) : Quaternion.identity);
                veil.localScale = new Vector3(cloud.size * 1.1f * Ease(cloud.grow), Mathf.Max(1, top - at.y), 1);
                tint.Clear();
                tint.SetColor(BaseColor, Color.Lerp(veilColour, stormVeilColour, cloud.storm));
                tint.SetFloat(Amount, Mathf.Clamp01((cloud.rain - .3f) / .4f) * cloud.grow);
                cloud.veilRenderer.SetPropertyBlock(tint);
            }
            // The rest (no longer raining, or further than the nearest) hang none.
            foreach (var cloud in clouds.Values)
            {
                if (!cloud.hung && cloud.veil && cloud.veil.gameObject.activeSelf) cloud.veil.gameObject.SetActive(false);
                cloud.hung = false;
            }
            Veils = shown;
        }

        // ---------- the rain round the camera: sound and the air ----------

        void Air(Vector3 cam)
        {
            var sky = SkyAt(cam);
            bool cold = Cold(cam);
            bool roof = sky.Rain > .02f && Sheltered(cam + Vector3.up * .3f, streamer.player);
            float ease = 1 - Mathf.Exp(-Time.deltaTime / 1.2f);
            Falling = Mathf.Lerp(Falling, cold || roof ? 0 : sky.Rain, ease);
            Pouring = Mathf.Lerp(Pouring, cold || roof ? 0 : sky.Storm, ease);
            Snowing = Mathf.Lerp(Snowing, cold ? sky.Rain : 0, ease);
            if (!sfx || !content) return;
            // Heard under a roof too, only muffled.
            float heard = Mathf.Lerp(Falling, cold ? 0 : sky.Rain * .45f, roof ? 1 : 0);
            float light = heard * (1 - .6f * Pouring), heavy = Mathf.Max(Pouring, roof && !cold ? sky.Storm * .45f : 0);
            if (light > .02f) sfx.Loop(this, content.rainLoop, cam, light); else sfx.StopLoop(this);
            if (heavy > .02f) sfx.Loop(transform, content.downpourLoop, cam, heavy); else sfx.StopLoop(transform);
        }

        // ---------- thunder and lightning ----------

        void Thunder(ExpeditionWorld world, Vector3 cam)
        {
            for (int i = thunders.Count - 1; i >= 0; i--)
            {
                if (Time.time < thunders[i].at) continue;
                if (content && content.thunder) world.Play(content.thunder, thunders[i].where, thunders[i].volume, Random.Range(.9f, 1.08f));
                thunders.RemoveAt(i);
            }
            if (stormy.Count == 0 || Time.time < nextStrike) return;
            float storm = 0;
            foreach (var cloud in stormy) storm = Mathf.Max(storm, cloud.storm);
            nextStrike = Time.time + Mathf.Lerp(strikeEvery.y, strikeEvery.x, storm) * Random.Range(.7f, 1.3f);
            Strike(stormy[Random.Range(0, stormy.Count)], cam);
        }

        /// <summary>Lightning out of a thunder cloud to the ground under it; the thunder follows by the distance.</summary>
        void Strike(Cloud cloud, Vector3 cam)
        {
            var top = cloud.transform.position + new Vector3(Random.Range(-.3f, .3f), -.25f, Random.Range(-.3f, .3f)) * cloud.size;
            var ground = new Vector3(top.x, cloud.ground, top.z);
            int n = Physics.RaycastNonAlloc(top, Vector3.down, hits, 90, ~((1 << 2) | (1 << 4)), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
                if (hits[i].distance < best) { best = hits[i].distance; ground = hits[i].point; }
            Strikes++;
            float d = Vector3.Distance(cam, ground);
            flashAt = Time.time;
            flashPower = Mathf.Lerp(1, .45f, Mathf.InverseLerp(strikeDistance.x, strikeDistance.y, d));
            if (content && content.lightning)
            {
                var bolt = Instantiate(content.lightning, top, Quaternion.identity, holder);
                bolt.points = 22;
                bolt.jitter = boltJitter;
                bolt.width = boltWidth;
                bolt.lifetime = .5f;
                bolt.flickerSeconds = .06f;
                bolt.Strike(top, ground);
            }
            thunders.Add((Time.time + d / soundSpeed, ground, Mathf.Lerp(1, .55f, Mathf.InverseLerp(strikeDistance.x, strikeDistance.y, d))));
        }

        /// <summary>A flash and a second, weaker one a moment later.</summary>
        static float FlashAt(float age)
        {
            if (age < 0 || age > .8f) return 0;
            float first = Mathf.Exp(-age / .08f), second = age > .14f ? .65f * Mathf.Exp(-(age - .14f) / .07f) : 0;
            return Mathf.Clamp01(first + second);
        }

        // ---------- what the rain does ----------

        void Soak(ExpeditionWorld world)
        {
            var weather = streamer.Map.Rules.Weather;
            fighters.Clear();
            world.Combatants(fighters);
            foreach (var fighter in fighters)
            {
                if (fighter == null || !fighter.Root) continue;
                var at = fighter.Centre;
                if (!RainsAt(at) || Sheltered(at, fighter.Root)) continue;
                bool player = ReferenceEquals(fighter, world.Player);
                bool wasWet = player && WetStatus.IsWet(fighter.Root);
                WetStatus.Soak(fighter.Root, world, WetStatus.DefaultLingerSeconds);
                if (player && !wasWet && content && content.wetBadge) PlayerStatusBadge.Show(world, content.wetBadge);
                if (fighter is WorldCreature creature && creature.Douse()) Doused++;
            }
            washables.Clear();
            world.Zones.OfType(washables);
            foreach (var washable in washables)
            {
                if (!(washable is ICardZone zone)) continue;
                var bounds = zone.Bounds;
                var top = new Vector3(bounds.center.x, bounds.max.y + .05f, bounds.center.z);
                if (!RainsAt(top) || Sheltered(top, (washable as Component)?.transform)) continue;
                // Not a card's cloud: the washables take any rain the same (tar goes whole, fire goes out).
                washable.Wash(null);
            }
        }
    }
}
