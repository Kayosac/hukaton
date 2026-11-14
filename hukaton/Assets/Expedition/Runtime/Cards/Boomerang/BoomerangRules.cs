using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Где летит «Бумеранг» (состояние одного броска). Позиция и скорость считаются только правилами
    /// <see cref="BoomerangRules"/> — и настоящим полётом, и превью у прицела, — поэтому превью не врёт.
    /// </summary>
    public struct BoomerangPath
    {
        /// <summary>Откуда брошен и куда (горизонтальное направление прицела с ограниченным наклоном).</summary>
        public Vector3 Origin, Forward;
        public Vector3 Position, Velocity;
        /// <summary>Дальность по уровню (м), скорость (м/с), сколько уже летит и сколько может лететь (с).</summary>
        public float Range, Speed, Elapsed, Lifetime;
        /// <summary>Где по направлению броска начинается разворот, м: так дальняя точка дуги — на дальности.</summary>
        public float TurnAt;
        /// <summary>Загиб вбок на пути туда, градусов в секунду (плюс — влево, против часовой сверху).</summary>
        public float CurlRate;
        /// <summary>Скорость разворота, градусов в секунду (на обратном пути растёт, чтобы карта не кружила).</summary>
        public float TurnRate;
        /// <summary>Сколько секунд летит обратно; false — ещё на пути туда.</summary>
        public float ReturningFor;
        public bool Returning;
        /// <summary>Время, ещё не просчитанное шагом (меньше <see cref="BoomerangRules.Step"/>).</summary>
        public float Pending;
    }

    /// <summary>
    /// Правила полёта «Бумеранга» без сцены (EditMode-тесты). Карта летит без гравитации: вперёд по прицелу с лёгким
    /// загибом влево, на дальности по уровню разворачивается (дуга в ту же сторону) и самонаводится на игрока; ловится
    /// в пределах радиуса от оси его тела. Время полёта ограничено: путь туда-обратно и запас.
    /// </summary>
    public static class BoomerangRules
    {
        /// <summary>Шаг интегрирования: одинаковый путь при любом шаге кадра; шаг физики (0,02 с) — ровно два шага.</summary>
        public const float Step = 1f / 100f;

        /// <param name="velocity">Скорость броска (после Кривых рук); её модуль, зажатый в
        /// [<paramref name="minimumSpeed"/>, <paramref name="maximumSpeed"/>], — скорость бумеранга.</param>
        /// <param name="curlDegrees">Насколько уходит влево за путь туда, градусов.</param>
        /// <param name="maximumPitch">Наибольший наклон полёта вверх или вниз, градусов.</param>
        /// <param name="spareSeconds">Запас времени сверх пути туда-обратно.</param>
        public static BoomerangPath Begin(Vector3 origin, Vector3 velocity, float range, float minimumSpeed, float maximumSpeed,
            float curlDegrees, float turnDegreesPerSecond, float maximumPitch, float spareSeconds)
        {
            float speed = Mathf.Clamp(velocity.magnitude, Mathf.Max(.1f, minimumSpeed), Mathf.Max(minimumSpeed, maximumSpeed));
            Vector3 direction = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector3.forward;
            Vector3 flat = new Vector3(direction.x, 0, direction.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            flat.Normalize();
            float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(direction.y, -1, 1)) * Mathf.Rad2Deg, -maximumPitch, maximumPitch);
            direction = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat;

            float turnRate = Mathf.Max(30, turnDegreesPerSecond);
            float turnRadius = speed / (turnRate * Mathf.Deg2Rad);
            // Курс к развороту уже загнут влево на curlDegrees: полукруг разворота выносит карту вперёд на
            // r·(1 − sin загиба), поэтому разворот начинается раньше на столько же — дальняя точка дуги на дальности.
            float turnAt = Mathf.Max(range * .5f, range - turnRadius * (1 - Mathf.Sin(Mathf.Clamp(curlDegrees, 0, 89) * Mathf.Deg2Rad)));
            float outSeconds = turnAt / speed;
            return new BoomerangPath
            {
                Origin = origin,
                Forward = flat,
                Position = origin,
                Velocity = direction * speed,
                Range = range,
                Speed = speed,
                TurnAt = turnAt,
                TurnRate = turnRate,
                CurlRate = outSeconds > 1e-4f ? curlDegrees / outSeconds : 0,
                // Туда, полукруг разворота, обратно и запас.
                Lifetime = (turnAt + Mathf.PI * turnRadius + range) / speed + Mathf.Max(0, spareSeconds),
            };
        }

        /// <summary>Продвигает полёт на <paramref name="seconds"/>; на обратном пути карта летит к <paramref name="target"/>.</summary>
        /// <remarks>Шаги всегда ровно <see cref="Step"/>, остаток копится до следующего вызова: путь не зависит от
        /// того, какими кусками его считают (кадры физики, превью).</remarks>
        public static void Advance(ref BoomerangPath path, Vector3 target, float seconds)
        {
            path.Pending += Mathf.Max(0, seconds);
            // Запас на ошибку округления, чтобы ровные куски (0,02 с) не теряли шаг.
            while (path.Pending >= Step - 1e-6f)
            {
                StepOnce(ref path, target, Step);
                path.Pending -= Step;
            }
            if (path.Pending < 0) path.Pending = 0;
        }

        static void StepOnce(ref BoomerangPath path, Vector3 target, float dt)
        {
            if (!path.Returning)
            {
                // Туда: курс понемногу загибается влево.
                path.Velocity = Quaternion.AngleAxis(-path.CurlRate * dt, Vector3.up) * path.Velocity;
                float along = Vector3.Dot(path.Position - path.Origin, path.Forward);
                // Отвесный бросок или стена ветра: разворот не позже, чем через полтора расчётных времени пути туда.
                if (along >= path.TurnAt || path.Elapsed >= path.TurnAt / path.Speed * 1.5f) path.Returning = true;
            }
            else
            {
                path.ReturningFor += dt;
                // Разворот тем круче, чем дольше летит обратно: бумеранг не кружит вокруг игрока.
                float rate = path.TurnRate * (1 + path.ReturningFor);
                Vector3 to = target - path.Position;
                Vector3 flatVelocity = new Vector3(path.Velocity.x, 0, path.Velocity.z), flatTo = new Vector3(to.x, 0, to.z);
                float yaw = 0;
                if (flatVelocity.sqrMagnitude > 1e-6f && flatTo.sqrMagnitude > 1e-6f)
                {
                    float angle = Vector3.SignedAngle(flatVelocity, flatTo, Vector3.up);
                    float limit = rate * dt;
                    // Цель почти позади — разворот в сторону загиба (влево), а не куда придётся.
                    yaw = Mathf.Abs(angle) > 150 ? -limit : Mathf.Clamp(angle, -limit, limit);
                }
                Vector3 velocity = Quaternion.AngleAxis(yaw, Vector3.up) * path.Velocity;
                // По высоте — к цели, плавно.
                float wantY = to.sqrMagnitude > 1e-6f ? to.normalized.y * path.Speed : 0;
                velocity.y = Mathf.MoveTowards(velocity.y, wantY, path.Speed * 2 * dt);
                path.Velocity = velocity.sqrMagnitude > 1e-6f ? velocity.normalized * path.Speed : path.Velocity;
            }
            path.Position += path.Velocity * dt;
            path.Elapsed += dt;
        }

        /// <summary>Время вышло: бумеранг не долетел (его не поймали), он падает.</summary>
        public static bool TimedOut(in BoomerangPath path) => path.Elapsed >= path.Lifetime;

        /// <summary>
        /// Поймали: на обратном пути карта в пределах <paramref name="radius"/> от оси тела игрока (от
        /// <paramref name="bottom"/> до <paramref name="top"/>).
        /// </summary>
        public static bool Caught(in BoomerangPath path, Vector3 bottom, Vector3 top, float radius)
            => path.Returning && DistanceToSegment(path.Position, bottom, top) <= radius;

        public static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0;
            return Vector3.Distance(point, a + ab * t);
        }

        /// <summary>Толчок по направлению полёта: горизонтальная часть силой <paramref name="strength"/> и доля вверх.</summary>
        public static Vector3 Push(Vector3 velocity, float strength, float lift)
        {
            velocity.y = 0;
            var flat = velocity.sqrMagnitude > .001f ? velocity.normalized : Vector3.forward;
            return flat * strength + Vector3.up * strength * lift;
        }
    }

    /// <summary>
    /// Кого бумеранг уже ранил: каждое существо — не чаще раза на пути туда и раза на пути обратно.
    /// </summary>
    public sealed class BoomerangHits
    {
        readonly System.Collections.Generic.HashSet<object> there = new System.Collections.Generic.HashSet<object>(),
            back = new System.Collections.Generic.HashSet<object>();

        /// <summary>True, если этого ещё не ранили на этом отрезке (и теперь он отмечен).</summary>
        public bool TryHit(object target, bool returning) => target != null && (returning ? back : there).Add(target);
    }
}
