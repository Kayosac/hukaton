using System;
using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Струя» (Вода · АТАКА, ранг 2, 130 монет). Прямое попадание в зверя: урон 2 / 3 / 5, лёгкий отброс по
    /// направлению броска (импульс 4 / 5 / 6), цель становится мокрой на 30 с (<see cref="WetStatus"/>: Молния ×2,
    /// Лёд заковывает), горящий зверь гаснет (<see cref="WorldCreature.Douse"/>). Мимо зверя — всплеск: всех бойцов
    /// (и игрока) в радиусе <see cref="splashRadius"/> мочит и тушит, горящую смолу там гасит, без урона.
    /// </summary>
    public sealed class WaterJetEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Числа таблицы (I / II / III)")]
        [Tooltip("Урон прямого попадания, HP.")]
        public LevelInt damage = new LevelInt(2, 3, 5);
        [Tooltip("Отброс по направлению броска (единицы импульса, Δv для массы 1).")]
        public LevelFloat knockback = new LevelFloat(4, 5, 6);
        [Tooltip("Сколько секунд цель остаётся мокрой.")]
        [Min(0)] public float wetSeconds = WetStatus.DefaultLingerSeconds;

        [Header("Попадание")]
        [Tooltip("Доля отброса, которая идёт вверх (чтобы зверя чуть подбросило, а не протащило по земле).")]
        [Range(0, 1)] public float lift = .2f;
        [Tooltip("Всплеск мимо зверя мочит и тушит всех в этом радиусе, м.")]
        [Min(0)] public float splashRadius = 1;
        [Tooltip("Во сколько раз крупнее брызги всплеска мимо зверя.")]
        [Min(0)] public float splashScale = 1.4f;

        [Header("Тушение")]
        [Tooltip("Пар над погашенным зверем (Retro Arsenal).")]
        public VfxBurst douseVfx;
        [Min(0)] public float douseScale = 1;
        public SfxCue douseSound;

        static readonly List<ICombatant> Fighters = new List<ICombatant>();
        static readonly List<ICardZone> Zones = new List<ICardZone>();

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            var hit = c.HitCombatant;
            bool direct = Combat.IsAlive(hit) && !ReferenceEquals(hit, world.Player);
            bool meaningful;
            if (direct)
            {
                Drench(world, hit);
                var push = WaterJetRules.Push(c.Direction, c.At(knockback), lift);
                int amount = c.At(damage);
                if (hit is WorldCreature creature) creature.Damage(amount, push, c.Point, 0);
                else if (hit is ICardCreature other) other.TakeDamage(new DamageInfo(amount, push, c.Point, c.ThrowOrigin));
                meaningful = true;
            }
            else meaningful = Splash(c);
            Feedback(c, meaningful, direct ? 1 : splashScale);
        }

        /// <summary>Всплеск мимо зверя: мочит и тушит бойцов рядом, гасит горящую смолу.</summary>
        bool Splash(in ImpactContext c)
        {
            var world = c.World;
            bool any = false;
            Fighters.Clear();
            world.Combatants(Fighters);
            foreach (var fighter in Fighters)
            {
                if (!Combat.IsAlive(fighter) || !fighter.Root) continue;
                if (!WaterJetRules.InSplash(c.Point, splashRadius, fighter.Position, fighter.Centre)) continue;
                Drench(world, fighter);
                any = true;
            }
            Zones.Clear();
            world.Zones.Near(c.Point, splashRadius, Zones, CardZoneKind.TarPuddle);
            foreach (var zone in Zones)
                if (zone is TarPuddleZone tar && tar && tar.Extinguish()) any = true;
            return any;
        }

        /// <summary>Мочит бойца (игроку — значок мокроты под сердцами) и тушит его, если он горел.</summary>
        void Drench(ExpeditionWorld world, ICombatant fighter)
        {
            bool player = ReferenceEquals(fighter, world.Player);
            bool wasWet = player && WetStatus.IsWet(fighter.Root);
            var creature = fighter as WorldCreature;
            bool doused = WaterJetRules.Drench(fighter.Root, world, wetSeconds, creature ? (Func<bool>)creature.Douse : null);
            if (player && !wasWet && world.vfx && world.vfx.wetBadge) PlayerStatusBadge.Show(world, world.vfx.wetBadge);
            if (!doused) return;
            world.Spawn(douseVfx, fighter.Centre, Quaternion.identity, Color.white, douseScale * (creature ? Mathf.Max(.6f, creature.BodyHeight) : 1));
            world.Play(douseSound, fighter.Centre);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Урон";
            values = damage;
            unit = "";
            return true;
        }

        /// <summary>Кольцо всплеска — только когда карта летит не в зверя.</summary>
        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = splashRadius;
            return splashRadius > 0 && Combat.Of(target) == null;
        }

        bool ICardArea.LeavesOutlinedZone => false;
    }

    /// <summary>Правила «Струи» без сцены (EditMode-тесты).</summary>
    public static class WaterJetRules
    {
        /// <summary>
        /// Отброс по направлению броска: горизонтальная часть направления силой <paramref name="strength"/> и
        /// доля <paramref name="lift"/> вверх. Отвесный бросок толкает вперёд по миру (как Удар).
        /// </summary>
        public static Vector3 Push(Vector3 direction, float strength, float lift)
        {
            direction.y = 0;
            var flat = direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward;
            return flat * strength + Vector3.up * strength * lift;
        }

        /// <summary>Всплеск достаёт бойца: его ноги или середина тела не дальше <paramref name="radius"/> от точки.</summary>
        public static bool InSplash(Vector3 point, float radius, Vector3 feet, Vector3 centre)
        {
            float r2 = radius * radius;
            return (feet - point).sqrMagnitude <= r2 || (centre - point).sqrMagnitude <= r2;
        }

        /// <summary>
        /// Делает тело мокрым на <paramref name="wetSeconds"/> (повторное намокание обновляет срок, не складывается)
        /// и тушит его через <paramref name="douse"/>. Возвращает true, если тело горело и погасло.
        /// </summary>
        public static bool Drench(Component body, ExpeditionWorld world, float wetSeconds, Func<bool> douse)
        {
            if (!body) return false;
            WetStatus.Soak(body, world, wetSeconds);
            return douse != null && douse();
        }
    }
}
