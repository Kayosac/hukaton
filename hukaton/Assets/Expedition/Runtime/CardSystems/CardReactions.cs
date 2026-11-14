using Hakaton.Character;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>What set something alight (board: both Fire and Lightning ignite tar and the branch bridge).</summary>
    public enum IgnitionSource { Fire, Lightning }

    /// <summary>
    /// Catches fire from a card: tar puddle, branch bridge. Found by <see cref="ExpeditionWorld.Ignite"/> on the
    /// collider that was hit or among the zones around the hit point.
    /// </summary>
    public interface IIgnitable
    {
        /// <summary>Returns true when this call set it alight (false when already burning, washed away…).</summary>
        bool Ignite(IgnitionSource source, Vector3 point);
    }

    /// <summary>Reacts to rain: tar is washed away (burning or not), fire goes out.</summary>
    public interface IWashable
    {
        /// <summary>Called by the rain zone overlapping this one. Returns true when the rain changed something.</summary>
        bool Wash(ICardZone rain);
    }

    /// <summary>
    /// Мокнет от воды, брошенной картой (Струя): пороховая бочка. Мокрое не горит, пока мокрое
    /// (<see cref="WetStatus"/>, 30 с); горящий фитиль вода гасит. Струя находит такие зоны у точки всплеска.
    /// </summary>
    public interface ISoakable
    {
        /// <summary>Мочит на <paramref name="seconds"/> с. True, если что-то изменилось (намокло, погасло).</summary>
        bool Soak(ExpeditionWorld world, float seconds);
    }

    /// <summary>A zone that hides what is behind or inside it (fog). Asked by <see cref="ExpeditionWorld.IsVisionBlocked"/>.</summary>
    public interface IVisionBlocker
    {
        bool BlocksVision(Vector3 from, Vector3 to);
    }

    /// <summary>
    /// Implemented by components of prefabs spawned through <see cref="ExpeditionWorld.SpawnForOuting{T}"/>: called
    /// right after instantiation with the world (spawned prefabs cannot hold a scene reference).
    /// </summary>
    public interface IOutingSpawned
    {
        void OnSpawned(ExpeditionWorld world);
    }

    /// <summary>A status put on a body by a card (wet, light). The world clears all of them when the outing ends.</summary>
    public interface ICardStatus
    {
        void ClearStatus();
    }

    /// <summary>
    /// A surface that takes over the player's landing on it (the mushroom cap). <see cref="PlayerVitals"/> asks the
    /// collider the player landed on; when <see cref="AbsorbLanding"/> returns true the fall does no damage. The motor
    /// has already settled on the ground, so the surface may relaunch the body with
    /// <see cref="FirstPersonMotor.SetVerticalSpeed"/>.
    /// </summary>
    public interface ILandingSurface
    {
        /// <param name="impactSpeed">Real landing speed in m/s (positive). Design units: <c>motor.ToReferenceSpeed</c>.</param>
        bool AbsorbLanding(FirstPersonMotor motor, float impactSpeed);
    }
}
