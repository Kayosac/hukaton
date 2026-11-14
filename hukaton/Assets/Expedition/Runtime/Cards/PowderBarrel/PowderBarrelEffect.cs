using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Пороховая бочка» (Огонь · ЛОВУШКА, ранг 3, 220 монет). Карта ставит на землю в точке попадания пороховую бочку
    /// (<see cref="PowderBarrel"/>): попала в зверя или в стену — бочка встаёт на землю под точкой; место занято —
    /// ближайшее свободное рядом (<see cref="PowderBarrelPlacement"/>), совсем негде — пшик. Бочка стоит до конца
    /// вылазки и взрывается только от огня (общая система поджига карт): урон 5 / 8 / 12 зверям, мимикам и союзникам в
    /// радиусе 3 / 4 / 5 м, поджигает их и отбрасывает от центра (отброс 10 / 13 / 16). Игроку — своё число:
    /// 2 / 3 / 4 половинки сердца и отброс, без горения; в Пузыре игрок неуязвим к взрыву.
    /// Кольцо при прицеливании показывает радиус будущего взрыва.
    /// </summary>
    public sealed class PowderBarrelEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Числа таблицы (I / II / III)")]
        [Tooltip("Урон взрыва каждому зверю, мимику и союзнику в радиусе, HP.")]
        public LevelInt damage = new LevelInt(5, 8, 12);
        [Tooltip("Урон взрыва игроку, половинки сердца (своё число, меньше звериного; Хрупкость удваивает, как любой удар).")]
        public LevelInt playerDamage = new LevelInt(2, 3, 4);
        [Tooltip("Радиус взрыва, м.")]
        public LevelFloat radius = new LevelFloat(3, 4, 5);
        [Tooltip("Отброс вплотную к бочке (единицы импульса, Δv для массы 1); к краю радиуса слабее.")]
        public LevelFloat knockback = new LevelFloat(10, 13, 16);

        [Header("Бочка")]
        public PowderBarrel barrelPrefab;
        [Tooltip("Бочка встаёт на землю, найденную не дальше этого под стеной или зверем, м.")]
        [Min(0)] public float dropDistance = 6;
        [Tooltip("Поверхности круче этого (стены) — не земля, градусы.")]
        [Range(0, 90)] public float groundSlope = 40;
        [Tooltip("Размер бочки для проверки свободного места: радиус и высота, м.")]
        [Min(.05f)] public float footprintRadius = .34f;
        [Min(.1f)] public float footprintHeight = .98f;
        [Tooltip("Занято — свободное место ищется по кольцам вокруг с таким шагом, до maxShift м.")]
        [Min(.1f)] public float searchStep = .45f;
        [Min(0)] public float maxShift = 1.8f;
        [Tooltip("Случайный поворот бочки вокруг вертикали, градусы.")]
        [Range(0, 180)] public float yawJitter = 180;
        [Tooltip("Пыль, где бочка встала (Retro Arsenal).")]
        public VfxBurst appearVfx;
        public Color appearTint = new Color(.55f, .45f, .35f);
        [Tooltip("Показывается, если бочке негде встать.")]
        public string noRoomNotice = "Бочке негде встать";

        public override void Apply(in ImpactContext c)
        {
            if (!barrelPrefab)
            {
                Debug.LogWarning("PowderBarrelEffect: не назначен префаб бочки.", this);
                Feedback(c, false);
                return;
            }
            int mask = CardFlight.CollisionMask | (1 << 2);
            float r = footprintRadius, h = footprintHeight;
            bool Free(Vector3 ground)
            {
                Vector3 a = ground + Vector3.up * (r + .06f), b = ground + Vector3.up * Mathf.Max(r + .06f, h - r);
                return !Physics.CheckCapsule(a, b, r, mask, QueryTriggerInteraction.Ignore);
            }
            bool Ground(Vector3 from, out Vector3 ground)
                => CardGround.TryFind(null, from + Vector3.up * 1.2f, Vector3.up, groundSlope, dropDistance + 1.2f, out ground, out _);

            bool found = CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var spot, out _);
            if (found && !Free(spot))
            {
                Vector3 toward = c.ThrowOrigin - spot;
                found = PowderBarrelPlacement.TryFind(spot, toward, searchStep, maxShift,
                    p => Ground(p, out var g) && Mathf.Abs(g.y - spot.y) < 1.2f && Free(g) ? g : (Vector3?)null, out spot);
            }
            if (!found)
            {
                Feedback(c, false, .6f);
                if (!string.IsNullOrEmpty(noRoomNotice)) c.World.Announce(noRoomNotice, 1.4f);
                return;
            }
            var rotation = Quaternion.Euler(0, Random.Range(-yawJitter, yawJitter), 0);
            var barrel = c.World.SpawnForOuting(barrelPrefab, spot + Vector3.up * .02f, rotation);
            if (barrel)
            {
                barrel.Setup(new PowderBarrelBlast { damage = c.At(damage), playerDamage = c.At(playerDamage), radius = c.At(radius), knockback = c.At(knockback) });
                c.World.Spawn(appearVfx, spot + Vector3.up * .05f, Quaternion.identity, appearTint, .8f);
            }
            Feedback(c, barrel);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Урон";
            values = damage;
            unit = "";
            return true;
        }

        /// <summary>Кольцо — радиус будущего взрыва бочки.</summary>
        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = this.radius.At(level);
            return radius > 0;
        }

        /// <summary>Бочка не рисует контур, пока стоит: только пока горит фитиль. Посадку показывает вспышка кольца.</summary>
        bool ICardArea.LeavesOutlinedZone => false;
    }

    /// <summary>Где встать бочке, если точка попадания занята (правила без сцены, EditMode-тесты).</summary>
    public static class PowderBarrelPlacement
    {
        /// <summary>
        /// Кандидаты по кольцам вокруг <paramref name="centre"/>: радиусы step, 2·step… до <paramref name="maxShift"/>,
        /// на каждом — 8 направлений, первым — к бросавшему (<paramref name="toward"/>), дальше поочерёдно по обе стороны.
        /// </summary>
        public static Vector3 Candidate(Vector3 centre, Vector3 toward, float step, int ring, int index)
        {
            toward.y = 0;
            float baseAngle = toward.sqrMagnitude > 1e-4f ? Mathf.Atan2(toward.z, toward.x) : 0;
            int k = (index + 1) / 2;
            float sign = index % 2 == 1 ? 1 : -1;
            float angle = baseAngle + sign * k * Mathf.PI / 4;
            float d = step * (ring + 1);
            return centre + new Vector3(Mathf.Cos(angle) * d, 0, Mathf.Sin(angle) * d);
        }

        /// <summary>
        /// Первый кандидат, для которого <paramref name="settle"/> вернул точку на земле со свободным местом.
        /// False — места нет (пшик).
        /// </summary>
        public static bool TryFind(Vector3 centre, Vector3 toward, float step, float maxShift,
            System.Func<Vector3, Vector3?> settle, out Vector3 spot)
        {
            int rings = Mathf.Max(0, Mathf.FloorToInt(maxShift / Mathf.Max(.05f, step) + 1e-4f));
            for (int ring = 0; ring < rings; ring++)
                for (int i = 0; i < 8; i++)
                {
                    var found = settle(Candidate(centre, toward, step, ring, i));
                    if (found.HasValue) { spot = found.Value; return true; }
                }
            spot = centre;
            return false;
        }
    }
}
