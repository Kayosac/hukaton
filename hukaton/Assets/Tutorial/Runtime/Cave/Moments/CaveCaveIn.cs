using System.Collections;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// Moment: once the player is over the water and off along the far bank, the ceiling behind gives way. A rumble,
    /// rocks come down one after another onto the bank edge and half-block the way back; dust rolls out. The way
    /// forward stays open.
    /// </summary>
    public sealed class CaveCaveIn : CaveMoment
    {
        public WorldObstacle water;
        [Tooltip("Where each rock breaks off (ceiling points over the bank edge).")]
        public Transform[] drops = new Transform[0];
        public GameObject[] rockPrefabs = new GameObject[0];
        public Vector2 rockSize = new Vector2(.6f, 1.2f);
        [Min(0)] public float stagger = .16f;
        public GameObject gritEffect, dustEffect;
        public SfxCue rumble, crash;
        [Range(0, 1)] public float shake = .55f;
        [Tooltip("How long the dust hangs before it starts to settle, and how long it takes to fade away, s.")]
        [Min(0)] public float dustHold = 1.6f, dustFade = 1.6f;

        protected override bool Ready => water && water.Resolved;

        protected override IEnumerator Play()
        {
            if (drops.Length == 0 || rockPrefabs.Length == 0) yield break;
            Vector3 centre = Vector3.zero;
            foreach (var d in drops) centre += d.position;
            centre /= drops.Length;
            Sound(rumble, centre, 1, .7f);
            Shake(.2f, centre);
            foreach (var d in drops) Effect(gritEffect, d.position, Quaternion.identity, 1, 4);
            yield return new WaitForSeconds(.7f);
            var bodies = new Rigidbody[drops.Length];
            var random = new System.Random(11);
            for (int i = 0; i < drops.Length; i++)
            {
                float size = Mathf.Lerp(rockSize.x, rockSize.y, (float)random.NextDouble());
                var rock = Rock(rockPrefabs[i % rockPrefabs.Length], drops[i].position + Vector3.down * size * .6f, size, transform);
                rock.angularVelocity = Random.insideUnitSphere * 2;
                // rubble piles up rather than rolling off over the water; a rock that starts touching the wall
                // is eased out of it instead of being shot through
                rock.angularDamping = 3;
                rock.linearDamping = .4f;
                rock.maxDepenetrationVelocity = 1;
                bodies[i] = rock;
                yield return new WaitForSeconds(stagger);
            }
            // the floor of the bank is a little below the ceiling points; the first rocks are down by now
            float floor = centre.y;
            if (Physics.Raycast(centre, Vector3.down, out var hit, 12, ~0, QueryTriggerInteraction.Ignore)) floor = hit.point.y;
            yield return new WaitForSeconds(.25f);
            Vector3 pile = new Vector3(centre.x, floor, centre.z);
            Sound(crash, pile);
            Sound(rumble, pile, .8f, .55f);
            Shake(shake, pile);
            // the dust rolls out, hangs a moment and settles away gently
            FadingEffect(dustEffect, pile + Vector3.up * .3f, Quaternion.identity, 1.6f, dustHold, dustFade);
            yield return new WaitForSeconds(.5f);
            Sound(crash, pile, .5f, .8f);
            yield return Settle(bodies, 3.5f);
            // anything that still slipped out of the cave is gone, not lying somewhere under the world
            foreach (var body in bodies) if (body && body.position.y < floor - 2) Destroy(body.gameObject);
        }
    }
}
