using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// A cylinder zone for the registry tests. A ScriptableObject, because MonoBehaviours of an Editor-only test
    /// assembly cannot be added to objects; it still is a Unity object that can be destroyed.
    /// </summary>
    public sealed class FakeCardZone : ScriptableObject, ICardZone, IIgnitable
    {
        public CardZoneKind kind = CardZoneKind.TarPuddle;
        public Vector3 position;
        public float radius = 2, height = .5f;
        public int ignitions;

        public CardZoneKind Kind => kind;
        public Bounds Bounds => new Bounds(position + Vector3.up * height * .5f, new Vector3(radius * 2, height, radius * 2));

        public bool Contains(Vector3 point)
        {
            Vector3 d = point - position;
            return d.x * d.x + d.z * d.z <= radius * radius && d.y >= 0 && d.y <= height;
        }

        public bool Ignite(IgnitionSource source, Vector3 point)
        {
            ignitions++;
            return true;
        }
    }
}
