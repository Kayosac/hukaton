using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Правила «Веера» без сцены (EditMode-тесты): раскладка карт по углу, раны каждой карты, повтор Эхом.
    /// </summary>
    public static class FanRules
    {
        /// <summary>
        /// Угол по горизонтали карты <paramref name="index"/> из <paramref name="count"/>, градусов от прицела (плюс —
        /// вправо): равномерно в пределах <paramref name="spread"/>, крайние — на половину угла по сторонам; при нечётном
        /// числе средняя — ровно по прицелу.
        /// </summary>
        public static float Angle(int index, int count, float spread)
            => count <= 1 ? 0 : -spread * .5f + spread * index / (count - 1f);

        /// <summary>
        /// Скорости карт одного броска: скорость броска, повёрнутая вокруг вертикали на <see cref="Angle"/> каждой карты
        /// (наклон вверх-вниз и скорость у всех те же). Возвращает, сколько записано (1 … длина буфера).
        /// </summary>
        public static int Spread(Vector3 velocity, int count, float spread, Vector3[] into)
        {
            count = Mathf.Clamp(count, 1, into.Length);
            for (int i = 0; i < count; i++) into[i] = Quaternion.AngleAxis(Angle(i, count, spread), Vector3.up) * velocity;
            return count;
        }

        /// <summary>Индекс карты, летящей ровно по прицелу.</summary>
        public static int Centre(int count) => Mathf.Max(0, count - 1) / 2;

        /// <summary>
        /// Попадание в <paramref name="now"/> сливается с вспышкой предыдущего (в <paramref name="lastHit"/>) той же копии
        /// по тому же существу: не позже <paramref name="window"/> секунд после него (в упор вся пачка — почти разом).
        /// </summary>
        public static bool Merges(float lastHit, float now, float window) => now >= lastHit - 1e-4f && now - lastHit <= window + 1e-4f;

        /// <summary>Размер общей вспышки <paramref name="cards"/> карт: на <paramref name="growth"/> крупнее за каждую сверх первой, не больше <paramref name="max"/>.</summary>
        public static float MergedScale(int cards, float growth, float max)
            => Mathf.Min(Mathf.Max(1, max), 1 + Mathf.Max(0, growth) * Mathf.Max(0, cards - 1));

        /// <summary>
        /// Удар повторило Эхо: оно передаёт эффекту свою копию (id «echo») с определением повторяемой карты.
        /// </summary>
        public static bool IsEcho(string copyId, string definitionId)
            => !string.IsNullOrEmpty(copyId) && !string.IsNullOrEmpty(definitionId) && copyId != definitionId;

        /// <summary>
        /// Раны как от Удара: живой зверь мира (<see cref="WorldCreature.Damage(int, Vector3, Vector3, float)"/>),
        /// иначе существо карт (мимик, <see cref="ICardCreature.TakeDamage"/>); союзников и игрока — нет.
        /// <paramref name="hits"/> ударов по <paramref name="amount"/> HP подряд (мёртвому — больше ни одного).
        /// True — хоть один удар пришёлся по живому.
        /// </summary>
        public static bool Wound(WorldCreature creature, ICardCreature other, int amount, int hits, Vector3 push, Vector3 point, Vector3 source)
        {
            bool landed = false;
            if (creature && creature.Alive)
            {
                for (int i = 0; i < hits && creature.Alive; i++) creature.Damage(amount, push, point, 0);
                return true;
            }
            if (other == null || !other.Alive) return false;
            for (int i = 0; i < hits && other.Alive; i++)
                landed |= other.TakeDamage(new DamageInfo(amount, push, point, source));
            return landed;
        }
    }
}
