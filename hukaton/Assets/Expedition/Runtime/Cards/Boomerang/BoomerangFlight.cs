using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Полёт одного брошенного «Бумеранга» (<see cref="ICardFlightPath"/>): каждый шаг физики продвигает
    /// <see cref="BoomerangPath"/>, ранит существ, сквозь которых пролетел (<see cref="BoomerangHits"/>), на твёрдом
    /// препятствии отскакивает и падает (в сброс), на обратном пути у игрока ловится (снова в руке), а когда время
    /// вышло — падает (в сброс).
    /// </summary>
    public sealed class BoomerangFlight : ICardFlightPath
    {
        /// <summary>Чем кончился полёт (для проверок).</summary>
        public enum Outcome { Flying, Caught, Struck, Dropped }

        readonly BoomerangEffect effect;
        readonly BoomerangHits hits = new BoomerangHits();
        BoomerangPath path;
        readonly int maxTargets;

        public Outcome Result { get; private set; }
        public BoomerangPath Path => path;
        /// <summary>Сколько раз ранил существ за этот полёт.</summary>
        public int HitCount { get; private set; }

        public BoomerangFlight(BoomerangEffect effect, in CardFlightLaunch launch)
        {
            this.effect = effect;
            path = effect.Begin(launch);
            maxTargets = effect.maxTargets.At(launch.Level);
        }

        public float SpinMultiplier => effect.spinMultiplier;
        public SfxCue FlightLoop => effect.whistle;

        public void Step(CardProjectile card, float dt)
        {
            var world = card.World;
            Vector3 from = path.Position;
            BoomerangEffect.Body(world, out var bottom, out var top, out var chest);
            BoomerangRules.Advance(ref path, chest, dt);
            Vector3 to = path.Position;
            float radius = world.settings.cardRadius;
            if (SweepSolid(from, to, radius, out var hit))
            {
                Strike(card, from, hit.point);
                card.MoveTo(hit.point, path.Velocity);
                Result = Outcome.Struck;
                card.Bounce(hit.collider, hit.point, hit.normal);
                return;
            }
            Strike(card, from, to);
            card.MoveTo(to, path.Velocity);
            if (BoomerangRules.Caught(path, bottom, top, effect.catchRadius))
            {
                Result = Outcome.Caught;
                card.Catch(effect.catchSound);
                return;
            }
            if (BoomerangRules.TimedOut(path))
            {
                Result = Outcome.Dropped;
                card.Drop();
            }
        }

        static readonly RaycastHit[] Casts = new RaycastHit[32];
        static readonly Collider[] Overlaps = new Collider[32];

        /// <summary>Ранит каждое существо, сквозь которое карта пролетела от <paramref name="from"/> до <paramref name="to"/>.</summary>
        void Strike(CardProjectile card, Vector3 from, Vector3 to)
        {
            var world = card.World;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            Vector3 direction = distance > 1e-5f ? delta / distance : (path.Velocity.sqrMagnitude > 1e-6f ? path.Velocity.normalized : Vector3.forward);
            float reach = effect.hitRadius;
            int count = distance > 1e-5f
                ? Physics.SphereCastNonAlloc(from, reach, direction, Casts, distance, CardFlight.CollisionMask, QueryTriggerInteraction.Ignore)
                : 0;
            for (int i = 0; i < count; i++)
            {
                var c = Casts[i];
                // Задетые уже в начале шага: точки нет, берём ближайшую к началу.
                Vector3 point = c.distance <= 0 && c.point == Vector3.zero ? c.collider.ClosestPoint(from) : c.point;
                TryHurt(card, world, c.collider, point, -direction);
            }
            int inside = Physics.OverlapSphereNonAlloc(to, reach, Overlaps, CardFlight.CollisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < inside; i++) TryHurt(card, world, Overlaps[i], Overlaps[i].ClosestPoint(to), -direction);
        }

        void TryHurt(CardProjectile card, ExpeditionWorld world, Collider collider, Vector3 point, Vector3 normal)
        {
            var fighter = Combat.Of(collider);
            if (!BoomerangEffect.IsPrey(world, fighter) || !hits.TryHit(fighter, path.Returning, maxTargets)) return;
            HitCount++;
            world.ApplyImpact(card.Card, card.Definition, collider, point, normal, path.Velocity, card.LaunchPosition);
        }

        static readonly RaycastHit[] Solids = new RaycastHit[32];

        /// <summary>
        /// Первое твёрдое препятствие на отрезке (стена, скала, дерево, куб, земля); существа и игрок не останавливают
        /// бумеранг — он пролетает сквозь них. Триггеры не мешают.
        /// </summary>
        public static bool SweepSolid(Vector3 from, Vector3 to, float radius, out RaycastHit hit)
        {
            hit = default;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-5f) return false;
            Vector3 direction = delta / distance;
            int count = Physics.SphereCastNonAlloc(from, radius, direction, Solids, distance, CardFlight.CollisionMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var candidate = Solids[i];
                if (Combat.Of(candidate.collider) != null) continue;
                if (candidate.distance >= best) continue;
                if (candidate.distance <= 0 && candidate.point == Vector3.zero)
                {
                    // Карта уже внутри препятствия (вылетела из стены): удар сразу, у начала отрезка.
                    candidate.point = from;
                    candidate.normal = -direction;
                }
                best = candidate.distance;
                hit = candidate;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Путь для превью у прицела: те же правила, что у настоящего полёта, до препятствия или до поимки (игрок
        /// стоит, где стоит); обратный путь — только до <see cref="BoomerangEffect.previewReturnStop"/> м от игрока.
        /// Возвращает число точек.
        /// </summary>
        public static int Preview(BoomerangEffect effect, in CardFlightLaunch launch, Vector3[] points)
        {
            if (points == null || points.Length == 0) return 0;
            var world = launch.World;
            var path = effect.Begin(launch);
            BoomerangEffect.Body(world, out var bottom, out var top, out var chest);
            float radius = world && world.settings ? world.settings.cardRadius : .06f;
            int max = points.Length;
            float step = path.Lifetime / Mathf.Max(1, max - 1);
            int used = 0;
            points[used++] = path.Position;
            while (used < max)
            {
                Vector3 from = path.Position;
                BoomerangRules.Advance(ref path, chest, step);
                if (SweepSolid(from, path.Position, radius, out var hit)) { points[used++] = hit.point; break; }
                // Обратный путь — только до нескольких метров от игрока: дальше он тает (концы полосы прозрачны).
                if (BoomerangRules.PreviewReturnEnds(path, from, chest, effect.previewReturnStop, out var end)) { points[used++] = end; break; }
                points[used++] = path.Position;
                if (BoomerangRules.Caught(path, bottom, top, effect.catchRadius) || BoomerangRules.TimedOut(path)) break;
            }
            return used;
        }
    }
}
