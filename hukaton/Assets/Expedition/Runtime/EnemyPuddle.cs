using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A puddle a creature's shot leaves where it lands (the drunken dwarf's beer, <see cref="EnemyProjectileProfile.puddleRadius"/>):
    /// a flat round stain on the ground that hurts the player standing in it now and then, and fades after a few seconds.
    /// It has no collider: it only reads where the player's feet are.
    /// </summary>
    public sealed class EnemyPuddle : MonoBehaviour
    {
        const int Segments = 28;
        static Material material;

        ExpeditionWorld world;
        WorldCreature shooter;
        float radius, seconds, interval, born, nextHurt;
        int damage;
        Color colour;
        MeshRenderer stain;

        public float Radius => radius;
        public bool Holds(Vector3 point) => Vector3.Distance(new Vector3(point.x, 0, point.z), new Vector3(transform.position.x, 0, transform.position.z)) <= radius
            && Mathf.Abs(point.y - transform.position.y) < 1.2f;

        /// <summary>A puddle on the ground under <paramref name="point"/>.</summary>
        public static EnemyPuddle Spawn(ExpeditionWorld world, Vector3 point, EnemyProjectileProfile profile, WorldCreature shooter)
        {
            if (!world || !profile || profile.puddleRadius <= 0) return null;
            Vector3 at = point;
            if (Physics.Raycast(point + Vector3.up * .6f, Vector3.down, out var hit, 3, ~(1 << 2), QueryTriggerInteraction.Ignore)) at = hit.point;
            var go = new GameObject("Enemy puddle");
            go.transform.SetParent(world.spawnedObjects, true);
            go.transform.position = at + Vector3.up * .04f;
            var puddle = go.AddComponent<EnemyPuddle>();
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
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = Disc(radius);
            stain = gameObject.AddComponent<MeshRenderer>();
            if (!material) material = new Material(Shader.Find("Sprites/Default"));
            stain.sharedMaterial = material;
            stain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stain.receiveShadows = false;
            Paint(0);
        }

        /// <summary>A flat disc, a little ragged at the rim, darker in the middle.</summary>
        static Mesh Disc(float radius)
        {
            var vertices = new Vector3[Segments + 1];
            var colours = new Color[Segments + 1];
            var triangles = new int[Segments * 3];
            vertices[0] = Vector3.zero;
            colours[0] = Color.white;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2 / Segments;
                float r = radius * (.86f + .14f * Mathf.PerlinNoise(i * .7f, radius));
                vertices[i + 1] = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                colours[i + 1] = new Color(1, 1, 1, .55f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i == Segments - 1 ? 1 : i + 2;
                triangles[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Puddle", vertices = vertices, colors = colours, triangles = triangles };
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        void Paint(float fade)
        {
            if (!stain) return;
            var block = new MaterialPropertyBlock();
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
            transform.localScale = new Vector3(grow, 1, grow);
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
