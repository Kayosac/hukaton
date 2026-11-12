using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// The air of the god mountains (docs/level-design/god-mountains, chapter 7): fog, sky colour and ambient light
    /// follow the viewer along the walking route. The route is the authored chain of points; the viewer's place on it is
    /// measured in metres from the first point, and the air at that distance is blended between the two nearest keys.
    /// Keys sit where nothing far is visible: behind the stone frame (inside → cloud) and in the bend of the gorge
    /// (cloud → heights), so the change is never seen.
    /// The sky is the game's pixel sky (Hukaton/Fantasy Day Sky) when one is given: inside and in the cloud the fog hides
    /// all of it, in the heights it opens. The template material is never changed; a copy made at runtime is.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(400)]
    public sealed class GodMountainsAtmosphere : MonoBehaviour
    {
        [Serializable]
        public struct Air
        {
            public string name;
            public Color fog;
            [Tooltip("Exponential squared fog: 95% of the colour is gone at about 1.73 / density metres.")]
            [Min(0)] public float density;
            [Tooltip("Background of the viewer's camera (Solid Color): the sky.")]
            public Color sky;
            [Tooltip("Ambient light from the sides; from above it is 1.35 times stronger, from below half as strong.")]
            public Color ambient;
            [Tooltip("With a sky material: 1 — the whole sky is fog colour, 0 — clear sky down to the template's fog start.")]
            [Range(0, 1)] public float skyHidden;
        }

        [Serializable]
        public struct Key
        {
            [Tooltip("Metres along the route from its first point.")]
            [Min(0)] public float metres;
            [Tooltip("Index in Profiles.")]
            [Min(0)] public int air;
        }

        [Tooltip("Whose place on the route decides the air: the player's camera.")]
        public Transform viewer;
        [Tooltip("Gets the sky colour as its background.")]
        public Camera viewCamera;
        [Tooltip("The walking route in order, from the start to the edge.")]
        public Transform[] route = Array.Empty<Transform>();
        public Air[] profiles = Array.Empty<Air>();
        [Tooltip("Ascending by metres. Before the first key and after the last the air is that key's own.")]
        public Key[] keys = Array.Empty<Key>();
        [Tooltip("Optional skybox (Hukaton/Fantasy Day Sky). Empty: the camera's solid background takes the air's sky colour.")]
        public Material sky;

        static readonly int FogStartId = Shader.PropertyToID("_FogStart");
        Material skyCopy;
        float clearFogStart;

        void OnDestroy()
        {
            if (!skyCopy) return;
            if (Application.isPlaying) Destroy(skyCopy); else DestroyImmediate(skyCopy);
        }

        Material SkyCopy()
        {
            if (!sky) return null;
            if (!skyCopy)
            {
                skyCopy = new Material(sky) { name = sky.name + " (route)", hideFlags = HideFlags.DontSave };
                clearFogStart = sky.HasProperty(FogStartId) ? sky.GetFloat(FogStartId) : 0;
            }
            return skyCopy;
        }

        void LateUpdate()
        {
            if (viewer) Apply(Evaluate(viewer.position));
        }

        /// <summary>The air at a world position: its nearest place on the route decides.</summary>
        public Air Evaluate(Vector3 position) => AirAt(RouteMetres(position));

        /// <summary>Metres along the route to the point of the route nearest to the position.</summary>
        public float RouteMetres(Vector3 position)
        {
            float best = float.PositiveInfinity, metres = 0, walked = 0;
            for (int i = 0; i + 1 < route.Length; i++)
            {
                if (!route[i] || !route[i + 1]) continue;
                Vector3 a = route[i].position, b = route[i + 1].position, ab = b - a;
                float length = ab.magnitude;
                float t = length > 1e-4f ? Mathf.Clamp01(Vector3.Dot(position - a, ab) / (length * length)) : 0;
                float distance = (a + ab * t - position).sqrMagnitude;
                if (distance < best) { best = distance; metres = walked + t * length; }
                walked += length;
            }
            return metres;
        }

        public Air AirAt(float metres)
        {
            if (keys.Length == 0 || profiles.Length == 0) return default;
            if (metres <= keys[0].metres) return Profile(keys[0].air);
            for (int k = 0; k + 1 < keys.Length; k++)
                if (metres <= keys[k + 1].metres)
                {
                    float t = Mathf.InverseLerp(keys[k].metres, keys[k + 1].metres, metres);
                    return Blend(Profile(keys[k].air), Profile(keys[k + 1].air), t);
                }
            return Profile(keys[keys.Length - 1].air);
        }

        public void Apply(Air air)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = air.fog;
            RenderSettings.fogDensity = air.density;
            // Light from above, less from the sides, little from below: the rock's big planes read even without sun.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = air.ambient * 1.35f;
            RenderSettings.ambientEquatorColor = air.ambient;
            RenderSettings.ambientGroundColor = air.ambient * .5f;
            if (viewCamera) viewCamera.backgroundColor = air.sky;
            var copy = SkyCopy();
            if (!copy) return;
            copy.SetFloat(FogStartId, Mathf.Lerp(clearFogStart, 90, air.skyHidden));
            if (RenderSettings.skybox != copy) RenderSettings.skybox = copy;
            if (viewCamera) viewCamera.clearFlags = CameraClearFlags.Skybox;
        }

        Air Profile(int index) => profiles[Mathf.Clamp(index, 0, profiles.Length - 1)];

        static Air Blend(Air a, Air b, float t) => new Air
        {
            name = t < .5f ? a.name : b.name,
            fog = Color.Lerp(a.fog, b.fog, t),
            // Density changes over orders of magnitude (0.2 in the cloud, 0.0003 in the heights): blend its logarithm.
            density = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(a.density, 1e-6f)), Mathf.Log(Mathf.Max(b.density, 1e-6f)), t)),
            sky = Color.Lerp(a.sky, b.sky, t),
            ambient = Color.Lerp(a.ambient, b.ambient, t),
            skyHidden = Mathf.Lerp(a.skyHidden, b.skyHidden, t),
        };
    }
}
