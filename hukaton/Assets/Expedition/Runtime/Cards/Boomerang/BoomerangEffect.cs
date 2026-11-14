using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Бумеранг» (Бой · УРОН, ранг 2, 170 монет) — первая карта со своим полётом (<see cref="ICustomFlight"/>). Брошенная
    /// карта летит без гравитации вперёд по прицелу с лёгким загибом влево, на дальности 10 / 12 / 15 м разворачивается
    /// и возвращается, наводясь на игрока (<see cref="BoomerangRules"/>, <see cref="BoomerangFlight"/>). По пути
    /// пролетает сквозь существ и ранит каждого (звери, мимики; союзников и игрока — нет) на 2 / 3 / 5 HP, одного и того
    /// же — не чаще раза туда и раза обратно, с небольшим толчком по полёту. Твёрдое препятствие — удар, отскок и
    /// падение, карта уходит в сброс. Долетела до игрока (до ~1 м от тела) — поймана: та же копия снова в руке
    /// (<see cref="Hakaton.Decks.Expedition.Catch"/>). Не долетела за отведённое время — падает, в сброс. В полёте
    /// вращается плашмя быстрее обычного и свистит.
    /// Эхо, повторяя Бумеранг, бьёт как мгновенный удар в своей точке попадания (<see cref="Apply"/>): из точки эха
    /// бумерангу некуда возвращаться.
    /// </summary>
    public sealed class BoomerangEffect : CardEffect, ICardStatLine, ICustomFlight
    {
        [Header("Числа таблицы (I / II / III)")]
        [Tooltip("Урон за каждое касание, HP.")]
        public LevelInt damage = new LevelInt(2, 3, 5);
        [Tooltip("Дальность: где бумеранг разворачивается, м.")]
        public LevelFloat range = new LevelFloat(10, 12, 15);

        [Header("Полёт")]
        [Tooltip("Скорость бумеранга, м/с: скорость броска (заряд), зажатая в эти пределы.")]
        [Min(.1f)] public float minimumSpeed = 11;
        [Min(.1f)] public float maximumSpeed = 18;
        [Tooltip("Насколько уходит влево за путь туда, градусов (загиб бумеранга).")]
        [Range(0, 60)] public float curlDegrees = 8;
        [Tooltip("Скорость разворота на дальности, градусов в секунду; на обратном пути растёт.")]
        [Min(30)] public float turnDegreesPerSecond = 480;
        [Tooltip("Наибольший наклон полёта вверх или вниз, градусов.")]
        [Range(0, 89)] public float maximumPitch = 30;
        [Tooltip("Запас времени сверх пути туда-обратно, с: потом бумеранг падает (в сброс).")]
        [Min(0)] public float spareSeconds = 1;
        [Tooltip("Поймали, если карта на обратном пути ближе этого к оси тела игрока, м.")]
        [Min(.1f)] public float catchRadius = 1;
        [Tooltip("Существо задето, если ближе этого к пути карты, м.")]
        [Min(.01f)] public float hitRadius = .3f;
        [Tooltip("Во сколько раз быстрее обычной карты вращается в полёте.")]
        [Min(0)] public float spinMultiplier = 2.4f;

        [Header("Попадание")]
        [Tooltip("Толчок по направлению полёта (единицы импульса).")]
        [Min(0)] public float nudge = 1.6f;
        [Tooltip("Доля толчка вверх.")]
        [Range(0, 1)] public float lift = .15f;

        [Header("Звуки")]
        [Tooltip("Свист в полёте (петля).")]
        public SfxCue whistle;
        [Tooltip("Поймал: карта снова в руке.")]
        public SfxCue catchSound;

        /// <summary>Касание существа на пути, удар о препятствие — и повтор Эхом (мгновенный удар в точке).</summary>
        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            var fighter = c.HitCombatant;
            bool meaningful = false;
            if (IsPrey(world, fighter))
            {
                int amount = c.At(damage);
                var push = BoomerangRules.Push(c.Velocity.sqrMagnitude > .001f ? c.Velocity : c.Direction, nudge, lift);
                if (fighter is WorldCreature creature) { creature.Damage(amount, push, c.Point, 0); meaningful = true; }
                else meaningful = fighter.TakeDamage(new DamageInfo(amount, push, c.Point, c.ThrowOrigin));
            }
            Feedback(c, meaningful, meaningful ? 1 : .6f);
        }

        /// <summary>Кого бумеранг ранит: живые звери и мимики; союзников (деревья) и игрока — нет.</summary>
        public static bool IsPrey(ExpeditionWorld world, ICombatant fighter)
            => Combat.IsAlive(fighter) && !(world && ReferenceEquals(fighter, world.Player)) && fighter.Faction != Faction.Player
               && (fighter is WorldCreature || fighter is ICardCreature);

        // ---------- свой полёт ----------

        public bool MayComeBack => true;

        public ICardFlightPath BeginFlight(in CardFlightLaunch launch) => new BoomerangFlight(this, launch);

        public int PreviewPath(in CardFlightLaunch launch, Vector3[] points) => BoomerangFlight.Preview(this, launch, points);

        /// <summary>Начало полёта по числам этого ассета для уровня копии.</summary>
        public BoomerangPath Begin(in CardFlightLaunch launch)
            => BoomerangRules.Begin(launch.Origin, launch.Velocity, range.At(launch.Level), minimumSpeed, maximumSpeed,
                curlDegrees, turnDegreesPerSecond, maximumPitch, spareSeconds);

        /// <summary>Ось тела игрока (низ и верх капсулы без полусфер) и грудь — куда бумеранг возвращается.</summary>
        public static void Body(ExpeditionWorld world, out Vector3 bottom, out Vector3 top, out Vector3 chest)
        {
            var motor = world ? world.motor : null;
            if (!motor || !motor.characterController)
            {
                bottom = top = chest = Vector3.zero;
                return;
            }
            var controller = motor.characterController;
            Vector3 centre = motor.transform.TransformPoint(controller.center);
            float half = Mathf.Max(0, controller.height * .5f - controller.radius);
            bottom = centre - Vector3.up * half;
            top = centre + Vector3.up * half;
            chest = centre + Vector3.up * half * .5f;
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Урон";
            values = damage;
            unit = "";
            return true;
        }
    }
}
