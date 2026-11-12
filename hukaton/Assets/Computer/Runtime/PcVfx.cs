using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>
    /// Particle effects inside the PC picture. An effect is spawned under the FX root in front of the PC camera, on its
    /// layer (PC Screen), and drawn above the PC canvas by sorting order; it runs on unscaled time and never plays a
    /// sound or a light of its own. The PC copies in <c>Assets/Computer/Fx</c> come configured like that; a plain
    /// Retro Arsenal prefab is adjusted here when it is spawned.
    /// </summary>
    public static class PcVfx
    {
        /// <summary>Above the PC canvas (order 0); the cursor and tooltip canvases override to <see cref="OverlayOrder"/>.</summary>
        public const int SortingOrder = 10;
        public const int OverlayOrder = 20;

        /// <summary>One spawned effect; <see cref="PcMergeShow"/> stops and destroys it on unscaled time.</summary>
        public sealed class Live
        {
            public GameObject root;
            public ParticleSystem[] systems;
            public float stopAt = float.PositiveInfinity, dieAt;
            public bool clearOnImpact, stopOnImpact, stopped;

            public void StopEmitting()
            {
                if (stopped) return;
                stopped = true;
                foreach (var system in systems)
                    if (system) system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// Instantiates and plays an effect. <paramref name="worldPosition"/> is projected onto the FX root's plane;
        /// <paramref name="hold"/> &gt; 0 stops emitting after that many seconds (looping effects). Null without a prefab or root.
        /// </summary>
        public static Live Spawn(GameObject prefab, Transform fxRoot, Vector3 worldPosition, Quaternion localRotation, float scale,
            Color accent, float accentAmount, float speed, float hold, float now)
        {
            if (!prefab || !fxRoot) return null;
            var go = Object.Instantiate(prefab, fxRoot, false);
            var t = go.transform;
            Vector3 local = fxRoot.InverseTransformPoint(worldPosition);
            local.z = 0;
            t.localPosition = local;
            t.localRotation = localRotation;
            t.localScale = Vector3.one * Mathf.Max(.001f, scale);

            int layer = fxRoot.gameObject.layer;
            foreach (var child in go.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            foreach (var source in go.GetComponentsInChildren<AudioSource>(true))
            {
                source.playOnAwake = false;
                source.Stop();
                source.enabled = false;
            }
            foreach (var light in go.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (var renderer in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                renderer.sortingLayerID = 0;
                if (renderer.sortingOrder < SortingOrder) renderer.sortingOrder = SortingOrder;
            }

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            float rate = speed > 0 ? speed : 1;
            float longest = 0;
            bool loops = false;
            foreach (var system in systems)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.useUnscaledTime = true;
                // Every system shrinks with the effect (some Retro children scale only by their own transform).
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.simulationSpeed *= rate;
                Tint(system, accent, accentAmount);
                loops |= main.loop;
                float life = Longest(main.startLifetime);
                longest = Mathf.Max(longest, ((main.loop ? 0 : main.duration) + life) / Mathf.Max(.01f, main.simulationSpeed));
            }
            foreach (var system in systems)
                if (IsTop(system, t)) system.Play(true);

            var live = new Live { root = go, systems = systems };
            if (hold <= 0 && loops) hold = 1;
            if (hold > 0)
            {
                live.stopAt = now + hold;
                live.dieAt = now + hold + longest + .1f;
            }
            else live.dieAt = now + longest + .1f;
            return live;
        }

        /// <summary>A system not driven by another one in the same effect (sub-emitters and children play with their parent).</summary>
        static bool IsTop(ParticleSystem system, Transform root)
        {
            for (var t = system.transform; t && t != root;)
            {
                t = t.parent;
                if (t && t.GetComponent<ParticleSystem>()) return false;
            }
            return true;
        }

        static float Longest(ParticleSystem.MinMaxCurve curve) => curve.mode switch
        {
            ParticleSystemCurveMode.Constant => curve.constant,
            ParticleSystemCurveMode.TwoConstants => Mathf.Max(curve.constantMin, curve.constantMax),
            _ => curve.curveMultiplier
        };

        /// <summary>Turns the start colour towards the accent, keeping the effect's own alpha.</summary>
        static void Tint(ParticleSystem system, Color accent, float amount)
        {
            if (amount <= 0) return;
            var main = system.main;
            var colour = main.startColor;
            Color low, high;
            switch (colour.mode)
            {
                case ParticleSystemGradientMode.Color:
                    low = high = colour.color;
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    low = colour.colorMin;
                    high = colour.colorMax;
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    low = colour.gradientMin != null ? colour.gradientMin.Evaluate(0) : Color.white;
                    high = colour.gradientMax != null ? colour.gradientMax.Evaluate(1) : Color.white;
                    break;
                default:
                    low = colour.gradient != null ? colour.gradient.Evaluate(0) : Color.white;
                    high = colour.gradient != null ? colour.gradient.Evaluate(1) : Color.white;
                    break;
            }
            Color a = accent, b = Color.Lerp(accent, Color.white, .45f);
            a.a = low.a;
            b.a = high.a;
            amount = Mathf.Clamp01(amount);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(low, a, amount), Color.Lerp(high, b, amount));
        }
    }
}
