using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Hukaton.Rendering
{
    /// <summary>Native-resolution depth-only occlusion or UI, clipped to the displayed world's viewport.</summary>
    public sealed class NativeWorldUIPass : ScriptableRenderPass
    {
        static readonly ShaderTagId[] Tags = { new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit") };
        readonly bool depthOnly, transparent;
        readonly Material depthMaterial;
        WorldResolution owner;

        public NativeWorldUIPass(string name, int order, bool depthOnly, bool transparent, Material material = null)
        {
            profilingSampler = new ProfilingSampler(name);
            renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.AfterRenderingPostProcessing + order);
            this.depthOnly = depthOnly; this.transparent = transparent; depthMaterial = material;
        }
        public void Setup(WorldResolution source) => owner = source;

        RendererListDesc Description(CullingResults culling, Camera camera)
        {
            var queue = transparent ? RenderQueueRange.transparent : RenderQueueRange.opaque;
            // Background sky meshes have no surface depth and must not occlude the UI.
            if (depthOnly) queue.lowerBound = (int)RenderQueue.Geometry;
            return new RendererListDesc(Tags, culling, camera) {
                layerMask = depthOnly ? owner.WorldLayerMask : NativeWorldUI.Mask,
                renderQueueRange = queue,
                sortingCriteria = transparent ? SortingCriteria.CommonTransparent : SortingCriteria.CommonOpaque,
                rendererConfiguration = PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.ReflectionProbes,
                overrideMaterial = depthOnly ? depthMaterial : null,
                overrideMaterialPassIndex = depthOnly ? 1 : 0,
                stateBlock = new RenderStateBlock(RenderStateMask.Depth) { depthState = new DepthState(!transparent, CompareFunction.LessEqual) }
            };
        }

        sealed class PassData { public RendererListHandle List; public Rect Rect; }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            using (var builder = graph.AddRasterRenderPass<PassData>(passName, out var data, profilingSampler))
            {
                data.List = graph.CreateRendererList(Description(rendering.cullResults, camera.camera));
                data.Rect = owner.Settings.OutputRect(camera.cameraTargetDescriptor.width, camera.cameraTargetDescriptor.height);
                builder.UseRendererList(data.List);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData pass, RasterGraphContext context) => {
                    context.cmd.EnableScissorRect(pass.Rect);
                    context.cmd.DrawRendererList(pass.List);
                    context.cmd.DisableScissorRect();
                });
            }
        }

        [System.Obsolete("Compatibility Mode rendering path")]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData;
            var list = context.CreateRendererList(Description(renderingData.cullResults, camera.camera));
            var commands = CommandBufferPool.Get(passName);
            CoreUtils.SetRenderTarget(commands, camera.renderer.cameraColorTargetHandle, camera.renderer.cameraDepthTargetHandle);
            commands.EnableScissorRect(owner.Settings.OutputRect(camera.cameraTargetDescriptor.width, camera.cameraTargetDescriptor.height));
            commands.DrawRendererList(list); commands.DisableScissorRect();
            context.ExecuteCommandBuffer(commands); CommandBufferPool.Release(commands);
        }
    }
}
