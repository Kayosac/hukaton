using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Hukaton.Rendering
{
    /// <summary>Native-resolution world occlusion, artwork or UI, clipped to the displayed world's viewport.</summary>
    public sealed class NativeWorldUIPass : ScriptableRenderPass
    {
        static readonly ShaderTagId[] Tags = { new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit") };
        static readonly ShaderTagId[] DepthTags = { new ShaderTagId("DepthOnly"), new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit") };
        readonly bool depthOnly, transparent, artwork;
        WorldResolution owner;

        public NativeWorldUIPass(string name, RenderPassEvent evt, bool depthOnly, bool transparent, bool artwork = false)
        {
            profilingSampler = new ProfilingSampler(name);
            renderPassEvent = evt;
            this.depthOnly = depthOnly; this.transparent = transparent; this.artwork = artwork;
        }
        public void Setup(WorldResolution source) => owner = source;

        RendererListDesc Description(CullingResults culling, Camera camera, PerObjectData perObjectData)
        {
            var queue = transparent ? RenderQueueRange.transparent : RenderQueueRange.opaque;
            // Background sky meshes have no surface depth and must not occlude the UI.
            if (depthOnly) queue.lowerBound = (int)RenderQueue.Geometry;
            // Keep each occluder's own alpha clipping and vertex animation (foliage, fences, etc.).
            // Prefer its DepthOnly pass, with a colour-masked forward pass as fallback.
            var state = new RenderStateBlock(RenderStateMask.Nothing);
            if (depthOnly || !artwork)
            {
                state.mask = RenderStateMask.Depth;
                state.depthState = new DepthState(!transparent, CompareFunction.LessEqual);
            }
            if (depthOnly)
            {
                state.mask |= RenderStateMask.Blend;
                state.blendState = new BlendState { blendState0 = new RenderTargetBlendState((ColorWriteMask)0) };
            }
            // Artwork retains material depth rules: world cards are occluded, hand cards use ZTest Always.
            return new RendererListDesc(depthOnly ? DepthTags : Tags, culling, camera) {
                // All scene transparencies must share ONE sorted list: a separate list for sprites
                // always puts them above foreground fog, particles and trajectory lines.
                layerMask = depthOnly ? owner.WorldLayerMask : artwork
                    ? transparent ? owner.SceneTransparencyLayerMask : owner.NativeArtworkLayerMask
                    : NativeWorldUI.Mask,
                renderQueueRange = queue,
                sortingCriteria = transparent ? SortingCriteria.CommonTransparent : SortingCriteria.CommonOpaque,
                rendererConfiguration = perObjectData,
                stateBlock = state
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
                data.List = graph.CreateRendererList(Description(rendering.cullResults, camera.camera, rendering.perObjectData));
                data.Rect = owner.Settings.OutputRect(camera.cameraTargetDescriptor.width, camera.cameraTargetDescriptor.height);
                builder.UseRendererList(data.List);
                // Water and soft particles can sample URP's opaque colour and depth copies.
                builder.UseAllGlobalTextures(true);
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
            var list = context.CreateRendererList(Description(renderingData.cullResults, camera.camera, renderingData.perObjectData));
            var commands = CommandBufferPool.Get(passName);
            CoreUtils.SetRenderTarget(commands, camera.renderer.cameraColorTargetHandle, camera.renderer.cameraDepthTargetHandle);
            commands.EnableScissorRect(owner.Settings.OutputRect(camera.cameraTargetDescriptor.width, camera.cameraTargetDescriptor.height));
            commands.DrawRendererList(list); commands.DisableScissorRect();
            context.ExecuteCommandBuffer(commands); CommandBufferPool.Release(commands);
        }
    }
}
