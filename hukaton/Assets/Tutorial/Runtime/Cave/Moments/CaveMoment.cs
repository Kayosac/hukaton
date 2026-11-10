using System.Collections;
using Hakaton.Expeditions;
using Hakaton.Feel;
using Hakaton.ShopBlockout;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// A one-off scripted moment of the cave prologue: a rock from the ceiling, a candle put out by a drip, a draft.
    /// It fires once, when the player first reaches its trigger (and its extra condition holds); the cave scene is
    /// loaded fresh for every new game, so nothing needs resetting. Nothing here hurts the player or blocks the route.
    /// </summary>
    public abstract class CaveMoment : MonoBehaviour
    {
        [Header("Trigger")]
        [Tooltip("Fires when the player comes within the radius of this point (on the ground plane). Empty: no zone.")]
        public Transform trigger;
        [Min(0)] public float radius = 2.5f;

        public bool Fired { get; private set; }
        protected CaveLevel Level { get; private set; }
        protected ExpeditionWorld World { get; private set; }
        protected CaveTutorial Tutorial { get; private set; }
        protected Vector3 Player => World.motor.transform.position;
        protected Vector3 Eye => World.viewCamera.transform.position;

        /// <summary>Called by the level once the prologue has started (creatures spawned, player at the start).</summary>
        public void Begin(CaveLevel level, ExpeditionWorld world, CaveTutorial tutorial)
        {
            Level = level;
            World = world;
            Tutorial = tutorial;
            Prepare();
        }

        protected virtual void Prepare() { }

        /// <summary>Extra condition on top of the zone (an obstacle passed, a lesson reached).</summary>
        protected virtual bool Ready => true;

        bool InZone
        {
            get
            {
                if (!trigger) return true;
                Vector3 d = Player - trigger.position;
                return Mathf.Abs(d.y) < 3 && new Vector2(d.x, d.z).sqrMagnitude < radius * radius;
            }
        }

        void Update()
        {
            if (Fired || World == null || !World.InPrologue) return;
            if (Ready && InZone) Fire();
        }

        /// <summary>Plays the moment now (the trigger calls it; tests may call it directly).</summary>
        public void Fire()
        {
            if (Fired || World == null) return;
            Fired = true;
            StartCoroutine(Play());
        }

        protected abstract IEnumerator Play();

        // ---------- helpers ----------

        protected void Sound(SfxCue cue, Vector3 position, float volume = 1, float pitch = 1) => World.Play(cue, position, volume, pitch);

        /// <summary>Camera shake that fades with distance from the source (full within 3 m, gone at 18 m).</summary>
        protected void Shake(float amount, Vector3 source)
        {
            if (!World.feel) return;
            float k = 1 - Mathf.InverseLerp(3, 18, Vector3.Distance(source, Eye));
            World.feel.AddTrauma(amount * k);
        }

        /// <summary>A Retro Arsenal effect (or any self-playing prefab), lit like the cave and removed after it plays.</summary>
        protected GameObject Effect(GameObject prefab, Vector3 position, Quaternion rotation, float scale = 1, float lifetime = 4)
        {
            if (!prefab) return null;
            var effect = Instantiate(prefab, position, rotation, transform);
            effect.transform.localScale *= scale;
            CaveOnly(effect);
            Destroy(effect, lifetime);
            return effect;
        }

        /// <summary>Puts spawned things on the cave's rendering layer, so only the cave's own lights reach them.</summary>
        public static void CaveOnly(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.renderingLayerMask = 1u << 1;
        }

        /// <summary>A loose copy of a rock prefab that falls and tumbles (convex colliders, its own rigidbody).</summary>
        protected Rigidbody Rock(GameObject prefab, Vector3 position, float size, Transform parent)
        {
            var rock = Instantiate(prefab, position, Random.rotation, parent);
            rock.transform.localScale = Vector3.one * size;
            foreach (var mesh in rock.GetComponentsInChildren<MeshCollider>(true)) mesh.convex = true;
            if (rock.GetComponentsInChildren<Collider>(true).Length == 0) rock.AddComponent<SphereCollider>().radius = .45f;
            CaveOnly(rock);
            var body = rock.GetComponent<Rigidbody>();
            if (!body) body = rock.AddComponent<Rigidbody>();
            body.mass = 40 * size * size * size;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return body;
        }

        /// <summary>Freezes settled rocks in place: rubble stays rubble and costs nothing afterwards.</summary>
        protected static IEnumerator Settle(Rigidbody[] bodies, float after)
        {
            yield return new WaitForSeconds(after);
            foreach (var body in bodies)
                if (body) { body.linearVelocity = body.angularVelocity = Vector3.zero; body.isKinematic = true; }
        }

        // ---------- fire ----------

        /// <summary>The flame of a candle or lantern prefab: its light, its flicker and the glowing meshes.</summary>
        protected struct Flame
        {
            public Light light;
            public Flicker flicker;
            public Renderer[] glow;
            public float intensity;
            public Vector3 Position => light ? light.transform.position : Vector3.zero;

            public static Flame Of(Transform root)
            {
                var flame = new Flame { light = root.GetComponentInChildren<Light>(true) };
                flame.flicker = flame.light ? flame.light.GetComponent<Flicker>() : null;
                var glow = new System.Collections.Generic.List<Renderer>();
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    string n = renderer.name;
                    if (n.Contains("Flame") || n.EndsWith("_Light")) glow.Add(renderer);
                }
                flame.glow = glow.ToArray();
                flame.intensity = flame.light ? flame.light.intensity : 0;
                return flame;
            }
        }

        /// <summary>The flame shrinks and dies: light fades, glowing meshes go, a wisp of smoke rises.</summary>
        protected IEnumerator PutOut(Flame flame, GameObject smoke, float seconds = .35f)
        {
            if (!flame.light) yield break;
            if (flame.flicker) flame.flicker.enabled = false;
            float start = flame.light.intensity;
            var scales = new Vector3[flame.glow.Length];
            for (int i = 0; i < flame.glow.Length; i++) scales[i] = flame.glow[i].transform.localScale;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                // a last couple of gasps before it goes
                flame.light.intensity = start * (1 - k) * (.7f + .3f * Mathf.Sin(t * 70));
                for (int i = 0; i < flame.glow.Length; i++) flame.glow[i].transform.localScale = Vector3.Scale(scales[i], new Vector3(1 - k * .6f, 1 - k, 1 - k * .6f));
                yield return null;
            }
            flame.light.intensity = 0;
            flame.light.enabled = false;
            foreach (var glow in flame.glow) glow.enabled = false;
            Effect(smoke, flame.Position, Quaternion.identity, .5f, 5);
        }

        /// <summary>A gust bends the flame: hard flicker and a dip, then it recovers.</summary>
        protected IEnumerator Gust(Flame flame, float seconds, float depth)
        {
            if (!flame.light) yield break;
            if (flame.flicker) flame.flicker.enabled = false;
            float start = flame.light.intensity;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float envelope = Mathf.Sin(Mathf.PI * t / seconds);
                float nervous = Mathf.PerlinNoise(t * 18, flame.intensity) * 2 - 1;
                flame.light.intensity = start * Mathf.Max(.05f, 1 - envelope * depth + nervous * .35f * envelope);
                yield return null;
            }
            flame.light.intensity = start;
            if (flame.flicker) flame.flicker.enabled = true;
        }
    }
}
