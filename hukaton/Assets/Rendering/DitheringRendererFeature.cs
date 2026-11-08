using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hukaton.Rendering
{
    /// <summary>Uses URP's fullscreen pass for both Render Graph and Compatibility Mode.</summary>
    [DisallowMultipleRendererFeature("Hukaton Dithering")]
    public sealed class DitheringRendererFeature : FullScreenPassRendererFeature
    {
        public DitheringSettings Settings;
        [SerializeField, HideInInspector] Shader ditheringShader;
        // Unity hot reload can serialize private fields unless explicitly excluded. These must
        // reset together with the base class's non-serializable render pass after a reload.
        [System.NonSerialized] Material runtimeMaterial;
        [System.NonSerialized] bool initialized;

        public void Configure(DitheringSettings settings, Shader shader)
        {
            Settings = settings;
            ditheringShader = shader;
            Create();
        }

        public override void Create()
        {
            ReleaseResources();
            // Keep the material private to this renderer; editing tuning never mutates an asset material.
            if (ditheringShader) runtimeMaterial = CoreUtils.CreateEngineMaterial(ditheringShader);
            passMaterial = runtimeMaterial;
            injectionPoint = InjectionPoint.AfterRenderingPostProcessing;
            fetchColorBuffer = true;
            requirements = ScriptableRenderPassInput.None;
            bindDepthStencilAttachment = false;
            passIndex = 0;
            base.Create();
            initialized = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!Settings || !Settings.IsActive || !runtimeMaterial) return;
            ref var cameraData = ref renderingData.cameraData;
            if (cameraData.cameraType != CameraType.Game &&
                !(cameraData.cameraType == CameraType.SceneView && Settings.ShowInSceneView)) return;
            // Apply once to the finished camera stack, after transparent effects and color grading.
            if (!cameraData.resolveFinalTarget) return;
            var descriptor = cameraData.cameraTargetDescriptor;
            Settings.ApplyTo(runtimeMaterial, new Vector2(
                descriptor.width / (float)Mathf.Max(1, cameraData.camera.pixelWidth),
                descriptor.height / (float)Mathf.Max(1, cameraData.camera.pixelHeight)));
            base.AddRenderPasses(renderer, ref renderingData);
        }

        protected override void Dispose(bool disposing) => ReleaseResources();

        void ReleaseResources()
        {
            if (initialized) base.Dispose(true);
            initialized = false;
            CoreUtils.Destroy(runtimeMaterial);
            runtimeMaterial = null;
            passMaterial = null;
        }
    }
}
