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

        /// <summary>
        /// A flat disc, a little ragged at the edge: deep in the middle, thinner toward the edge, with a light rim of
        /// froth so it reads as a spill on any ground.
        /// </summary>
        static Mesh Disc(float radius)
        {
            // Centre, then a ring of the body and a ring of the rim.
            var vertices = new Vector3[1 + Segments * 2];
            var colours = new Color[vertices.Length];
            var triangles = new int[Segments * 9];
            vertices[0] = Vector3.zero;
            colours[0] = new Color(.8f, .8f, .8f, 1);
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2 / Segments;
                float r = radius * (.84f + .16f * Mathf.PerlinNoise(i * .7f, radius * 3));
                var way = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                vertices[1 + i] = way * r * .82f;
                colours[1 + i] = new Color(.95f, .95f, .95f, .8f);
                vertices[1 + Segments + i] = way * r;
                colours[1 + Segments + i] = new Color(1, 1, 1, .35f);
            }
            int t = 0;
            for (int i = 0; i < Segments; i++)
            {
                int next = (i + 1) % Segments;
                int inner = 1 + i, innerNext = 1 + next, outer = 1 + Segments + i, outerNext = 1 + Segments + next;
                triangles[t++] = 0; triangles[t++] = innerNext; triangles[t++] = inner;
                triangles[t++] = inner; triangles[t++] = innerNext; triangles[t++] = outerNext;
                triangles[t++] = inner; triangles[t++] = outerNext; triangles[t++] = outer;
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
