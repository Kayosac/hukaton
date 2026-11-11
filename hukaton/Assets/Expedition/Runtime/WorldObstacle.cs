using System.Collections;
using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public enum ObstacleKind { Thorns, Lake }

    /// <summary>
    /// A place cards change for the rest of the outing. Thorns burn open with Fire (the change animates, the thicket
    /// burning down, but takes effect immediately). A lake is water: deep water drowns, and the Ice card freezes it in a
    /// radius around the hit through <see cref="IceField"/> — never the whole mesh at once; Fire melts it back.
    /// </summary>
    public sealed class WorldObstacle : MonoBehaviour
    {
        public ObstacleKind kind;
        [Tooltip("Thorns: the solid wall. Lake: the water volume; shallow water is harmless, deep water drowns (ExpeditionSettings.drownDepth).")]
        public Collider barrier;
        public GameObject intact, changed;
        [Tooltip("Lake only: solid surface on the Water layer that cards hit and the player falls through.")]
        public Collider waterSurface;
        [Tooltip("Lake only: an Ice card that hits this water freezes at least this radius, m (0 = the card's own). For a stretch a story step needs crossed with one card.")]
        [Min(0)] public float minimumIceRadius;
        public ExpeditionWorld world;
        [Header("Feedback")]
        public VfxBurst reactVfx;
        [Tooltip("Looping effect while the obstacle transforms (flames over burning thorns).")]
        public VfxBurst loopVfx;
        public SfxCue reactSound;
        public SfxCue loopSound;
        [Min(.1f)] public float transformSeconds = 1.1f;
        public Color tint = Color.white;

        /// <summary>Thorns: burnt open. Lake: some of it is frozen right now (ice that holds, in <see cref="IceField"/>).</summary>
        public bool Resolved => kind == ObstacleKind.Lake ? Iced : burnt;
        /// <summary>Lake: is any of this water under ice that holds?</summary>
        public bool Iced => world && world.ice && HasSurface && world.ice.Covers(SurfaceBounds);

        bool burnt;
        Vector3[] surfaceTriangles;
        Bounds surfaceBounds;
        bool hasSurfaceBounds;
        // Surface triangles bucketed by 1 m squares (start indices into surfaceTriangles) for quick point queries.
        Dictionary<Vector2Int, List<int>> surfaceGrid;
        Renderer surfaceRenderer;
        Vector3 changedScale = Vector3.one, intactScale = Vector3.one;
        bool recorded;
        Renderer[] intactRenderers;
        MaterialPropertyBlock block;

        void Record()
        {
            if (recorded) return;
            recorded = true;
            if (changed) changedScale = changed.transform.localScale;
            intactScale = intact.transform.localScale;
            intactRenderers = intact.GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();
            if (kind == ObstacleKind.Lake) RecordSurface();
        }

        /// <summary>The visible water mesh is the surface the depth is measured from (rivers slope and step).</summary>
        void RecordSurface()
        {
            var filter = intact.GetComponent<MeshFilter>();
            surfaceRenderer = intact.GetComponent<Renderer>();
            var mesh = filter ? filter.sharedMesh : null;
            if (!mesh || !mesh.isReadable) return;
            var vertices = mesh.vertices;
            var indices = mesh.triangles;
            var toWorld = filter.transform.localToWorldMatrix;
            surfaceTriangles = new Vector3[indices.Length];
            for (int i = 0; i < indices.Length; i++) surfaceTriangles[i] = toWorld.MultiplyPoint3x4(vertices[indices[i]]);
            surfaceGrid = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i + 2 < surfaceTriangles.Length; i += 3)
            {
                Vector3 a = surfaceTriangles[i], b = surfaceTriangles[i + 1], c = surfaceTriangles[i + 2];
                int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z))), z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!surfaceGrid.TryGetValue(key, out var list)) surfaceGrid[key] = list = new List<int>();
                        list.Add(i);
                    }
            }
        }

        /// <summary>Lake: does it have a surface the ice can form on (a readable water mesh or the surface collider)?</summary>
        public bool HasSurface
        {
            get
            {
                if (kind != ObstacleKind.Lake || !intact) return false;
                Record();
                return surfaceTriangles != null || waterSurface != null;
            }
        }

        /// <summary>Lake: world box around the water surface (the water mesh, else the surface collider).</summary>
        public Bounds SurfaceBounds
        {
            get
            {
                Record();
                if (!hasSurfaceBounds)
                {
                    // The water mesh never moves once placed: measured once.
                    if (surfaceTriangles != null && surfaceTriangles.Length > 0)
                    {
                        surfaceBounds = new Bounds(surfaceTriangles[0], Vector3.zero);
                        foreach (var v in surfaceTriangles) surfaceBounds.Encapsulate(v);
                        hasSurfaceBounds = true;
                    }
                    else if (waterSurface) return waterSurface.bounds;
                    else return barrier ? barrier.bounds : new Bounds(transform.position, Vector3.zero);
                }
                return surfaceBounds;
            }
        }

        /// <summary>
        /// Lake: the water surface height right above or below <paramref name="point"/>; false beside the water (unlike
        /// <see cref="WaterLevelAt"/>, which falls back to a level for drowning). Used by the ice to follow the shore.
        /// </summary>
        public bool SurfaceAt(Vector3 point, out float level)
        {
            level = 0;
            if (kind != ObstacleKind.Lake) return false;
            Record();
            if (surfaceTriangles != null)
            {
                if (!surfaceGrid.TryGetValue(new Vector2Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.z)), out var starts)) return false;
                float best = float.NegativeInfinity;
                foreach (int i in starts)
                {
                    Vector3 a = surfaceTriangles[i], b = surfaceTriangles[i + 1], c = surfaceTriangles[i + 2];
                    float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(d) < 1e-7f) continue;
                    float u = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / d;
                    float v = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / d;
                    const float edge = -1e-4f;
                    if (u < edge || v < edge || 1 - u - v < edge) continue;
                    best = Mathf.Max(best, u * a.y + v * b.y + (1 - u - v) * c.y);
                }
                if (float.IsNegativeInfinity(best)) return false;
                level = best;
                return true;
            }
            if (!waterSurface || !waterSurface.enabled) return false;
            var box = waterSurface.bounds;
            var ray = new Ray(new Vector3(point.x, box.max.y + 1, point.z), Vector3.down);
            if (!waterSurface.Raycast(ray, out var hit, box.size.y + 2)) return false;
            level = hit.point.y;
            return true;
        }

        /// <summary>Water surface height above <paramref name="point"/>; false when the point is beside the water.</summary>
        public bool WaterLevelAt(Vector3 point, out float level)
        {
            Record();
            if (surfaceTriangles != null) return ExpeditionEffectRules.SurfaceHeight(surfaceTriangles, point.x, point.z, out level);
            // No readable mesh: the lowest level the surface collider allows, so a sloping river never drowns early.
            if (waterSurface && waterSurface.enabled) level = waterSurface.bounds.min.y + .1f;
            else if (surfaceRenderer) level = surfaceRenderer.bounds.max.y;
            else level = barrier.bounds.max.y;
            return true;
        }

        public void ResetForOuting()
        {
            Record();
            StopAllCoroutines();
            burnt = false;
            intact.SetActive(true);
            intact.transform.localScale = intactScale;
            foreach (var r in intactRenderers) r.SetPropertyBlock(null);
            // A lake's old whole-mesh ice (still baked into some prefabs) is never shown: ice lives in the IceField.
            if (changed)
            {
                changed.SetActive(false);
                changed.transform.localScale = changedScale;
            }
            barrier.enabled = true;
            if (waterSurface) waterSurface.enabled = true;
        }

        /// <summary>
        /// Already burnt when its part of the world loads again in the same outing: no animation. A lake's ice is kept
        /// by the <see cref="IceField"/> in world space, so a lake just comes back as water under it.
        /// </summary>
        public void ResolveAtOnce()
        {
            ResetForOuting();
            if (kind == ObstacleKind.Lake) return;
            burnt = true;
            barrier.enabled = false;
            if (waterSurface) waterSurface.enabled = false;
            changed.SetActive(true);
            intact.SetActive(false);
        }

        public float DistanceTo(Vector3 point)
        {
            var reference = waterSurface ? waterSurface : barrier;
            bool wasEnabled = reference.enabled;
            if (!wasEnabled) return float.MaxValue;
            return Vector3.Distance(reference.ClosestPoint(point), point);
        }

        /// <summary>Returns true when this card changed the obstacle.</summary>
        public bool React(CardAction action, Vector3 point)
        {
            Record();
            if (kind == ObstacleKind.Lake)
            {
                // Ice and fire on water go through the field, in a radius around the point.
                if (!world || !world.ice) return false;
                if (action == CardAction.Freeze) return world.ice.Freeze(point, Mathf.Max(DefaultIceRadius, minimumIceRadius));
                if (action == CardAction.Fire) return world.ice.Melt(point, DefaultIceRadius);
                return false;
            }
            if (burnt || action != CardAction.Fire) return false;
            burnt = true;
            StartCoroutine(Burn(point));
            world.Announce("Заросли выгорели — путь открыт", 2.5f);
            return true;
        }

        /// <summary>Radius for a freeze or melt asked of the lake itself (not through a card), m.</summary>
        const float DefaultIceRadius = 2.5f;

        IEnumerator Burn(Vector3 point)
        {
            world.Play(reactSound, point);
            var bounds = barrier.bounds;
            for (int i = 0; i < 3; i++)
            {
                Vector3 along = Vector3.Lerp(bounds.min, bounds.max, (i + .5f) / 3f);
                world.Spawn(reactVfx, new Vector3(along.x, bounds.min.y + .3f, along.z), Quaternion.identity, tint, 1.3f);
            }
            var flames = world.Spawn(loopVfx, new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), Quaternion.identity, tint,
                Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) / 2.5f, .6f, 2.5f));
            if (flames) flames.HoldFor(transformSeconds);
            float time = 0;
            while (time < transformSeconds)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / transformSeconds);
                if (loopSound && world.sfx) world.sfx.Loop(this, loopSound, bounds.center, 1 - k);
                // Branches char and flicker, then collapse.
                Color colour = Color.Lerp(new Color(1f, .55f, .2f), new Color(.12f, .1f, .09f), k) * (1 + Mathf.Sin(time * 40) * .15f * (1 - k));
                foreach (var r in intactRenderers)
                {
                    r.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", colour);
                    r.SetPropertyBlock(block);
                }
                intact.transform.localScale = new Vector3(intactScale.x, intactScale.y * Mathf.Lerp(1, .25f, Ease.InCubic(k)), intactScale.z);
                if (k > .35f && barrier.enabled) { barrier.enabled = false; if (changed) changed.SetActive(true); }
                yield return null;
            }
            if (world.sfx) world.sfx.StopLoop(this);
            if (flames) flames.Release(.8f);
            intact.SetActive(false);
        }

        // Ground only: not the player (Ignore Raycast), the water surface, cards or their artwork.
        const int BedMask = ~((1 << 2) | (1 << 4) | (1 << 8) | (1 << 9));

        /// <summary>Distance from the water surface down to the bed at this point; infinite when no bed is found.</summary>
        public float DepthAt(Vector3 point, float level)
        {
            var from = new Vector3(point.x, level + .05f, point.z);
            return Physics.Raycast(from, Vector3.down, out var hit, 20, BedMask, QueryTriggerInteraction.Ignore)
                ? level - hit.point.y
                : float.PositiveInfinity;
        }

        void OnTriggerStay(Collider other)
        {
            if (kind != ObstacleKind.Lake || !world || !world.Running) return;
            if (other.GetComponentInParent<PlayerVitals>() != world.vitals) return;
            // Wading is free. The moment the feet touch water over a deep bed the player is lost — drown at once,
            // no waiting until the body has sunk. Standing on ice that holds is not in the water.
            Vector3 feet = world.motor.transform.position;
            if (world.ice && world.ice.IsSolidAt(feet)) return;
            if (!WaterLevelAt(feet, out float level) || feet.y > level + .05f) return;
            float limit = world.settings.drownDepth;
            if (level - feet.y >= limit || DepthAt(feet, level) >= limit) world.Drown();
        }
    }
}
