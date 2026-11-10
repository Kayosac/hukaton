using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Hukaton.Rendering
{
    /// <summary>Renders the world at an authored resolution, then presents native artwork and UI.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Camera)), DefaultExecutionOrder(500)]
    public sealed class WorldResolution : MonoBehaviour
    {
        public WorldResolutionSettings Settings;
        [Tooltip("Renderer, который выводит 3D-кадр, исключённые слои и UI в разрешении экрана.")]
        public ScriptableRendererData PresentationRenderer;

        sealed class RuntimeState
        {
            public Camera World, Output;
            public RenderTexture Image, OriginalTarget;
            public float OriginalAspect;
            public int OriginalCullingMask;
            public bool OriginalDynamicResolution, OriginalMSAA;
        }
        struct PipelineOverride
        {
            public Camera Camera;
            public UniversalRenderPipelineAsset Asset;
            public float Scale;
            public UpscalingFilterSelection Filter;
        }
        [System.NonSerialized] RuntimeState state;
        [System.NonSerialized] List<PipelineOverride> overrides = new List<PipelineOverride>();
        [System.NonSerialized] bool missingRendererReported;

        public RenderTexture Image => state?.Image;
        public Camera OutputCamera => state?.Output;
        public int NativeArtworkLayerMask => state == null || !Settings ? 0
            : state.OriginalCullingMask & Settings.NativeResolutionLayers.value & ~NativeWorldUI.Mask;
        public int SceneTransparencyLayerMask => state == null ? 0 : state.OriginalCullingMask & ~NativeWorldUI.Mask;
        public int WorldLayerMask => state == null ? 0 : state.OriginalCullingMask & ~(NativeWorldUI.Mask | NativeArtworkLayerMask);
        public bool IsActive => isActiveAndEnabled && Settings && Settings.Enabled && Image;

        void OnEnable() { if (Application.isPlaying) Refresh(); }
        void LateUpdate() => Refresh();
        void OnDisable() => Release();
        void OnDestroy() => Release();

        // Also used by isolated editor render validation; does not change the saved camera or pipeline assets.
        public void Refresh()
        {
            if (!isActiveAndEnabled || !Settings || !Settings.Enabled || !PresentationRenderer) { Release(); return; }
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            int rendererIndex = -1;
            if (pipeline)
                for (int i = 0; i < pipeline.rendererDataList.Length; i++)
                    if (pipeline.rendererDataList[i] == PresentationRenderer) { rendererIndex = i; break; }
            if (rendererIndex < 0)
            {
                Release();
                if (!missingRendererReported) Debug.LogError("Add the World Presentation renderer to the active URP pipeline before enabling World Resolution.", this);
                missingRendererReported = true;
                return;
            }
            missingRendererReported = false;
            if (state == null)
            {
                var world = GetComponent<Camera>();
                state = new RuntimeState { World = world, OriginalTarget = world.targetTexture, OriginalAspect = world.aspect, OriginalCullingMask = world.cullingMask,
                    OriginalDynamicResolution = world.allowDynamicResolution, OriginalMSAA = world.allowMSAA };
                var output = new GameObject("World presentation · native artwork and UI") { hideFlags = HideFlags.HideAndDontSave };
                output.transform.SetParent(transform, false);
                state.Output = output.AddComponent<Camera>();
                state.Output.cullingMask = state.OriginalCullingMask;
                state.Output.clearFlags = CameraClearFlags.SolidColor;
                state.Output.allowHDR = false; state.Output.allowMSAA = false; state.Output.allowDynamicResolution = false;
                state.Output.useOcclusionCulling = false;
                var data = state.Output.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false; data.renderShadows = false;
                // The shared transparent pass includes water and effects that sample scene colour/depth.
                data.requiresDepthOption = CameraOverrideOption.On; data.requiresColorOption = CameraOverrideOption.On;
                data.antialiasing = AntialiasingMode.None; data.volumeLayerMask = 0;
                output.AddComponent<WorldResolutionOutput>().Owner = this;
                RenderPipelineManager.beginCameraRendering += BeginCamera;
                RenderPipelineManager.endCameraRendering += EndCamera;
                SceneManager.sceneLoaded += SceneLoaded;
                if (Application.isPlaying) NativeWorldUI.RegisterSceneUI();
            }
            state.Output.targetDisplay = state.World.targetDisplay;
            int outputWidth = Mathf.Max(1, state.Output.pixelWidth), outputHeight = Mathf.Max(1, state.Output.pixelHeight);
            Vector2Int size = Settings.ResolutionForOutput(outputWidth, outputHeight);
            if (!state.Image || state.Image.width != size.x || state.Image.height != size.y)
            {
                if (state.Image) { state.World.targetTexture = state.OriginalTarget; state.Image.Release(); CoreUtils.Destroy(state.Image); }
                var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                state.Image = new RenderTexture(size.x, size.y, 24, format, RenderTextureReadWrite.Default) {
                    name = "Fixed 3D image", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, antiAliasing = 1, useMipMap = false, autoGenerateMips = false, useDynamicScale = false
                };
            }
            if (!state.Image.IsCreated()) state.Image.Create();
            state.World.targetTexture = state.Image;
            // Use the exact display aspect in Adaptive mode: rounding the texture width must not distort geometry.
            state.World.aspect = Settings.Upscaling == WorldUpscaling.Adaptive ? outputWidth / (float)outputHeight : size.x / (float)size.y;
            state.World.allowDynamicResolution = false; state.World.allowMSAA = false;
            state.World.cullingMask = WorldLayerMask;
            state.Output.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex);
            state.Output.depth = state.World.depth + 1;
            state.Output.backgroundColor = Settings.BorderColor;
            state.Output.enabled = state.World.enabled;
            NativeWorldUI.UseCamera(state.World, state.Output);
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode) => NativeWorldUI.RegisterSceneUI();

        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (state == null || (camera != state.World && camera != state.Output)) return;
            if (camera == state.Output)
            {
                // Match the world camera exactly, then place its frustum inside the letterboxed output.
                camera.worldToCameraMatrix = state.World.worldToCameraMatrix;
                camera.scene = state.World.scene;
                camera.nearClipPlane = state.World.nearClipPlane; camera.farClipPlane = state.World.farClipPlane;
                camera.orthographic = state.World.orthographic; camera.orthographicSize = state.World.orthographicSize;
                camera.fieldOfView = state.World.fieldOfView;
                Rect rect = Settings.OutputRect(camera.pixelWidth, camera.pixelHeight);
                var fit = Matrix4x4.identity;
                fit.m00 = rect.width / camera.pixelWidth; fit.m11 = rect.height / camera.pixelHeight;
                fit.m03 = (2 * rect.x + rect.width) / camera.pixelWidth - 1;
                fit.m13 = (2 * rect.y + rect.height) / camera.pixelHeight - 1;
                camera.projectionMatrix = fit * state.World.projectionMatrix;
            }
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!asset) return;
            // URP applies its global Render Scale even to target textures. Scope the override to
            // these two cameras so mobile scale/FSR cannot resample either the world or native UI.
            overrides.Add(new PipelineOverride { Camera = camera, Asset = asset, Scale = asset.renderScale, Filter = asset.upscalingFilter });
            asset.renderScale = 1; asset.upscalingFilter = UpscalingFilterSelection.Point;
        }

        void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (overrides.Count == 0 || overrides[overrides.Count - 1].Camera != camera) return;
            RestorePipeline();
        }

        void RestorePipeline()
        {
            int index = overrides.Count - 1;
            var previous = overrides[index]; overrides.RemoveAt(index);
            if (previous.Asset) { previous.Asset.renderScale = previous.Scale; previous.Asset.upscalingFilter = previous.Filter; }
        }

        void Release()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            SceneManager.sceneLoaded -= SceneLoaded;
            while (overrides.Count > 0) RestorePipeline();
            if (state == null) return;
            if (state.Output) { NativeWorldUI.ReleaseCamera(state.Output); state.Output.enabled = false; CoreUtils.Destroy(state.Output.gameObject); }
            if (state.World)
            {
                if (state.World.targetTexture == state.Image) state.World.targetTexture = state.OriginalTarget;
                if (state.OriginalTarget) state.World.aspect = state.OriginalAspect; else state.World.ResetAspect();
                state.World.allowDynamicResolution = state.OriginalDynamicResolution;
                state.World.allowMSAA = state.OriginalMSAA;
                state.World.cullingMask = state.OriginalCullingMask;
            }
            if (state.Image) { state.Image.Release(); CoreUtils.Destroy(state.Image); }
            state = null;
        }

        public static Vector3 WorldToOutputPoint(Camera camera, Vector3 position)
        {
            if (!camera.TryGetComponent<WorldResolution>(out var resolution) || !resolution.IsActive)
                return camera.WorldToScreenPoint(position);
            Vector3 viewport = camera.WorldToViewportPoint(position);
            Rect rect = resolution.Settings.OutputRect(Screen.width, Screen.height);
            return new Vector3(rect.x + viewport.x * rect.width, rect.y + viewport.y * rect.height, viewport.z);
        }
    }
}
