using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Shows how far area cards reach (<see cref="ICardArea"/>), as crooked rings on the ground in the card's colour:
    /// <list type="bullet">
    /// <item>while a throw is charged — a ring where the card will land (the same flight model as the throw, run to
    /// the end; the aiming line itself stays short);</item>
    /// <item>after a landing — a short flash of the area for cards that act at once (impulse, heal, tar…);</item>
    /// <item>while a lasting zone works — its outline (<see cref="ICardAreaOutline"/>: rain, fog, updraft, lure),
    /// fading out when the zone ends.</item>
    /// </list>
    /// Lives on the player beside the <see cref="CardThrower"/>; rings are pooled children.
    /// </summary>
    public sealed class CardAreaDisplay : MonoBehaviour
    {
        public CardThrower thrower;
        public CardAreaRing ringPrefab;

        [Header("Aim")]
        [Range(0, 1)] public float aimAlpha = .9f;
        [Tooltip("The landing is searched this far into the flight, s.")]
        [Min(.5f)] public float simulateSeconds = 6;
        [Tooltip("Flight checked in steps of this length, s.")]
        [Range(.01f, .1f)] public float simulateStep = 1f / 30f;

        [Header("Landing flash (cards that act at once)")]
        [Min(.05f)] public float flashSeconds = 1;
        [Range(0, 1)] public float flashAlpha = .95f;
        [Tooltip("Stroke width at the moment of landing, × normal; settles to normal.")]
        [Min(1)] public float flashPop = 1.6f;

        [Header("Zone outlines")]
        [Range(0, 1)] public float outlineAlpha = .6f;
        [Tooltip("Fade when a zone ends, s.")]
        [Min(.01f)] public float outlineFadeSeconds = .5f;
        [Tooltip("Outline colour when the card that made the zone is not known.")]
        public Color fallbackTint = new Color(1, .95f, .8f);

        struct Fading
        {
            public CardAreaRing ring;
            public Color tint;
            public float start, seconds, alpha, pop;
        }

        struct Outline
        {
            public CardAreaRing ring;
            public Color tint;
        }

        struct Landing
        {
            public Vector3 point;
            public Color tint;
        }

        readonly Stack<CardAreaRing> pool = new Stack<CardAreaRing>();
        readonly List<Fading> fading = new List<Fading>();
        readonly Dictionary<ICardAreaOutline, Outline> outlines = new Dictionary<ICardAreaOutline, Outline>();
        readonly HashSet<ICardAreaOutline> seen = new HashSet<ICardAreaOutline>();
        readonly List<ICardAreaOutline> gone = new List<ICardAreaOutline>();
        readonly List<Landing> landings = new List<Landing>();
        CardAreaRing aimRing;
        ExpeditionWorld subscribed;

        ExpeditionWorld World => thrower ? thrower.world : null;

        void OnEnable() => Subscribe();

        void OnDisable()
        {
            Unsubscribe();
            HideAll();
        }

        void Subscribe()
        {
            var world = World;
            if (!world || subscribed == world) return;
            Unsubscribe();
            world.Impacted += OnImpact;
            subscribed = world;
        }

        void Unsubscribe()
        {
            if (subscribed) subscribed.Impacted -= OnImpact;
            subscribed = null;
        }

        void LateUpdate()
        {
            var world = World;
            if (!world || !world.Running)
            {
                HideAll();
                return;
            }
            Subscribe();
            UpdateAim(world);
            UpdateOutlines(world);
            UpdateFading();
            landings.Clear();
        }

        // ---------------- aim ----------------

        void UpdateAim(ExpeditionWorld world)
        {
            if (TryAim(world, out var point, out float radius, out var tint))
            {
                if (!aimRing) aimRing = Take();
                aimRing.Show(point, radius, WithAlpha(tint, aimAlpha));
            }
            else if (aimRing)
            {
                Give(aimRing);
                aimRing = null;
            }
        }

        bool TryAim(ExpeditionWorld world, out Vector3 point, out float radius, out Color tint)
        {
            point = default;
            radius = 0;
            tint = default;
            var card = thrower.Selected;
            if (!thrower.Preparation.Active || card == null || card.IsReturn) return false;
            var definition = Definition(world, card);
            if (!definition || !(definition.effectAsset is ICardArea area)) return false;
            if (!Land(world, out var hit)) return false;
            if (!area.TryGetArea(definition, card.Level, hit.collider, out radius) || radius <= 0) return false;
            point = hit.point;
            tint = definition.accent;
            return true;
        }

        /// <summary>Where the charged card would land now: the thrower's launch and the shared flight model.</summary>
        bool Land(ExpeditionWorld world, out RaycastHit hit)
        {
            var preparation = thrower.Preparation;
            thrower.ComputeLaunch(preparation.Charge, out var position, out var velocity);
            var spin = new Vector2(preparation.SpinX, preparation.SpinY);
            var clock = default(CardFlight.Clock);
            var settings = world.settings;
            int steps = Mathf.CeilToInt(simulateSeconds / simulateStep);
            for (int i = 0; i < steps; i++)
            {
                Vector3 next = position, nextVelocity = velocity;
                CardFlight.Advance(ref next, ref nextVelocity, ref clock, spin, settings, simulateStep);
                if (CardFlight.Sweep(position, next, settings.cardRadius, out hit)) return true;
                position = next;
                velocity = nextVelocity;
            }
            hit = default;
            return false;
        }

        // ---------------- landings ----------------

        void OnImpact(CardCopy copy, Collider target, Vector3 point)
        {
            var world = World;
            if (!world || copy == null || copy.IsReturn) return;
            var definition = Definition(world, copy);
            if (!definition) return;
            landings.Add(new Landing { point = point, tint = definition.accent });
            if (!(definition.effectAsset is ICardArea area) || area.LeavesOutlinedZone) return;
            if (!area.TryGetArea(definition, copy.Level, target, out float radius) || radius <= 0) return;
            var ring = Take();
            ring.Show(point, radius, WithAlpha(definition.accent, flashAlpha), flashPop);
            fading.Add(new Fading { ring = ring, tint = definition.accent, start = Time.time, seconds = flashSeconds, alpha = flashAlpha, pop = flashPop });
        }

        // ---------------- zone outlines ----------------

        void UpdateOutlines(ExpeditionWorld world)
        {
            seen.Clear();
            foreach (var zone in world.Zones.All)
            {
                if (!(zone is ICardAreaOutline outlined) || !outlined.TryGetOutline(out var centre, out float radius) || radius <= 0) continue;
                seen.Add(outlined);
                if (!outlines.TryGetValue(outlined, out var outline))
                    outline = new Outline { ring = Take(), tint = TintOfNewZone(centre) };
                outline.ring.Show(centre, radius, WithAlpha(outline.tint, outlineAlpha));
                outlines[outlined] = outline;
            }
            gone.Clear();
            foreach (var pair in outlines)
                if (!seen.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (var key in gone)
            {
                var outline = outlines[key];
                outlines.Remove(key);
                fading.Add(new Fading { ring = outline.ring, tint = outline.tint, start = Time.time, seconds = outlineFadeSeconds, alpha = outlineAlpha, pop = 1 });
            }
        }

        /// <summary>A zone that just appeared was made by the card that landed nearest to it this frame.</summary>
        Color TintOfNewZone(Vector3 centre)
        {
            float best = float.MaxValue;
            var tint = fallbackTint;
            foreach (var landing in landings)
            {
                float distance = (landing.point - centre).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    tint = landing.tint;
                }
            }
            return tint;
        }

        // ---------------- fading ----------------

        void UpdateFading()
        {
            for (int i = fading.Count - 1; i >= 0; i--)
            {
                var item = fading[i];
                float t = (Time.time - item.start) / item.seconds;
                if (t >= 1 || !item.ring)
                {
                    if (item.ring) Give(item.ring);
                    fading.RemoveAt(i);
                    continue;
                }
                // Holds, then drops off; the stroke settles from its pop.
                float alpha = item.alpha * (1 - t * t);
                float stroke = Mathf.Lerp(item.pop, 1, Mathf.Clamp01(t * 3));
                item.ring.SetLook(WithAlpha(item.tint, alpha), stroke);
            }
        }

        // ---------------- pool ----------------

        CardAreaRing Take()
        {
            var ring = pool.Count > 0 ? pool.Pop() : Instantiate(ringPrefab, transform);
            ring.gameObject.SetActive(true);
            return ring;
        }

        void Give(CardAreaRing ring)
        {
            if (!ring) return;
            ring.Hide();
            ring.gameObject.SetActive(false);
            pool.Push(ring);
        }

        void HideAll()
        {
            if (aimRing) Give(aimRing);
            aimRing = null;
            foreach (var outline in outlines.Values) Give(outline.ring);
            outlines.Clear();
            foreach (var item in fading) Give(item.ring);
            fading.Clear();
            landings.Clear();
        }

        string cachedId;
        CardDefinition cachedDefinition;

        // The aim asks every frame: keep the last lookup (the catalogue search allocates).
        CardDefinition Definition(ExpeditionWorld world, CardCopy card)
        {
            if (card.Spec.Id == cachedId && cachedDefinition) return cachedDefinition;
            cachedDefinition = world.workshop && world.workshop.config ? world.workshop.config.Find(card.Spec.Id) : null;
            cachedId = cachedDefinition ? card.Spec.Id : null;
            return cachedDefinition;
        }

        static Color WithAlpha(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, alpha);
    }
}
