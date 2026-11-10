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
        [System.NonSerialized] NativeWorldUIPass depthPass, opaqueArtwork, sceneTransparency, opaqueUI, transparentUI;

        public void Configure(Shader shader) { presentationShader = shader; Create(); }

        public override void Create()
        {
            Dispose(true);
            if (presentationShader) runtimeMaterial = CoreUtils.CreateEngineMaterial(presentationShader);
            passMaterial = runtimeMaterial; fetchColorBuffer = false; requirements = ScriptableRenderPassInput.None;
            // Present the opaque world before URP copies its colour/depth for transparent shaders.
            injectionPoint = (InjectionPoint)RenderPassEvent.BeforeRenderingOpaques;
            bindDepthStencilAttachment = false; passIndex = 0;
            base.Create(); initialized = true;
            depthPass = new NativeWorldUIPass("Native world occlusion (depth only)", (RenderPassEvent)251, true, false);
            opaqueArtwork = new NativeWorldUIPass("Native artwork (opaque cards)", (RenderPassEvent)252, false, false, true);
            sceneTransparency = new NativeWorldUIPass("Sorted scene transparency (sprites, effects and hand)",
                RenderPassEvent.BeforeRenderingTransparents, false, true, true);
            opaqueUI = new NativeWorldUIPass("Native world UI (opaque)", (RenderPassEvent)601, false, false);
            transparentUI = new NativeWorldUIPass("Native world UI (text and canvas)", (RenderPassEvent)602, false, true);
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
            depthPass.Setup(owner); opaqueArtwork.Setup(owner); sceneTransparency.Setup(owner);
            opaqueUI.Setup(owner); transparentUI.Setup(owner);
            renderer.EnqueuePass(depthPass); renderer.EnqueuePass(opaqueArtwork); renderer.EnqueuePass(sceneTransparency);
            renderer.EnqueuePass(opaqueUI); renderer.EnqueuePass(transparentUI);
        }

        protected override void Dispose(bool disposing)
        {
            if (initialized) base.Dispose(disposing);
            initialized = false;
            CoreUtils.Destroy(runtimeMaterial); runtimeMaterial = null; passMaterial = null;
        }
    }
}
