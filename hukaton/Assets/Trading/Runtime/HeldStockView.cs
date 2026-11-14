using System.Collections.Generic;
using Hakaton.Loot;
using UnityEngine;

namespace Hakaton.Trading.Unity
{
    /// <summary>
    /// Frames the selected stock in the camera's lower-right foreground. Drawings and models fill the same share of the
    /// view: a drawing by its imported outline, a model by its silhouette as the camera sees it (its mesh points in
    /// perspective), so a model takes about as much of the screen as a drawing. A model is held close to the eye: its
    /// nearest point just in front of it, so it never pokes into a shelf or a wall the player stands against.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(600)]
    public sealed class HeldStockView : MonoBehaviour
    {
        public Camera viewCamera;
        public Transform visualRoot;
        [Tooltip("Maximum fraction of the view occupied by the item: drawings by their imported outline (transparent " +
                 "margins excluded), models by their silhouette.")]
        public Vector2 viewportSize = new Vector2(.32f, .46f);
        [Tooltip("Inset from the right and bottom edges, as fractions of the view.")]
        public Vector2 viewportInset = new Vector2(.025f, .015f);
        [Tooltip("Drawings: how far in front of the eye they are held.")]
        [Min(.1f)] public float distance = .45f;
        [Tooltip("Models: how far in front of the eye their nearest point is (kept past the camera's near plane).")]
        [Min(.01f)] public float modelNearest = .14f;

        GameObject visual;
        LootItem shownItem;
        Vector3 sourceScale;
        Bounds localBounds;
        // A model's silhouette points, relative to its bounds' centre, at its source scale (visualRoot space).
        readonly List<Vector3> silhouette = new List<Vector3>();
        bool model;
        // The model's fit for a view (normalised to depth 1): scale per unit of depth and the centre's offsets.
        float fittedFov = -1, fittedAspect = -1, fitScale, fitNearest;
        Vector2 fitCentre;

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
            var drawings = visual.GetComponentsInChildren<LootSpriteView>();
            foreach (var drawing in drawings)
            {
                drawing.faceCamera = false;
                drawing.transform.localRotation = Quaternion.identity;
            }
            foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
            var renderers = visual.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            visual.transform.localPosition = Vector3.zero;
            sourceScale = visual.transform.localScale;
            localBounds = Measure(renderers, visualRoot);
            model = drawings.Length == 0 && CollectSilhouette(visual, visualRoot, localBounds.center, silhouette);
            fittedFov = -1;
            Frame();
        }

        void LateUpdate() => Frame();

        void Frame()
        {
            if (!visual || !viewCamera || !visualRoot) return;
            if (model && !viewCamera.orthographic) FrameModel();
            else FrameDrawing();
        }

        void FrameDrawing()
        {
            float depth = Mathf.Max(distance, viewCamera.nearClipPlane + .1f);
            float height = viewCamera.orthographic ? 2 * viewCamera.orthographicSize
                : 2 * depth * Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            float width = height * viewCamera.aspect;
            float scale = Mathf.Min(width * viewportSize.x / Mathf.Max(.0001f, localBounds.size.x),
                height * viewportSize.y / Mathf.Max(.0001f, localBounds.size.y));

            var size = localBounds.size * scale;
            var centre = new Vector3(1 - viewportInset.x - size.x / (2 * width),
                viewportInset.y + size.y / (2 * height), depth);
            transform.SetPositionAndRotation(viewCamera.ViewportToWorldPoint(centre), viewCamera.transform.rotation);
            visual.transform.localScale = sourceScale * scale;
            visual.transform.localPosition = -localBounds.center * scale;
        }

        /// <summary>
        /// The model in perspective: the biggest size whose silhouette fits the frame in the lower-right corner, then the
        /// whole arrangement brought close (the picture does not change with distance when size and distance scale
        /// together). Refitted only when the view's angle or shape changes.
        /// </summary>
        void FrameModel()
        {
            float fov = viewCamera.fieldOfView, aspect = viewCamera.aspect;
            if (Mathf.Abs(fov - fittedFov) > .25f || Mathf.Abs(aspect - fittedAspect) > .001f)
            {
                fittedFov = fov;
                fittedAspect = aspect;
                float tanV = Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
                Fit(silhouette, tanV * aspect, tanV, viewportSize, viewportInset, out fitScale, out fitCentre, out fitNearest);
            }
            float nearest = Mathf.Max(modelNearest, viewCamera.nearClipPlane + .02f);
            float depth = nearest / Mathf.Max(.05f, fitNearest);
            float scale = fitScale * depth;
            var eye = viewCamera.transform;
            transform.SetPositionAndRotation(eye.TransformPoint(new Vector3(fitCentre.x * depth, fitCentre.y * depth, depth)), eye.rotation);
            visual.transform.localScale = sourceScale * scale;
            visual.transform.localPosition = -localBounds.center * scale;
        }

