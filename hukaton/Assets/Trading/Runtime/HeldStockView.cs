using System.Collections.Generic;
using Hakaton.Loot;
using UnityEngine;

namespace Hakaton.Trading.Unity
{
    /// <summary>Frames the selected stock in the camera's lower-right foreground.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(600)]
    public sealed class HeldStockView : MonoBehaviour
    {
        public Camera viewCamera;
        public Transform visualRoot;
        [Tooltip("Maximum fraction of the view occupied by the item. Sprite framing uses its imported outline, excluding transparent margins.")]
        public Vector2 viewportSize = new Vector2(.32f, .46f);
        [Tooltip("Inset from the right and bottom edges, as fractions of the view.")]
        public Vector2 viewportInset = new Vector2(.025f, .015f);
        [Min(.1f)] public float distance = .45f;

        GameObject visual;
        LootItem shownItem;
        Vector3 sourceScale;
        Bounds localBounds;

        public bool IsConfigured => viewCamera && visualRoot;

        public void Show(LootCatalog catalog, LootItem item)
        {
            if (!IsConfigured || !catalog || !item) return;
            if (visual && shownItem == item) return;
            if (visual)
            {
                visual.SetActive(false);
                Destroy(visual);
            }
            shownItem = item;
            visual = catalog.CreateVisual(item, visualRoot, true);
            if (!visual) return;

            // A held drawing is parallel to the image plane, including when looking up/down.
            // Keep the exact materials of the shelf/world visual.
            foreach (var drawing in visual.GetComponentsInChildren<LootSpriteView>())
            {
                drawing.faceCamera = false;
                drawing.transform.localRotation = Quaternion.identity;
            }
            foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            visual.transform.localPosition = Vector3.zero;
            sourceScale = visual.transform.localScale;
            localBounds = Measure(visual.GetComponentsInChildren<Renderer>(), visualRoot);
            Frame();
        }

        void LateUpdate() => Frame();

        void Frame()
        {
            if (!visual || !viewCamera || !visualRoot) return;
            float depth = Mathf.Max(distance, viewCamera.nearClipPlane + .1f);
            float height = viewCamera.orthographic ? 2 * viewCamera.orthographicSize
                : 2 * depth * Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            float width = height * viewCamera.aspect;
            float scale = Mathf.Min(width * viewportSize.x / Mathf.Max(.0001f, localBounds.size.x),
                height * viewportSize.y / Mathf.Max(.0001f, localBounds.size.y));

            var size = localBounds.size * scale;
            var centre = new Vector3(1 - viewportInset.x - size.x / (2 * width),
                viewportInset.y + size.y / (2 * height), depth);
            if (!viewCamera.orthographic && localBounds.extents.z > 0)
            {
                // Fit the whole model inside this off-centre frustum. Its closest corners
                // project larger than its centre; a flat-sprite fit would cut them off.
                float right = 1 - viewportInset.x, bottom = viewportInset.y;
                float horizontalSlope = width / depth * Mathf.Max(Mathf.Abs(right - .5f), Mathf.Abs(right - size.x / width - .5f));
                float verticalSlope = height / depth * Mathf.Max(Mathf.Abs(bottom - .5f), Mathf.Abs(bottom + size.y / height - .5f));
                var extents = localBounds.extents;
                scale = Mathf.Min(scale, size.x * .5f / Mathf.Max(.0001f, extents.x + horizontalSlope * extents.z));
                scale = Mathf.Min(scale, size.y * .5f / Mathf.Max(.0001f, extents.y + verticalSlope * extents.z));
                scale = Mathf.Min(scale, (depth - viewCamera.nearClipPlane - .02f) / extents.z);
            }
            transform.SetPositionAndRotation(viewCamera.ViewportToWorldPoint(centre), viewCamera.transform.rotation);
            visual.transform.localScale = sourceScale * scale;
            visual.transform.localPosition = -localBounds.center * scale;
        }

        static Bounds Measure(Renderer[] renderers, Transform relativeTo)
        {
            var bounds = new Bounds();
            bool first = true;
            foreach (var renderer in renderers)
            {
                var local = renderer.localBounds;
                if (renderer is SpriteRenderer sprite && sprite.sprite && sprite.sprite.GetPhysicsShapeCount() > 0)
                {
                    var outline = new List<Vector2>();
                    bool firstPoint = true;
                    for (int shape = 0; shape < sprite.sprite.GetPhysicsShapeCount(); shape++)
                    {
                        sprite.sprite.GetPhysicsShape(shape, outline);
                        foreach (var point in outline)
                        {
                            var p = new Vector3(sprite.flipX ? -point.x : point.x, sprite.flipY ? -point.y : point.y, 0);
                            if (firstPoint) { local = new Bounds(p, Vector3.zero); firstPoint = false; }
                            else local.Encapsulate(p);
                        }
                    }
                    // The imported contour is simplified. Leave a little breathing room for edge pixels.
                    if (!firstPoint) local.Expand(new Vector3(local.size.x * .04f, local.size.y * .04f, 0));
                }
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var point = relativeTo.InverseTransformPoint(renderer.transform.TransformPoint(local.center + offset));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            return bounds;
        }
    }
}
