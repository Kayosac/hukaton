using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The referee of one «Раздор» fight (prefab <c>Assets/Game/Prefabs/Cards/Discord/Discord brawl.prefab</c>), made by
    /// <see cref="DiscordEffect"/> at the hit. Every beast it was given fights the nearest other one in
    /// <see cref="WorldCreature"/>'s brawl mode (the same as «Звериная разборка», <see cref="Events.BeastBrawl"/>, but all
    /// against all): a short standoff, then blows; when a rival falls the beast turns to the nearest one left. Over when
    /// one fighter is left or after the card's maximum: the survivors go back to their ordinary behaviour (and to the
    /// event they came from), the referee goes away. A beast the player hurts, or walks right into, goes for the player.
    /// With fewer than two beasts it only makes them bristle for a moment.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DiscordBrawl : MonoBehaviour, IOutingSpawned, IBrawlReferee
    {
        [Header("Look")]
        [Tooltip("Flash where one fighter's blow lands on another (Retro Arsenal).")]
        public VfxBurst hitFlash;
        [Min(0)] public float hitFlashScale = .8f;
        public Color hitFlashTint = Color.white;
        [Tooltip("Dust kicked up where two fighters are at each other (Retro Arsenal).")]
        public VfxBurst brawlDust;
        [Min(0)] public float dustScale = 1;
        [Min(.05f)] public float dustInterval = .4f;
        public Color dustTint = new Color(.86f, .78f, .64f);

        sealed class Member
        {
            public WorldCreature creature;
            public IBrawlReferee previous;
            public bool againstPlayer;
            public float nextRetarget;
        }

        readonly List<Member> members = new List<Member>();
        ExpeditionWorld world;
        DiscordEffect rules;
        Vector3 centre;
        float arena = 8, started, standoffUntil, nextDust;
        bool fighting, done;

        /// <summary>Beasts are at each other (false while only bristling, and once over).</summary>
        public bool Fighting => fighting && !done;
        public bool Over => done;
        /// <summary>Seconds since the fight began.</summary>
        public float Seconds => Time.time - started;
        /// <summary>Fighters still in it: alive, not gone after the player.</summary>
        public int FightersLeft { get { int n = 0; foreach (var m in members) if (InFight(m)) n++; return n; } }
        /// <summary>Every beast the card caught, the fallen included.</summary>
        public IEnumerable<WorldCreature> Beasts { get { foreach (var m in members) if (m.creature) yield return m.creature; } }

        void IOutingSpawned.OnSpawned(ExpeditionWorld outing) => world = outing;

        /// <summary>
        /// Starts it, right after spawning: <paramref name="fight"/> false only makes them bristle. The fight stays
        /// within <paramref name="radius"/> plus the card's margin around this object.
        /// </summary>
        public void Begin(IReadOnlyList<WorldCreature> creatures, bool fight, float radius, DiscordEffect settings)
        {
            rules = settings;
            centre = transform.position;
            arena = Mathf.Max(2, radius + (rules ? rules.arenaMargin : 3));
            fighting = fight;
            started = Time.time;
            standoffUntil = started + (rules ? (fight ? rules.standoffSeconds : rules.bristleSeconds) : 1);
            foreach (var creature in creatures)
            {
                if (!creature || Find(creature) != null) continue;
                members.Add(new Member { creature = creature, previous = creature.Referee });
                creature.JoinBrawl(this);
            }
            foreach (var m in members)
            {
                var rival = fight ? NearestRival(m) : null;
                m.creature.Posture(rival != null ? rival.creature : null, centre, arena);
                m.nextRetarget = Time.time + Retarget;
            }
        }

        float Retarget => rules ? rules.retargetSeconds : .8f;

        bool InFight(Member m) => m.creature && m.creature.gameObject.activeInHierarchy && m.creature.Alive && !m.againstPlayer
            && ReferenceEquals(m.creature.Referee, this);

        void Update()
        {
            if (done || !world || !world.Running) return;
            if (!fighting)
            {
                if (Time.time >= standoffUntil) Finish();
                return;
            }
            bool standoff = Time.time < standoffUntil;
            Vector3 player = world.Player.Position;
            float notice = rules ? rules.noticeDistance : 0;
            foreach (var m in members)
            {
                if (!InFight(m)) continue;
                if (notice > 0 && Flat(player - m.creature.Position).magnitude < notice) { TurnOnPlayer(m); continue; }
                var current = m.creature.Target as WorldCreature;
                var currentMember = Find(current);
                bool lost = currentMember == null || !InFight(currentMember);
                bool holding = m.creature.Posturing;
                if (!lost && holding == standoff && Time.time < m.nextRetarget) continue;
                m.nextRetarget = Time.time + Retarget * Random.Range(.8f, 1.2f);
                var rival = NearestRival(m);
                if (rival == null) continue;
                if (!lost && rival != currentMember
                    && DiscordRules.KeepsRival(Distance(m, currentMember), Distance(m, rival), rules ? rules.loyalty : 1.5f))
                    rival = currentMember;
                if (standoff) m.creature.Posture(rival.creature, centre, arena);
                else m.creature.Fight(rival.creature, centre, arena);
            }
            if (!standoff) KickDust();
            if (DiscordRules.Over(FightersLeft, Seconds, rules ? rules.maximumSeconds : 45)) Finish();
        }

        /// <summary>Ends it now: everyone left goes back to their ordinary behaviour.</summary>
        public void Finish()
        {
            if (done) return;
            done = true;
            ReleaseAll();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            // The outing ended mid-fight: nobody is left brawling for a referee that is gone.
            if (!done) ReleaseAll();
            done = true;
        }

        void ReleaseAll()
        {
            foreach (var m in members) Release(m);
        }

        void Release(Member m)
        {
            var creature = m.creature;
            if (!creature || !ReferenceEquals(creature.Referee, this)) return;
            if (creature.Brawling) creature.LeaveBrawl();
            // Back to the event it came from (a «Звериная разборка» that is over), or to none.
            var previous = m.previous;
            if (previous is Object unityObject && !unityObject) previous = null;
            creature.JoinBrawl(previous);
        }

        void TurnOnPlayer(Member m)
        {
            if (m.againstPlayer || !m.creature || !m.creature.Alive) return;
            m.againstPlayer = true;
            m.creature.FightPlayer();
        }

        Member NearestRival(Member of)
        {
            Member best = null;
            float nearest = float.MaxValue;
            foreach (var m in members)
            {
                if (m == of || !InFight(m)) continue;
                float d = Distance(of, m);
                if (d < nearest) { nearest = d; best = m; }
            }
            return best;
        }

        static float Distance(Member a, Member b) => Flat(a.creature.Position - b.creature.Position).magnitude;

        Member Find(WorldCreature creature)
        {
            if (!creature) return null;
            foreach (var m in members) if (m.creature == creature) return m;
            return null;
        }

        void KickDust()
        {
            if (!brawlDust || Time.time < nextDust) return;
            nextDust = Time.time + dustInterval * Random.Range(.7f, 1.3f);
            Member a = null, b = null;
            float best = 2.6f;
            foreach (var m in members)
            {
                if (!InFight(m) || !(m.creature.Target is WorldCreature rival)) continue;
                var other = Find(rival);
                if (other == null || !InFight(other)) continue;
                float d = Distance(m, other);
                if (d < best && Random.value < .6f) { best = d; a = m; b = other; }
            }
            if (a == null || b == null) return;
            Vector3 at = Vector3.Lerp(a.creature.Position, b.creature.Position, Random.Range(.3f, .7f)) + Vector3.up * .2f;
            world.Spawn(brawlDust, at, Quaternion.identity, dustTint, dustScale * Random.Range(.8f, 1.25f));
        }

        // ---------------- referee ----------------

        void IBrawlReferee.CreatureHurt(WorldCreature creature, BrawlHurtSource source, ICombatant by, Vector3 impulse)
        {
            if (done || !this) return;
            var m = Find(creature);
            if (m == null) return;
            if (source == BrawlHurtSource.Creature)
            {
                if (hitFlash && world && Find(by as WorldCreature) != null)
                    world.Spawn(hitFlash, creature.Centre, Quaternion.identity, hitFlashTint, hitFlashScale);
                return;
            }
            // The player (or his ally) hurt it: it leaves the fight and goes for him, as in «Звериная разборка».
            if (source == BrawlHurtSource.Player) TurnOnPlayer(m);
        }

        void IBrawlReferee.CreatureBumped(WorldCreature creature, WorldCreature other) { }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
    }
}
