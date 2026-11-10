using System.Collections;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// Moment: as the player comes up to the camp, the sitting skeleton's skull snaps off: it is a real body from that
    /// moment, knocked forward with a little impulse, and tumbles and rolls on its own (convex collider of its shape).
    /// </summary>
    public sealed class CaveSkullFall : CaveMoment
    {
        public Rigidbody skull;
        public SfxCue creak;
        [Tooltip("Impulse of the snap, N·s: mostly towards the player, a little up.")]
        [Min(0)] public float impulse = .55f;

        protected override void Prepare()
        {
            if (skull) skull.isKinematic = true;
        }

        protected override IEnumerator Play()
        {
            if (!skull) yield break;
            Transform head = skull.transform;
            Sound(creak, head.position, .8f, .7f);
            skull.isKinematic = false;
            skull.maxDepenetrationVelocity = .6f;
            Vector3 towards = Player - head.position; towards.y = 0;
            towards = towards.sqrMagnitude > .01f ? towards.normalized : head.forward;
            Vector3 side = Vector3.Cross(Vector3.up, towards) * Random.Range(-.25f, .25f);
            // pushed a little above its centre, so it pitches forward as it goes
            skull.AddForceAtPosition((towards + side + Vector3.up * .45f).normalized * impulse, skull.worldCenterOfMass + Vector3.up * .06f - towards * .03f, ForceMode.Impulse);
        }
    }
}
