using System.Collections;
using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Веер» (Бой · УРОН, ранг 1, 150 монет). Один бросок выпускает сразу 3 / 5 / 7 карт (<see cref="ICardVolley"/>):
    /// веером по горизонтали в пределах 40° вокруг прицела, равномерно, средняя — ровно по прицелу (<see cref="FanRules"/>).
    /// Тратится одна копия, как у обычного броска; летящие карты — её копии с тем же артом, каждая — обычный снаряд с
    /// обычной баллистикой и столкновением. Попала в существо — ранит его, как Удар (звери, мимики; союзников и игрока —
    /// нет), на 1 / 1 / 2 HP с небольшим толчком; в землю или стену — втыкается и сгорает, как обычная карта. Друг друга
    /// карты не сбивают (снаряды карт без коллайдеров, слой карт вне маски). Зверь в упор получает всю пачку. Кривые руки
    /// отклоняют весь веер: веер раскладывается от уже отклонённого броска.
    /// В первый миг карты раскрываются из руки веером (<see cref="handSpreadDegrees"/>), камеру слегка ведёт вбок, к
    /// обычному взмаху броска добавляются наложенные взмахи выше тоном.
    /// Эхо, повторяя Веер, бьёт в своей точке сразу всеми картами веера (её уровня) — как Веер в упор: 3 / 5 / 14 HP.
    /// Несколько карт одного веера, попавших в одно существо почти разом (в упор), дают одну вспышку чуть крупнее
    /// (<see cref="mergeSeconds"/>), один звук и одну тряску, а не 7 вспышек на весь экран.
    /// </summary>
    public sealed class FanEffect : CardEffect, ICardStatLine, ICardVolley
    {
        [Header("Числа таблицы (I / II / III)")]
        [Tooltip("Карт в одном броске.")]
        public LevelInt cards = new LevelInt(3, 5, 7);
        [Tooltip("Урон каждой карты, HP.")]
        public LevelInt damage = new LevelInt(1, 1, 2);
        [Tooltip("Угол веера по горизонтали, градусов: крайние карты — на половину угла по сторонам от прицела.")]
        [Range(0, 120)] public float spreadDegrees = 40;

        [Header("Попадание")]
        [Tooltip("Небольшой толчок по броску от каждой карты (единицы импульса).")]
        [Min(0)] public float nudge = .8f;
        [Tooltip("Искры удара, повторённого Эхом (все карты разом), крупнее во столько раз.")]
        [Min(0)] public float echoVfxScale = 1.6f;
        [Tooltip("Карты одного веера, попавшие в одно существо (или в одну стену) не дальше этого друг от друга по времени, дают одну общую вспышку, с.")]
        [Min(0)] public float mergeSeconds = .1f;
        [Tooltip("Общая вспышка крупнее на эту долю за каждую карту сверх первой…")]
        [Min(0)] public float mergeGrowth = .1f;
        [Tooltip("…но не больше, чем во столько раз.")]
        [Min(1)] public float mergeMaxScale = 1.5f;

        [Header("Мах из руки")]
        [Tooltip("Наклон крайней карты в руке, когда веер раскрывается, градусов.")]
        [Range(0, 60)] public float handSpreadDegrees = 26;
        [Tooltip("Рывок камеры при махе веером (градусов в секунду: вверх-вниз, вбок, крен).")]
        public Vector3 swingKick = new Vector3(-6, -24, 10);

        [Header("Звук броска")]
        [Tooltip("Наложенный взмах поверх обычного броска (card_throw).")]
        public SfxCue layerSound;
        [Tooltip("Сколько взмахов наложить самое большее: по одному на каждые две карты сверх средней (3 → 1, 5 → 2, 7 → 3).")]
        [Range(0, 6)] public int maxLayers = 3;
        [Tooltip("Взмахи идут друг за другом через столько секунд (больше паузы повтора звука).")]
        [Min(0)] public float layerDelay = .045f;
        [Tooltip("Каждый следующий взмах выше тоном на столько.")]
        [Range(0, .5f)] public float layerPitchStep = .09f;
        [Range(0, 1)] public float layerVolume = .65f;

        /// <summary>
        /// Попадание одной карты веера — или повтор Эхом: тогда сразу все карты веера уровня копии Эха в эту точку.
        /// </summary>
        public override void Apply(in ImpactContext c)
        {
            bool echo = FanRules.IsEcho(c.Copy?.Spec?.Id, c.Definition ? c.Definition.id : null);
            int hits = HitsPerImpact(echo, c.Level);
            Vector3 push = StrikeEffect.Flat(c.Direction) * nudge;
            bool meaningful = FanRules.Wound(c.Creature, c.TargetAs<ICardCreature>(), c.At(damage), hits, push, c.Point, c.ThrowOrigin);
            if (echo)
            {
                Feedback(c, meaningful, (meaningful ? 1 : .6f) * echoVfxScale);
                Riffle(c.World, hits, c.Point);
            }
            else Flash(c, meaningful);
        }

        // ---------- одна вспышка на пачку ----------

        sealed class MergedFlash
        {
            public float time;
            public int cards;
            public VfxBurst burst;
        }

        static readonly Dictionary<(CardCopy, Object), MergedFlash> Flashes = new Dictionary<(CardCopy, Object), MergedFlash>();
        static readonly List<(CardCopy, Object)> Stale = new List<(CardCopy, Object)>();

        /// <summary>
        /// Вспышка, звук и тряска попадания одной карты. Следующая карта того же веера в то же существо (или ту же
        /// стену) раньше <see cref="mergeSeconds"/> не добавляет своей: прежняя вспышка заменяется одной чуть
        /// крупнее (<see cref="FanRules.MergedScale"/>), без второго звука и тряски.
        /// </summary>
        void Flash(in ImpactContext c, bool meaningful)
        {
            float scale = meaningful ? 1 : .6f;
            Object target = c.Creature ? c.Creature : c.TargetAs<ICardCreature>() as Object;
            if (!target) target = c.Target;
            if (c.Copy == null || !target || mergeSeconds <= 0)
            {
                Feedback(c, meaningful, scale);
                return;
            }
            float now = Time.time;
            Forget(now);
            var key = (c.Copy, target);
            if (Flashes.TryGetValue(key, out var flash) && FanRules.Merges(flash.time, now, mergeSeconds))
            {
                flash.cards++;
                flash.time = now;
                if (flash.burst) Destroy(flash.burst.gameObject);
                flash.burst = c.World.Spawn(impactVfx, c.Point + c.Normal * .05f, Quaternion.LookRotation(c.Normal), c.Accent,
                    vfxScale * scale * FanRules.MergedScale(flash.cards, mergeGrowth, mergeMaxScale));
                return;
            }
            Flashes[key] = new MergedFlash { time = now, cards = 1, burst = Feedback(c, meaningful, scale) };
        }

        /// <summary>Сколько карт уже слилось в последнюю вспышку на этом существе от этой копии (0 — ни одной; тесты).</summary>
        public static int MergedCards(CardCopy copy, Object target)
            => copy != null && target && Flashes.TryGetValue((copy, target), out var flash) ? flash.cards : 0;

        static void Forget(float now)
        {
            Stale.Clear();
            foreach (var pair in Flashes)
                if (now - pair.Value.time > 1 || !pair.Key.Item2) Stale.Add(pair.Key);
            foreach (var key in Stale) Flashes.Remove(key);
        }

        /// <summary>Сколько карт бьёт в точку попадания: одна — или, при повторе Эхом, весь веер уровня <paramref name="level"/>.</summary>
        public int HitsPerImpact(bool echo, int level) => echo ? Mathf.Max(1, cards.At(level)) : 1;

        // ---------- веер ----------

        public int Volley(in CardFlightLaunch launch, Vector3[] velocities, float[] handRolls, out int centre)
        {
            int count = FanRules.Spread(launch.Velocity, cards.At(launch.Level), spreadDegrees, velocities);
            centre = FanRules.Centre(count);
            if (handRolls != null)
            {
                float half = spreadDegrees * .5f;
                for (int i = 0; i < count && i < handRolls.Length; i++)
                    // Карта правее прицела клонится вправо (по часовой для глаза), левее — влево.
                    handRolls[i] = half > 0 ? -FanRules.Angle(i, count, spreadDegrees) / half * handSpreadDegrees : 0;
            }
            return count;
        }

        public void Launched(ExpeditionWorld world, IReadOnlyList<CardProjectile> thrown)
        {
            if (!world) return;
            if (world.feel) world.feel.Kick(swingKick);
            Riffle(world, thrown != null ? thrown.Count : 1, null);
        }

        /// <summary>Наложенные взмахи веера: друг за другом, каждый выше тоном.</summary>
        void Riffle(ExpeditionWorld world, int count, Vector3? at)
        {
            int layers = Mathf.Min(maxLayers, count / 2);
            if (layers <= 0 || !layerSound || !world || !world.isActiveAndEnabled) return;
            world.StartCoroutine(RiffleRoutine(world, layers, at));
        }

        IEnumerator RiffleRoutine(ExpeditionWorld world, int layers, Vector3? at)
        {
            for (int i = 1; i <= layers; i++)
            {
                // Без масштаба времени: стоп-кадр попадания в упор не растягивает взмахи.
                yield return new WaitForSecondsRealtime(layerDelay);
                if (!world) yield break;
                world.Play(layerSound, at, layerVolume, 1 + layerPitchStep * i);
            }
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            // Одна строка ПК: число карт растёт на каждом уровне и решает, скольких зверей заденет веер
            // (урон каждой, 1 / 1 / 2, меняется только на III и сказан в описании словами «каждая ранит»).
            label = "Карт в веере";
            values = cards;
            unit = "";
            return true;
        }
    }
}
