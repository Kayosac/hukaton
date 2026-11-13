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
        [Tooltip("Pixel dust clouds, big to tiny: where stones hit, walls shed, the draft comes through.")]
        public GameObject smokeBig, smokeMedium, smokeSmall, smokeTiny, smokeFlat;
        [Tooltip("Rock dust: the pixel clouds are tinted to it (pale, see-through), not their own dark grey.")]
        public Color dustColor = new Color(.8f, .72f, .82f, .55f);
        [Tooltip("Cloud from the white shelf.")]
        public Color cloudColor = new Color(.9f, .88f, .94f, .22f);
        [Tooltip("A stone of the kit for things that fall and roll (fallback when Rock Models is empty).")]
        public GameObject rockModel;
        [Tooltip("Stones of the kit that fall and roll; each is scaled to the size asked.")]
        public GameObject[] rockModels = new GameObject[0];

        public bool Running { get; private set; }
        /// <summary>The furthest the player has come along the route, m (never jumps across walls).</summary>
        public float Progress { get; private set; }
        /// <summary>Where along the route the player stands now, m.</summary>
        public float Metres { get; private set; }
        public float RunningFor => Running ? Time.time - startedAt : 0;
        public Transform Player => encounter && encounter.PlayerMotor ? encounter.PlayerMotor.transform : null;
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
            Vector3 floor = RoutePoint(Progress + Random.Range(2.5f, 4.5f));
            var side = Vector3.Cross(Vector3.up, RouteForward(Progress + 3)) * (Random.value < .5f ? 1 : -1);
            if (Wall(floor + Vector3.up * Random.Range(3f, 4.5f), side, 4, out var hit, out var normal))
            {
                Vector3 at = hit + normal * .35f;
                Dust(smokeTiny, at, 1.2f, 4);
                for (int i = 0; i < 2; i++)
                {
                    var s = Stone(at + Random.insideUnitSphere * .2f, Random.Range(.12f, .2f));
                    if (s) { s.linearVelocity = normal * .4f; StartCoroutine(Settle(s, 3f)); Destroy(s.gameObject, 12); }
                }
                Sound(crumble, at, .45f, Random.Range(.8f, 1f));
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

        /// <summary>The player's view at a place on the route (eye height, looking along it): for previews and placing.</summary>
        public (Vector3 eye, Vector3 forward) ViewAt(float metres) => (RoutePoint(metres) + Vector3.up * 1.62f, RouteForward(metres + 1.5f));

        /// <summary>A solid thing the player can see: the level's invisible safety walls (root "Colliders") do not count.</summary>
        public static bool Seen(Collider c) => c && !c.isTrigger && c.transform.root.name != "Colliders";

        /// <summary>The first visible surface along a ray: its point and normal.</summary>
        public bool Wall(Vector3 from, Vector3 dir, float reach, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            var hits = Physics.RaycastAll(from, dir, reach, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
                if (Seen(h.collider) && h.distance < best && !(Player && h.collider.transform.IsChildOf(Player)))
                { best = h.distance; point = h.point; normal = h.normal; }
            return best < float.MaxValue;
        }

        /// <summary>
        /// A spot on a wall ahead that the player will actually see: in front of the given view (within the cone, not too
        /// high), with nothing in between. Searches the route from Min to Max Ahead metres, Min to Max Up above the path,
        /// both walls, and takes the highest such spot nearest Prefer metres ahead.
        /// </summary>
        public bool VisibleSpot(float progress, Vector3 eye, Vector3 forward, Vector2 ahead, Vector2 up, out Vector3 at, out Vector3 normal,
                                float prefer = 6, float cone = 26, float tilt = 14)
        {
            at = normal = default;
            forward.y = 0; forward.Normalize();
            float best = float.MaxValue;
            for (float d = ahead.x; d <= ahead.y; d += .75f)
            {
                Vector3 floor = RoutePoint(progress + d);
                var side = Vector3.Cross(Vector3.up, RouteForward(progress + d));
                for (float h = up.x; h <= up.y; h += .5f)
                    foreach (var s in new[] { side, -side })
                    {
                        if (!Wall(floor + Vector3.up * h, s, 5, out var p, out var n)) continue;
                        Vector3 c = p + n * .5f;
                        Vector3 to = c - eye;
                        var flat = new Vector3(to.x, 0, to.z);
                        if (flat.sqrMagnitude < 1 || Vector3.Angle(forward, flat) > cone) continue;
                        if (Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg > tilt) continue;
                        if (Wall(eye, to.normalized, to.magnitude - .4f, out _, out _)) continue;
                        float score = Mathf.Abs(d - prefer) - h * 1.2f + Vector3.Angle(forward, flat) / 5;
                        if (score < best) { best = score; at = c; normal = n; }
                    }
            }
            return best < float.MaxValue;
        }

        /// <summary>Everything in the level's scene with this in its name (the plants sit outside the level root).</summary>
        public List<Transform> Named(string part)
        {
            var list = new List<Transform>();
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.Contains(part) && !(t.parent && t.parent.name.Contains(part))) list.Add(t);
            return list;
        }

        /// <summary>The ground under a point.</summary>
        public Vector3 Floor(Vector3 from) => Wall(from + Vector3.up * .2f, Vector3.down, 30, out var p, out _) ? p : from;

        /// <summary>Rock dust bursting out at a point (tinted to <see cref="dustColor"/>).</summary>
        public GameObject Dust(GameObject prefab, Vector3 at, float scale, float lifetime = 5) => Effect(prefab, at, Quaternion.identity, scale, lifetime, dustColor);

        /// <summary>A cloud that drifts and fades on its own (no hard cut).</summary>
        public void Drift(GameObject prefab, Vector3 at, Vector3 velocity, float scale, float seconds, Color? tint = null) => StartCoroutine(DriftRoutine(prefab, at, velocity, scale, seconds, tint));

        IEnumerator DriftRoutine(GameObject prefab, Vector3 at, Vector3 velocity, float scale, float seconds, Color? tint)
        {
            var fx = Effect(prefab, at, Quaternion.identity, scale, seconds + 3, tint ?? cloudColor);
            for (float t = 0; t < seconds && fx; t += Time.deltaTime)
            {
                fx.transform.position += velocity * Time.deltaTime;
                yield return null;
            }
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
        public GameObject Effect(GameObject prefab, Vector3 at, Quaternion rotation, float scale = 1, float lifetime = 4, Color? tint = null)
        {
            if (!prefab) return null;
            var effect = Instantiate(prefab, at, rotation, transform);
            effect.transform.localScale *= scale;
            if (tint.HasValue)
                foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.startColor = tint.Value; }
            Destroy(effect, lifetime);
            return effect;
        }

        /// <summary>A loose stone of the kit that falls and tumbles; it settles into rubble and stops costing anything.</summary>
        public Rigidbody Stone(Vector3 at, float size)
        {
            var model = rockModels != null && rockModels.Length > 0 ? rockModels[Random.Range(0, rockModels.Length)] : rockModel;
            if (!model) return null;
            var rock = Instantiate(model, at, Random.rotation, transform);
            // the size asked is the stone's diagonal, whatever the model's own
            var mesh = rock.GetComponentInChildren<MeshFilter>();
            float own = mesh && mesh.sharedMesh ? mesh.sharedMesh.bounds.size.magnitude * mesh.transform.lossyScale.x / Mathf.Max(1e-4f, rock.transform.lossyScale.x) : 1;
            rock.transform.localScale = Vector3.one * size / Mathf.Max(.05f, own);
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
