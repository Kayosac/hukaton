using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>
    /// What touches the plants this frame, for the plant shaders (ForestWind.hlsl): bodies standing in them (benders —
    /// grass, ferns and reeds lean away) and impacts (shakes — what is near shivers for a moment). Presentation only.
    /// No scene object: the game loop calls <see cref="BeginFrame"/>, <see cref="AddBender"/> for each body and
    /// <see cref="Commit"/> once a frame; <see cref="Shake"/> whenever something lands; <see cref="Clear"/> when the
    /// outing ends, so the next scene's plants stand straight.
    /// </summary>
    public static class PlantReactions
    {
        // The shader's loops are this long, and Unity fixes a global array's length at its first upload.
        public const int MaxBenders = 6, MaxShakes = 4;

        const float PhaseSpeed = 38;          // rad/s: about six shivers a second
        const float Attack = .05f;            // a few frames, so a hit does not pop the leaves to full swing at once
        const float Falloff = 2.5f;           // most of a shiver is over in its first half

        static readonly int BendersId = Shader.PropertyToID("_HkBenders"), BenderParamsId = Shader.PropertyToID("_HkBenderParams");
        static readonly int ShakesId = Shader.PropertyToID("_HkShakes"), ShakeStateId = Shader.PropertyToID("_HkShakeState");

        static readonly Vector4[] benders = new Vector4[MaxBenders], benderParams = new Vector4[MaxBenders];
        static readonly Vector4[] shakes = new Vector4[MaxShakes], shakeState = new Vector4[MaxShakes];
        // A slot is in use while its life is above zero.
        static readonly float[] shakePeak = new float[MaxShakes], shakeAge = new float[MaxShakes], shakeLife = new float[MaxShakes];
        static int benderCount;

        /// <summary>Benders added since <see cref="BeginFrame"/>.</summary>
        public static int BenderCount => benderCount;

        /// <summary>The amplitude slot <paramref name="slot"/> had at the last <see cref="Commit"/> (zero: free, or started since).</summary>
        public static float ShakeAmplitude(int slot) => slot >= 0 && slot < MaxShakes ? shakeState[slot].x : 0;

        /// <summary>Slot <paramref name="slot"/>'s impact: xyz the point, w the radius; zero when the slot is free.</summary>
        public static Vector4 ShakeAt(int slot) => slot >= 0 && slot < MaxShakes && shakeLife[slot] > 0 ? shakes[slot] : Vector4.zero;

        /// <summary>Forgets last frame's bodies; shakes run on.</summary>
        public static void BeginFrame() => benderCount = 0;

        /// <summary>
        /// A body standing at <paramref name="feet"/>: plants within <paramref name="radius"/> metres lean away. Only the
        /// first <see cref="MaxBenders"/> of a frame count, so add the ones that matter first.
        /// </summary>
        public static void AddBender(Vector3 feet, float radius, float strength = 1)
        {
            // One that would move nothing must not take a slot from one that would.
            if (benderCount >= MaxBenders || !(radius > 0) || !(strength > 0)) return;
            benders[benderCount] = new Vector4(feet.x, feet.y, feet.z, radius);
            benderParams[benderCount] = new Vector4(Mathf.Clamp01(strength), 0, 0, 0);
            benderCount++;
        }

        /// <summary>
        /// Plants within <paramref name="radius"/> metres of <paramref name="point"/> shiver, dying away over
        /// <paramref name="seconds"/>. Seen from the next <see cref="Commit"/>.
        /// </summary>
        public static void Shake(Vector3 point, float radius, float strength = 1, float seconds = .6f)
        {
            if (!(radius > 0) || !(strength > 0) || !(seconds > 0)) return;
            strength = Mathf.Clamp01(strength);
            // A second hit on the same spot feeds the shiver already there: a volley must not use up all the slots,
            // and four shivers on one bush would only add up to the shader's cap.
            for (int i = 0; i < MaxShakes; i++)
            {
                if (shakeLife[i] <= 0) continue;
                float same = Mathf.Min(radius, shakes[i].w) * .35f;
                if (((Vector3)shakes[i] - point).sqrMagnitude > same * same) continue;
                // Already swinging: no second attack, or the shiver would dip to nothing mid-volley.
                float age = Mathf.Min(shakeAge[i], AttackOf(seconds));
                Start(i, point, Mathf.Max(radius, shakes[i].w), Mathf.Max(strength, Remaining(i)), seconds, age);
                return;
            }
            int slot = -1;
            for (int i = 0; i < MaxShakes && slot < 0; i++)
                if (shakeLife[i] <= 0) slot = i;
            if (slot < 0)
            {
                // All busy: the weakest goes, and of equally weak ones the oldest. By what is left of it, not by what
                // is shown: one started this frame still shows nothing.
                slot = 0;
                for (int i = 1; i < MaxShakes; i++)
                {
                    float left = Remaining(i), best = Remaining(slot);
                    if (left < best || (Mathf.Approximately(left, best) && shakeAge[i] > shakeAge[slot])) slot = i;
                }
            }
            Start(slot, point, radius, strength, seconds, 0);
        }

        /// <summary>
        /// Once a frame, after the frame's benders: moves the shivers on by <paramref name="deltaTime"/> and hands
        /// everything to the shaders. <paramref name="time"/> is the caller's clock, kept for the record only
        /// (<see cref="LastCommitTime"/>): pass the clock <paramref name="deltaTime"/> belongs to.
        /// </summary>
        public static void Commit(float time, float deltaTime)
        {
            LastCommitTime = time;
            deltaTime = Mathf.Max(0, deltaTime);
            for (int i = benderCount; i < MaxBenders; i++) benders[i] = benderParams[i] = Vector4.zero;
            for (int i = 0; i < MaxShakes; i++)
            {
                if (shakeLife[i] <= 0) continue;
                shakeAge[i] += deltaTime;
                if (shakeAge[i] >= shakeLife[i]) { Free(i); continue; }
                shakeState[i] = new Vector4(Envelope(shakeAge[i], shakeLife[i]) * shakePeak[i], shakeState[i].y + deltaTime * PhaseSpeed, 0, 0);
            }
            Upload();
        }

        /// <summary>The clock of the last <see cref="Commit"/>.</summary>
        public static float LastCommitTime { get; private set; }

        /// <summary>Nothing bends or shivers any more, at once.</summary>
        public static void Clear()
        {
            benderCount = 0;
            for (int i = 0; i < MaxBenders; i++) benders[i] = benderParams[i] = Vector4.zero;
            for (int i = 0; i < MaxShakes; i++) Free(i);
            Upload();
        }

        /// <summary>
        /// A shiver's strength 0..1 at <paramref name="age"/> of <paramref name="seconds"/>: up to full within a few
        /// frames, then dying away fast at first, and exactly zero from <paramref name="seconds"/> on.
        /// </summary>
        public static float Envelope(float age, float seconds)
        {
            if (!(seconds > 0) || age <= 0 || age >= seconds) return 0;
            return Mathf.Clamp01(age / AttackOf(seconds)) * Tail(age, seconds);
        }

        static float AttackOf(float seconds) => Mathf.Min(Attack, seconds * .25f);

        // What is left after the attack: exponential, times a line that brings it to exactly zero at the end instead
        // of leaving a tail to cut off.
        static float Tail(float age, float seconds)
        {
            float attack = AttackOf(seconds);
            float u = Mathf.Clamp01((age - attack) / (seconds - attack));
            return Mathf.Exp(-Falloff * u) * (1 - u);
        }

        static float Remaining(int slot) => shakeLife[slot] > 0 ? shakePeak[slot] * Tail(shakeAge[slot], shakeLife[slot]) : 0;

        static void Start(int slot, Vector3 point, float radius, float strength, float seconds, float age)
        {
            shakes[slot] = new Vector4(point.x, point.y, point.z, radius);
            shakePeak[slot] = strength;
            shakeAge[slot] = age;
            shakeLife[slot] = seconds;
            // A fresh shiver shows nothing until the next Commit; one fed mid-swing keeps what it shows and its phase.
            // Slots start out of step, so two shivers that overlap do not double each other.
            if (age <= 0) shakeState[slot] = new Vector4(0, slot * 1.7f, 0, 0);
        }

        static void Free(int slot)
        {
            shakes[slot] = shakeState[slot] = Vector4.zero;
            shakePeak[slot] = shakeAge[slot] = shakeLife[slot] = 0;
        }

        static void Upload()
        {
            Shader.SetGlobalVectorArray(BendersId, benders);
            Shader.SetGlobalVectorArray(BenderParamsId, benderParams);
            Shader.SetGlobalVectorArray(ShakesId, shakes);
            Shader.SetGlobalVectorArray(ShakeStateId, shakeState);
        }

        // Play mode without a domain reload keeps statics, and the editor keeps shader globals between plays: start
        // from nothing in both.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            LastCommitTime = 0;
            Clear();
        }
    }
}
