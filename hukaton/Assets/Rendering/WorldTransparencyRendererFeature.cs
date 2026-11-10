using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Hukaton.Rendering
{
    /// <summary>
    /// Routes scene transparency to the presentation camera while World Resolution is active.
    /// On ordinary cameras (including Scene View and when the effect is disabled), preserves
    /// the usual transparent pass. The renderer's built-in Transparent Layer Mask must be zero.
    /// </summary>
    [DisallowMultipleRendererFeature("Hukaton World Transparency")]
    public sealed class WorldTransparencyRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("Обычная маска прозрачных объектов. В Renderer встроенный Transparent Layer Mask должен быть Nothing: этот проход заменяет его.")]
        public LayerMask Layers = -1;
        [System.NonSerialized] RenderObjectsPass pass;

        public override void Create()
        {
            pass = new RenderObjectsPass("World transparency (without resolution split)",
                RenderPassEvent.BeforeRenderingTransparents, null, RenderQueueType.Transparent,
                Layers, new RenderObjects.CustomCameraSettings());
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (camera.TryGetComponent<WorldResolution>(out var resolution) && resolution.IsActive) return;
            renderer.EnqueuePass(pass);
        }
    }
}
