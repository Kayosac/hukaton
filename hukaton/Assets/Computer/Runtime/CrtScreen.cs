using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// The PC's picture: the render texture shown full screen (4:3, black bars at the sides) through the CRT material,
    /// and the mouse mapped through the same bulge. The power parameters (open width and height, afterglow, degauss,
    /// static) are driven by <see cref="PcBoot"/>.
    /// </summary>
    public sealed class CrtScreen : MonoBehaviour
    {
        [Header("Picture")]
        [Tooltip("The full-screen 4:3 image the tube is drawn on.")]
        public RawImage picture;
        [Tooltip("The CRT material; the screen draws with its own copy.")]
        public Material material;
        public CrtShape shape = CrtShape.Default;
        public RenderTexture content;
        [Tooltip("The camera that renders the PC's canvas into the texture.")]
        public Camera contentCamera;

        [Header("Power (driven by the boot)")]
        [Range(0, 1)] public float openX;
        [Range(0, 1)] public float openY;
        [Range(0, 1)] public float afterglow, wobble, noise, flash;

        Material instance;
        static readonly int QuadAspectId = Shader.PropertyToID("_QuadAspect"), ContentAspectId = Shader.PropertyToID("_ContentAspect"),
            FillId = Shader.PropertyToID("_Fill"), CurveId = Shader.PropertyToID("_Curve"), CornerId = Shader.PropertyToID("_Corner"),
            OpenXId = Shader.PropertyToID("_OpenX"), OpenYId = Shader.PropertyToID("_OpenY"), DotId = Shader.PropertyToID("_Dot"),
            WobbleId = Shader.PropertyToID("_Wobble"), NoiseId = Shader.PropertyToID("_Noise"), FlashId = Shader.PropertyToID("_Flash");

        void Awake()
        {
            if (material) instance = new Material(material) { name = material.name + " (screen)" };
            if (picture)
            {
                picture.material = instance;
                picture.texture = content;
            }
            Apply();
        }

        void OnDestroy()
        {
            if (instance) Destroy(instance);
        }

        /// <summary>Off: the PC camera renders only while the tube shows anything.</summary>
        public void SetPowered(bool powered)
        {
            if (contentCamera) contentCamera.enabled = powered;
        }

        void LateUpdate() => Apply();

        public void Apply()
        {
            if (!instance) return;
            instance.SetFloat(QuadAspectId, shape.quadAspect);
            instance.SetFloat(ContentAspectId, shape.contentAspect);
            instance.SetFloat(FillId, shape.fill);
            instance.SetFloat(CurveId, shape.curve);
            instance.SetFloat(CornerId, shape.corner);
            instance.SetFloat(OpenXId, openX);
            instance.SetFloat(OpenYId, openY);
            instance.SetFloat(DotId, afterglow);
            instance.SetFloat(WobbleId, wobble);
            instance.SetFloat(NoiseId, noise);
            instance.SetFloat(FlashId, flash);
        }

        /// <summary>Screen point → picture UV, through the bulge. False off the picture (bars, tube border).</summary>
        public bool TryPictureUV(Vector2 screenPoint, out Vector2 pictureUV)
        {
            pictureUV = default;
            if (!picture) return false;
            var rect = picture.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, null, out var local)) return false;
            var r = rect.rect;
            var glass = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
            pictureUV = CrtScreenMath.ContentUV(glass, shape);
            return CrtScreenMath.InsidePicture(pictureUV, shape);
        }

        /// <summary>Screen point → pixel in the render texture (what the PC's canvas camera sees).</summary>
        public bool TryPicturePixel(Vector2 screenPoint, out Vector2 pixel)
        {
            bool inside = TryPictureUV(screenPoint, out var uv);
            pixel = content ? new Vector2(uv.x * content.width, uv.y * content.height) : uv;
            return inside;
        }
    }
}
