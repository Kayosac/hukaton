using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Owner's rule (DECISIONS.md, 09.11 evening): a lightning strike that hits a fighter — a creature, an ally or the
    /// player — standing in a tar puddle or touching its edge sets that puddle alight. The strike's own damage still
    /// goes only to the fighters it hits; the burning tar then hurts whoever stays in its fire by its own rules.
    /// </summary>
    public static class LightningTar
    {
        static readonly List<ICardZone> Nearby = new List<ICardZone>();

        /// <summary>
        /// Lights every ignitable tar zone whose box the feet are inside or within <paramref name="touch"/> metres of
        /// (a <see cref="TarPuddleZone"/> then checks its own round edge and the rain). Returns how many caught fire now.
        /// </summary>
        public static int IgniteUnder(CardZoneRegistry zones, Vector3 feet, float touch)
        {
            if (zones == null) return 0;
            Nearby.Clear();
            zones.Near(feet, Mathf.Max(0, touch), Nearby, CardZoneKind.TarPuddle);
            int lit = 0;
            foreach (var zone in Nearby)
                if (zone is IIgnitable tar && tar.Ignite(IgnitionSource.Lightning, feet)) lit++;
            Nearby.Clear();
            return lit;
        }
    }
}
