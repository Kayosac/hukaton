using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A puddle a creature's shot leaves where it lands (the drunken dwarf's beer, <see cref="EnemyProjectileProfile.puddleRadius"/>):
    /// a flat round stain on the ground that hurts the player standing in it now and then, and fades after a few seconds.
    /// It has no collider: it only reads where the player's feet are. The prefab carries the stain (a disc of radius one,
    /// scaled to the profile's radius) and its material; the profile names the prefab (<see cref="EnemyProjectileProfile.puddle"/>).
    /// </summary>
    public sealed class EnemyPuddle : MonoBehaviour
    {
        [Tooltip("The stain: a flat disc of radius one on the ground, tinted by the profile's puddle colour.")]
        public MeshRenderer stain;

        ExpeditionWorld world;
        WorldCreature shooter;
        float radius, seconds, interval, born, nextHurt;
        int damage;
        Color colour;
        MaterialPropertyBlock block;

        public float Radius => radius;
        public bool Holds(Vector3 point) => Vector3.Distance(new Vector3(point.x, 0, point.z), new Vector3(transform.position.x, 0, transform.position.z)) <= radius
            && Mathf.Abs(point.y - transform.position.y) < 1.2f;

        /// <summary>A puddle on the ground under <paramref name="point"/>.</summary>
        public static EnemyPuddle Spawn(ExpeditionWorld world, Vector3 point, EnemyProjectileProfile profile, WorldCreature shooter)
        {
            if (!world || !profile || !profile.puddle || profile.puddleRadius <= 0) return null;
            Vector3 at = point;
            if (Physics.Raycast(point + Vector3.up * .6f, Vector3.down, out var hit, 3, ~(1 << 2), QueryTriggerInteraction.Ignore)) at = hit.point;
            var puddle = Instantiate(profile.puddle, at + Vector3.up * .04f, Quaternion.Euler(0, Random.Range(0f, 360f), 0), world.spawnedObjects);
            puddle.Init(world, shooter, profile);
            return puddle;
        }

        void Init(ExpeditionWorld owner, WorldCreature by, EnemyProjectileProfile profile)
        {
            world = owner;
            shooter = by;
            radius = profile.puddleRadius;
            seconds = profile.puddleSeconds;
            interval = profile.puddleInterval;
            damage = profile.puddleDamage;
            colour = profile.puddleColour;
            born = Time.time;
            nextHurt = Time.time + .15f;
            Paint(0);
        }

        void Paint(float fade)
        {
            if (!stain) return;
            block ??= new MaterialPropertyBlock();
            var c = colour;
            c.a *= 1 - fade;
            block.SetColor("_Color", c);
            stain.SetPropertyBlock(block);
        }

        void Update()
        {
            float age = Time.time - born;
            if (!world || age >= seconds) { Destroy(gameObject); return; }
            // It soaks in over the last half second; the edge grows in at the start.
            float grow = Mathf.Clamp01(age / .15f);
            transform.localScale = new Vector3(grow * radius, 1, grow * radius);
            Paint(Mathf.Clamp01((age - (seconds - .5f)) / .5f));
            if (damage <= 0 || !world.Running || Time.time < nextHurt || !world.motor) return;
            Vector3 feet = world.motor.transform.position;
            if (!Holds(feet)) return;
            nextHurt = Time.time + interval;
            var player = world.Player;
            if (Combat.IsAlive(player))
                player.TakeDamage(new DamageInfo(damage, Vector3.zero, feet, transform.position, DamageKind.Other, shooter && shooter.Alive ? shooter : null));
        }
    }
}
