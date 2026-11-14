using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Обвал «Камнепада» (префаб <c>Assets/Game/Prefabs/Cards/Rockfall/Rockfall.prefab</c>), стоит на земле в точке
    /// попадания. Сразу сыплет сверху камни и, пока они летят, контур зоны виден на земле (<see cref="ICardAreaOutline"/>).
    /// Камни — меш-частицы системы Retro Arsenal StonesFalling: четыре кривых камня мира (тело и верх лесных валунов
    /// RK01, RK02, RK05, RK06 — <c>Rockfall stone RK*.asset</c>, материал M_Forest_Rock), разного размера, крутятся и
    /// падают отвесно из объёма над зоной — весь разброс, с пылью и обломками, внутри кольца. Коснувшись земли (или
    /// зверя), камень пропадает в пыли Retro Arsenal и паре мелких обломков, которые тают. Через задержку — обвал:
    /// грохот, урон всем бойцам в зоне (<see cref="RockfallRules.Reaches"/>), мелких зверей оглушает. Потом объект
    /// удаляется — ни камней, ни коллайдеров в мире не остаётся.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RockfallZone : CardZoneBehaviour, ICardAreaOutline
    {
        [Header("Камни (меш-частицы в системе Retro Arsenal)")]
        [Tooltip("Падающие камни: объём-цилиндр над зоной, падают отвесно; радиус подгоняется под зону.")]
        public ParticleSystem stones;
        [Tooltip("Доля радиуса зоны, по которой сыплются камни. С запасом на половину крупного камня, пыль и обломки " +
                 "(до ~0,5 м от места удара): весь разброс остаётся внутри кольца зоны.")]
        [Range(.1f, 1.2f)] public float stonesSpread = .72f;
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

        /// <summary>
        /// Ранит всех бойцов в зоне (игрока — его числом, в Пузыре — никак), мелких зверей оглушает. True, если
        /// кого-то задело.
        /// </summary>
        public bool HitEveryoneInside(Vector3 centre)
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
                // Игрок в Пузыре неуязвим к своему обвалу: ни урона, ни толчка.
                if (player && CardBarriers.ShieldsPlayer(world, fighter.Position)) continue;
                var push = RockfallRules.Push(centre, fighter.Position, numbers.knockback, numbers.lift);
                // Игроку — своё число в половинках сердца (Хрупкость и защита после удара — как у любого удара).
                int amount = player ? numbers.playerDamage : numbers.damage;
                fighter.TakeDamage(new DamageInfo(amount, push, fighter.Centre, above, DamageKind.Hit));
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
