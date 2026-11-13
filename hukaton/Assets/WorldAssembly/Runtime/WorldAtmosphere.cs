using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// The air of the endless world changes with the place (Kayospace «Открытый мир v2»: атмосфера по местам): fog
    /// gathers over water and thickens in the bog and under trees, thins on the heights where the wind blows, dust
    /// hangs at the bottom of pits, and away from the start the rain fronts of <see cref="WorldRain"/> go over: the air
    /// darkens as their clouds come near, under them it rains (snow on the summits) and lightning lights it up. The
    /// chunks round the player each add their mood, weighted by how near they are; everything eases over a few
    /// seconds. The scene's own fog, ambient light and sun come back when the outing ends.
    /// </summary>
    [RequireComponent(typeof(WorldStreamer))]
    public sealed class WorldAtmosphere : MonoBehaviour
    {
        [Serializable]
        public sealed class Mood
        {
            public string name;
            [Tooltip("Fog density times this.")]
            [Min(.1f)] public float fog = 1;
            [Tooltip("The fog's colour moves this far towards Tint.")]
            public Color tint = Color.white;
            [Range(0, 1)] public float tintAmount;
            [Tooltip("Sunlight times this.")]
            [Min(0)] public float sun = 1;
            [Tooltip("Ambient light times this.")]
            [Min(0)] public float ambient = 1;
            [Tooltip("Particles that drift round the player while this mood holds (rain, leaves, dust …).")]
            public GameObject effect;
            [Tooltip("Where the effect hangs relative to the camera, m.")]
            public Vector3 effectOffset = new Vector3(0, 8, 0);
            [Tooltip("Spread of the effect round the player, m (0: as the prefab has it).")]
            [Min(0)] public float effectRadius;
            [Tooltip("Particles a second at full strength (0: as the prefab has it).")]
            [Min(0)] public float effectRate;

            public Mood(string name, float fog, Color tint, float tintAmount, float sun, float ambient)
            {
                this.name = name; this.fog = fog; this.tint = tint; this.tintAmount = tintAmount; this.sun = sun; this.ambient = ambient;
            }
        }

        public const string Water = "water", Swamp = "swamp", Forest = "forest", High = "high", Deep = "deep", Rain = "rain", Storm = "storm", Snow = "snow";
        /// <summary>The swamp's parts and its fog banks (inside one the world is white).</summary>
        public const string Drowned = "drowned", Burnt = "burnt", Moss = "moss", Reach = "reach", Mist = "mist";

        /// <summary>Moods added after scenes were saved: put in at start if the scene's list lacks them.</summary>
        static Mood[] Later => new[]
        {
            new Mood(Drowned, 1.55f, new Color(.27f, .35f, .31f), .45f, .55f, .78f),
            new Mood(Burnt, 1.25f, new Color(.5f, .47f, .44f), .55f, .8f, .9f),
            new Mood(Moss, .8f, new Color(.55f, .62f, .48f), .25f, 1.05f, 1.05f),
            new Mood(Reach, .85f, new Color(.6f, .66f, .6f), .2f, 1, 1),
            new Mood(Mist, 6f, new Color(.74f, .79f, .74f), .8f, .85f, 1.05f),
        };

        readonly Dictionary<string, float> held = new Dictionary<string, float>();

        /// <summary>
        /// The forest and the swamp read apart (14.11): the forest's air is clear and cool teal far off, its light
        /// warm; the swamp's is thick olive murk (rot and marsh gas, not green), its light dim and flat. These set
        /// the moods over what the scene saved, so the two lands keep their looks wherever the moods were edited.
        /// </summary>
        void Retune()
        {
            void Set(string name, float fog, Color tint, float amount, float sunlight, float ambient)
            {
                var m = moods.Find(x => x.name == name);
                if (m == null) moods.Add(m = new Mood(name, fog, tint, amount, sunlight, ambient));
                m.fog = fog; m.tint = tint; m.tintAmount = amount; m.sun = sunlight; m.ambient = ambient;
            }
            Set(Swamp, 2.5f, new Color(.6f, .58f, .4f), .78f, .55f, .82f);
            Set(Drowned, 1.25f, new Color(.4f, .4f, .28f), .5f, .7f, .85f);
            Set(Moss, .78f, new Color(.66f, .65f, .48f), .3f, 1.1f, 1.08f);
            Set(Reach, .88f, new Color(.64f, .64f, .5f), .25f, 1.05f, 1);
            Set(Burnt, 1.15f, new Color(.55f, .5f, .45f), .55f, .85f, .92f);
            Set(Mist, 5.5f, new Color(.78f, .76f, .64f), .8f, .85f, 1.05f);
            // The deep forest: thicker, but its air stays cool and blue-green, never the swamp's olive.
            Set(Forest, 1.45f, new Color(.36f, .52f, .52f), .35f, .72f, .8f);
        }

        /// <summary>Holds a mood at <paramref name="k"/> from outside (SwampLife's fog banks) until it says otherwise.</summary>
        public void Hold(string mood, float k) => held[mood] = k;

        public List<Mood> moods = new List<Mood>
        {
            new Mood(Water, 1.3f, new Color(.56f, .69f, .76f), .25f, .95f, 1),
            new Mood(Swamp, 1.9f, new Color(.45f, .57f, .46f), .5f, .7f, .85f),
            new Mood(Forest, 1.5f, new Color(.4f, .52f, .44f), .35f, .7f, .78f),
            new Mood(High, .65f, new Color(.68f, .82f, .86f), .2f, 1.1f, 1.05f),
            new Mood(Deep, 1.3f, new Color(.5f, .54f, .53f), .3f, .8f, .8f),
            new Mood(Rain, 1.35f, new Color(.55f, .6f, .62f), .4f, .6f, .85f),
            new Mood(Storm, 1.7f, new Color(.47f, .51f, .54f), .55f, .45f, .75f),
            new Mood(Snow, 1.4f, new Color(.8f, .85f, .88f), .4f, .8f, 1),
        };

        [Tooltip("Seconds for the air to settle after the place changes.")]
        [Min(.1f)] public float settle = 2.5f;
        [Tooltip("A lightning flash: ambient light and sunlight times 1 + this at its brightest.")]
        public Vector2 flash = new Vector2(2.2f, 1.4f);

        [Header("Post-processing")]
        [Tooltip("The static location's look (WorldPP): a global volume switched on only while the endless world is out there.")]
        public Volume postProcess;

        [Header("Plants")]
        [Tooltip("The world's ground material: its biome patches (lush, dry, damp) colour the plants too.")]
        public Material groundMaterial;
        [Tooltip("How strongly plants take the colour of the patch they stand in (materials say how much each takes: Biome tint).")]
        [Range(0, 1)] public float plantTint = .75f;

        WorldStreamer streamer;
        WorldRain rain;
        FarSilhouettes silhouettes;
        bool active;
        float baseFog, baseAmbient, baseSun;
        Color baseColour;
        Light sun;
        readonly Dictionary<string, float> target = new Dictionary<string, float>();
        readonly Dictionary<string, float> current = new Dictionary<string, float>();
        readonly Dictionary<string, ParticleSystem[]> effects = new Dictionary<string, ParticleSystem[]>();
        readonly Dictionary<string, Transform> effectRoots = new Dictionary<string, Transform>();
        readonly Dictionary<ParticleSystem, float> baseRates = new Dictionary<ParticleSystem, float>();
        Transform holder;

        /// <summary>Strength of a mood round the player now (0…1), for tests and other effects.</summary>
        public float Strength(string mood) => current.TryGetValue(mood, out var k) ? k : 0;

        void Awake()
        {
            streamer = GetComponent<WorldStreamer>();
            // The rain goes with the air (added here for scenes made before it).
            rain = GetComponent<WorldRain>();
            if (!rain) rain = gameObject.AddComponent<WorldRain>();
            silhouettes = GetComponent<FarSilhouettes>();
            if (postProcess) postProcess.enabled = false;
            foreach (var mood in Later)
                if (!moods.Exists(m => m.name == mood.name)) moods.Add(mood);
            Retune();
            // The swamp's fog banks, fish, frogs and dragonflies go with the air too.
            if (!GetComponent<SwampLife>()) gameObject.AddComponent<SwampLife>();
        }

        void LateUpdate()
        {
            bool on = streamer.Active && streamer.player;
            if (on != active)
            {
                if (on) Begin(); else End();
            }
            if (!active) return;
            Sense();
            float ease = 1 - Mathf.Exp(-Time.deltaTime / settle);
            foreach (var mood in moods)
            {
                target.TryGetValue(mood.name, out float want);
                current.TryGetValue(mood.name, out float now);
                current[mood.name] = Mathf.Lerp(now, want, ease);
            }
            Apply();
        }

        void OnDisable() { if (active) End(); }

        void Begin()
        {
            active = true;
            if (postProcess) postProcess.enabled = true;
            baseFog = RenderSettings.fogDensity;
            baseColour = RenderSettings.fogColor;
            baseAmbient = RenderSettings.ambientIntensity;
            sun = RenderSettings.sun;
            baseSun = sun ? sun.intensity : 1;
            current.Clear();
            target.Clear();
            // Plants take the ground's patches (Hukaton/Forest Wind Lit, _BiomeTake per material).
            if (groundMaterial)
            {
                Shader.SetGlobalColor("_WorldBiomeA", groundMaterial.GetColor("_BiomeA"));
                Shader.SetGlobalColor("_WorldBiomeB", groundMaterial.GetColor("_BiomeB"));
                Shader.SetGlobalColor("_WorldBiomeC", groundMaterial.GetColor("_BiomeC"));
                Shader.SetGlobalFloat("_WorldBiomeScale", groundMaterial.GetFloat("_BiomeScale"));
                Shader.SetGlobalFloat("_WorldBiomeStrength", plantTint * groundMaterial.GetFloat("_BiomeStrength"));
            }
        }

        void End()
        {
            active = false;
            if (postProcess) postProcess.enabled = false;
            if (swampLook) swampLook.weight = 0;
            Grade(null);
            Shader.SetGlobalFloat(ReflectFog, 0);
            held.Clear();
            RenderSettings.fogDensity = baseFog;
            RenderSettings.fogColor = baseColour;
            RenderSettings.ambientIntensity = baseAmbient;
            if (sun) sun.intensity = baseSun;
            if (silhouettes) silhouettes.FogChanged();
            Shader.SetGlobalFloat("_WorldBiomeStrength", 0);
            foreach (var list in effects.Values)
                foreach (var ps in list)
                    if (ps) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            current.Clear();
        }

        // ---------- what the place is like ----------

        void Sense()
        {
            target.Clear();
            var map = streamer.Map;
            float size = map.Rules.ChunkSize, plateau = map.Rules.PlateauHeight;
            var origin = streamer.root ? streamer.root.position : transform.position;
            var at = streamer.player.position;
            var here = streamer.CoordAt(at);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var c = new ChunkCoord(here.X + dx, here.Y + dy);
                    var plan = map.Chunk(c);
                    var centre = origin + new Vector3(c.X * size, plan.BaseLevel * plateau, c.Y * size);
                    float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(centre.x, centre.z));
                    float near = Mathf.Clamp01(1.3f - d / size);
                    if (near <= 0) continue;
                    foreach (var (mood, k) in MoodsOf(plan)) Raise(mood, k * near);
                }
            // The chunk the player stands in: height over its ground.
            var own = map.Chunk(here);
            float ground = origin.y + own.BaseLevel * plateau;
            Raise(High, own.BaseLevel >= 3 ? 1 : own.BaseLevel == 2 ? .55f : 0);
            Raise(High, Mathf.Clamp01((at.y - ground - 5) / 4) * .7f);
            Raise(Deep, Mathf.Clamp01((ground - 1.5f - at.y) / 2.5f));
            // Rain fronts go over with the wind: the air greys as their clouds come near and under them it rains
            // (thunderstorms far out); on the summits it snows.
            var sky = rain.SkyAt(at);
            bool cold = own.BaseLevel >= 3;
            Raise(cold ? Snow : Rain, Mathf.Max(sky.Rain, rain.Gloom * .6f));
            if (!cold) Raise(Storm, Mathf.Max(sky.Storm, rain.StormGloom * .5f));
            foreach (var kv in held) Raise(kv.Key, kv.Value);
        }

        void Raise(string mood, float k)
        {
            if (k <= 0) return;
            target.TryGetValue(mood, out float was);
            target[mood] = Mathf.Max(was, Mathf.Clamp01(k));
        }

        /// <summary>The moods a chunk brings, by its kind and what stands on it.</summary>
        static IEnumerable<(string, float)> MoodsOf(ChunkPlan plan)
        {
            // The rotten swamp: thick green air over all of it, whatever stands on the chunk; its parts each their own.
            if (plan.Biome == Biome.Swamp)
            {
                yield return (Swamp, 1);
                switch (plan.Zone)
                {
                    case SwampZone.Drowned: yield return (Drowned, 1); break;
                    case SwampZone.Burnt: yield return (Burnt, 1); break;
                    case SwampZone.Moss: yield return (Moss, 1); break;
                    case SwampZone.Reach: yield return (Reach, 1); break;
                }
            }
            switch (plan.Shape.Kind)
            {
                case ShapeKind.River:
                    yield return (Water, 1);
                    break;
                case ShapeKind.Cliff when plan.Variant.Id == "falls":
                    yield return (Water, .5f);
                    break;
                case ShapeKind.Maze when plan.Forest:
                    // Under the giant trees: the deepest forest air there is.
                    yield return (Forest, 1);
                    break;
                case ShapeKind.Plain:
                case ShapeKind.PlateauTop:
                    if (plan.Variant.Id == "pond") yield return (Water, .6f);
                    if (plan.Variant.Id == "grove") yield return (Forest, .6f);
                    break;
                case ShapeKind.Landmark:
                {
                    var (kind, _, _) = Landmarks.Parse(plan.Shape.Id);
                    switch (kind.Id)
                    {
                        case "swamp": yield return (Swamp, 1); break;
                        case "lake": yield return (Water, 1); break;
                        case "giant-log": case "stone-forest": yield return (Forest, 1); break;
                    }
                    break;
                }
            }
        }

        // ---------- the air ----------

        Volume swampLook;

        static readonly int ReflectFog = Shader.PropertyToID("_WorldReflectFog");
        static readonly int GradeOn = Shader.PropertyToID("_WorldGradeOn"), GradeFilter = Shader.PropertyToID("_WorldGradeFilter"), GradeSaturation = Shader.PropertyToID("_WorldGradeSaturation");

        /// <summary>
        /// The place's colour grading for what is drawn after the world's post-processing (World Resolution: water,
        /// ground fog, clouds, rain), as shader globals (Assets/WorldAssembly/Shaders/WorldGrade.hlsl).
        /// </summary>
        static void Grade(Volume look)
        {
            float w = look ? look.weight : 0;
            if (w <= .001f || !look.sharedProfile || !look.sharedProfile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var colour))
            {
                Shader.SetGlobalFloat(GradeOn, 0);
                return;
            }
            Shader.SetGlobalFloat(GradeOn, w);
            Shader.SetGlobalColor(GradeFilter, colour.colorFilter.overrideState ? colour.colorFilter.value : Color.white);
            Shader.SetGlobalFloat(GradeSaturation, colour.saturation.overrideState ? 1 + colour.saturation.value / 100 : 1);
        }

        void Apply()
        {
            // The swamp's own colour (WorldContent.swampLook) as strong as the swamp round the player.
            var look = streamer.content ? streamer.content.swampLook : null;
            if (look && !swampLook)
            {
                var go = new GameObject("Swamp look");
                go.transform.SetParent(transform, false);
                swampLook = go.AddComponent<Volume>();
                swampLook.isGlobal = true;
                swampLook.priority = 10;
                swampLook.sharedProfile = look;
            }
            if (swampLook) swampLook.weight = Strength(Swamp);
            Grade(swampLook);
            float fog = 1, sunlight = 1, ambient = 1;
            var colour = baseColour;
            foreach (var mood in moods)
            {
                float k = Strength(mood.name);
                if (k > .001f)
                {
                    fog *= Mathf.Lerp(1, mood.fog, k);
                    sunlight *= Mathf.Lerp(1, mood.sun, k);
                    ambient *= Mathf.Lerp(1, mood.ambient, k);
                    colour = Color.Lerp(colour, mood.tint, mood.tintAmount * k);
                }
                Drift(mood, Falling(mood.name, k));
            }
            // Lightning lights the land for a moment.
            float lit = rain.Flash;
            if (lit > 0)
            {
                ambient *= 1 + flash.x * lit;
                sunlight *= 1 + flash.y * lit;
                colour = Color.Lerp(colour, new Color(.86f, .89f, .96f), .35f * lit);
            }
            RenderSettings.fogDensity = baseFog * fog;
            Shader.SetGlobalFloat(ReflectFog, Mathf.Clamp01((fog - 1) / 1.2f));
            RenderSettings.fogColor = colour;
            RenderSettings.ambientIntensity = baseAmbient * ambient;
            if (sun) sun.intensity = baseSun * sunlight;
            if (silhouettes) silhouettes.FogChanged();
        }

        /// <summary>
        /// How much of a mood's particles fall round the camera: the rain, the downpour and the snow only where they
        /// fall (not under a roof, not while their clouds are only coming near); the rest by the mood's strength.
        /// </summary>
        float Falling(string mood, float k) => mood == Rain ? rain.Falling : mood == Storm ? rain.Pouring : mood == Snow ? rain.Snowing : k;

        /// <summary>A mood's particles follow the camera and grow with its strength; they stop when it fades.</summary>
        void Drift(Mood mood, float k)
        {
            if (!mood.effect) return;
            effects.TryGetValue(mood.name, out var systems);
            if (systems == null)
            {
                if (k < .02f) return;
                if (!holder)
                {
                    holder = new GameObject("Weather").transform;
                    holder.SetParent(transform, false);
                }
                var go = Instantiate(mood.effect, holder);
                go.name = mood.name + " " + mood.effect.name;
                systems = go.GetComponentsInChildren<ParticleSystem>();
                foreach (var ps in systems)
                {
                    var main = ps.main;
                    // Particles stay where they fell while the player walks on.
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    if (mood.effectRadius > 0)
                    {
                        var shape = ps.shape;
                        shape.radius = mood.effectRadius;
                        shape.scale = Vector3.one;
                    }
                    var emission = ps.emission;
                    baseRates[ps] = mood.effectRate > 0 ? mood.effectRate : emission.rateOverTimeMultiplier;
                }
                effects[mood.name] = systems;
                effectRoots[mood.name] = go.transform;
            }
            var cam = streamer.World ? streamer.World.viewCamera : null;
            var anchor = cam ? cam.transform.position : streamer.player.position;
            foreach (var ps in systems)
            {
                if (!ps) continue;
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = baseRates[ps] * k;
                if (k > .02f && !ps.isEmitting) ps.Play(false);
                if (k <= .02f && ps.isEmitting) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            if (effectRoots.TryGetValue(mood.name, out var root) && root) root.position = anchor + mood.effectOffset;
        }
    }
}
