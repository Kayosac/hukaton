using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Камнепад» (Земля · АТАКА, ранг 2, 170 монет). Карта попадает в точку (в зверя или в землю) и ставит на земле
    /// под ней зону обвала (<see cref="RockfallZone"/>): сверху сразу начинают падать камни, контур зоны виден на
    /// земле, и через <see cref="delay"/> с камни обрушиваются — ранят на 3 / 4 / 6 зверей, мимиков и союзников в
    /// радиусе 3 / 4 / 5 м, а самого игрока, если он внутри, — на 1 / 2 / 3 половинки сердца (в Пузыре игрок
    /// неуязвим к обвалу). Мелких зверей (масса μ не больше
    /// <see cref="smallMaxMass"/>, то есть не тяжелее игрока) ещё и оглушает на <see cref="smallStunSeconds"/> с.
    /// Камни — только частицы: после обвала в мире не остаётся ни объектов, ни коллайдеров.
    /// </summary>
    public sealed class RockfallEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Числа таблицы (I / II / III)")]
        [Tooltip("Урон каждому зверю, мимику и союзнику в зоне, HP.")]
        public LevelInt damage = new LevelInt(3, 4, 6);
        [Tooltip("Урон игроку в зоне, половинки сердца (своё число; Хрупкость удваивает, как любой удар).")]
        public LevelInt playerDamage = new LevelInt(1, 2, 3);
        [Tooltip("Радиус зоны обвала, м.")]
        public LevelFloat radius = new LevelFloat(3, 4, 5);
        [Tooltip("Через сколько секунд после попадания обрушиваются камни.")]
        [Min(0)] public float delay = .5f;

        [Header("Мелкие звери")]
        [Tooltip("Мелких зверей камни оглушают (сбивают с ног) на столько секунд.")]
        [Min(0)] public float smallStunSeconds = 1;
        [Tooltip("Мелкий зверь — с массой μ не больше этой (1 — масса игрока): червяк 0,2, грибы и пчёлы 0,3, " +
                 "комары 0,4, клоун 0,5, призрак 0,6, крыса-тыква 0,8. Собака гоблина (1,5) и крупнее — уже нет.")]
        [Min(0)] public float smallMaxMass = 1;

        [Header("Удар")]
        [Tooltip("Ранит ли обвал союзников (ожившее дерево, пугало). Камни не разбирают сторон, как горящая смола и цепная молния.")]
        public bool hurtAllies = true;
        [Tooltip("Небольшой толчок от центра обвала (единицы импульса).")]
        [Min(0)] public float knockback = .5f;
        [Tooltip("Доля толчка вверх.")]
        [Range(0, 1)] public float lift = .3f;

        [Header("Зона")]
        public RockfallZone zonePrefab;
        [Tooltip("Зона ложится на землю, найденную не дальше этого под стеной или зверем, м.")]
        [Min(0)] public float dropDistance = 6;
        [Tooltip("Поверхности круче этого (стены) — не земля, градусы.")]
        [Range(0, 90)] public float groundSlope = 60;

        public override void Apply(in ImpactContext c)
        {
            if (!zonePrefab)
            {
                Feedback(c, false);
                return;
            }
            if (!CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var ground, out _)) ground = c.Point;
            var zone = c.World.SpawnForOuting(zonePrefab, ground, Quaternion.identity);
            if (zone)
                zone.Setup(new RockfallStrike
                {
                    damage = c.At(damage),
                    playerDamage = c.At(playerDamage),
                    radius = c.At(radius),
                    delay = delay,
                    smallStunSeconds = smallStunSeconds,
                    smallMaxMass = smallMaxMass,
                    hurtAllies = hurtAllies,
                    knockback = knockback,
                    lift = lift
                });
            Feedback(c, zone);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Урон";
            values = damage;
            unit = "";
            return true;
        }

        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = this.radius.At(level);
            return radius > 0;
        }

        /// <summary>Контур рисует сама зона (<see cref="RockfallZone"/>), пока камни ещё не упали.</summary>
        bool ICardArea.LeavesOutlinedZone => true;
    }

    /// <summary>Числа одного обвала, передаются зоне при появлении.</summary>
    public struct RockfallStrike
    {
        /// <summary>Урон зверям, мимикам и союзникам, HP; <see cref="playerDamage"/> — игроку, половинки сердца.</summary>
        public int damage, playerDamage;
        public float radius, delay, smallStunSeconds, smallMaxMass, knockback, lift;
        public bool hurtAllies;
    }

    /// <summary>Правила «Камнепада» без сцены (EditMode-тесты).</summary>
    public static class RockfallRules
    {
        /// <summary>
        /// Достают ли камни бойца, стоящего ногами в <paramref name="feet"/>: по горизонтали не дальше
        /// <paramref name="radius"/> от центра зоны, по высоте — от <paramref name="below"/> ниже до
        /// <paramref name="above"/> выше её (неровная земля, зверь на кочке, низко летящий рой).
        /// </summary>
        public static bool Reaches(Vector3 centre, float radius, float below, float above, Vector3 feet)
        {
            float dx = feet.x - centre.x, dz = feet.z - centre.z;
            if (dx * dx + dz * dz > radius * radius) return false;
            float dy = feet.y - centre.y;
            return dy >= -below && dy <= above;
        }

        /// <summary>Мелкий зверь: масса μ больше нуля и не больше <paramref name="maxMass"/>.</summary>
        public static bool IsSmall(float mass, float maxMass) => mass > 0 && mass <= maxMass + .001f;

        /// <summary>Толчок от центра обвала наружу силой <paramref name="strength"/>, доля <paramref name="lift"/> — вверх.</summary>
        public static Vector3 Push(Vector3 centre, Vector3 feet, float strength, float lift)
        {
            var away = feet - centre;
            away.y = 0;
            var flat = away.sqrMagnitude > .0001f ? away.normalized : Vector3.zero;
            return flat * strength + Vector3.up * strength * lift;
        }

        /// <summary>Сколько камней сыпать на зону радиуса <paramref name="radius"/>: плотность на м², не меньше минимума.</summary>
        public static int StoneCount(float radius, float perSquareMetre, int minimum, int maximum)
            => Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * radius * radius * perSquareMetre), minimum, Mathf.Max(minimum, maximum));
    }
}
