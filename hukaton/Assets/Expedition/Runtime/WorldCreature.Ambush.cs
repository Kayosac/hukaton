using System;
using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Hakaton.Expeditions
{
    /// <summary>How a creature lies in wait (mantis man): where it hides and how it springs out.</summary>
    [Serializable]
    public sealed class AmbushSettings
    {
        [Tooltip("Lies in wait: does not wander, stands still in its cover and springs at the target once it comes close. " +
                 "It does not give itself away by the aggression pose before that.")]
        public bool enabled;
        [Tooltip("Lives in water (the leech, the drowned swimmer): settles in the nearest wadeable water round where it appears, an ambusher or not.")]
        public bool inWater;
        [Tooltip("Lies under the surface while it waits: the drawing sinks out of sight and its body does not block the way.")]
        public bool submerge;
        [Tooltip("Springs at the target (a leap). Off: it only comes out of hiding and fights as usual.")]
        public bool springs = true;
        [Tooltip("Cover it hides in: plants of the place whose names start with one of these (the swamp's cattails and sedge).")]
        public string[] cover = { "SM_SW02_", "SM_SW03_" };
        [Tooltip("Looks for cover this far from where it appears, metres.")]
        [Min(0)] public float coverSearch = 9;
        [Tooltip("Two ambushers of one place hide at least this far apart, metres (each in its own clump).")]
        [Min(0)] public float coverSpacing = 3;
        [Tooltip("Springs out when the target it sees is closer than this, metres.")]
        [Min(1)] public float pounceRange = 6;
        [Tooltip("The spring: a short crouch before it, seconds.")]
        [Min(.05f)] public float pounceWindup = .25f;
        [Tooltip("The spring: how fast it flies at the target, m/s, and for how long, seconds (speed × time ≈ how far it reaches).")]
        [Min(1)] public float pounceSpeed = 13;
        [Min(.05f)] public float pounceSeconds = .45f;

        [Header("Disguise")]
        [Tooltip("3D looks it wears while it lies in wait — children of the prefab: the very plant or thing of the place it " +
                 "pretends to be (a bush, a pumpkin). Its drawing is hidden until it springs. One is picked: the one named like " +
                 "the cover it settles by, else any. Empty: it lies in wait in its drawing.")]
        public GameObject[] disguises = new GameObject[0];
        [Tooltip("In disguise it stands next to its cover instead of inside it: this far from the plant's middle, metres, per unit of the plant's scale.")]
        [Min(0)] public float besideCover = 1.3f;
        [Tooltip("Copies of its disguise laid out round where it appears (a pumpkin patch), so it is one among many.")]
        public GameObject companion;
        [Tooltip("How many copies each one lays out: from and to.")]
        public Vector2Int companions = new Vector2Int(1, 2);
        [Tooltip("The copies lie this far from it at most, metres.")]
        [Min(.5f)] public float companionRadius = 2.6f;
        [Tooltip("The copies keep at least this far from each other and from creatures, metres.")]
        [Min(.2f)] public float companionSpacing = 1.1f;
        [Tooltip("The burst when it throws off its disguise (leaves for a bush). Empty: a puff of dust.")]
        public VfxBurst revealVfx;
    }

    public sealed partial class WorldCreature
    {
        [Header("Combo")]
        [Tooltip("Blows in one attack: after the first strike the next ones follow at once with a short wind-up (the mantis' two sickles).")]
        [Min(1)] public int strikesPerAttack = 1;
        [Tooltip("Wind-up before each next blow of the combo, seconds.")]
        [Min(.05f)] public float followUpWindup = .2f;
        [Tooltip("Each next blow of the combo steps in at this speed, m/s, so it still reaches a target pushed back by the first. 0: from where it stands.")]
        [Min(0)] public float followUpStep = 5;

        [Header("Ambush")]
        public AmbushSettings ambush = new AmbushSettings();

        int followUpsLeft;
        bool followUp, chaining, pouncing, pounceArmed, lunging;

        /// <summary>It sits in cover waiting (not yet sprung, not fighting).</summary>
        public bool Lurking => ambush.enabled && IsCalm && State != CreatureState.Investigate;

        float WindupSeconds => blinkStrike ? blink.windup : pouncing ? ambush.pounceWindup : tonguing ? tongue.windup
            : followUp ? (afterTongue ? tongue.slamWindup : followUpWindup) : windup;
        bool afterTongue;
        float StrikeSeconds => pouncing ? ambush.pounceSeconds : strikeSeconds;
        float LungeSpeed => pouncing ? ambush.pounceSpeed : followUp ? followUpStep : dashSpeed;

        /// <summary>A new attack: the spring from cover if one was readied, and the blows of the combo still to come.</summary>
        void BeginAttack()
        {
            pouncing = pounceArmed;
            pounceArmed = false;
            followUp = false;
            followUpsLeft = Mathf.Max(0, strikesPerAttack - 1);
            // The tongue (WorldCreature.Tongue.cs) only pulls: the slam after it is the next blow.
            tonguing = tongueArmed;
            tongueArmed = false;
            afterTongue = false;
            if (tonguing) followUpsLeft = Mathf.Max(followUpsLeft, 1);
        }

        void EndAttack()
        {
            pouncing = pounceArmed = followUp = chaining = lunging = blinkStrike = false;
            tonguing = tongueArmed = afterTongue = false;
            followUpsLeft = 0;
        }

        /// <summary>At the end of a strike: the next blow of the combo, if any are left and the target still stands.</summary>
        bool NextBlow()
        {
            if (followUpsLeft <= 0 || !Combat.IsAlive(Target)) return false;
            followUpsLeft--;
            followUp = true;
            pouncing = false;
            afterTongue = tonguing;
            tonguing = false;
            chaining = true;
            Enter(CreatureState.Windup);
            return true;
        }

        /// <summary>
        /// Calm and lying in wait: stays in its cover (back to it if it was moved), still, until the target it sees comes
        /// within <see cref="AmbushSettings.pounceRange"/>; then it springs. False when it is no ambusher.
        /// </summary>
        bool ThinkLurk()
        {
            if (!ambush.enabled || State == CreatureState.Investigate) return false;
            if (sees && TargetDistance < ambush.pounceRange)
            {
                world.Play(threatCue, body.position);
                if (!ambush.springs) { squash.Kick(-3); Enter(CreatureState.Chase); return true; }
                pounceArmed = true;
                Enter(CreatureState.Windup);
                return true;
            }
            if (Flat(home - body.position).magnitude > .8f)
            {
                if (State != CreatureState.Wander) Enter(CreatureState.Wander);
                Go(home, wanderSpeed);
            }
            else
            {
                if (State != CreatureState.Idle) Enter(CreatureState.Idle);
                Halt();
            }
            return true;
        }

        /// <summary>
        /// Settles in the nearest cover (<see cref="AmbushSettings.cover"/>) among <paramref name="plants"/> within reach of
        /// where it appeared and not taken by another ambusher (<paramref name="taken"/>): that becomes its home.
        /// <paramref name="move"/>: puts it there at once (a fresh one; one coming back keeps where it was left).
        /// </summary>
        public void HideIn(IEnumerable<Component> plants, List<Vector3> taken, bool move)
        {
            if (!ambush.enabled || plants == null) return;
            Vector3 from = home;
            float best = ambush.coverSearch;
            Vector3? spot = null;
            Component cover = null;
            foreach (var plant in plants)
            {
                if (!plant || !IsCover(plant.name)) continue;
                Vector3 at = plant.transform.position;
                float distance = Flat(at - from).magnitude;
                if (distance > best) continue;
                bool free = true;
                if (taken != null) foreach (var other in taken) if (Flat(other - at).magnitude < ambush.coverSpacing) { free = false; break; }
                if (!free || !NavMesh.SamplePosition(at, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                best = distance;
                spot = hit.position;
                cover = plant;
            }
            if (spot == null) return;
            // In disguise it stands beside the plant, as one more of its kind, and wears its look and size.
            if (ambush.disguises.Length > 0 && cover != null)
            {
                spot = BesideCover(cover.transform, from) ?? spot;
                WearDisguise(cover.name, cover.transform.lossyScale);
            }
            taken?.Add(spot.Value);
            home = spot.Value + Vector3.up * .15f;
            if (!move) return;
            body.position = home;
            transform.position = home;
            if (navigator.isOnNavMesh) navigator.Warp(home);
        }

        /// <summary>
        /// Settles in the nearest wadeable water among <paramref name="waters"/> within reach of where it appeared and away
        /// from other ambushers (<paramref name="taken"/>): that becomes its home. <paramref name="move"/> as in <see cref="HideIn"/>.
        /// </summary>
        public void HideInWater(IEnumerable<WorldObstacle> waters, List<Vector3> taken, bool move)
        {
            if (!ambush.inWater || waters == null) return;
            Vector3 from = home;
            Vector3? spot = null;
            for (float r = 0; r <= ambush.coverSearch && spot == null; r += 1.5f)
            {
                int steps = r <= 0 ? 1 : 12;
                for (int i = 0; i < steps && spot == null; i++)
                {
                    float a = i * Mathf.PI * 2 / steps;
                    var probe = from + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                    if (!NavMesh.SamplePosition(probe, out var hit, 1.2f, NavMesh.AllAreas)) continue;
                    bool free = true;
                    if (taken != null) foreach (var other in taken) if (Flat(other - hit.position).magnitude < ambush.coverSpacing) { free = false; break; }
                    if (!free) continue;
                    foreach (var water in waters)
                        if (water && water.kind == ObstacleKind.Lake && water.InWater(hit.position)) { spot = hit.position; break; }
                }
            }
            if (spot == null) return;
            taken?.Add(spot.Value);
            home = spot.Value + Vector3.up * .15f;
            if (!move) return;
            body.position = home;
            transform.position = home;
            if (navigator.isOnNavMesh) navigator.Warp(home);
        }

        float sunk;
        bool passable;

        /// <summary>Under the water the player walks over it: its body does not block him (it still stands on the bottom).</summary>
        public bool Passable => passable;

        /// <summary>
        /// How far the drawing sinks below its feet now (lying under the water while it waits), metres at size one;
        /// its body stops blocking the way once it is under.
        /// </summary>
        float Submerged(float dt)
        {
            if (!ambush.submerge && !burrows) return 0;
            // A burrower (the worm) digs in when it notices the player (WorldCreature.Tricks.cs).
            bool under = burrows ? burrowed : Lurking && !lured;
            sunk = Mathf.MoveTowards(sunk, under ? 1 : 0, dt * (under ? (burrows ? 2.2f : 1.2f) : 3.5f));
            bool through = sunk >= .6f && !dying;
            if (burrows)
            {
                // Under the ground it has no body at all: nothing invisible to bump into or to hit.
                if (through != passable) { passable = through; SetBurrowed(through); }
                return sunk * (view ? view.height + .15f : 1.7f);
            }
            if (through != passable && capsule && world && world.motor && world.motor.characterController)
            {
                passable = through;
                Physics.IgnoreCollision(capsule, world.motor.characterController, through);
            }
            return sunk * (view ? view.height + .15f : 1.7f);
        }

        GameObject worn;
        bool disguised;

        /// <summary>Wears it now: lying in wait with a disguise, its drawing hidden and the 3D look shown.</summary>
        public bool Disguised => disguised;

        /// <summary>
        /// Picks the look to wear: the disguise named like <paramref name="like"/> (the plant it settles by), else one at
        /// random, sized like that plant (<paramref name="scale"/>; null keeps the authored size) and turned at random.
        /// </summary>
        public void WearDisguise(string like, Vector3? scale)
        {
            if (ambush.disguises == null || ambush.disguises.Length == 0) return;
            GameObject chosen = null;
            if (!string.IsNullOrEmpty(like))
                foreach (var look in ambush.disguises)
                    if (look && like.StartsWith(look.name, StringComparison.Ordinal)) { chosen = look; break; }
            if (!chosen) chosen = ambush.disguises[Random.Range(0, ambush.disguises.Length)];
            foreach (var look in ambush.disguises) if (look) look.SetActive(false);
            worn = chosen;
            if (!worn) return;
            if (scale.HasValue)
            {
                var parent = worn.transform.parent ? worn.transform.parent.lossyScale : Vector3.one;
                worn.transform.localScale = new Vector3(scale.Value.x / parent.x, scale.Value.y / parent.y, scale.Value.z / parent.z);
            }
            worn.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            disguised = false;
        }

        /// <summary>A free spot on the navigation next to <paramref name="plant"/>, on the side towards <paramref name="from"/> if it can.</summary>
        Vector3? BesideCover(Transform plant, Vector3 from)
        {
            Vector3 centre = plant.position;
            Vector3 away = Flat(from - centre);
            away = away.sqrMagnitude > .01f ? away.normalized : Vector3.forward;
            float reach = ambush.besideCover * Mathf.Max(.5f, Mathf.Max(plant.lossyScale.x, plant.lossyScale.z));
            for (int k = 0; k < 8; k++)
            {
                var probe = centre + Quaternion.Euler(0, k * 45 * (k % 2 == 0 ? 1 : -1), 0) * away * reach;
                if (NavMesh.SamplePosition(probe, out var hit, .8f, NavMesh.AllAreas)) return hit.position;
            }
            return null;
        }

        /// <summary>
        /// Each frame: while it lies in wait it wears its disguise and its drawing is gone; once it springs (or is hurt,
        /// lured, frozen) the disguise drops in a burst and the drawing is back.
        /// </summary>
        void UpdateDisguise()
        {
            if (ambush.disguises == null || ambush.disguises.Length == 0) return;
            if (!worn) WearDisguise(null, null);
            bool wear = Lurking && !lured && Alive && !dying;
            if (wear == disguised) return;
            disguised = wear;
            if (worn) worn.SetActive(wear);
            if (view) view.gameObject.SetActive(!wear);
            if (overhead) overhead.gameObject.SetActive(!wear);
            if (!wear && world && Time.time > 1)
            {
                var burst = ambush.revealVfx ? ambush.revealVfx : world.vfx ? world.vfx.dustPuff : null;
                world.Spawn(burst, Centre, Quaternion.identity, bodyColour, Mathf.Clamp(TopHeight, .6f, 1.6f));
            }
        }

        /// <summary>
        /// Lays out copies of its disguise round its home (a pumpkin patch), away from <paramref name="taken"/> spots
        /// (other creatures and copies), under <paramref name="parent"/>. Returns how many it laid.
        /// </summary>
        public int LayOutCompanions(Transform parent, System.Random random, List<Vector3> taken)
        {
            if (!ambush.companion) return 0;
            int low = Mathf.Min(ambush.companions.x, ambush.companions.y), high = Mathf.Max(ambush.companions.x, ambush.companions.y);
            int count = low + (int)(random.NextDouble() * (high - low + 1));
            int laid = 0;
            for (int n = 0, tries = 0; n < count && tries < count * 10; tries++)
            {
                double a = random.NextDouble() * Math.PI * 2, r = ambush.companionSpacing + random.NextDouble() * (ambush.companionRadius - ambush.companionSpacing);
                var probe = home + new Vector3((float)Math.Cos(a), 0, (float)Math.Sin(a)) * (float)r;
                if (!NavMesh.SamplePosition(probe, out var hit, 1, NavMesh.AllAreas)) continue;
                bool free = true;
                if (taken != null) foreach (var other in taken) if (Flat(other - hit.position).magnitude < ambush.companionSpacing) { free = false; break; }
                if (Flat(home - hit.position).magnitude < ambush.companionSpacing) free = false;
                if (!free) continue;
                var copy = Instantiate(ambush.companion, hit.position, Quaternion.Euler(0, (float)random.NextDouble() * 360, 0), parent);
                copy.transform.localScale *= .85f + (float)random.NextDouble() * .3f;
                taken?.Add(hit.position);
                n++;
                laid++;
            }
            return laid;
        }

        bool IsCover(string name)
        {
            foreach (var prefix in ambush.cover)
                if (!string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
