using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Чёрная метка «Сглаза» (префаб <c>Assets/Game/Prefabs/Cards/EvilEye/Evil eye mark.prefab</c>). Висит над головой
    /// помеченного существа и каждый кадр догоняет его (отбросило, заморожено, в ледяной глыбе, уменьшено Кривым
    /// зеркалом — знак остаётся над ним и меняет размер вместе с ним). Знак — частицы Retro Arsenal: пульсирует, к
    /// взрыву разрастается и пульсирует чаще. В срок лопается: тёмная вспышка, звук, урон, лёгкий hit-stop.
    /// Существо умерло, выгрузилось с чанком или вылазка кончилась — метка пропадает без урона.
    /// Несколько меток на одном существе — каждая со своим отсчётом, знаки стоят друг над другом.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EvilEyeMark : MonoBehaviour, IOutingSpawned
    {
        [Header("Знак")]
        [Tooltip("Узел знака: его масштаб пульсирует и растёт к взрыву.")]
        public Transform sign;
        [Tooltip("Частицы знака (зацикленные): запускаются при появлении метки, к взрыву играют быстрее.")]
        public ParticleSystem[] loops;
        [Tooltip("Зазор между макушкой существа и знаком, м.")]
        [Min(0)] public float gap = .35f;
        [Tooltip("На сколько выше встаёт знак второй, третьей метки на том же существе, м (на метр роста).")]
        [Min(0)] public float slotStep = .55f;
        [Tooltip("Размер знака на метр роста существа и пределы.")]
        [Min(0)] public float sizePerMetre = .45f;
        [Min(0)] public float minSize = .4f, maxSize = 1.3f;
        [Tooltip("Размер знака в начале и в конце отсчёта (доля).")]
        [Min(0)] public float swellFrom = .75f, swellTo = 1.35f;
        [Tooltip("Частота пульса в начале и в конце отсчёта, Гц.")]
        [Min(0)] public float pulseFrom = 1.5f, pulseTo = 7;
        [Tooltip("Глубина пульса (доля размера).")]
        [Range(0, 1)] public float pulseDepth = .18f;
        [Tooltip("Скорость частиц знака в начале и в конце отсчёта.")]
        [Min(0)] public float speedFrom = 1, speedTo = 2.2f;

        [Header("Взрыв")]
        [Tooltip("Тёмная вспышка (Retro Arsenal) на теле существа; размер — на метр роста.")]
        public VfxBurst popVfx;
        [Min(0)] public float popScalePerMetre = 1;
        public SfxCue popSound;
        [Tooltip("Небольшой толчок вверх при взрыве (единицы импульса).")]
        [Min(0)] public float popLift = .6f;
        [Range(0, 1)] public float shake = .3f;
        [Min(0)] public float hitStop = .05f;

        static readonly List<EvilEyeMark> Active = new List<EvilEyeMark>();

        ExpeditionWorld world;
        ICombatant target;
        int damage, slot;
        float startAt, popAt, phase;
        bool attached, done;

        /// <summary>Существо под меткой (null — метка ещё не повешена).</summary>
        public ICombatant Target => target;
        /// <summary>Сколько HP снимет взрыв.</summary>
        public int Damage => damage;
        /// <summary>Когда метка лопнет (Time.time).</summary>
        public float PopAt => popAt;
        /// <summary>Метка уже лопнула или пропала.</summary>
        public bool Done => done;

        /// <summary>Сколько живых меток висит на этом существе.</summary>
        public static int CountOn(ICombatant fighter)
        {
            int count = 0;
            foreach (var mark in Active)
                if (mark && !mark.done && ReferenceEquals(mark.target, fighter)) count++;
            return count;
        }

        public void OnSpawned(ExpeditionWorld owner) => world = owner;

        /// <summary>Вешает метку: урон <paramref name="amount"/>, лопнет в момент <paramref name="popTime"/>.</summary>
        public void Attach(ICombatant fighter, int amount, float popTime, float now)
        {
            slot = CountOn(fighter);
            target = fighter;
            damage = Mathf.Max(0, amount);
            startAt = now;
            popAt = popTime;
            attached = true;
            if (!Active.Contains(this)) Active.Add(this);
            if (loops != null)
                foreach (var system in loops)
                    if (system) system.Play(false);
            Pose(now);
        }

        void Update()
        {
            if (attached && !done) Tick(Time.time);
        }

        void LateUpdate()
        {
            if (attached && !done && Combat.IsAlive(target) && target.Root) Pose(Time.time);
        }

        /// <summary>Один шаг отсчёта; возвращает, что метка сделала.</summary>
        public EvilEyeRules.Step Tick(float now)
        {
            if (done) return EvilEyeRules.Step.Vanish;
            bool alive = Combat.IsAlive(target) && target.Root;
            var step = EvilEyeRules.Decide(alive, now, popAt);
            if (step == EvilEyeRules.Step.Pop) Pop();
            if (step != EvilEyeRules.Step.Wait) Finish();
            return step;
        }

        /// <summary>Ставит знак над существом, пульс и разрастание.</summary>
        void Pose(float now)
        {
            Vector3 feet = target.Position, centre = target.Centre;
            float height = Mathf.Max(.2f, (centre.y - feet.y) * 2);
            float size = Mathf.Clamp(height * sizePerMetre, minSize, maxSize);
            transform.position = EvilEyeRules.SignPosition(feet, centre, gap, slot, slotStep * size);
            float progress = EvilEyeRules.Progress(now, startAt, popAt);
            phase += Time.deltaTime * EvilEyeRules.PulseRate(progress, pulseFrom, pulseTo) * Mathf.PI * 2;
            float pulse = 1 + pulseDepth * Mathf.Sin(phase);
            if (sign) sign.localScale = Vector3.one * (size * EvilEyeRules.Swell(progress, swellFrom, swellTo) * pulse);
            if (loops == null) return;
            float speed = Mathf.Lerp(speedFrom, speedTo, progress);
            foreach (var system in loops)
            {
                if (!system) continue;
                var main = system.main;
                main.simulationSpeed = speed;
            }
        }

        void Pop()
        {
            Vector3 feet = target.Position, centre = target.Centre;
            float height = Mathf.Max(.2f, (centre.y - feet.y) * 2);
            target.TakeDamage(new DamageInfo(damage, Vector3.up * popLift, centre, centre + Vector3.up, DamageKind.Other));
            if (!world) return;
            world.Spawn(popVfx, centre, Quaternion.identity, Color.white, popScalePerMetre * Mathf.Max(.6f, height));
            world.Play(popSound, centre);
            if (world.feel && world.viewCamera)
            {
                float distance = Vector3.Distance(world.viewCamera.transform.position, centre);
                world.feel.AddTrauma(shake * Mathf.Clamp01(1.4f - distance / 16f));
            }
            if (hitStop > 0 && world.time) world.time.HitStop(hitStop);
        }

        void Finish()
        {
            done = true;
            Active.Remove(this);
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        void OnDestroy() => Active.Remove(this);
    }
}
