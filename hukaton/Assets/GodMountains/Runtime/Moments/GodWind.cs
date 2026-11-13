using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// The wind on the white shelf (Z5, the walk along the edge of the drop): long thin streaks of wind that snake past
    /// the player, a few all the time and a whole bundle with every gust (the hub's gust sound and wind swell come with
    /// it). The wind blows along the path from behind and a little in off the open side; streaks die on rock.
    /// One particle system with trails, built at start; no prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodWind : MonoBehaviour
    {
        public GodMoments hub;
        [Tooltip("Unlit transparent material for the streaks (white; the particles' colour tints it).")]
        public Material streakMaterial;

        [Header("Where")]
        [Tooltip("Along the route, m: the white shelf.")]
        public Vector2 between = new Vector2(100.5f, 146);
        [Min(.1f)] public float fadeMetres = 5;

        [Header("Wind")]
        [Tooltip("How much the wind comes in off the open side (0 = straight along the path).")]
        [Range(0, 1)] public float fromTheDrop = .35f;
        [Min(1)] public float speed = 15;
        [Tooltip("Streaks a second between gusts.")]
        [Min(0)] public float steadyRate = 4;
        [Tooltip("Seconds between gusts.")]
        public Vector2 gustEvery = new Vector2(3.5f, 6.5f);
        public Vector2Int streaksPerGust = new Vector2Int(16, 24);
        [Min(0)] public float gustSpread = .6f;

        [Header("Look")]
        public Color color = new Color(1, 1, 1, .8f);
        [Min(.005f)] public float width = .11f;
        [Tooltip("Streak length, as a share of the particle's life.")]
        [Range(.05f, 1)] public float trail = .4f;
        [Min(.1f)] public float lifetime = 1.4f;
        [Tooltip("How much the streaks snake about.")]
        [Min(0)] public float wave = 1.4f;

        ParticleSystem system;
        float nextGust, steady;
        int gustLeft;
        float gustTick;

        void Start() => Build();

        /// <summary>Builds the streak system (also for the editor previews).</summary>
        public ParticleSystem Build()
        {
            if (system) return system;
            var go = new GameObject("Полосы ветра");
            go.transform.SetParent(transform, false);
            system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * .8f, lifetime * 1.2f);
            main.startSpeed = 0;
            main.startSize = .001f;
            main.startColor = color;
            main.maxParticles = 200;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = wave;
            noise.frequency = .35f;
            noise.scrollSpeed = .4f;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(1, .6f), new GradientAlphaKey(0, 1) });
            fade.color = g;

            var collision = system.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.lifetimeLoss = 1;
            collision.radiusScale = .1f;
            collision.collidesWith = ~(1 << 2);

            var trails = system.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1;
            trails.lifetime = trail;
            trails.minVertexDistance = .2f;
            trails.worldSpace = true;
            trails.dieWithParticles = true;
            trails.sizeAffectsWidth = false;
            trails.inheritParticleColor = true;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(width, new AnimationCurve(
                new Keyframe(0, .2f), new Keyframe(.35f, 1), new Keyframe(1, .15f)));
            var tg = new Gradient();
            tg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.9f, .4f), new GradientAlphaKey(0, 1) });
            trails.colorOverTrail = tg;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = streakMaterial;
            renderer.trailMaterial = streakMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.minParticleSize = 0;
            system.Play();
            return system;
        }

        void Update()
        {
            if (!hub || !hub.Running || !hub.Eye || !system) return;
            if (hub.encounter && hub.encounter.Stage != GodStage.Path) return;
            float m = hub.Metres;
            float w = Mathf.Clamp01((m - between.x) / fadeMetres) * Mathf.Clamp01((between.y - m) / fadeMetres);
            if (w <= 0) { nextGust = 0; return; }

            Vector3 eye = hub.Eye.position;
            Vector3 dir = Direction(m, eye);

            steady += steadyRate * w * Time.deltaTime;
            while (steady >= 1) { steady -= 1; Emit(eye, dir, 1); }

            if (nextGust <= 0) nextGust = Time.time + Random.Range(1f, 2f);
            if (Time.time >= nextGust)
            {
                nextGust = Time.time + Random.Range(gustEvery.x, gustEvery.y);
                gustLeft = Mathf.RoundToInt(Random.Range(streaksPerGust.x, streaksPerGust.y + 1) * w);
                gustTick = 0;
                hub.Sound(hub.gust, eye - dir * 4, Mathf.Lerp(.5f, 1, w), Random.Range(.65f, .8f));
                hub.BoostWind(Mathf.Lerp(.5f, .9f, w), 2.2f);
            }
            // a gust's streaks come in over half a second, not all in one frame
            if (gustLeft > 0)
            {
                gustTick += Time.deltaTime * 30;
                while (gustTick >= 1 && gustLeft > 0) { gustTick -= 1; gustLeft--; Emit(eye, dir, 1.25f); }
            }
        }

        /// <summary>Along the path from behind, in a little off the open side of the shelf.</summary>
        public Vector3 Direction(float metres, Vector3 eye)
        {
            Vector3 along = hub.RouteForward(metres);
            along.y = 0;
            along.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, along);
            // the open side: the one with no rock close by
            bool rightOpen = !hub.Wall(eye, side, 6, out _, out _);
            bool leftOpen = !hub.Wall(eye, -side, 6, out _, out _);
            Vector3 fromDrop = rightOpen == leftOpen ? Vector3.zero : rightOpen ? -side : side;
            return (along + fromDrop * fromTheDrop + Vector3.down * .04f).normalized;
        }

        /// <summary>One streak, starting upwind of the player so it runs through the view.</summary>
        public void Emit(Vector3 eye, Vector3 dir, float boost)
        {
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 at = eye - dir * Random.Range(5f, 9f) + dir * Random.Range(0, 12f)
                         + side * (Random.Range(.9f, 3.8f) * (Random.value < .5f ? -1 : 1)) + Vector3.up * Random.Range(-1.2f, 2.2f);
            var p = new ParticleSystem.EmitParams
            {
                position = at,
                velocity = (dir + Random.insideUnitSphere * .06f * gustSpread) * speed * Random.Range(.85f, 1.15f) * boost,
                startLifetime = lifetime * Random.Range(.8f, 1.2f),
                startColor = color,
                startSize = .001f,
            };
            system.Emit(p, 1);
        }

        void OnDisable()
        {
            if (system) system.Clear(true);
        }
    }
}
