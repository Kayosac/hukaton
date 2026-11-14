using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Сглаз» (Тьма · АТАКА, ранг 4, 270 монет). Прямое попадание в зверя (мимика, любое <see cref="ICardCreature"/>)
    /// вешает на него чёрную метку (<see cref="EvilEyeMark"/>): тёмный знак над головой пульсирует и разрастается,
    /// через <see cref="delay"/> с лопается и ранит на 6 / 9 / 14 HP (урон «тьмой» — <see cref="DamageKind.Other"/>:
    /// не раскалывает ледяную глыбу, как физический удар, а проходит сквозь неё). Зверь умер или выгрузился раньше —
    /// метка пропадает без эффекта. Повторный Сглаз (и Эхо, повторившее Сглаз) по уже помеченному зверю вешает ещё
    /// одну метку со своим отсчётом — сработают обе. Мимо существ — пшик: тёмный дымок и звук промаха.
    /// </summary>
    public sealed class EvilEyeEffect : CardEffect, ICardStatLine
    {
        [Header("Числа таблицы (I / II / III)")]
        [Tooltip("Урон, когда метка лопается, HP.")]
        public LevelInt damage = new LevelInt(6, 9, 14);
        [Tooltip("Через сколько секунд после попадания метка лопается.")]
        [Min(0)] public float delay = 2;

        [Header("Метка")]
        [Tooltip("Префаб метки (EvilEyeMark): знак над зверем, вспышка и звук, когда лопается.")]
        public EvilEyeMark markPrefab;

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            var target = c.HitCombatant;
            bool marked = false;
            if (markPrefab && EvilEyeRules.CanMark(target, world.Player))
            {
                var mark = world.SpawnForOuting(markPrefab, target.Centre, Quaternion.identity);
                if (mark)
                {
                    mark.Attach(target, c.At(damage), Time.time + delay, Time.time);
                    marked = true;
                }
            }
            Feedback(c, marked);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Урон";
            values = damage;
            unit = "";
            return true;
        }
    }

    /// <summary>Правила «Сглаза» без сцены (EditMode-тесты).</summary>
    public static class EvilEyeRules
    {
        public enum Step { Wait, Pop, Vanish }

        /// <summary>Метку можно повесить на живого зверя или другое существо карт (мимика), но не на игрока и не на союзника.</summary>
        public static bool CanMark(ICombatant target, ICombatant player)
        {
            if (!Combat.IsAlive(target) || ReferenceEquals(target, player) || !target.Root) return false;
            return target is WorldCreature || target is ICardCreature;
        }

        /// <summary>
        /// Что делать метке сейчас: зверя нет или он умер — пропасть без эффекта; срок вышел — лопнуть; иначе ждать.
        /// </summary>
        public static Step Decide(bool targetAlive, float now, float popAt)
        {
            if (!targetAlive) return Step.Vanish;
            return now >= popAt ? Step.Pop : Step.Wait;
        }

        /// <summary>Доля отсчёта 0..1 (0 — только повесили, 1 — пора лопаться).</summary>
        public static float Progress(float now, float startAt, float popAt)
            => popAt <= startAt ? 1 : Mathf.Clamp01((now - startAt) / (popAt - startAt));

        /// <summary>Размер знака: от <paramref name="from"/> к <paramref name="to"/>, к концу быстрее (нарастает к взрыву).</summary>
        public static float Swell(float progress, float from, float to) => Mathf.Lerp(from, to, progress * progress);

        /// <summary>Частота пульса знака, Гц: учащается к взрыву.</summary>
        public static float PulseRate(float progress, float from, float to) => Mathf.Lerp(from, to, progress * progress);

        /// <summary>
        /// Место для новой метки: первое свободное (0 — у самой головы, 1 — выше на шаг…), а не число меток: лопнула
        /// нижняя — новая встаёт на её место, а не поверх ещё висящей.
        /// </summary>
        public static int FirstFreeSlot(System.Collections.Generic.IEnumerable<int> taken)
        {
            var used = new System.Collections.Generic.HashSet<int>(taken);
            int slot = 0;
            while (used.Contains(slot)) slot++;
            return slot;
        }

        /// <summary>Знак над головой зверя: верх тела (по ногам и середине) плюс зазор, следующая метка — выше на шаг.</summary>
        public static Vector3 SignPosition(Vector3 feet, Vector3 centre, float gap, int slot, float slotStep)
        {
            var top = feet + (centre - feet) * 2;
            if (top.y < feet.y) top = feet;
            return top + Vector3.up * (gap + slot * slotStep);
        }
    }
}
