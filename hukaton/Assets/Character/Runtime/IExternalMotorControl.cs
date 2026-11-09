using UnityEngine;

namespace Hakaton.Character
{
    /// <summary>
    /// A script that drives the player's body instead of <see cref="FirstPersonMotor"/> for a while (rope, hanging).
    /// Claim with <see cref="FirstPersonMotor.TryClaimExternalControl"/>, move with
    /// <see cref="FirstPersonMotor.MoveExternally"/>, give back with <see cref="FirstPersonMotor.ReleaseExternalControl"/>.
    /// </summary>
    public interface IExternalMotorControl
    {
        /// <summary>
        /// Control was taken away (teleport, the end of the outing, a full pose reset). Stop driving the body and
        /// return its current world velocity in m/s: the motor carries it on.
        /// </summary>
        Vector3 OnControlRevoked();
    }
}
