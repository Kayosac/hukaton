using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The wind strip of «Сквозняк»: from the ground under the impact along the horizontal direction of the throw,
    /// <see cref="Length"/> long and <see cref="HalfWidth"/> × 2 wide, from <see cref="Below"/> under the start to
    /// <see cref="Above"/> over it. Pure geometry (EditMode-tested).
    /// </summary>
    public readonly struct DraftStrip
    {
        public readonly Vector3 Origin, Direction;
        public readonly float Length, HalfWidth, Below, Above;

        public DraftStrip(Vector3 origin, Vector3 direction, float length, float width, float below, float above)
        {
            Origin = origin;
            direction.y = 0;
            Direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
            Length = Mathf.Max(0, length);
            HalfWidth = Mathf.Max(0, width) * .5f;
            Below = Mathf.Max(0, below);
            Above = Mathf.Max(0, above);
        }

        /// <summary>Horizontal, to the right of the wind.</summary>
        public Vector3 Side => new Vector3(Direction.z, 0, -Direction.x);
        public Vector3 End => Origin + Direction * Length;
        public Quaternion Rotation => Quaternion.LookRotation(Direction, Vector3.up);
        /// <summary>Centre and half extents of the strip as a box turned by <see cref="Rotation"/> (physics queries).</summary>
        public Vector3 Centre => Origin + Direction * (Length * .5f) + Vector3.up * ((Above - Below) * .5f);
        public Vector3 HalfExtents => new Vector3(HalfWidth, (Above + Below) * .5f, Length * .5f);

        /// <summary>Distance along the wind from the start (negative behind it).</summary>
        public float Along(Vector3 point) => Vector3.Dot(Flat(point - Origin), Direction);
        /// <summary>Signed distance across the wind (right positive).</summary>
        public float Across(Vector3 point) => Vector3.Dot(Flat(point - Origin), Side);

        public bool Contains(Vector3 point) => Overlaps(point, 0);

        /// <summary>A disc of <paramref name="radius"/> on <paramref name="centre"/> (a puddle, a cloud's foot) reaches into the strip.</summary>
        public bool Overlaps(Vector3 centre, float radius)
        {
            float along = Along(centre), across = Mathf.Abs(Across(centre)), dy = centre.y - Origin.y;
            return along >= -radius && along <= Length + radius && across <= HalfWidth + radius && dy >= -Below && dy <= Above;
        }

        /// <summary>Where the wind carries something (a rain cloud): to the far end of the strip, keeping its offset across.</summary>
        public Vector3 CarriedToEnd(Vector3 point) => point + Direction * Mathf.Max(0, Length - Along(point));

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    }

    /// <summary>Rules of «Сквозняк» free of scene objects (EditMode-tested).</summary>
    public static class DraftRules
    {
        /// <summary>
        /// The wind blows along the horizontal direction of the throw: the card's velocity at the impact, flattened; for
        /// a card falling straight down, from where it was thrown to where it landed; else <paramref name="fallback"/>.
        /// </summary>
        public static Vector3 WindDirection(Vector3 velocity, Vector3 throwOrigin, Vector3 point, Vector3 fallback)
        {
            var flat = new Vector3(velocity.x, 0, velocity.z);
            if (flat.sqrMagnitude > .04f) return flat.normalized;
            flat = new Vector3(point.x - throwOrigin.x, 0, point.z - throwOrigin.z);
            if (flat.sqrMagnitude > .01f) return flat.normalized;
            fallback.y = 0;
            return fallback.sqrMagnitude > 1e-8f ? fallback.normalized : Vector3.forward;
        }

        /// <summary>
        /// The wind carries fire from <paramref name="source"/> to <paramref name="target"/>: the target is downwind of it,
        /// no further than <paramref name="reach"/> along the wind (a little behind the source still counts — it stands in
        /// the flames) and no further than <paramref name="spread"/> to either side.
        /// </summary>
        public static bool Downwind(Vector3 source, Vector3 target, Vector3 direction, float reach, float spread, float behind = .25f)
        {
            direction.y = 0;
            if (direction.sqrMagnitude < 1e-8f) return false;
            direction.Normalize();
            float dx = target.x - source.x, dz = target.z - source.z;
            float along = dx * direction.x + dz * direction.z;
            if (along < -behind || along > reach) return false;
            return Mathf.Abs(dx * direction.z - dz * direction.x) <= spread;
        }

        /// <summary>The point of a disc (puddle) nearest to <paramref name="from"/>, on the disc's level.</summary>
        public static Vector3 NearestOnDisc(Vector3 centre, float radius, Vector3 from)
        {
            var d = new Vector3(from.x - centre.x, 0, from.z - centre.z);
            float distance = d.magnitude;
            return distance <= radius || distance < 1e-6f ? new Vector3(from.x, centre.y, from.z) : centre + d * (radius / distance);
        }
    }
}
