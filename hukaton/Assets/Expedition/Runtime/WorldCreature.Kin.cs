using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Standing up for kin: a peaceful creature that can fight back (a strike or a shot) turns on the player when it
    /// notices the player's side attack one of its own kind (<see cref="IsKinOf"/>) — a hit, a knock-down or a freeze,
    /// anything that provokes the victim itself. Close by it hears the cry from any side; further away it has to see
    /// it: a calm one not behind itself, and not through fog, rocks or trees (other bodies do not hide it). After a
    /// short start it acts as if it had been hit: the threat sound, the chase, the telegraphed attacks, and it calms
    /// down when the grudge runs out. Only an attack raises the alarm, so it never spreads from kin to kin by itself.
    /// A creature's strike or stray shot, a crash, brawlers and lured creatures are left out.
    /// </summary>
    public sealed partial class WorldCreature
    {
        [Header("Kin")]
        [Tooltip("Peaceful and able to fight back (a strike or a shot): turns on the player when it notices the player's side hit, knock down or freeze one of its own kind (the same name).")]
        public bool defendsKin = true;
        [Tooltip("An attack on its kin further away than this, metres, goes unnoticed. Within the hearing range it hears it from any side; beyond, the attack has to be in view.")]
        [Min(0)] public float kinWitnessRange = 12;
        [Tooltip("How long it stays angry after noticing it, seconds (a hit on itself: 8).")]
        [Min(0)] public float kinGrudgeSeconds = 8;
        [Tooltip("The start before it turns on the attacker, seconds: rolled between these two.")]
        public Vector2 kinReactionDelay = new Vector2(.15f, .45f);

        /// <summary>A calm creature does not see what happens behind it: the dot of its forward and the direction below this.</summary>
        const float BehindDot = -.25f;
        /// <summary>At most one cry per victim this often: a heavy strike hits and knocks down in the same frame.</summary>
        const float KinCryInterval = .25f;

        bool kinAlarmed;
        float kinAlarmAt, nextKinCry;
        ICombatant kinAttacker;
        static readonly RaycastHit[] kinSightHits = new RaycastHit[16];

        /// <summary>Peaceful, with something to fight back with (a strike or a shot), and <see cref="defendsKin"/> on.</summary>
        public bool StandsUpForKin => defendsKin && temperament == Temperament.Peaceful && (attackHalfHearts > 0 || HasRangedAttack);

        /// <summary>It noticed an attack on its kin and turns on the attacker in a moment.</summary>
        public bool AlarmedForKin => kinAlarmed;

        /// <summary>The same kind of creature: the same name (one per prefab), or, both without a name, the same drawing.</summary>
        public bool IsKinOf(WorldCreature other)
        {
            if (!other) return false;
            if (other == this) return true;
            if (!string.IsNullOrEmpty(displayName) || !string.IsNullOrEmpty(other.displayName)) return displayName == other.displayName;
            return view && other.view && view.idle && view.idle == other.view.idle;
        }

        /// <summary>
        /// Does a creature notice an attack on its kin <paramref name="distance"/> metres from its eyes? Never beyond
        /// <paramref name="range"/>; within <paramref name="hearing"/> it hears the cry from any side and through cover;
        /// further it has to see it: a <paramref name="calm"/> one not behind itself (<paramref name="facing"/>: the dot of
        /// its forward and the direction to the victim), and only with a <paramref name="clearView"/>.
        /// </summary>
        public static bool NoticesAttack(float distance, float range, float hearing, bool calm, float facing, bool clearView)
        {
            if (distance > range) return false;
            if (distance <= hearing) return true;
            if (calm && facing < BehindDot) return false;
            return clearView;
        }

        /// <summary>
        /// The player's side (<paramref name="by"/>: null for cards and hazards, or an ally) just hit, knocked down or froze
        /// <paramref name="victim"/>. If this is a peaceful creature of the same kind that can fight back and notices it,
        /// it turns on the attacker after <see cref="kinReactionDelay"/>. Called by <see cref="ExpeditionWorld.ReportPlayerAttack"/>.
        /// </summary>
        public void WitnessKinAttacked(WorldCreature victim, ICombatant by)
        {
            if (!victim || victim == this || !StandsUpForKin || !IsKinOf(victim)) return;
            if (!world || !world.Running || !Alive || !isActiveAndEnabled) return;
            // A brawl has its own referee; a lured one hears only the noise; frozen or knocked out, it notices nothing.
            if (Referee != null || Brawling || lured || Time.time < frozenUntil || Time.time < stunnedUntil) return;
            if (!NoticesAttackOn(victim)) return;
            float delay = Random.Range(Mathf.Min(kinReactionDelay.x, kinReactionDelay.y), Mathf.Max(kinReactionDelay.x, kinReactionDelay.y));
            float at = Time.time + Mathf.Max(0, delay);
            if (!kinAlarmed || at < kinAlarmAt) kinAlarmAt = at;
            kinAlarmed = true;
            kinAttacker = by;
        }

        bool NoticesAttackOn(WorldCreature victim)
        {
            Vector3 eye = Eye;
            Vector3 seen = victim.Centre;
            Vector3 to = seen - eye;
            float distance = to.magnitude;
            if (distance > kinWitnessRange) return false;
            float facing = distance > .01f ? Vector3.Dot(transform.forward, to / distance) : 1;
            bool clearView = distance <= hearingRange || KinInView(eye, seen, victim);
            return NoticesAttack(distance, kinWitnessRange, hearingRange, IsCalm, facing, clearView);
        }

        /// <summary>Fog and anything solid (rocks, trees, walls, the ground) hide the victim; bodies and flying cards do not.</summary>
        bool KinInView(Vector3 eye, Vector3 seen, WorldCreature victim)
        {
            if (world.IsVisionBlocked(eye, seen)) return false;
            Vector3 line = seen - eye;
            float distance = line.magnitude;
            if (distance < .01f) return true;
            int count = Physics.RaycastNonAlloc(eye, line / distance, kinSightHits, distance, ~(1 << 2), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = kinSightHits[i].collider;
                if (!hit || Combat.Of(hit) != null || hit.GetComponentInParent<CardProjectile>()) continue;
                if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(victim.transform)) continue;
                return false;
            }
            return true;
        }

        /// <summary>The player's side attacked this creature: its kin around may notice (throttled per victim).</summary>
        void CryForKin(ICombatant by)
        {
            if (!world || Time.time < nextKinCry) return;
            nextKinCry = Time.time + KinCryInterval;
            world.ReportPlayerAttack(this, by);
        }

        /// <summary>Update: the start is over, it turns on whoever attacked its kin (the player, or the ally that did it).</summary>
        void RiseForKin()
        {
            var by = kinAttacker;
            CancelKinAlarm();
            if (Brawling || Referee != null || lured) return;
            provokedUntil = Mathf.Max(provokedUntil, Time.time + kinGrudgeSeconds);
            // An ally that struck its kin draws the attack, as an ally's hit on itself would (TakeDamage).
            if (Combat.IsAlive(by) && !ReferenceEquals(by, world.Player) && by.Faction != Faction) SetTarget(by);
            lastSeen = TargetPosition;
        }

        void CancelKinAlarm()
        {
            kinAlarmed = false;
            kinAlarmAt = 0;
            kinAttacker = null;
        }

        void ResetKin()
        {
            CancelKinAlarm();
            nextKinCry = 0;
        }
    }
}
