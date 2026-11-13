using System.Collections;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// Z3, the bottom of the stairs where the walls close in: the mountain draws a deep breath, the throat squeezes —
    /// a low thud, the walls shed dust on both sides, grit from the roof, the view shakes.
    /// </summary>
    public sealed class GodThroat : GodMoment
    {
        [Range(0, 1)] public float shake = .32f;

        void Reset() => atMetres = 85.5f;

        protected override IEnumerator Play()
        {
            var eye = Hub.Eye;
            Hub.Sound(Hub.rumble, eye.position + eye.forward * 3, .9f, .7f);
            yield return new WaitForSeconds(.5f);
            Hub.Clip(Hub.thud, eye.position, .9f, .9f, .3f, 3, 40);
            Hub.Shake(shake);
            foreach (var side in new[] { eye.right, -eye.right })
            {
                var dir = side; dir.y = 0; dir.Normalize();
                if (Physics.Raycast(eye.position, dir, out var hit, 3, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    Hub.Effect(Hub.dustEffect, hit.point + hit.normal * .2f, Quaternion.LookRotation(hit.normal), .6f, 5);
            }
            var roof = Hub.Ceiling(eye.position, 5);
            if (roof.HasValue) Hub.Effect(Hub.gritEffect, roof.Value, Quaternion.identity, 1f, 4);
            yield return new WaitForSeconds(.8f);
            Hub.Sound(Hub.crumble, eye.position + Vector3.up, .6f, .7f);
        }
    }
}
