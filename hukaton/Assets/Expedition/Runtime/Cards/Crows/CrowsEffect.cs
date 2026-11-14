using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Вороньё» (Звери · АТАКА, ранг 3, 200 монет). Прямое попадание в зверя или мимика (любое
    /// <see cref="ICardCreature"/>) — три вороны слетаются на него (<see cref="CrowFlock"/>), кружат над головой и
    /// клюют: 1 / 2 / 3 HP в секунду, <see cref="duration"/> с (всего 4 / 8 / 12). Пока клюют, существо отвлечено
    /// (<see cref="IDistractable"/>) и ни на кого не нападает. Зверь умер — вороны разлетаются; срок вышел — улетают,
    /// зверь возвращается к обычному поведению. Повторное попадание (и Эхо) по тому же зверю продлевает стаю на полный
    /// срок, урон в секунду — больший из двух. Союзников и игрока не трогает. Промах — вороны покружат над местом
    /// и улетят (пшик).
    /// </summary>
    public sealed class CrowsEffect : CardEffect, ICardStatLine
    {
        [Header("Числа таблицы (I / II / III)")]
        [Tooltip("Урон клевков в секунду, HP.")]
        public LevelInt damagePerSecond = new LevelInt(1, 2, 3);
        [Tooltip("Сколько секунд вороны клюют (и зверь отвлечён).")]
        [Min(0)] public float duration = 4;

        [Header("Стая")]
        [Tooltip("Префаб стаи (CrowFlock): три вороны, клевки, перья, карканье.")]
        public CrowFlock flockPrefab;

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            var target = c.HitCombatant;
            bool hit = false;
            if (flockPrefab && CrowsRules.CanPeck(target, world.Player))
            {
                var flock = CrowFlock.On(target);
                if (flock) flock.Renew(c.At(damagePerSecond), duration, Time.time);
                else
                {
                    flock = world.SpawnForOuting(flockPrefab, target.Centre, Quaternion.identity);
                    if (flock) flock.Attach(target, c.At(damagePerSecond), duration, Time.time);
                }
                hit = flock;
            }
            else if (flockPrefab)
            {
                // Пшик: вороны покружат над местом попадания и улетят.
                var flock = world.SpawnForOuting(flockPrefab, c.Point, Quaternion.identity);
                if (flock) flock.Circle(c.Point, Time.time);
            }
            Feedback(c, hit);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Урон/с";
            values = damagePerSecond;
            unit = "";
            return true;
        }
    }

    /// <summary>Правила «Вороньё» без сцены (EditMode-тесты).</summary>
    public static class CrowsRules
    {
        public enum Step { Peck, Wait, Leave, Done }

        /// <summary>Клевать можно живого зверя или существо карт (мимика); не игрока, не союзника.</summary>
        public static bool CanPeck(ICombatant target, ICombatant player)
        {
            if (!Combat.IsAlive(target) || ReferenceEquals(target, player) || !target.Root) return false;
            if (target.Faction == Faction.Player) return false;
            return target is WorldCreature || target is ICardCreature;
        }

        /// <summary>Клевок в срок <paramref name="peckAt"/> положен: настал и не позже конца стаи (с допуском).</summary>
        public static bool PeckDue(float now, float peckAt, float endAt) => now >= peckAt - 1e-4f && peckAt <= endAt + 1e-3f;

        /// <summary>
        /// Урон одного ранящего клевка: урон в секунду × интервал клевков. Дробная часть копится в
        /// <paramref name="owed"/> и выдаётся следующими клевками, так что за срок стая наносит ровно
        /// урон в секунду × срок (интервал 0,5 с при 3 HP/с — клевки 1, 2, 1, 2…).
        /// </summary>
        public static int PeckDamage(ref float owed, int perSecond, float interval)
        {
            owed += Mathf.Max(0, perSecond) * Mathf.Max(0, interval);
            int amount = Mathf.FloorToInt(owed + 1e-4f);
            owed = Mathf.Max(0, owed - amount);
            return amount;
        }

        /// <summary>Урон в секунду после повторного попадания: больший из двух.</summary>
        public static int RenewedDamage(int current, int fresh) => Mathf.Max(current, fresh);

        /// <summary>Точка над головой: макушка (по ногам и середине тела) плюс зазор.</summary>
        public static Vector3 HeadTop(Vector3 feet, Vector3 centre, float gap)
        {
            var top = feet + (centre - feet) * 2;
            if (top.y < feet.y) top = feet;
            return top + Vector3.up * gap;
        }

        /// <summary>Радиус круга ворон: по росту существа, в пределах.</summary>
        public static float OrbitRadius(float height, float perMetre, float min, float max) => Mathf.Clamp(height * perMetre, min, max);

        /// <summary>Кадр взмаха: туда-обратно по кадрам (0,1,2,1,0…).</summary>
        public static int FlapFrame(float cycles, int frames)
        {
            if (frames <= 1) return 0;
            int period = (frames - 1) * 2;
            int k = Mathf.FloorToInt(cycles * period) % period;
            if (k < 0) k += period;
            return k < frames ? k : period - k;
        }
    }
}
