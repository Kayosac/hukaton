using System.Collections;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// «Странное»: something that cannot be here — a street payphone ringing in the woods, a pot-bellied old television
    /// hissing in a clearing. It calls from afar (the ring or the hiss carries ~25 m, its lamp or screen flickers through
    /// the fog). E: the receiver comes off the hook / a knock on the set; a short strange sound (a voice and breathing in
    /// the line, a picture on the screen), then one random <see cref="OddityEffect"/> from its <see cref="OddityTable"/>,
    /// with a sound and a short line. After that it is silent and dark for good. One prefab per kind; the world comes
    /// from whoever spawns it. It stands on uneven ground by itself. State: <see cref="Capture"/> / <see cref="Restore"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Oddity : MonoBehaviour, IInteractable
    {
        public enum Kind { Payphone, Television }
        /// <summary>What survives an unload: untouched (calling) or used (silent).</summary>
        public enum State { Untouched, Used }

        public Kind kind;
        [Tooltip("Set by whoever spawns it (a location, a chunk of the world).")]
        public ExpeditionWorld world;
        public OddityTable effects;
        public string prompt = "E — снять трубку";

        [Header("Calling")]
        [Tooltip("Ring or hiss loop; its cue's max distance is how far it carries.")]
        public SfxCue callLoop;
        [Range(0, 1)] public float callVolume = 1;
        [Tooltip("Lamp under the hood / glow of the screen: lights the fog around it while it calls.")]
        public Light glow;
        [Min(0)] public float glowIntensity = 2;
        [Tooltip("How unsteady the glow is: 0 steady, 1 wild.")]
        [Range(0, 1)] public float flicker = .35f;
        [Tooltip("Glows while it calls (payphone lamp: emission through _EmissionColor).")]
        public Renderer lamp;
        [ColorUsage(false, true)] public Color lampColour = new Color(1.6f, 1.7f, 1.4f);

        [Header("Answer")]
        public SfxCue answerCue;
        [Tooltip("Seconds of the strange sound / picture before the effect.")]
        [Min(0)] public float answerSeconds = 1.4f;
        public VfxBurst answerBurst;
        public Color answerTint = Color.white;
        [Min(.01f)] public float answerBurstScale = .5f;

        [Header("Payphone")]
        [Tooltip("Swings on its pivot at the cord's anchor: shakes while ringing, dangles off the hook once answered.")]
        public Transform handset;
        [Tooltip("Handset rotation (local Euler) when it hangs off the hook.")]
        public Vector3 handsetDangle = new Vector3(0, 0, -150);

        [Header("Television")]
        [Tooltip("The glass with the CRT screen material.")]
        public Renderer screen;
        public Texture staticPicture, answerPicture;
        [Tooltip("Glass aspect (width / height) for the CRT shader.")]
        [Min(.1f)] public float screenAspect = 1.22f;

        public State Current => used ? State.Used : State.Untouched;
        /// <summary>What happened when it was used (null before).</summary>
        public OddityEffect LastEffect { get; private set; }
        public bool Answering { get; private set; }

        public bool CanInteract => !used && !Answering && isActiveAndEnabled && world && world.Running;
        public string Prompt => prompt;

        bool used, grounded;
        Quaternion handsetRest;
        Vector3 rest;
        MaterialPropertyBlock block;
        float openY = 1, dot, flash;
        Texture picture;
        static readonly int MainTex = Shader.PropertyToID("_MainTex"), Noise = Shader.PropertyToID("_Noise"),
            Flash = Shader.PropertyToID("_Flash"), OpenY = Shader.PropertyToID("_OpenY"), OpenX = Shader.PropertyToID("_OpenX"),
            Dot = Shader.PropertyToID("_Dot"), QuadAspect = Shader.PropertyToID("_QuadAspect"), ContentAspect = Shader.PropertyToID("_ContentAspect"),
            Emission = Shader.PropertyToID("_EmissionColor");
        static readonly RaycastHit[] Hits = new RaycastHit[12];

        void Awake()
        {
            if (handset) handsetRest = handset.localRotation;
            picture = staticPicture;
        }

        void Start()
        {
            if (!grounded) SnapToGround();
            rest = transform.localPosition;
            Show();
        }

        void OnDisable()
        {
            if (world && world.sfx) world.sfx.StopLoop(this);
        }

        public void SetFocused(bool focused) { }

        public bool Interact()
        {
            if (!CanInteract) return false;
            used = true;
            StartCoroutine(Answer());
            return true;
        }

        // ---------------- saving ----------------

        public State Capture() => Current;

        /// <summary>Back as it was left: calling, or silent and dark (a used one never calls again).</summary>
        public void Restore(State state)
        {
            StopAllCoroutines();
            Answering = false;
            used = state == State.Used;
            LastEffect = null;
            if (handset) handset.localRotation = used ? Quaternion.Euler(handsetDangle) : handsetRest;
            openY = used ? 0 : 1;
            dot = 0;
            flash = 0;
            picture = staticPicture;
            if (used && world && world.sfx) world.sfx.StopLoop(this);
            Show();
        }

        // ---------------- calling ----------------

        void Update()
        {
            if (used || Answering) return;
            if (!world || !world.Running)
            {
                if (world && world.sfx) world.sfx.StopLoop(this);
                return;
            }
            if (world.sfx && callLoop) world.sfx.Loop(this, callLoop, transform.position + Vector3.up, callVolume);
            if (handset && kind == Kind.Payphone)
            {
                // Rattles on the hook in time with the ring (1.1 s of ringing every 2 s).
                bool ringing = Time.time % 2f < 1.1f;
                float shake = ringing ? Mathf.Sin(Time.time * 120) * 1.6f : 0;
                handset.localRotation = handsetRest * Quaternion.Euler(0, 0, shake);
            }
            if (kind == Kind.Television) flash = Random.value < .03f ? Random.Range(.3f, .8f) : Mathf.MoveTowards(flash, 0, Time.deltaTime * 4);
            Show();
        }

        /// <summary>Applies the current look: glow, lamp and screen.</summary>
        void Show()
        {
            bool on = !used;
            float wave = kind == Kind.Payphone
                ? (Random.value < flicker * .08f ? Random.Range(0f, .3f) : 1)                            // a tired fluorescent tube
                : Mathf.Lerp(1, Random.Range(.55f, 1.25f), flicker);                                   // the jumping light of a CRT
            if (Answering) wave = Random.Range(.1f, 1.6f);
            if (glow)
            {
                glow.enabled = on || Answering;
                glow.intensity = glowIntensity * wave * (kind == Kind.Television ? openY : 1);
            }
            block ??= new MaterialPropertyBlock();
            if (lamp)
            {
                lamp.GetPropertyBlock(block);
                block.SetColor(Emission, (on || Answering) ? lampColour * wave : Color.black);
                lamp.SetPropertyBlock(block);
            }
            if (screen)
            {
                screen.GetPropertyBlock(block);
                if (picture) block.SetTexture(MainTex, picture);
                block.SetFloat(QuadAspect, screenAspect);
                block.SetFloat(ContentAspect, 128f / 96f);
                block.SetFloat(Noise, Answering ? .25f : .85f + Random.Range(-.1f, .1f));
                block.SetFloat(Flash, flash);
                block.SetFloat(OpenY, openY);
                block.SetFloat(OpenX, Mathf.Clamp01(openY * 3));
                block.SetFloat(Dot, dot);
                screen.SetPropertyBlock(block);
            }
        }

        // ---------------- answering ----------------

        IEnumerator Answer()
        {
            Answering = true;
            if (world.sfx) world.sfx.StopLoop(this);
            world.Play(answerCue, transform.position + Vector3.up);
            world.Spawn(answerBurst, AnswerPoint(), Quaternion.identity, answerTint, answerBurstScale);
            if (kind == Kind.Television) { picture = answerPicture ? answerPicture : staticPicture; flash = 1; }
            float time = 0;
            while (time < answerSeconds)
            {
                time += Time.deltaTime;
                float k = time / Mathf.Max(.01f, answerSeconds);
                if (kind == Kind.Payphone && handset)
                {
                    // Off the hook: up and away, held as if listening, trembling.
                    float lift = Mathf.Min(1, k * 5);
                    handset.localRotation = handsetRest * Quaternion.Euler(0, 0, Mathf.Lerp(0, 35, lift) + Mathf.Sin(Time.time * 50) * 2);
                }
                else
                {
                    // The knock: the set jolts, then the picture holds and stutters.
                    float jolt = Mathf.Max(0, 1 - time * 6);
                    transform.localPosition = rest + new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)) * .025f * jolt;
                    flash = Mathf.MoveTowards(flash, Random.value < .1f ? .6f : 0, Time.deltaTime * 6);
                }
                Show();
                yield return null;
            }
            transform.localPosition = rest;

            LastEffect = effects ? effects.Pick(world, Random.value) : null;
            if (LastEffect) LastEffect.Apply(world, this);

            // Then silence and dark, for good.
            time = 0;
            Quaternion from = handset ? handset.localRotation : Quaternion.identity;
            while (time < .5f)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / .5f);
                if (kind == Kind.Payphone && handset)
                    handset.localRotation = Quaternion.SlerpUnclamped(from, Quaternion.Euler(handsetDangle), Ease.OutBack(k));
                if (kind == Kind.Television) { openY = 1 - Mathf.Clamp01(k * 2.5f); dot = k < .4f ? 0 : 1 - (k - .4f) / .6f; }
                Show();
                yield return null;
            }
            openY = 0;
            dot = 0;
            Answering = false;
            Show();
        }

        Vector3 AnswerPoint()
        {
            if (kind == Kind.Payphone && handset) return handset.position + Vector3.up * .3f;
            return screen ? screen.bounds.center : transform.position + Vector3.up;
        }

        // ---------------- ground ----------------

        void SnapToGround()
        {
            grounded = true;
            int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * 1.5f, Vector3.down, Hits, 5, ~((1 << 2) | (1 << 4)), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 at = transform.position;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= best) continue;
                if (hit.collider.attachedRigidbody && !hit.collider.attachedRigidbody.isKinematic) continue;
                best = hit.distance;
                at = hit.point;
            }
            if (best < float.MaxValue) transform.position = at;
        }
    }
}
