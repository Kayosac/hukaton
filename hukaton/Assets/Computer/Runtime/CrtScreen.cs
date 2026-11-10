using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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

        [Header("Sharpness")]
        [Tooltip("The PC's canvas is laid out at the content texture's size (640×480) but rendered k times larger, " +
                 "k chosen so the texture has at least as many rows as the picture has on screen: pixel art stays whole " +
                 "pixels, while text, logo and card art get real screen detail instead of being stretched.")]
        [Range(1, 4)] public int maxSupersample = 4;

        [Header("Power (driven by the boot)")]
        [Range(0, 1)] public float openX;
        [Range(0, 1)] public float openY;
        [Range(0, 1)] public float afterglow, wobble, noise, flash;

        Material instance;
        RenderTexture layout, sharp;
        CanvasScaler contentScaler;
        int supersample = 1;
        float savedRenderScale = -1;
        UpscalingFilterSelection savedFilter;
        static readonly int QuadAspectId = Shader.PropertyToID("_QuadAspect"), ContentAspectId = Shader.PropertyToID("_ContentAspect"),
            FillId = Shader.PropertyToID("_Fill"), ArtScaleId = Shader.PropertyToID("_ArtScale"), CurveId = Shader.PropertyToID("_Curve"), CornerId = Shader.PropertyToID("_Corner"),
            OpenXId = Shader.PropertyToID("_OpenX"), OpenYId = Shader.PropertyToID("_OpenY"), DotId = Shader.PropertyToID("_Dot"),
            WobbleId = Shader.PropertyToID("_Wobble"), NoiseId = Shader.PropertyToID("_Noise"), FlashId = Shader.PropertyToID("_Flash");

        void Awake()
        {
            if (material) instance = new Material(material) { name = material.name + " (screen)" };
            layout = content;
            if (contentCamera)
                foreach (var canvas in contentCamera.transform.root.GetComponentsInChildren<Canvas>(true))
                    if (canvas.isRootCanvas && canvas.worldCamera == contentCamera && canvas.TryGetComponent(out contentScaler)) break;
            if (picture) picture.material = instance;
            Resize();
            Apply();
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RestorePipeline();
        }

        void OnDestroy()
        {
            if (instance) Destroy(instance);
            if (sharp) { if (contentCamera && contentCamera.targetTexture == sharp) contentCamera.targetTexture = layout; sharp.Release(); Destroy(sharp); }
        }

        /// <summary>How many texture pixels one canvas pixel takes (1 = the plain 640×480 texture).</summary>
        public int Supersample => supersample;

        /// <summary>Supersample factor for a picture this many screen pixels tall.</summary>
        public static int SupersampleFor(float pictureScreenHeight, int layoutHeight, int max) =>
            Mathf.Clamp(Mathf.CeilToInt(pictureScreenHeight / Mathf.Max(1, layoutHeight) - .02f), 1, Mathf.Max(1, max));

        /// <summary>Re-renders the canvas at a whole multiple of its layout size, matched to the picture's on-screen height.</summary>
        void Resize()
        {
            if (!layout || !picture) return;
            var canvas = picture.canvas;
            float pictureHeight = picture.rectTransform.rect.height * (canvas ? canvas.scaleFactor : 1);
            if (pictureHeight <= 0) pictureHeight = Screen.height;
            int k = contentScaler ? SupersampleFor(pictureHeight * shape.fill, layout.height, maxSupersample) : 1;
            if (k != supersample || (k > 1 && !sharp) || picture.texture == null)
            {
                supersample = k;
                RenderTexture target = layout;
                if (k > 1)
                {
                    if (!sharp || sharp.width != layout.width * k)
                    {
                        if (sharp) { sharp.Release(); Destroy(sharp); }
                        sharp = new RenderTexture(layout.width * k, layout.height * k, 24, layout.graphicsFormat) {
                            name = layout.name + " ×" + k, hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp, antiAliasing = 1, useMipMap = true, autoGenerateMips = true
                        };
                        sharp.Create();
                    }
                    target = sharp;
                }
                if (contentCamera) contentCamera.targetTexture = target;
                if (contentScaler) contentScaler.scaleFactor = k;
                picture.texture = target;
                content = target;
            }
        }

        // URP applies its global Render Scale (0.8 on the Mobile/WebGL tier) even to cameras that render into a
        // texture: the PC's canvas would be drawn smaller and stretched back. Keep it at 1 for this camera only.
        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != contentCamera || savedRenderScale >= 0) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)) return;
            savedRenderScale = asset.renderScale; savedFilter = asset.upscalingFilter;
            asset.renderScale = 1; asset.upscalingFilter = UpscalingFilterSelection.Point;
        }

        void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == contentCamera) RestorePipeline();
        }

        void RestorePipeline()
        {
            if (savedRenderScale < 0) return;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)
            { asset.renderScale = savedRenderScale; asset.upscalingFilter = savedFilter; }
            savedRenderScale = -1;
        }

        /// <summary>
        /// Hidden during the blinks: the picture is black then anyway, and the full-screen black under it must fade as
        /// one layer — two black layers over each other would fade unevenly and show the 4:3 frame.
        /// </summary>
        public void SetPictureVisible(bool visible)
        {
            if (picture) picture.enabled = visible;
        }

        /// <summary>Off: the PC camera renders only while the tube shows anything.</summary>
        public void SetPowered(bool powered)
        {
            if (contentCamera) contentCamera.enabled = powered;
        }

        void LateUpdate()
        {
            Resize();
            Apply();
        }

        public void Apply()
        {
            if (!instance) return;
            instance.SetFloat(QuadAspectId, shape.quadAspect);
            instance.SetFloat(ContentAspectId, shape.contentAspect);
            instance.SetFloat(FillId, shape.fill);
            instance.SetFloat(ArtScaleId, supersample);
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

        /// <summary>Screen point → pixel in the render texture (what the PC's canvas camera sees; supersampled pixels).</summary>
        public bool TryPicturePixel(Vector2 screenPoint, out Vector2 pixel)
        {
            bool inside = TryPictureUV(screenPoint, out var uv);
            pixel = content ? new Vector2(uv.x * content.width, uv.y * content.height) : uv;
            return inside;
        }
    }
}
