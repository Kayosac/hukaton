using System.Collections;
using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// The scripted little events on the walk to the god (docs/level-design/god-mountains, chapter 6) and the breath of
    /// the mountain that runs under all of them (S2): one looped breath (in 3 s, out 4 s), quiet in the pocket, loudest in
    /// the throat, muffled behind the stone frame (a pre-filtered copy, the web has no live filters), cut short before
    /// the mouth (S7, a few seconds of silence) and back as the god's own breath on the summit. On the out-breath in
    /// Z1–Z3 grit trickles from the roof. The one-off events (<see cref="GodMoment"/>) sit on this object and fire by the
    /// metres the player has walked along the route. Nothing here hurts the player or blocks the way; it all plays the
    /// same in the game and with the scene opened alone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodMoments : MonoBehaviour
    {
        [Header("Level")]
        public GodEncounter encounter;
        public GodMountainsAtmosphere atmosphere;
        [Tooltip("Mark S3: the hanging slab of the third corner is the one nearest to it.")]
        public Transform thirdCorner;

        [Header("Breath of the mountain (S2)")]
        public AudioClip breath;
        public AudioClip breathMuffled;
        [Tooltip("Seconds into the breath clip where the out-breath starts.")]
        public float exhaleAt = 3f;
        [Tooltip("Breath volume along the route (metres → volume).")]
        public AnimationCurve breathByMetres = new AnimationCurve(
            new Keyframe(0, .22f), new Keyframe(40, .35f), new Keyframe(80, .6f), new Keyframe(92, .65f), new Keyframe(97, 0));
        [Tooltip("The muffled breath behind the frame (metres → volume).")]
        public AnimationCurve muffledByMetres = new AnimationCurve(
            new Keyframe(94, 0), new Keyframe(98, .55f), new Keyframe(150, .7f), new Keyframe(161, .85f));
        [Tooltip("The breath stops here (S7): silence for Silence Seconds.")]
        public float silenceAt = 161.5f;
        [Min(0)] public float silenceSeconds = 3.5f;
        [Tooltip("From here on the summit the breath comes back, slower and from far away: the god's chest.")]
        public float summitAt = 171f;
        [Range(0, 1)] public float summitVolume = .45f;
        [Range(.5f, 1.5f)] public float summitPitch = .88f;

        [Header("Wind")]
        public AudioClip wind;
        public AnimationCurve windByMetres = new AnimationCurve(
            new Keyframe(94, 0), new Keyframe(100, .35f), new Keyframe(125, .45f), new Keyframe(150, .4f), new Keyframe(160, .55f));
        [Range(0, 1)] public float summitWind = .3f;

        [Header("Grit on the out-breath (Z1–Z3)")]
        public Vector2 gritBetweenMetres = new Vector2(8, 92);
        [Range(0, 1)] public float gritChance = .65f;
        public float gritShake = .06f;

        [Header("Sounds")]
        public SfxCue crumble;
        public SfxCue rockImpact;
        public SfxCue rumble;
        public SfxCue gust;
        public SfxCue crash;
        public AudioClip groan;
        public AudioClip thud;
        [Tooltip("The player's own footsteps (the echo behind them).")]
        public AudioClip[] steps = new AudioClip[0];

        [Header("Effects (Retro Arsenal)")]
        public GameObject gritEffect;
        public GameObject impactEffect;
        public GameObject dustEffect;
        public GameObject draftEffect;
        public GameObject leavesEffect;
        [Tooltip("A stone of the kit for things that fall and roll.")]
        public GameObject rockModel;

        public bool Running { get; private set; }
        /// <summary>The furthest the player has come along the route, m (never jumps across walls).</summary>
        public float Progress { get; private set; }
        /// <summary>Where along the route the player stands now, m.</summary>
        public float Metres { get; private set; }
        public float RunningFor => Running ? Time.time - startedAt : 0;
        public Transform Player => encounter && encounter.Player ? encounter.Player.transform : null;
        public Transform Eye => atmosphere && atmosphere.viewer ? atmosphere.viewer : Player;

        AudioSource breathSource, mutedSource, windSource;
        float startedAt, heldUntil = -1, silentUntil = -1, windBoost, windBoostUntil;
        bool silenced;
        float lastBreathTime;
        bool gritThisBreath = true;
        CameraFeel feel;
        readonly List<float> routeMetres = new List<float>();

        void Start()
        {
            breathSource = Loop("Дыхание горы", breath);
            mutedSource = Loop("Дыхание за рамкой", breathMuffled);
            windSource = Loop("Ветер", wind);
        }

        AudioSource Loop(string label, AudioClip clip)
        {
            if (!clip) return null;
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0;
            source.volume = 0;
            source.playOnAwake = false;
            return source;
        }

        void Update()
        {
            if (!Running)
            {
                if (!encounter || !encounter.Ready || !Player || !atmosphere) return;
                Running = true;
                startedAt = Time.time;
                Metres = Progress = atmosphere.RouteMetres(Player.position);
                if (breathSource) breathSource.Play();
                if (mutedSource) { mutedSource.Play(); mutedSource.timeSamples = breathSource ? breathSource.timeSamples : 0; }
                if (windSource) windSource.Play();
            }
            if (!feel) feel = FindFirstObjectByType<CameraFeel>();
            float m = atmosphere.RouteMetres(Player.position);
            // only small steps count: the route folds, the nearest point can be across a wall
            if (Mathf.Abs(m - Metres) < 8 || encounter.Stage != GodStage.Path) Metres = m;
            Progress = Mathf.Max(Progress, Metres);
            UpdateSound();
            UpdateGrit();
        }

        void UpdateSound()
        {
            bool onPath = encounter.Stage == GodStage.Path && Progress < summitAt;
            if (!silenced && Progress >= silenceAt) { silenced = true; silentUntil = Time.time + silenceSeconds; }
            float breathVolume, mutedVolume, windVolume;
            if (onPath && !silenced)
            {
                breathVolume = breathByMetres.Evaluate(Metres);
                mutedVolume = muffledByMetres.Evaluate(Metres);
                windVolume = windByMetres.Evaluate(Metres);
            }
            else if (silenced && (Time.time < silentUntil || Progress < summitAt))
            {
                breathVolume = mutedVolume = 0;
                windVolume = Time.time < silentUntil ? 0 : summitWind * .6f;
            }
            else
            {
                // the summit: the god breathes, far and slow, and the open wind
                breathVolume = summitVolume;
                mutedVolume = 0;
                windVolume = summitWind;
                if (breathSource) breathSource.pitch = Mathf.MoveTowards(breathSource.pitch, summitPitch, Time.deltaTime * .05f);
            }
            if (Time.time < heldUntil) breathVolume = mutedVolume = 0;
            if (Time.time < windBoostUntil) windVolume = Mathf.Max(windVolume, windBoost);
            // hard cut into the silence, soft everywhere else
            float rate = silenced && Time.time < silentUntil ? 100 : Time.time < heldUntil ? 3 : .5f;
            Fade(breathSource, breathVolume, rate);
            Fade(mutedSource, mutedVolume, rate);
            Fade(windSource, windVolume, silenced && Time.time < silentUntil ? 100 : .35f);
        }

        static void Fade(AudioSource source, float target, float rate)
        {
            if (source) source.volume = Mathf.MoveTowards(source.volume, target, rate * Time.deltaTime);
        }

        void UpdateGrit()
        {
            if (!breathSource || !breathSource.isPlaying || encounter.Stage != GodStage.Path) return;
            // once per breath, just after the out-breath starts
            float t = breathSource.time;
            if (t < lastBreathTime) gritThisBreath = false;
            lastBreathTime = t;
            if (gritThisBreath || t < exhaleAt + .2f) return;
            gritThisBreath = true;
            if (Time.time < heldUntil || Metres < gritBetweenMetres.x || Metres > gritBetweenMetres.y) return;
            if (Random.value > gritChance) return;
            var at = Ceiling(RoutePoint(Progress + Random.Range(1f, 3.5f)) + Vector3.up * 1.2f, 6);
            if (at.HasValue)
            {
                Effect(gritEffect, at.Value, Quaternion.identity, Random.Range(.5f, .8f), 4);
                Sound(crumble, at.Value, .35f, Random.Range(.7f, .9f));
            }
            Shake(gritShake * Mathf.Lerp(.6f, 1.6f, Mathf.InverseLerp(gritBetweenMetres.x, gritBetweenMetres.y, Metres)));
        }

        // ---------- what the moments use ----------

        /// <summary>The breath stops for a while (under the hanging slab), then comes back.</summary>
        public void HoldBreath(float seconds) => heldUntil = Time.time + seconds;

        /// <summary>The wind swells for a while (a gust from behind).</summary>
        public void BoostWind(float volume, float seconds) { windBoost = volume; windBoostUntil = Time.time + seconds; }

        /// <summary>The point of the route at the given metres (on the walking line).</summary>
        public Vector3 RoutePoint(float metres)
        {
            var route = atmosphere.route;
            float walked = 0;
            for (int i = 0; i + 1 < route.Length; i++)
            {
                if (!route[i] || !route[i + 1]) continue;
                float length = Vector3.Distance(route[i].position, route[i + 1].position);
                if (walked + length >= metres) return Vector3.Lerp(route[i].position, route[i + 1].position, (metres - walked) / Mathf.Max(length, 1e-3f));
                walked += length;
            }
            return route.Length > 0 && route[route.Length - 1] ? route[route.Length - 1].position : transform.position;
        }

        /// <summary>The direction the route runs at the given metres.</summary>
        public Vector3 RouteForward(float metres)
        {
            var d = RoutePoint(metres + 1) - RoutePoint(metres - 1);
            d.y = 0;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
        }

        /// <summary>The roof right above a point (a hit within reach), or nothing under the open sky.</summary>
        public Vector3? Ceiling(Vector3 from, float reach)
        {
            if (Physics.Raycast(from, Vector3.up, out var hit, reach, ~(1 << 2), QueryTriggerInteraction.Ignore)) return hit.point - Vector3.up * .05f;
            return null;
        }

        public void Sound(SfxCue cue, Vector3 at, float volume = 1, float pitch = 1)
        {
            if (!cue) return;
            var clip = cue.Pick();
            if (!clip) return;
            float p = pitch * Random.Range(cue.pitch.x, cue.pitch.y);
            Clip(clip, at, cue.volume * volume, p, cue.spatialBlend, cue.minDistance, cue.maxDistance);
        }

        public void Clip(AudioClip clip, Vector3 at, float volume, float pitch = 1, float spatial = .85f, float min = 2, float max = 40)
        {
            if (!clip) return;
            var go = new GameObject(clip.name);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.volume = volume;
            s.pitch = pitch;
            s.spatialBlend = spatial;
            s.minDistance = min;
            s.maxDistance = max;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.Play();
            Destroy(go, clip.length / Mathf.Max(.1f, Mathf.Abs(pitch)) + .2f);
        }

        /// <summary>Camera shake that fades with distance from the source (full within 3 m, gone at 18 m).</summary>
        public void Shake(float amount, Vector3? source = null)
        {
            if (!feel) return;
            float k = source.HasValue && Eye ? 1 - Mathf.InverseLerp(3, 18, Vector3.Distance(source.Value, Eye.position)) : 1;
            feel.AddTrauma(amount * k);
        }

        /// <summary>A self-playing effect (Retro Arsenal), removed after it plays.</summary>
        public GameObject Effect(GameObject prefab, Vector3 at, Quaternion rotation, float scale = 1, float lifetime = 4)
        {
            if (!prefab) return null;
            var effect = Instantiate(prefab, at, rotation, transform);
            effect.transform.localScale *= scale;
            Destroy(effect, lifetime);
            return effect;
        }

        /// <summary>A loose stone of the kit that falls and tumbles; it settles into rubble and stops costing anything.</summary>
        public Rigidbody Stone(Vector3 at, float size)
        {
            if (!rockModel) return null;
            var rock = Instantiate(rockModel, at, Random.rotation, transform);
            rock.transform.localScale = Vector3.one * size;
            foreach (var filter in rock.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                var c = filter.gameObject.AddComponent<MeshCollider>();
                c.sharedMesh = filter.sharedMesh;
                c.convex = true;
            }
            var body = rock.AddComponent<Rigidbody>();
            body.mass = 60 * size * size * size;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return body;
        }

        public IEnumerator Settle(Rigidbody body, float after)
        {
            yield return new WaitForSeconds(after);
            if (body) { body.linearVelocity = body.angularVelocity = Vector3.zero; body.isKinematic = true; }
        }
    }
}
