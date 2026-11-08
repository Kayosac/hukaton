using System.Collections;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public enum ObstacleKind { Thorns, Lake }

    /// <summary>
    /// A place cards change for the rest of the outing. Thorns burn open with Fire; the lake freezes with Ice.
    /// The change animates (ice spreading, thicket burning down) but takes effect immediately.
    /// </summary>
    public sealed class WorldObstacle : MonoBehaviour
    {
        public ObstacleKind kind;
        [Tooltip("Thorns: the solid wall. Lake: the deep-water hazard trigger.")]
        public Collider barrier;
        public GameObject intact, changed;
        [Tooltip("Lake only: solid surface on the Water layer that cards hit and the player falls through.")]
        public Collider waterSurface;
        public ExpeditionWorld world;
        [Header("Feedback")]
        public VfxBurst reactVfx;
        [Tooltip("Looping effect while the obstacle transforms (flames over burning thorns).")]
        public VfxBurst loopVfx;
        public SfxCue reactSound;
        public SfxCue loopSound;
        [Min(.1f)] public float transformSeconds = 1.1f;
        public Color tint = Color.white;

        public bool Resolved { get; private set; }
        float nextHazard;
        Vector3 changedScale = Vector3.one, intactScale = Vector3.one;
        bool recorded;
        Renderer[] intactRenderers;
        MaterialPropertyBlock block;

        void Record()
        {
            if (recorded) return;
            recorded = true;
            changedScale = changed.transform.localScale;
            intactScale = intact.transform.localScale;
            intactRenderers = intact.GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();
        }

        public void ResetForOuting()
        {
            Record();
            StopAllCoroutines();
            Resolved = false;
            intact.SetActive(true);
            intact.transform.localScale = intactScale;
            foreach (var r in intactRenderers) r.SetPropertyBlock(null);
            changed.SetActive(false);
            changed.transform.localScale = changedScale;
            barrier.enabled = true;
            if (waterSurface) waterSurface.enabled = true;
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
            if (Resolved) return false;
            bool matches = kind == ObstacleKind.Thorns ? action == CardAction.Fire : action == CardAction.Freeze;
            if (!matches) return false;
            Resolved = true;
            StartCoroutine(kind == ObstacleKind.Lake ? Freeze(point) : Burn(point));
            world.Announce(kind == ObstacleKind.Lake ? "Лёд выдержит до конца вылазки" : "Заросли выгорели — путь открыт", 2.5f);
            return true;
        }

        IEnumerator Freeze(Vector3 point)
        {
            barrier.enabled = false;
            if (waterSurface) waterSurface.enabled = false;
            changed.SetActive(true);
            world.Play(reactSound, point);
            world.Spawn(reactVfx, point, Quaternion.identity, tint, 1.2f);
            world.Spawn(reactVfx, changed.transform.position + Vector3.up * .2f, Quaternion.identity, tint, 2f);
            float time = 0;
            while (time < transformSeconds)
            {
                time += Time.deltaTime;
                float k = Ease.OutBack(time / transformSeconds, 1.1f);
                changed.transform.localScale = new Vector3(changedScale.x * k, changedScale.y, changedScale.z * k);
                yield return null;
            }
            changed.transform.localScale = changedScale;
            intact.SetActive(false);
        }

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
                if (k > .35f && barrier.enabled) { barrier.enabled = false; changed.SetActive(true); }
                yield return null;
            }
            if (world.sfx) world.sfx.StopLoop(this);
            if (flames) flames.Release(.8f);
            intact.SetActive(false);
        }

        void OnTriggerStay(Collider other)
        {
            if (kind != ObstacleKind.Lake || Resolved || Time.time < nextHazard || !world.Running) return;
            if (other.GetComponentInParent<PlayerVitals>() != world.vitals) return;
            nextHazard = Time.time + 1.2f;
            world.vitals.Hurt(1, Vector3.up * 5, null);
            world.Announce("Глубокая вода. Заморозьте поверхность", 2.5f);
        }
    }
}
