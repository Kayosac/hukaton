using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Отвлечение (карта «Вороньё», <see cref="IDistractable"/>): пока отвлечён, зверь не нападает ни на кого —
    /// атака, выстрел, захват прерываются; он стоит и отмахивается (дёргается и вертится на месте). Пугливый зверь
    /// бегает как обычно (он и так не нападает), подманенный Манком идёт к шуму. Заморозка и оглушение важнее
    /// (проверяются раньше). Когда отвлечение снято, зверь возвращается к обычному поведению с короткой паузы.
    /// </summary>
    public sealed partial class WorldCreature : IDistractable
    {
        [Header("Distraction")]
        [Tooltip("Отвлечённый зверь дёргается в новую сторону раз в столько секунд (мин., макс.).")]
        public Vector2 flailEvery = new Vector2(.18f, .42f);
        [Tooltip("Пауза перед обычным поведением, когда отвлечение кончилось, с.")]
        [Min(0)] public float distractionRecovery = .3f;

        float distractedUntil, nextFlail;

        /// <summary>Отвлечён (Вороньё): не нападает.</summary>
        public bool Distracted => Alive && Time.time < distractedUntil;

        public bool Distract(float seconds)
        {
            if (!Alive || seconds <= 0) return false;
            distractedUntil = Mathf.Max(distractedUntil, Time.time + seconds);
            return true;
        }

        public void EndDistraction() => distractedUntil = 0;

        /// <summary>
        /// Шаг отвлечённого зверя. False — не отвлечён, думает как обычно. Нападающие состояния (погоня, замах, удар,
        /// прицел, выстрел, угроза) сменяются «передышкой» (Recover): она прерывает атаку и ведёт обратно к охоте,
        /// когда отвлечение кончится.
        /// </summary>
        bool ThinkDistracted()
        {
            if (Time.time >= distractedUntil) return false;
            // Захват отпускает: держать игрока — тоже нападать.
            holding = false;
            if (temperament == Temperament.Timid) { ThinkTimid(); return true; }
            if (lured) { ThinkIdle(false); return true; }
            if (State != CreatureState.Recover) Enter(CreatureState.Recover);
            stateTime = 0;
            idleDuration = distractionRecovery;
            Halt();
            if (Time.time >= nextFlail)
            {
                nextFlail = Time.time + Random.Range(flailEvery.x, Mathf.Max(flailEvery.x, flailEvery.y));
                Face(Quaternion.Euler(0, Random.Range(-180f, 180f), 0) * Vector3.forward);
                squash.Kick(Random.Range(-4.5f, -2f));
            }
            return true;
        }
    }
}
