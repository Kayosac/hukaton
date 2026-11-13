using UnityEngine;
using UnityEngine.Rendering;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Flying creatures (bee swarm, clown emoji). They hover at a cruising height over the ground and go straight
    /// over rocks and bushes instead of following the NavMesh, slide along walls and trunks, pull up during the
    /// wind-up and dive at the target to strike, then climb back. Frozen or knocked out, the wings stop and they
    /// drop; dead, they fall to the ground. A blob shadow on the ground shows where they are.
    /// The navigation agent stays on the NavMesh under them, so everything that expects one still works.
    /// </summary>
    public sealed partial class WorldCreature
    {
        [Header("Flight")]
        [Tooltip("Flies instead of walking: hovers above the ground, dives at the target to strike and climbs back. Falls while frozen, stunned or dead.")]
        public bool flying;
        [Tooltip("Height of the feet above the ground while flying about, in metres.")]
        [Min(0)] public float cruiseHeight = 1.8f;
        [Tooltip("Extra height gained during the wind-up: it pulls up before the dive.")]
        [Min(0)] public float windupClimb = .7f;
        [Tooltip("At the bottom of the dive the middle of the drawing is this high above the target's feet.")]
        public float diveHeight = .9f;
        [Tooltip("Vertical speed limit (m/s) and how fast it gets there (m/s²). The dive itself goes at the dash speed.")]
        [Min(.1f)] public float climbSpeed = 4, climbAcceleration = 14;
        [Tooltip("Slow up-and-down drift of the drawing (metres) and its frequency (per second).")]
        [Min(0)] public float bobHeight = .12f, bobFrequency = .9f;
        [Tooltip("Restless buzzing: random shake of the drawing in metres. A swarm jitters, a balloon barely does.")]
        [Min(0)] public float jitter;
        [Tooltip("Width of the ground shadow relative to the creature's size; zero for none.")]
        [Min(0)] public float shadowSize = .4f;
        [Tooltip("A ghost: flies straight through trees, rocks, walls and the player (no collisions at all; cards and strikes still find it).")]
        public bool phasing;
        [Tooltip("Ghost: how see-through it is from afar (1: solid). It thickens to solid as it comes within the distance below.")]
        [Range(.1f, 1)] public float farOpacity = 1;
        [Min(0)] public float solidWithin = 6;
        [Range(0, 1)] public float shadowOpacity = .35f;

        float flightGround, swayOffset;
        bool flightGroundFound;
        SpriteRenderer flightShadow;
        static Sprite shadowSprite;
        static readonly RaycastHit[] flightHits = new RaycastHit[8];

        /// <summary>Gravity off for a flyer, its shadow made; on a new outing it starts in the air.</summary>
        void PrepareFlight(bool placeInAir)
        {
            if (!body) return;
            body.useGravity = !flying;
            // A ghost touches nothing; whatever looks for it (cards, strikes, sight) still finds its collider.
            if (capsule) capsule.excludeLayers = phasing ? ~0 : 0;
            if (!flying)
            {
                if (flightShadow) flightShadow.gameObject.SetActive(false);
                return;
            }
            swayOffset = Random.value * 10;
            EnsureShadow();
            if (placeInAir)
            {
                Vector3 inAir = body.position + Vector3.up * cruiseHeight;
                body.position = inAir;
                transform.position = inAir;
            }
            flightGroundFound = GroundBelow(body.position, .4f, out flightGround);
        }

        /// <summary>Straight at the point through the air, sliding along whatever stands in the way.</summary>
        void Fly(Vector3 destination, float moveSpeed)
        {
            Vector3 to = Flat(destination - body.position);
            float distance = to.magnitude;
            if (distance < .05f) { desired = Vector3.zero; return; }
            Vector3 direction = AvoidObstacles(to / distance);
            // Eases in over the last half metre instead of overshooting and circling the point.
            desired = direction * moveSpeed * Mathf.Clamp01(distance / .5f);
            Face(direction);
        }

        Vector3 AvoidObstacles(Vector3 direction)
        {
            if (phasing) return direction;
            float radius = capsule ? capsule.radius : .25f;
            int count = Physics.SphereCastNonAlloc(Centre, radius, direction, flightHits, 1.6f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                var hit = flightHits[i];
                if (hit.rigidbody == body || hit.rigidbody && !hit.rigidbody.isKinematic) continue;
                // Started inside it, or it is a slope or a top the flyer simply rises over.
                if (hit.distance <= 0 || hit.normal.y > .6f) continue;
                if (hit.distance < nearest) { nearest = hit.distance; normal = hit.normal; }
            }
            normal = Flat(normal);
            if (normal.sqrMagnitude < .0001f) return direction;
            normal.Normalize();
            Vector3 slide = Vector3.ProjectOnPlane(direction, normal);
            // Head-on: each creature always goes round the same side, so it does not dither in front of a trunk.
            if (slide.sqrMagnitude < .04f) slide = Vector3.Cross(Vector3.up, normal) * ((GetInstanceID() & 1) == 0 ? 1 : -1);
            return slide.normalized;
        }

        /// <summary>Holds the flying height in FixedUpdate after the horizontal movement has been set.</summary>
        void FlyVertical(float dt, bool controlled)
        {
            Vector3 position = body.position;
            flightGroundFound = GroundBelow(position, .4f, out flightGround);
            // Frozen or knocked out: the wings stop and it drops; physics has it until it recovers.
            body.useGravity = controlled;
            if (controlled) return;
            float ground = flightGround;
            bool found = flightGroundFound;
            // Looks a little ahead so it starts to climb before a rock, not once it is already in it.
            Vector3 planar = Flat(body.linearVelocity);
            if (planar.sqrMagnitude > .25f && GroundBelow(position + planar * .4f, 2.5f, out float ahead))
            {
                ground = found ? Mathf.Max(ground, ahead) : ahead;
                found = true;
            }
            // Over a drop with no ground in reach it keeps its height.
            if (!found) ground = position.y - cruiseHeight;
            bool diving = State == CreatureState.Strike;
            float altitude = diving
                ? Mathf.Max(ground + .05f, TargetPosition.y + diveHeight - TopHeight * .5f)
                : ground + cruiseHeight + (State == CreatureState.Windup ? windupClimb * Mathf.Clamp01(stateTime / Mathf.Max(.05f, windup)) : 0);
            float limit = diving ? Mathf.Max(climbSpeed, dashSpeed) : climbSpeed;
            float wanted = Mathf.Clamp((altitude - position.y) * (diving ? 8 : 3), -limit, limit);
            Vector3 velocity = body.linearVelocity;
            velocity.y = Mathf.MoveTowards(velocity.y, wanted, climbAcceleration * (diving ? 4 : 1) * dt);
            body.linearVelocity = velocity;
        }

        /// <summary>Height of the ground under a point: the first solid surface below <paramref name="above"/> metres over it.</summary>
        bool GroundBelow(Vector3 point, float above, out float height)
        {
            height = point.y;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * above, Vector3.down, groundHits, above + 40, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.rigidbody == body || hit.rigidbody && !hit.rigidbody.isKinematic) continue;
                if (hit.distance < best) { best = hit.distance; height = hit.point.y; }
            }
            return best < float.MaxValue;
        }

        /// <summary>How far a dead flyer drops before it lies on the ground.</summary>
        float FallHeight(Vector3 from) => flying && GroundBelow(from, .4f, out float ground) ? Mathf.Max(0, from.y - ground) : 0;

        /// <summary>Bobbing and buzzing of the drawing, in the drawing's own units.</summary>
        Vector3 FlightSway()
        {
            if (!flying || State == CreatureState.Frozen || State == CreatureState.Stunned) return Vector3.zero;
            float t = Time.time + swayOffset;
            Vector3 sway = Vector3.up * Mathf.Sin(t * bobFrequency * Mathf.PI * 2) * bobHeight;
            if (jitter > 0)
            {
                float j = t * 9;
                sway += new Vector3(Mathf.PerlinNoise(j, .3f) - .5f, Mathf.PerlinNoise(.7f, j) - .5f, Mathf.PerlinNoise(j, j + 5) - .5f) * 2 * jitter;
            }
            // The drawing multiplies its offset by the size.
            return sway / Mathf.Max(.05f, Size);
        }

        void EnsureShadow()
        {
            if (flightShadow || shadowSize <= 0) return;
            if (!shadowSprite) shadowSprite = MakeShadowSprite();
            var shadow = new GameObject("Flight shadow");
            shadow.transform.SetParent(transform, false);
            flightShadow = shadow.AddComponent<SpriteRenderer>();
            flightShadow.sprite = shadowSprite;
            flightShadow.shadowCastingMode = ShadowCastingMode.Off;
            flightShadow.receiveShadows = false;
        }

        /// <summary>Lies on the ground under the flyer: smaller and paler the higher it is, fading with the corpse.</summary>
        void UpdateShadow()
        {
            if (!flightShadow) return;
            Vector3 position = transform.position;
            float height = position.y - flightGround;
            bool show = flightGroundFound && height < 12 && !blinking;
            if (flightShadow.gameObject.activeSelf != show) flightShadow.gameObject.SetActive(show);
            if (!show) return;
            float near = 1 - Mathf.Clamp01(height / 8);
            float width = shadowSize * Size * Mathf.Lerp(.55f, 1, near);
            var shadow = flightShadow.transform;
            shadow.SetPositionAndRotation(new Vector3(position.x, flightGround + .03f, position.z), Quaternion.Euler(90, 0, 0));
            float parentScale = Mathf.Max(.0001f, transform.lossyScale.x);
            shadow.localScale = new Vector3(width / parentScale, width / parentScale, 1);
            float fade = view && view.spriteRenderer ? view.spriteRenderer.color.a : 1;
            flightShadow.color = new Color(0, 0, 0, shadowOpacity * Mathf.Lerp(.5f, 1, near) * fade);
        }

        /// <summary>A small pixel blob with a lumpy edge, like the rest of the world.</summary>
        static Sprite MakeShadowSprite()
        {
            const int n = 16;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "Flight shadow",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[n * n];
            float centre = (n - 1) * .5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2(x - centre, y - centre) / (n * .5f);
                float angle = Mathf.Atan2(p.y, p.x);
                float edge = .9f + .07f * Mathf.Sin(angle * 3) + .04f * Mathf.Sin(angle * 5 + 1);
                float d = p.magnitude;
                byte alpha = d < edge * .7f ? (byte)255 : d < edge ? (byte)140 : (byte)0;
                pixels[y * n + x] = new Color32(0, 0, 0, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n);
        }
    }
}
