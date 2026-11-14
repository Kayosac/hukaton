using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Стая «Вороньё» (префаб <c>Assets/Game/Prefabs/Cards/Crows/Crows.prefab</c>): три рисованные вороны
    /// (SpriteRenderer на HandDrawnSprite, плотность общей сетки) слетаются на существо, машут крыльями, кружат над
    /// головой и по очереди пикируют клевать. Раз в <see cref="peckInterval"/> с клевок ранит (вспышка и перья из
    /// Retro Arsenal, звук), между ними — короткие клевки без урона (перья). Пока стая клюёт, существо отвлечено
    /// (<see cref="IDistractable"/>). Существо умерло или выгрузилось — вороны разлетаются; срок вышел — улетают и
    /// отвлечение снимается. Без цели (промах) вороны кружат над местом и улетают. На одном существе — одна стая:
    /// повторное попадание её продлевает (<see cref="Renew"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrowFlock : MonoBehaviour, IOutingSpawned
    {
        enum Phase { Idle, Pecking, Circling, Leaving, Done }

        [Header("Вороны")]
        [Tooltip("Рисунки ворон (дети префаба); смотрят в камеру, рисунок — ворона клювом вправо.")]
        public SpriteRenderer[] crows = new SpriteRenderer[0];
        [Tooltip("Кадры взмаха: крылья вверх, посередине, вниз.")]
        public Sprite[] frames = new Sprite[0];
        [Tooltip("Взмахов в секунду (полный цикл кадров).")]
        [Min(0)] public float flapRate = 3.2f;
        [Tooltip("Во сколько раз чаще машут, улетая.")]
        [Min(1)] public float leaveFlapBoost = 1.6f;

        [Header("Круг над головой")]
        [Tooltip("Зазор над макушкой, м.")]
        public float headGap = .15f;
        [Tooltip("Радиус круга на метр роста существа и пределы, м.")]
        [Min(0)] public float orbitPerMetre = .45f;
        [Min(0)] public float minOrbit = .55f, maxOrbit = 1.4f;
        [Tooltip("Скорость круга, градусов в секунду.")]
        public float orbitSpeed = 150;
        [Tooltip("Покачивание вверх-вниз, м, и его частота, Гц.")]
        [Min(0)] public float bob = .12f, bobRate = 1.3f;
        [Tooltip("Слетаются за столько секунд издалека (м, вверх и в сторону).")]
        [Min(.01f)] public float arriveSeconds = .55f;
        [Min(0)] public float arriveFrom = 5;

        [Header("Клевки")]
        [Tooltip("Раз в столько секунд клевок ранит (урон в секунду × интервал).")]
        [Min(.05f)] public float peckInterval = 1;
        [Tooltip("Сколько пике (с ранящим) на один интервал: остальные — без урона, для вида.")]
        [Min(1)] public int divesPerInterval = 3;
        [Tooltip("Полвремени пике (туда или обратно), с.")]
        [Min(.01f)] public float diveSeconds = .14f;
        [Tooltip("Ранящий клевок: вспышка и перья (Retro Arsenal), размер на метр роста.")]
        public VfxBurst peckVfx;
        public VfxBurst featherVfx;
        [Min(0)] public float peckScale = .35f, featherScale = .12f, puffScale = .07f;
        public Color featherTint = new Color(.16f, .11f, .12f, 1);
        public SfxCue peckSound;

        [Header("Голос и крылья")]
        public SfxCue cawSound;
        public SfxCue flapSound;
        [Tooltip("Карканье раз в столько секунд (мин., макс.).")]
        public Vector2 cawEvery = new Vector2(.8f, 1.6f);

        [Header("Отлёт и промах")]
        [Min(.01f)] public float leaveSeconds = 1.3f;
        [Min(0)] public float leaveDistance = 8;
        [Tooltip("Промах: сколько кружат над местом, с; высота круга и радиус, м.")]
        [Min(0)] public float circleSeconds = 1.8f;
        [Min(0)] public float circleHeight = 1.6f, circleRadius = 1.1f;

        static readonly List<CrowFlock> Active = new List<CrowFlock>();

        ExpeditionWorld world;
        ICombatant target;
        IDistractable distractable;
        Phase phase;
        int damagePerSecond, dives;
        float startAt, endAt, nextPeckAt, leaveAt, doneAt, nextCaw, spin;
        Vector3 anchor;
        Vector3[] from, leaveFrom, leaveDirection, previous;

        /// <summary>Существо под стаей (null — промах или стая улетает).</summary>
        public ICombatant Target => phase == Phase.Pecking ? target : null;
        /// <summary>Урон клевков в секунду.</summary>
        public int DamagePerSecond => damagePerSecond;
        /// <summary>Когда вороны улетят (Time.time).</summary>
        public float EndAt => endAt;
        /// <summary>Стая клюёт существо.</summary>
        public bool Pecking => phase == Phase.Pecking;
        /// <summary>Вороны улетают (или уже улетели).</summary>
        public bool Leaving => phase == Phase.Leaving || phase == Phase.Done;

        /// <summary>Стая, которая сейчас клюёт это существо, или null.</summary>
        public static CrowFlock On(ICombatant fighter)
        {
            foreach (var flock in Active)
                if (flock && flock.phase == Phase.Pecking && ReferenceEquals(flock.target, fighter)) return flock;
            return null;
        }

        public void OnSpawned(ExpeditionWorld owner) => world = owner;

        /// <summary>Слетаются на существо: <paramref name="perSecond"/> HP в секунду, <paramref name="seconds"/> с.</summary>
        public void Attach(ICombatant fighter, int perSecond, float seconds, float now)
        {
            target = fighter;
            distractable = fighter as IDistractable;
            damagePerSecond = Mathf.Max(0, perSecond);
            startAt = now;
            endAt = now + Mathf.Max(0, seconds);
            nextPeckAt = now + peckInterval;
            phase = Phase.Pecking;
            if (!Active.Contains(this)) Active.Add(this);
            distractable?.Distract(endAt - now);
            Begin(Head(), now);
        }

        /// <summary>Повторное попадание: стая клюёт полный срок заново, урон — больший из двух.</summary>
        public void Renew(int perSecond, float seconds, float now)
        {
            if (phase != Phase.Pecking) return;
            damagePerSecond = CrowsRules.RenewedDamage(damagePerSecond, perSecond);
            endAt = Mathf.Max(endAt, now + Mathf.Max(0, seconds));
            distractable?.Distract(endAt - now);
            Caw(1.1f);
        }

        /// <summary>Промах: вороны покружат над <paramref name="point"/> и улетят.</summary>
        public void Circle(Vector3 point, float now)
        {
            target = null;
            distractable = null;
            phase = Phase.Circling;
            startAt = now;
            leaveAt = now + circleSeconds;
            Begin(point + Vector3.up * circleHeight, now);
        }

        void Begin(Vector3 centre, float now)
        {
            anchor = centre;
            spin = Random.Range(0f, 360f);
            int count = crows.Length;
            from = new Vector3[count];
            previous = new Vector3[count];
            leaveFrom = new Vector3[count];
            leaveDirection = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 side = Quaternion.Euler(0, spin + i * 360f / Mathf.Max(1, count) + Random.Range(-25f, 25f), 0) * Vector3.forward;
                from[i] = centre + side * arriveFrom + Vector3.up * arriveFrom * .6f;
                previous[i] = from[i];
                if (crows[i]) crows[i].transform.position = from[i];
            }
            nextCaw = now + Random.Range(cawEvery.x, Mathf.Max(cawEvery.x, cawEvery.y));
            Caw(1);
            if (world) world.Play(flapSound, centre);
        }

        void Update()
        {
            if (phase == Phase.Idle || phase == Phase.Done) return;
            float now = Time.time;
            Tick(now);
            if (this && phase != Phase.Done) Pose(now);
        }

        /// <summary>Один шаг стаи: клевки по сроку, отвлечение, отлёт. Возвращает, что стая сделала.</summary>
        public CrowsRules.Step Tick(float now)
        {
            switch (phase)
            {
                case Phase.Pecking:
                {
                    if (!TargetAlive()) { Leave(now); return CrowsRules.Step.Leave; }
                    bool pecked = false;
                    while (CrowsRules.PeckDue(now, nextPeckAt, endAt))
                    {
                        Peck(nextPeckAt);
                        pecked = true;
                        nextPeckAt += peckInterval;
                        if (!TargetAlive()) { Leave(now); return CrowsRules.Step.Leave; }
                    }
                    if (now >= endAt - 1e-4f) { Leave(now); return CrowsRules.Step.Leave; }
                    distractable?.Distract(endAt - now);
                    if (now >= nextCaw) { nextCaw = now + Random.Range(cawEvery.x, Mathf.Max(cawEvery.x, cawEvery.y)); Caw(Random.Range(.9f, 1.15f)); }
                    return pecked ? CrowsRules.Step.Peck : CrowsRules.Step.Wait;
                }
                case Phase.Circling:
                    if (now >= leaveAt) { Leave(now); return CrowsRules.Step.Leave; }
                    return CrowsRules.Step.Wait;
                case Phase.Leaving:
                    if (now >= doneAt) { Finish(); return CrowsRules.Step.Done; }
                    return CrowsRules.Step.Leave;
                case Phase.Done:
                    return CrowsRules.Step.Done;
                default:
                    return CrowsRules.Step.Wait;
            }
        }

        bool TargetAlive() => Combat.IsAlive(target) && target.Root;

        void Peck(float at)
        {
            Vector3 head = Head();
            Vector3 point = Vector3.Lerp(target.Centre, head, .6f);
            target.TakeDamage(new DamageInfo(damagePerSecond, Vector3.zero, point, point + Vector3.up, DamageKind.Hit));
            if (!world) return;
            float height = Height();
            world.Spawn(peckVfx, point, Quaternion.identity, Color.white, peckScale * Mathf.Max(.6f, height));
            world.Spawn(featherVfx, point, Quaternion.identity, featherTint, featherScale * Mathf.Max(.6f, height));
            world.Play(peckSound, point, 1, Random.Range(.95f, 1.1f));
        }

        void Caw(float pitch)
        {
            if (world) world.Play(cawSound, anchor, 1, pitch);
        }

        /// <summary>Вороны улетают; отвлечение с живого существа снимается.</summary>
        void Leave(float now)
        {
            if (phase == Phase.Leaving || phase == Phase.Done) return;
            if (phase == Phase.Pecking && TargetAlive()) distractable?.EndDistraction();
            phase = Phase.Leaving;
            Active.Remove(this);
            doneAt = now + leaveSeconds;
            int count = crows.Length;
            leaveFrom ??= new Vector3[count];
            leaveDirection ??= new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 at = crows[i] ? crows[i].transform.position : anchor;
                leaveFrom[i] = at;
                Vector3 away = at - anchor;
                away.y = 0;
                if (away.sqrMagnitude < .01f) away = Quaternion.Euler(0, i * 120f, 0) * Vector3.forward;
                away = Quaternion.Euler(0, Random.Range(-30f, 30f), 0) * away.normalized;
                leaveDirection[i] = (away + Vector3.up * .7f).normalized;
            }
            startAt = now;
            Caw(1.2f);
            if (world) world.Play(flapSound, anchor);
        }

        void Finish()
        {
            phase = Phase.Done;
            Active.Remove(this);
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        void OnDestroy()
        {
            Active.Remove(this);
            // Стаю убрали раньше срока (конец вылазки): существо больше не отвлечено.
            if (phase == Phase.Pecking && TargetAlive()) distractable?.EndDistraction();
        }

        float Height() => target != null && TargetAlive() ? Mathf.Max(.3f, (target.Centre.y - target.Position.y) * 2) : 1;

        Vector3 Head() => TargetAlive() ? CrowsRules.HeadTop(target.Position, target.Centre, headGap) : anchor;

        // ---------------- вид ----------------

        void Pose(float now)
        {
            if (crows == null || from == null) return;
            var view = world && world.viewCamera ? world.viewCamera.transform : Camera.main ? Camera.main.transform : null;
            int count = crows.Length;
            float age = now - startAt;
            Vector3 head = anchor;
            float radius = circleRadius;
            if (phase == Phase.Pecking)
            {
                head = anchor = Head();
                radius = CrowsRules.OrbitRadius(Height(), orbitPerMetre, minOrbit, maxOrbit);
            }
            float arrive = phase == Phase.Leaving ? 1 : Smooth(age / arriveSeconds);
            // Пике по очереди: срок i-го пике — через равные доли интервала; ранящее совпадает с клевком.
            float slot = peckInterval / Mathf.Max(1, divesPerInterval);
            int diveIndex = Mathf.RoundToInt((now - startAt) / slot);
            float diveTime = startAt + diveIndex * slot;
            float diveWeight = phase == Phase.Pecking && arrive >= 1 ? Mathf.Clamp01(1 - Mathf.Abs(now - diveTime) / diveSeconds) : 0;
            int diver = count > 0 ? ((diveIndex % count) + count) % count : 0;
            if (phase == Phase.Pecking && diveIndex > dives && arrive >= 1)
            {
                // Перья от каждого пике (ранящий клевок даёт ещё вспышку в Peck).
                dives = diveIndex;
                if (world) world.Spawn(featherVfx, head + Vector3.down * Mathf.Min(.25f, headGap + .1f), Quaternion.identity, featherTint, puffScale);
            }

            for (int i = 0; i < count; i++)
            {
                var crow = crows[i];
                if (!crow) continue;
                Vector3 position;
                float flap = flapRate;
                if (phase == Phase.Leaving)
                {
                    float k = Mathf.Clamp01((now - startAt) / leaveSeconds);
                    position = leaveFrom[i] + leaveDirection[i] * leaveDistance * (k * k * .7f + k * .3f);
                    flap *= leaveFlapBoost;
                }
                else
                {
                    float angle = spin + i * 360f / count + age * orbitSpeed * (i % 2 == 0 ? 1 : .85f);
                    Vector3 round = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                    float r = radius * (1 + .12f * Mathf.Sin(age * 1.7f + i * 2.1f));
                    Vector3 orbit = head + round * r + Vector3.up * (bob * Mathf.Sin(age * bobRate * Mathf.PI * 2 + i * 1.9f) + .1f * i);
                    position = Vector3.Lerp(from[i], orbit, arrive);
                    if (i == diver && diveWeight > 0)
                    {
                        Vector3 strike = head + round * Mathf.Min(.18f, r * .2f) + Vector3.down * Mathf.Min(.25f, headGap + .1f);
                        position = Vector3.Lerp(position, strike, diveWeight * diveWeight * (3 - 2 * diveWeight));
                    }
                }
                Vector3 velocity = position - previous[i];
                previous[i] = position;
                var t = crow.transform;
                t.position = position;
                if (view)
                {
                    t.rotation = view.rotation;
                    float sideways = Vector3.Dot(velocity, view.right);
                    if (Mathf.Abs(sideways) > .0005f) crow.flipX = sideways < 0;
                }
                bool diving = i == diver && diveWeight > .3f;
                if (frames != null && frames.Length > 0)
                    crow.sprite = frames[diving ? 0 : CrowsRules.FlapFrame(now * flap + i * .37f, frames.Length)];
            }
        }

        static float Smooth(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3 - 2 * k);
        }
    }
}
