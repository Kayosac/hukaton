using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hukaton.Rendering
{
    [DisallowMultipleRendererFeature("Hukaton World Presentation")]
    public sealed class WorldPresentationRendererFeature : FullScreenPassRendererFeature
    {
        [SerializeField, HideInInspector] Shader presentationShader;
        [System.NonSerialized] Material runtimeMaterial;
        [System.NonSerialized] bool initialized;
        [System.NonSerialized] NativeWorldUIPass depthPass, opaqueUI, transparentUI;

        public void Configure(Shader shader) { presentationShader = shader; Create(); }

        public override void Create()
        {
            Dispose(true);
            if (presentationShader) runtimeMaterial = CoreUtils.CreateEngineMaterial(presentationShader);
            passMaterial = runtimeMaterial; fetchColorBuffer = false; requirements = ScriptableRenderPassInput.None;
            injectionPoint = InjectionPoint.AfterRenderingPostProcessing;
            bindDepthStencilAttachment = false; passIndex = 0;
            base.Create(); initialized = true;
            depthPass = new NativeWorldUIPass("World UI occlusion (depth only)", 1, true, false, runtimeMaterial);
            opaqueUI = new NativeWorldUIPass("Native world UI (opaque)", 2, false, false);
            transparentUI = new NativeWorldUIPass("Native world UI (text and canvas)", 3, false, true);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!runtimeMaterial || !renderingData.cameraData.camera.TryGetComponent<WorldResolutionOutput>(out var output)) return;
            var owner = output.Owner;
            if (!owner || !owner.IsActive) return;
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            Rect rect = owner.Settings.OutputRect(descriptor.width, descriptor.height);
            runtimeMaterial.SetTexture("_WorldImage", owner.Image);
            runtimeMaterial.SetVector("_WorldSize", new Vector4(owner.Image.width, owner.Image.height, 0, 0));
            runtimeMaterial.SetVector("_OutputRect", new Vector4(rect.x / descriptor.width, rect.y / descriptor.height, rect.width / descriptor.width, rect.height / descriptor.height));
            runtimeMaterial.SetColor("_BorderColor", QualitySettings.activeColorSpace == ColorSpace.Linear ? owner.Settings.BorderColor.linear : owner.Settings.BorderColor);
            base.AddRenderPasses(renderer, ref renderingData);
            depthPass.Setup(owner); opaqueUI.Setup(owner); transparentUI.Setup(owner);
            renderer.EnqueuePass(depthPass); renderer.EnqueuePass(opaqueUI); renderer.EnqueuePass(transparentUI);
        }

        protected override void Dispose(bool disposing)
        {
            if (initialized) base.Dispose(disposing);
            initialized = false;
            CoreUtils.Destroy(runtimeMaterial); runtimeMaterial = null; passMaterial = null;
        }
    }
}
