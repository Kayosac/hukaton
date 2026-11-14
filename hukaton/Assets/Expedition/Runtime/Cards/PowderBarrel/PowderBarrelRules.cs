using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>Состояние фитиля пороховой бочки.</summary>
    public enum BarrelFuseState
    {
        /// <summary>Стоит и ждёт огня.</summary>
        Idle,
        /// <summary>Фитиль горит: бочка вспыхнула и шипит, скоро бум.</summary>
        Lit,
        /// <summary>Взорвалась.</summary>
        Blown
    }

    /// <summary>
    /// Фитиль пороховой бочки без сцены (EditMode-тесты). Таймера «сам по себе» нет: бочка стоит в
    /// <see cref="BarrelFuseState.Idle"/>, пока её не подожгут (<see cref="Light"/>); мокрую поджечь нельзя.
    /// Подожжённая взрывается через короткий фитиль (<see cref="Due"/>); вода гасит фитиль (<see cref="Douse"/>).
    /// </summary>
    public sealed class BarrelFuse
    {
        public BarrelFuseState State { get; private set; } = BarrelFuseState.Idle;
        /// <summary>Когда бабахнет (только пока <see cref="BarrelFuseState.Lit"/>).</summary>
        public float BlowAt { get; private set; } = float.PositiveInfinity;
        public bool Lit => State == BarrelFuseState.Lit;
        public bool Blown => State == BarrelFuseState.Blown;

        /// <summary>
        /// Поджигает фитиль в момент <paramref name="now"/>: взрыв через <paramref name="fuseSeconds"/>. Мокрую бочку не
        /// поджечь. Уже горящий фитиль только укорачивается (цепная реакция не ждёт дольше своего срока). True — если
        /// этот вызов поджёг бочку или ускорил взрыв.
        /// </summary>
        public bool Light(float now, float fuseSeconds, bool wet)
        {
            if (Blown || wet) return false;
            float at = now + Mathf.Max(0, fuseSeconds);
            if (Lit)
            {
                if (at >= BlowAt) return false;
                BlowAt = at;
                return true;
            }
            State = BarrelFuseState.Lit;
            BlowAt = at;
            return true;
        }

        /// <summary>Вода гасит горящий фитиль, бочка снова ждёт огня. True — если фитиль горел.</summary>
        public bool Douse()
        {
            if (!Lit) return false;
            State = BarrelFuseState.Idle;
            BlowAt = float.PositiveInfinity;
            return true;
        }

        /// <summary>Пора взрываться: фитиль горит и срок вышел.</summary>
        public bool Due(float now) => Lit && now >= BlowAt;

        /// <summary>Отмечает взрыв. True — в первый раз.</summary>
        public bool Blow()
        {
            if (Blown) return false;
            State = BarrelFuseState.Blown;
            BlowAt = float.PositiveInfinity;
            return true;
        }
    }

    /// <summary>Правила взрыва пороховой бочки без сцены (EditMode-тесты).</summary>
    public static class PowderBarrelRules
    {
        /// <summary>
        /// Достаёт ли взрыв из <paramref name="centre"/> радиуса <paramref name="radius"/> тело: его ноги
        /// <paramref name="feet"/> или середина <paramref name="middle"/> не дальше радиуса (шар, а не цилиндр:
        /// взрыв бьёт и вверх, и вниз).
        /// </summary>
        public static bool Reaches(Vector3 centre, float radius, Vector3 feet, Vector3 middle)
        {
            float r2 = radius * radius;
            return (feet - centre).sqrMagnitude <= r2 || (middle - centre).sqrMagnitude <= r2;
        }

        /// <summary>
        /// Отброс от центра взрыва: полная сила <paramref name="strength"/> вплотную, к краю радиуса —
        /// <paramref name="edgeShare"/> от неё; доля <paramref name="lift"/> — вверх. Тело прямо в центре летит вверх.
        /// </summary>
        public static Vector3 Push(Vector3 centre, Vector3 body, float radius, float strength, float edgeShare, float lift)
        {
            var away = body - centre;
            float distance = away.magnitude;
            away.y = 0;
            var flat = away.sqrMagnitude > .0001f ? away.normalized : Vector3.zero;
            float k = radius > 0 ? Mathf.Clamp01(distance / radius) : 0;
            float force = strength * Mathf.Lerp(1, Mathf.Clamp01(edgeShare), k);
            return (flat * (1 - lift) + Vector3.up * lift).normalized * force;
        }

        /// <summary>Сила тряски камеры: сильная рядом, к <paramref name="farDistance"/> м сходит на нет.</summary>
        public static float Shake(float strength, float distance, float farDistance)
            => strength * Mathf.Clamp01(1 - distance / Mathf.Max(.1f, farDistance));
    }
}
