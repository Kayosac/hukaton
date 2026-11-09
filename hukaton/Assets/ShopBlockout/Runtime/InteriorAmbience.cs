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

        AmbientMode savedMode;
        Color savedSky, savedEquator, savedGround;
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
        }
    }
}
