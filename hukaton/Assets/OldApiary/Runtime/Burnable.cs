using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.OldApiary
{
    /// <summary>What is left after the fire: <see cref="Ash"/> burns away (until the outing ends), <see cref="Char"/> stays, black.</summary>
    public enum BurnStyle { Ash, Char }

    /// <summary>
    /// A thing of the Old Apiary that Fire or Lightning sets alight: grass, sticks, sacks, crates, hives, fences, logs.
    /// It burns for <see cref="seconds"/>, passes the flames on to burnables and thorn thickets close by, then burns away
    /// or stays charred. The <see cref="BurnableField"/> of the level runs the fire; this component answers the cards:
    /// it is a card zone (so a Fire landing next to grass without colliders still reaches it), ignitable and washable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Burnable : MonoBehaviour, ICardZone, IIgnitable, IWashable
    {
        [Tooltip("The level's fire runner.")]
        public BurnableField field;
        public BurnStyle style = BurnStyle.Ash;
        [Tooltip("How long it burns, s.")]
        [Min(.2f)] public float seconds = 1.5f;

        Renderer[] renderers;
        Rigidbody body;
        Bounds cached;
        bool hasCached;

        internal Renderer[] Renderers => renderers ??= GetComponentsInChildren<Renderer>(true);
        internal Rigidbody Body => body ? body : body = GetComponent<Rigidbody>();

        /// <summary>World box around the look (live while a loose prop is moving, cached otherwise).</summary>
        internal Bounds Look
        {
            get
            {
                if (hasCached && (!Body || Body.IsSleeping() || Body.isKinematic)) return cached;
                bool any = false;
                foreach (var r in Renderers)
                {
                    if (!r) continue;
                    if (!any) { cached = r.bounds; any = true; }
                    else cached.Encapsulate(r.bounds);
                }
                if (!any) cached = new Bounds(transform.position, Vector3.one * .5f);
                hasCached = true;
                return cached;
            }
        }

        internal void Forget() => hasCached = false;

        public CardZoneKind Kind => CardZoneKind.Other;

        public Bounds Bounds
        {
            get
            {
                var b = Look;
                b.Expand(field ? field.catchReach * 2 : 1);
                return b;
            }
        }

        public bool Contains(Vector3 point) => Bounds.Contains(point);
        public bool Ignite(IgnitionSource source, Vector3 point) => field && field.Ignite(this, point);
        public bool Wash(ICardZone rain) => field && field.Douse(this);
    }
}
