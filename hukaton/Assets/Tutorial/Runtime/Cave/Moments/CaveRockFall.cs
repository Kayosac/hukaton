using System.Collections;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// Moment: the ceiling crumbles ahead of the player. Grit trickles and the rock creaks first, then a rock drops a few
    /// metres ahead, bursts into pieces on the floor and shakes the camera; a lantern nearby swings from the blow.
    /// </summary>
    public sealed class CaveRockFall : CaveMoment
    {
        [Header("Rock")]
        [Tooltip("Where the rock breaks off (on the ceiling).")]
        public Transform ceiling;
        public GameObject rockPrefab;
        [Min(.1f)] public float rockSize = .55f;
        [Min(0)] public int pieces = 5;
        [Tooltip("Warning before the drop: grit and creaking.")]
        [Min(0)] public float warning = 1.1f;
        [Header("Effects")]
        public GameObject gritEffect;
        public GameObject impactEffect;
        public SfxCue crumble, impact;
        [Range(0, 1)] public float shake = .4f;
        [Header("Lantern")]
        [Tooltip("A lantern on a bracket nearby: it swings from the blow (its pivot is rigged when the prologue starts).")]
        public Transform lanternBracket;
        CaveSwing lantern;

        protected override void Prepare()
        {
            if (lanternBracket) lantern = CaveSwing.Rig(lanternBracket);
        }

        protected override IEnumerator Play()
        {
            Effect(gritEffect, ceiling.position, Quaternion.identity, .8f, 4);
            Sound(crumble, ceiling.position, .9f, .8f);
            Shake(.08f, ceiling.position);
            yield return new WaitForSeconds(warning);
            Effect(gritEffect, ceiling.position, Quaternion.identity, 1.2f, 4);
            var rock = Rock(rockPrefab, ceiling.position + Vector3.down * rockSize * .6f, rockSize, transform);
            rock.angularVelocity = Random.insideUnitSphere * 3;
            // falls until it meets the floor
            float time = 0;
            Vector3 last = rock.position;
            while (time < 3)
            {
                time += Time.deltaTime;
                if (time > .25f && rock.linearVelocity.y > -.5f && (rock.position - last).sqrMagnitude < .0004f) break;
                if (Physics.Raycast(rock.position, Vector3.down, rockSize * .55f, ~0, QueryTriggerInteraction.Ignore) && time > .15f) break;
                last = rock.position;
                yield return null;
            }
            Vector3 at = rock.position;
            Effect(impactEffect, at, Quaternion.identity, .9f, 4);
            Sound(impact, at);
            Shake(shake, at);
            if (lantern) lantern.Push((lantern.transform.position - at).normalized * 22);
            // the rock bursts: a few small pieces scatter and settle as rubble
            Destroy(rock.gameObject);
            var bodies = new Rigidbody[pieces];
            for (int i = 0; i < pieces; i++)
            {
                Vector3 offset = Quaternion.Euler(0, 360f * i / pieces + Random.Range(-20f, 20f), 0) * Vector3.forward * rockSize * .3f;
                var piece = Rock(rockPrefab, at + offset + Vector3.up * .15f, rockSize * Random.Range(.3f, .48f), transform);
                piece.linearVelocity = offset.normalized * Random.Range(.6f, 1.6f) + Vector3.up * Random.Range(1f, 2.2f);
                piece.angularVelocity = Random.insideUnitSphere * 8;
                bodies[i] = piece;
            }
            yield return Settle(bodies, 3.5f);
        }
    }
}