        /// <summary>
        /// Fits silhouette points (camera axes, around the model's centre) at depth 1 into the frame: the largest scale
        /// whose projection fits <paramref name="size"/> with its right and bottom edges on the inset. Returns that
        /// scale, the centre's offsets and the depth of the nearest point (all per unit of depth).
        /// </summary>
        public static void Fit(IReadOnlyList<Vector3> points, float tanH, float tanV, Vector2 size, Vector2 inset,
            out float scale, out Vector2 centre, out float nearest)
        {
            float minZ = 0;
            foreach (var p in points) minZ = Mathf.Min(minZ, p.z);
            // Never so big that the nearest point comes up to the eye.
            float high = minZ < 0 ? .8f / -minZ : 100, low = 0;
            float bestA = 0, bestB = 0;
            for (int step = 0; step < 28; step++)
            {
                float k = (low + high) * .5f;
                if (Spans(points, k, tanH, tanV, inset, out float width, out float height, out float a, out float b)
                    && width <= size.x && height <= size.y)
                {
                    low = k;
                    bestA = a;
                    bestB = b;
                }
                else high = k;
            }
            if (low <= 0) Spans(points, 0, tanH, tanV, inset, out _, out _, out bestA, out bestB);
            scale = low;
            centre = new Vector2(bestA, bestB);
            nearest = 1 + low * minZ;
        }

        /// <summary>
        /// At scale <paramref name="k"/> (depth 1): the centre's offsets (a, b) that put the silhouette's right edge and
        /// bottom edge on the inset, and the silhouette's resulting width and height as fractions of the view.
        /// </summary>
        static bool Spans(IReadOnlyList<Vector3> points, float k, float tanH, float tanV, Vector2 inset,
            out float width, out float height, out float a, out float b)
        {
            float right = 1 - inset.x - .5f, bottom = inset.y - .5f;
            a = float.MaxValue;
            b = float.MinValue;
            foreach (var p in points)
            {
                float w = 1 + k * p.z;
                if (w <= .01f) { width = height = float.MaxValue; return false; }
                a = Mathf.Min(a, right * w * 2 * tanH - k * p.x);
                b = Mathf.Max(b, bottom * w * 2 * tanV - k * p.y);
            }
            float left = float.MaxValue, top = float.MinValue;
            foreach (var p in points)
            {
                float w = 1 + k * p.z;
                left = Mathf.Min(left, (a + k * p.x) / (w * 2 * tanH));
                top = Mathf.Max(top, (b + k * p.y) / (w * 2 * tanV));
            }
            width = right - left;
            height = top - bottom;
            return points.Count > 0;
        }

        /// <summary>
        /// A model's silhouette points in <paramref name="relativeTo"/>'s space around <paramref name="centre"/>: of its
        /// meshes' vertices (the bounds' corners of a mesh that cannot be read), the outermost ones in many directions —
        /// the hull a perspective view can see the edge of, a few hundred points at most. False when it has no mesh.
        /// </summary>
        static bool CollectSilhouette(GameObject visual, Transform relativeTo, Vector3 centre, List<Vector3> points)
        {
            points.Clear();
            var all = new List<Vector3>();
            var vertices = new List<Vector3>();
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = null;
                if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
                else if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                if (!mesh || !renderer.enabled) continue;
                var toRoot = relativeTo.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                vertices.Clear();
                if (mesh.isReadable) mesh.GetVertices(vertices);
                else
                {
                    var box = mesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                        vertices.Add(box.center + Vector3.Scale(box.extents, new Vector3(
                            (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                }
                foreach (var v in vertices) all.Add(toRoot.MultiplyPoint3x4(v) - centre);
            }
            if (all.Count == 0) return false;
            var hull = new HashSet<int>();
            foreach (var direction in Directions)
            {
                int best = 0;
                float most = float.MinValue;
                for (int i = 0; i < all.Count; i++)
                {
                    float along = Vector3.Dot(all[i], direction);
                    if (along > most) { most = along; best = i; }
                }
                hull.Add(best);
            }
            foreach (int i in hull) points.Add(all[i]);
            return true;
        }

        /// <summary>Evenly spread directions (a Fibonacci sphere) and the six axes.</summary>
        static readonly Vector3[] Directions = MakeDirections(250);

        static Vector3[] MakeDirections(int count)
        {
            var list = new List<Vector3> { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            float golden = Mathf.PI * (3 - Mathf.Sqrt(5));
            for (int i = 0; i < count; i++)
            {
                float y = 1 - (i + .5f) * 2 / count, r = Mathf.Sqrt(1 - y * y), turn = golden * i;
                list.Add(new Vector3(Mathf.Cos(turn) * r, y, Mathf.Sin(turn) * r));
            }
            return list.ToArray();
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
