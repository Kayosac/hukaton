using UnityEngine;
using UnityEngine.Rendering;

namespace Hakaton.ShopBlockout
{
    /// <summary>
    /// Ambient light of an interior. While this object is active it replaces the scene's ambient light;
    /// when it is disabled (the game switches to another area) the previous ambient light comes back.
    /// </summary>
    public sealed class InteriorAmbience : MonoBehaviour
    {
        [Tooltip("Gradient ambient: light from above, from the walls and from the floor.")]
        public Color sky = new Color(.36f, .29f, .24f);
        public Color equator = new Color(.25f, .19f, .15f);
        public Color ground = new Color(.13f, .10f, .08f);

        [Tooltip("Also replace the scene's fog while active (the cave: dark, close fog instead of the valley's haze).")]
        public bool overrideFog;
        public Color fogColor = new Color(.03f, .03f, .05f);
        [Min(0)] public float fogDensity = .045f;

        AmbientMode savedMode;
        Color savedSky, savedEquator, savedGround;
        bool savedFog;
        Color savedFogColor;
        float savedFogDensity;
        FogMode savedFogMode;
        bool applied;

        void OnEnable()
        {
            savedMode = RenderSettings.ambientMode;
            savedSky = RenderSettings.ambientSkyColor;
            savedEquator = RenderSettings.ambientEquatorColor;
            savedGround = RenderSettings.ambientGroundColor;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            if (overrideFog)
            {
                savedFog = RenderSettings.fog;
                savedFogColor = RenderSettings.fogColor;
                savedFogDensity = RenderSettings.fogDensity;
                savedFogMode = RenderSettings.fogMode;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
            }
            applied = true;
        }

        void OnDisable()
        {
            if (!applied) return;
            applied = false;
            RenderSettings.ambientMode = savedMode;
            RenderSettings.ambientSkyColor = savedSky;
            RenderSettings.ambientEquatorColor = savedEquator;
            RenderSettings.ambientGroundColor = savedGround;
            if (overrideFog)
            {
                RenderSettings.fog = savedFog;
                RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogDensity = savedFogDensity;
                RenderSettings.fogMode = savedFogMode;
            }
        }
    }
}
