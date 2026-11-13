using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.Expeditions
{
    /// <summary>How a creature that only comes in squads walks its route (<see cref="PatrolSquad"/>).</summary>
    [Serializable]
    public sealed class PatrolSettings
    {
        [Tooltip("Comes in squads that walk a route together: one leads from point to point, the others keep their places behind it. " +
                 "A squad member that notices the player makes the others notice him too; one that goes for him takes them all along.")]
        public bool enabled;
        [Tooltip("Points of the route round the place the squad appeared.")]
        [Range(2, 8)] public int points = 4;
        [Tooltip("How far the route points lie from the place the squad appeared, metres: from and to.")]
        public Vector2 radius = new Vector2(4, 7);
        [Tooltip("At every point the squad stops and looks round this long, seconds: from and to.")]
        public Vector2 pause = new Vector2(2, 4);
        [Tooltip("Distance between neighbours in the column, metres.")]
        [Min(.5f)] public float spacing = 1.6f;
        [Tooltip("The leader waits while one of the squad is further than this from its place, metres.")]
        [Min(1)] public float straggle = 4;
        [Tooltip("A squad member far from its place hurries: its walk is this many times faster.")]
        [Min(1)] public float hurry = 1.6f;
        [Tooltip("A hunting pack (goblin dogs): in a fight the members spread round the target and come at it from the sides, not all from one.")]
        public bool flank;
        [Tooltip("The flankers first make for their side this far from the target, metres, then close in.")]
        [Min(1)] public float flankRadius = 5;
    }

    /// <summary>
    /// A squad on patrol (giant dung beetles): the first calm member leads from route point to route point and stops
    /// at each to look round; the others follow in a column of pairs behind it. While any of them fights, the squad
    /// holds; each goes back to where it left its place, and the walk goes on once all are calm and together again.
    /// When one notices the player (the threat stance) the others turn on him too; when one goes for him, so do all.
    /// </summary>
    public sealed class PatrolSquad
    {
        readonly List<WorldCreature> members = new List<WorldCreature>();
        readonly Vector3[] route;
        readonly PatrolSettings settings;
        readonly System.Random random;
        int next, steppedFrame = -1;
        float pauseUntil;
        bool broadcasting;
        Vector3 heading;

        public IReadOnlyList<WorldCreature> Members => members;
        public IReadOnlyList<Vector3> Route => route;
        public int NextPoint => next;
        /// <summary>The member that leads: the first one still alive.</summary>
        public WorldCreature Leader { get; private set; }
        /// <summary>The leader walks on (not holding for a fight, not paused at a point, not waiting for stragglers).</summary>
        public bool Moving { get; private set; }
        /// <summary>One of the squad is fighting or on its way back: the others stay where they are.</summary>
        public bool Holding { get; private set; }

        PatrolSquad(Vector3[] route, PatrolSettings settings, System.Random random, Vector3 heading)
        {
            this.route = route;
            this.settings = settings;
            this.random = random;
            this.heading = heading;
        }

        /// <summary>
        /// A squad of <paramref name="group"/> (just spawned round <paramref name="centre"/>) with a route of points on
        /// the navigation round it; <paramref name="seed"/> makes the route the same whenever the place is loaded again.
        /// </summary>
        public static PatrolSquad Form(IReadOnlyList<WorldCreature> group, Vector3 centre, int seed)
        {
            if (group == null || group.Count == 0 || !group[0]) return null;
            var settings = group[0].patrol;
            var random = new System.Random(seed);
            int count = Mathf.Max(2, settings.points);
            float start = (float)random.NextDouble() * Mathf.PI * 2;
            float low = Mathf.Min(settings.radius.x, settings.radius.y), high = Mathf.Max(settings.radius.x, settings.radius.y);
            var points = new List<Vector3>();
            for (int i = 0; i < count; i++)
            {
                // Round the place, one after another, each a little off the circle: a walk, not a parade ground.
                float a = start + i * Mathf.PI * 2 / count + ((float)random.NextDouble() - .5f) * .6f;
                float r = Mathf.Lerp(low, high, (float)random.NextDouble());
                var probe = centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                if (NavMesh.SamplePosition(probe, out var hit, 3, NavMesh.AllAreas)) points.Add(hit.position);
                else if (!NavMesh.SamplePosition(centre, out _, 3, NavMesh.AllAreas)) points.Add(probe);
            }
            if (points.Count == 0) points.Add(centre);
            var first = points[0] - centre;
            first.y = 0;
            var squad = new PatrolSquad(points.ToArray(), settings, random, first.sqrMagnitude > .01f ? first.normalized : Vector3.forward);
            foreach (var member in group)
            {
                if (!member) continue;
                squad.members.Add(member);
                member.JoinSquad(squad);
            }
            squad.Leader = squad.FirstAlive();
            return squad;
        }

        WorldCreature FirstAlive()
        {
            foreach (var member in members) if (member && member.Alive && member.isActiveAndEnabled) return member;
            return null;
        }

        /// <summary>Once a frame, whichever member asks first: who leads, whether to hold, walk or wait, where to.</summary>
        public void Step()
        {
            if (steppedFrame == Time.frameCount) return;
            steppedFrame = Time.frameCount;
            Leader = FirstAlive();
            Holding = false;
            foreach (var member in members)
                if (member && member.Alive && member.isActiveAndEnabled && !member.OnPatrol) { Holding = true; break; }
            Moving = false;
            if (!Leader || Holding || route.Length < 2) return;

            Vector3 to = route[next] - Leader.Position;
            to.y = 0;
            if (to.magnitude < 1.2f)
            {
                if (pauseUntil <= 0)
                {
                    float low = Mathf.Min(settings.pause.x, settings.pause.y), high = Mathf.Max(settings.pause.x, settings.pause.y);
                    pauseUntil = Time.time + Mathf.Lerp(low, high, (float)random.NextDouble());
                }
                if (Time.time < pauseUntil) return;
                pauseUntil = 0;
                next = (next + 1) % route.Length;
                to = route[next] - Leader.Position;
                to.y = 0;
            }
            // Nobody is left behind: the leader waits for the stragglers.
            foreach (var member in members)
                if (member && member != Leader && member.Alive && member.isActiveAndEnabled && Flat(SlotOf(member) - member.Position).magnitude > settings.straggle)
                    return;
            Moving = true;
            if (to.sqrMagnitude > .01f) heading = to.normalized;
        }

        /// <summary>Where the leader heads.</summary>
        public Vector3 Destination => route.Length > 0 ? route[next] : Leader ? Leader.Position : Vector3.zero;
        /// <summary>The way the squad faces.</summary>
        public Vector3 Heading => heading;
        public PatrolSettings Settings => settings;

        /// <summary>
        /// A follower's place: in pairs behind the leader, the first pair right behind it, each next one a step further.
        /// </summary>
        public Vector3 SlotOf(WorldCreature member)
        {
            if (!Leader || member == Leader) return member ? member.Position : Vector3.zero;
            int place = 0;
            foreach (var other in members)
            {
                if (other == member) break;
                if (other && other != Leader && other.Alive && other.isActiveAndEnabled) place++;
            }
            int row = place / 2 + 1;
            float side = place % 2 == 0 ? -.55f : .55f;
            var right = new Vector3(heading.z, 0, -heading.x);
            return Leader.Position - heading * (row * settings.spacing) + right * (side * settings.spacing);
        }

        /// <summary>
        /// <paramref name="from"/> turned on the player (<paramref name="state"/>: the threat stance or the attack): the
        /// others follow it. Called by the member itself; the members it stirs do not call back.
        /// </summary>
        public void Raise(WorldCreature from, CreatureState state)
        {
            if (broadcasting || (state != CreatureState.Threat && state != CreatureState.Chase)) return;
            broadcasting = true;
            try
            {
                foreach (var member in members)
                    if (member && member != from && member.Alive && member.isActiveAndEnabled)
                        member.SquadAlarm(from, state == CreatureState.Chase);
            }
            finally { broadcasting = false; }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
    }

    public sealed partial class WorldCreature
    {
        [Header("Patrol")]
        [Tooltip("Squads: in the world it only comes in groups that walk a route together and fight together.")]
        public PatrolSettings patrol = new PatrolSettings();

        PatrolSquad squad;

        /// <summary>The squad it walks with; null for a creature on its own.</summary>
        public PatrolSquad Squad => squad;

        /// <summary>Calm and free to walk with the squad: not fighting, not on its way back, not lured or held.</summary>
        public bool OnPatrol => IsCalm && State != CreatureState.Investigate && !lured && !Brawling && Referee == null
            && Time.time >= frozenUntil && Time.time >= stunnedUntil;

        /// <summary>Joins <paramref name="joined"/> (set by <see cref="PatrolSquad.Form"/> after it appears).</summary>
        public void JoinSquad(PatrolSquad joined) => squad = joined;

        /// <summary>Calm: walks with the squad instead of wandering about. False when it has no squad to walk with.</summary>
        bool ThinkPatrol()
        {
            if (squad == null || !OnPatrol) return false;
            squad.Step();
            if (!squad.Leader) return false;
            bool leads = squad.Leader == this;
            Vector3 goal = leads ? squad.Destination : squad.SlotOf(this);
            float off = Flat(goal - body.position).magnitude;
            bool walk = leads ? squad.Moving : off > .6f;
            if (walk)
            {
                if (State != CreatureState.Wander) Enter(CreatureState.Wander);
                Go(goal, wanderSpeed * (!leads && off > squad.Settings.spacing * 1.5f ? squad.Settings.hurry : 1));
            }
            else
            {
                if (State != CreatureState.Idle) Enter(CreatureState.Idle);
                Halt();
                Face(squad.Heading);
            }
            // After a fight it comes back to where it walked with the squad, not to where the squad first appeared.
            home = body.position;
            return true;
        }

        static readonly float[] flankTurns = { 0, 75, -75, 145, -145 };

        /// <summary>
        /// A flanking pack member on its way to its own side of the target: the first of the fighting pack comes head on,
        /// the next from the left and the right, the rest from behind. False once it is on its side (or the target is
        /// right on it, or it hunts alone): then it attacks as usual.
        /// </summary>
        bool Flanking(float distance, out Vector3 goal)
        {
            var target = TargetPosition;
            goal = target;
            if (squad == null || !patrol.flank || distance <= contactRange) return false;
            int place = 0, fighting = 0;
            Vector3 centre = Vector3.zero;
            foreach (var mate in squad.Members)
            {
                if (!mate || !mate.Alive || !mate.isActiveAndEnabled || mate.IsCalm) continue;
                if (mate == this) place = fighting;
                fighting++;
                centre += mate.Position;
            }
            if (fighting < 2) return false;
            float turn = flankTurns[place % flankTurns.Length];
            if (turn == 0) return false;
            Vector3 from = Flat(centre / fighting - target);
            if (from.sqrMagnitude < .01f) from = Flat(body.position - target);
            if (from.sqrMagnitude < .01f) return false;
            Vector3 side = Quaternion.Euler(0, turn, 0) * from.normalized;
            Vector3 mine = Flat(body.position - target);
            // On its side already: in it goes.
            if (mine.magnitude < patrol.flankRadius * 1.35f && Vector3.Angle(mine, side) < 40) return false;
            goal = target + side * patrol.flankRadius;
            return true;
        }

        /// <summary>Fighting, and a squad mate that fights the same target sees it: the squad hunts as one.</summary>
        bool SquadSees()
        {
            if (squad == null || IsCalm) return false;
            var target = Target;
            foreach (var mate in squad.Members)
                if (mate && mate != this && mate.Alive && mate.sees && !mate.IsCalm && ReferenceEquals(mate.Target, target)) return true;
            return false;
        }

        /// <summary>
        /// A squad mate turned on the player: in the threat stance this one also turns to face him (and judges for itself
        /// whether to attack); when the mate attacks, this one attacks too, at the same target.
        /// </summary>
        public void SquadAlarm(WorldCreature from, bool attack)
        {
            if (!world || !world.Running || !Alive || dying) return;
            if (Brawling || Referee != null || lured || Time.time < frozenUntil || Time.time < stunnedUntil) return;
            if (attack)
            {
                provokedUntil = Mathf.Max(provokedUntil, Time.time + 8);
                if (from && Combat.IsAlive(from.attackTarget)) SetTarget(from.attackTarget);
                lastSeen = TargetPosition;
                if (IsCalm || State == CreatureState.Threat) { squash.Kick(-3); Enter(CreatureState.Chase); }
            }
            else if (IsCalm) Enter(CreatureState.Threat);
        }
    }
}
