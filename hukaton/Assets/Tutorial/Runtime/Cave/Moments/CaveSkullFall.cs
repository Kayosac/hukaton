using System.Collections;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// Moment: as the player passes the camp, the sitting skeleton's skull wobbles, drops off and rolls to the player's feet.
    /// The skull is a separate body resting on the headless skeleton until then.
    /// </summary>
    public sealed class CaveSkullFall : CaveMoment
    {
        public Rigidbody skull;
        public SfxCue creak;
        [Min(0)] public float rollSpeed = 1.6f;

        protected override void Prepare()
        {
            if (skull) skull.isKinematic = true;
        }

        protected override IEnumerator Play()
        {
            if (!skull) yield break;
            Transform head = skull.transform;
            Quaternion rest = head.rotation;
            Sound(creak, head.position, .7f, .55f);
            // a wobble on the neck: it is going to go
            for (float t = 0; t < .7f; t += Time.deltaTime)
            {
                float k = t / .7f;
                head.rotation = rest * Quaternion.Euler(Mathf.Sin(t * 26) * 7 * k, 0, Mathf.Sin(t * 19) * 5 * k);
                yield return null;
            }
            skull.isKinematic = false;
            Vector3 towards = Player - head.position; towards.y = 0;
            towards = towards.sqrMagnitude > .01f ? towards.normalized : head.forward;
            skull.linearVelocity = towards * rollSpeed + Vector3.up * .6f;
            skull.angularVelocity = Vector3.Cross(Vector3.up, towards) * 9;
        }
    }
}
