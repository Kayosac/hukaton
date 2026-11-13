using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Обвал «Камнепада» (префаб <c>Assets/Game/Prefabs/Cards/Rockfall/Rockfall.prefab</c>), стоит на земле в точке
    /// попадания. Сразу сыплет сверху камни (частицы Retro Arsenal, разброс по радиусу зоны) и, пока они летят, контур
    /// зоны виден на земле (<see cref="ICardAreaOutline"/>). Через задержку камни обрушиваются: пыль и грохот, урон всем
    /// бойцам в зоне (<see cref="RockfallRules.Reaches"/>), мелких зверей оглушает. Потом камни догорают частицами и
    /// объект удаляется — твёрдого в мире не остаётся.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RockfallZone : CardZoneBehaviour, ICardAreaOutline
    {
        [Header("Камни (частицы Retro Arsenal)")]
        [Tooltip("Падающие камни: конус вниз над зоной; радиус его основания подгоняется под зону.")]
        public ParticleSystem stones;
        [Tooltip("Доля радиуса зоны, по которой сыплются камни.")]
        [Range(.1f, 1.2f)] public float stonesSpread = .85f;
        [Tooltip("Камней на квадратный метр зоны (и пределы).")]
        [Min(0)] public float stonesPerSquareMetre = 1.1f;
        [Min(1)] public int minStones = 12, maxStones = 60;

        [Header("Обвал")]
        [Tooltip("Пыль и обломки в момент обвала (Retro Arsenal), размер — на метр радиуса.")]
        public VfxBurst crashVfx;
        [Min(0)] public float crashScalePerMetre = .35f;
        public SfxCue crashSound;
        [Range(0, 1)] public float shake = .45f;
        [Min(0)] public float hitStop = .05f;
        [Tooltip("Сколько объект живёт после обвала, пока камни и пыль оседают, с.")]
        [Min(0)] public float lingerSeconds = 2.5f;

        static readonly List<ICombatant> Fighters = new List<ICombatant>();

        RockfallStrike numbers;
        float strikeAt, struckAt;
        bool configured, struck;

        public override CardZoneKind Kind => CardZoneKind.Other;

        /// <summary>Камни ещё летят: зона ждёт обвала.</summary>
        public bool Pending => World && configured && !struck;

        /// <summary>Числа карты сразу после появления.</summary>
        public void Setup(in RockfallStrike strike)
        {
            numbers = strike;
            radius = Mathf.Max(.1f, strike.radius);
            strikeAt = Time.time + Mathf.Max(0, strike.delay);
            configured = true;
            if (!stones) return;
            var shape = stones.shape;
            shape.radius = radius * stonesSpread;
            var emission = stones.emission;
            if (emission.burstCount > 0)
            {
                var burst = emission.GetBurst(0);
                burst.count = RockfallRules.StoneCount(radius, stonesPerSquareMetre, minStones, maxStones);
                emission.SetBurst(0, burst);
            }
            stones.Play(true);
        }

        void Update()
        {
            if (!World || !configured) return;
            if (!struck && Time.time >= strikeAt) Strike();
            if (struck && Time.time - struckAt >= lingerSeconds) Destroy(gameObject);
        }

        void Strike()
        {
            struck = true;
            struckAt = Time.time;
            World.Zones.Remove(this);
            Vector3 centre = transform.position;
            World.Spawn(crashVfx, centre, Quaternion.identity, Color.white, crashScalePerMetre * radius);
            World.Play(crashSound, centre);
            bool any = HitEveryoneInside(centre);
            Shake(centre, any);
        }

        /// <summary>Ранит всех бойцов в зоне, мелких зверей оглушает. True, если кого-то задело.</summary>
        bool HitEveryoneInside(Vector3 centre)
        {
            var world = World;
            bool any = false;
            Vector3 above = centre + Vector3.up * Mathf.Max(1, height);
            Fighters.Clear();
            world.Combatants(Fighters);
            foreach (var fighter in Fighters)
            {
                if (!Combat.IsAlive(fighter) || !fighter.Root) continue;
                bool player = ReferenceEquals(fighter, world.Player);
                if (!player && !numbers.hurtAllies && fighter.Faction == Faction.Player) continue;
                if (!RockfallRules.Reaches(centre, radius, depth, height, fighter.Position)) continue;
                var push = RockfallRules.Push(centre, fighter.Position, numbers.knockback, numbers.lift);
                fighter.TakeDamage(new DamageInfo(numbers.damage, push, fighter.Centre, above, DamageKind.Hit));
                any = true;
                if (fighter is WorldCreature creature && creature.Alive && creature.body
                    && RockfallRules.IsSmall(creature.body.mass, numbers.smallMaxMass) && numbers.smallStunSeconds > 0)
                    creature.Stagger(numbers.smallStunSeconds, centre);
            }
            return any;
        }

        void Shake(Vector3 centre, bool hitSomeone)
        {
            var world = World;
            if (world.feel && world.viewCamera)
            {
                float distance = Vector3.Distance(world.viewCamera.transform.position, centre);
                world.feel.AddTrauma(shake * Mathf.Clamp01(1.4f - distance / 16f));
            }
            if (hitSomeone && hitStop > 0 && world.time) world.time.HitStop(hitStop);
        }

        /// <summary>Контур на земле, пока камни летят (<see cref="CardAreaDisplay"/>).</summary>
        public bool TryGetOutline(out Vector3 centre, out float radius)
        {
            centre = transform.position;
            radius = this.radius;
            return Pending;
        }
    }
}
