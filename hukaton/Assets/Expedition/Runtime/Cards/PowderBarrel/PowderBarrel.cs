using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.Pool;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Пороховая бочка карты «Пороховая бочка» (префаб <c>Assets/Game/Prefabs/Cards/PowderBarrel/Powder barrel.prefab</c>):
    /// твёрдый физический предмет (Rigidbody + выпуклый коллайдер — на неё можно забраться, её толкают Импульс и
    /// Притяжение), стоит до конца вылазки. Звери её не трогают: она не боец (<see cref="ICombatant"/>), удар её не берёт.
    /// <para>
    /// Взрывается ТОЛЬКО от огня, через общую систему поджига карт: бочка — зона <see cref="ICardZone"/> и
    /// <see cref="IIgnitable"/>, как смола, ветвь-мост и Чучело. Поэтому её поджигают все, кто поджигает зоны:
    /// карта Огонь (попала в неё или рядом), Молния, горящий уязвимый к огню зверь вплотную, горящая смола рядом,
    /// пламя, перенесённое Сквозняком, вспышка болотного газа, вспыхнувшее Чучело, взрыв другой бочки. Порох
    /// капризнее смолы: бочку поджигает и любой горящий зверь, подошедший вплотную (<see cref="burningTouchReach"/>).
    /// </para>
    /// <para>
    /// Подожжённая бочка вспыхивает и шипит короткий фитиль (<see cref="fuseSeconds"/>), контур взрыва виден на
    /// земле, потом бум: ранит всех бойцов в радиусе — зверей, мимиков и союзников поджигает и отбрасывает от
    /// центра; игрока ранит своим числом (<see cref="PowderBarrelBlast.playerDamage"/>, половинки сердца) и
    /// отбрасывает, но не поджигает, а в Пузыре не трогает вовсе. Поджигает горючее вокруг (смола, ветвь-мост, газ)
    /// и взрывает соседние бочки с маленькой задержкой (<see cref="chainDelay"/>). Мокрая бочка (<see cref="WetStatus"/>:
    /// Струя, дождь, вода озера или топи) не загорается, пока мокрая; вода гасит и уже горящий фитиль.
    /// Чанк выгрузился, пока горел фитиль, — фитиль гаснет (<see cref="OnDisable"/>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PowderBarrel : CardZoneBehaviour, IIgnitable, IWashable, ISoakable, ICardAreaOutline
    {
        [Header("Части префаба")]
        public Rigidbody body;
        [Tooltip("Твёрдый выпуклый коллайдер бочки (по нему ходят, в него попадают карты).")]
        public Collider solid;
        [Tooltip("Модель (кривая бочка). Пульсирует, пока горит фитиль.")]
        public Transform model;
        [Tooltip("Кончик фитиля: тут горит пламя фитиля.")]
        public Transform fuseTip;

        [Header("Фитиль")]
        [Tooltip("От поджига до взрыва, с: бочка вспыхивает и шипит, чтобы было видно, что сейчас бабахнет.")]
        [Min(0)] public float fuseSeconds = .4f;
        [Tooltip("Соседнюю бочку взрыв подрывает через столько секунд (цепная реакция).")]
        [Min(0)] public float chainDelay = .15f;
        [Tooltip("Пламя на фитиле (и на бочке) пока он горит (Retro Arsenal).")]
        public VfxBurst fuseFlames;
        [Min(0)] public float fuseFlamesScale = .35f, bodyFlamesScale = .9f;
        public SfxCue igniteSound, fuseSound;
        [Tooltip("Насколько вздувается бочка перед взрывом (доля размера).")]
        [Min(0)] public float swell = .1f;
        [Min(0)] public float swellFrequency = 14;

        [Header("Горящие звери рядом")]
        [Tooltip("Любой горящий зверь, подошедший к бочке ближе этого, поджигает её, м.")]
        [Min(0)] public float burningTouchReach = .35f;
        [Min(.02f)] public float touchInterval = .15f;

        [Header("Мокрая бочка")]
        [Tooltip("Огонь по мокрой бочке: пар и шипение вместо взрыва.")]
        public VfxBurst fizzleVfx;
        public SfxCue fizzleSound;
        [Min(0)] public float fizzleCooldown = .6f;

        [Header("Взрыв")]
        [Tooltip("Огненный взрыв, дым и искры (Retro Arsenal); размер — на метр радиуса.")]
        public VfxBurst blastVfx;
        [Min(0)] public float blastScalePerMetre = 1;
        [Tooltip("Цвет вспышки света взрыва.")]
        public Color blastTint = new Color(1f, .62f, .28f);
        public SfxCue blastSound;
        [Tooltip("Тряска камеры вплотную; к shakeDistance м сходит на нет.")]
        [Range(0, 1)] public float shake = .9f;
        [Min(1)] public float shakeDistance = 24;
        [Min(0)] public float hitStop = .1f;
        [Tooltip("Отброс у края радиуса — такая доля от отброса вплотную.")]
        [Range(0, 1)] public float pushAtEdge = .5f;
        [Tooltip("Доля отброса вверх.")]
        [Range(0, 1)] public float lift = .45f;
        [Tooltip("Сколько секунд горят подожжённые звери (уязвимые к огню — дольше, по их правилам). Игрока взрыв не поджигает.")]
        [Min(0)] public float creatureBurnSeconds = 3;
        [Tooltip("Ранит ли взрыв союзников (ожившее дерево, Чучело). Порох не разбирает сторон.")]
        public bool hurtAllies = true;

        static readonly List<ICombatant> Fighters = new List<ICombatant>();
        // Растёт, если вещей в радиусе больше (свалка ящиков и лута): ни одну не пропустить.
        static Collider[] overlaps = new Collider[128];

        readonly BarrelFuse fuse = new BarrelFuse();
        PowderBarrelBlast numbers = new PowderBarrelBlast { damage = 5, playerDamage = 2, radius = 3, knockback = 10 };
        VfxBurst tipFlames, bodyFlames;
        float nextTouch, lastFizzle = float.NegativeInfinity;

        public override CardZoneKind Kind => CardZoneKind.PowderBarrel;
        public BarrelFuseState State => fuse.State;
        public bool Lit => fuse.Lit;
        /// <summary>Мокрая: не загорается.</summary>
        public bool Wet => WetStatus.IsWet(this);
        public PowderBarrelBlast Numbers => numbers;
        /// <summary>Когда бабахнет (пока горит фитиль).</summary>
        public float BlowAt => fuse.BlowAt;
        /// <summary>Центр взрыва: середина бочки.</summary>
        public Vector3 Centre => solid ? solid.bounds.center : transform.position + Vector3.up * height * .5f;

        public override Bounds Bounds => solid && solid.enabled ? solid.bounds : base.Bounds;
        public override bool Contains(Vector3 point) => solid && solid.enabled
            ? (solid.ClosestPoint(point) - point).sqrMagnitude < 1e-6f
            : base.Contains(point);

        /// <summary>Числа карты сразу после появления.</summary>
        public void Setup(in PowderBarrelBlast blast) => numbers = blast;

        // ---------------- поджиг ----------------

        /// <summary>
        /// Огонь или молния (через <see cref="ExpeditionWorld.Ignite(Collider, Vector3, float, IgnitionSource)"/> и
        /// <see cref="CardZoneRegistry.Ignite"/>): бочка вспыхивает, через фитиль — взрыв. Мокрая шипит и не горит.
        /// </summary>
        public bool Ignite(IgnitionSource source, Vector3 point) => Light(fuseSeconds, point);

        /// <summary>Подрыв соседней бочкой: фитиль короче обычного. Мокрую не берёт.</summary>
        public bool Detonate(float delay) => Light(delay, Centre);

        bool Light(float seconds, Vector3 point)
        {
            if (fuse.Blown) return false;
            bool wasLit = fuse.Lit;
            if (!fuse.Light(Time.time, seconds, Wet))
            {
                if (!wasLit && Wet) Fizzle(point);
                return false;
            }
            if (!wasLit) StartFuse();
            return true;
        }

        /// <summary>
        /// Подрывает соседние бочки из <paramref name="zones"/>, достающие до взрыва этой (кроме неё самой), через
        /// <see cref="chainDelay"/>. Возвращает, сколько подожжено.
        /// </summary>
        public int DetonateNeighbours(IReadOnlyList<ICardZone> zones)
        {
            int count = 0;
            Vector3 centre = Centre;
            float r2 = numbers.radius * numbers.radius;
            foreach (var zone in zones)
            {
                if (!(zone is PowderBarrel other) || !other || ReferenceEquals(other, this)) continue;
                if (other.Bounds.SqrDistance(centre) > r2) continue;
                if (other.Detonate(chainDelay)) count++;
            }
            return count;
        }

        // ---------------- вода ----------------

        /// <summary>Дождь: бочка мокнет (ещё 30 с после), горящий фитиль гаснет.</summary>
        public bool Wash(ICardZone rain) => Soak(World, WetStatus.DefaultLingerSeconds);

        /// <summary>Струя (и любая вода): бочка мокнет на <paramref name="seconds"/> с, горящий фитиль гаснет.</summary>
        public bool Soak(ExpeditionWorld world, float seconds)
        {
            if (fuse.Blown) return false;
            bool wasWet = Wet;
            WetStatus.Soak(this, world ? world : World, seconds);
            bool doused = fuse.Douse();
            if (doused) StopFuse(true);
            return doused || !wasWet;
        }

        // ---------------- кадр ----------------

        /// <summary>
        /// Бочку спрятали (чанк выгрузился, <c>WorldStreamer.HideLoose</c>) с горящим фитилём: фитиль гаснет, бочка
        /// вернётся целой и снова будет ждать огня. Иначе срок фитиля прошёл бы, пока чанка нет, и бочка взорвалась бы
        /// сразу по возвращении.
        /// </summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            if (fuse.Douse()) StopFuse(false);
        }

        void Update()
        {
            if (fuse.Blown) return;
            if (fuse.Lit)
            {
                if (model) model.localScale = Vector3.one * (1 + swell * (.5f + .5f * Mathf.Sin(Time.time * swellFrequency * Mathf.PI * 2)));
                if (fuse.Due(Time.time)) Explode();
                return;
            }
            if (World && burningTouchReach > 0 && Time.time >= nextTouch)
            {
                nextTouch = Time.time + touchInterval;
                TouchedByBurningCreature();
            }
        }

        /// <summary>Горящий зверь вплотную поджигает порох (любой, не только уязвимый к огню).</summary>
        void TouchedByBurningCreature()
        {
            var box = Bounds;
            Fighters.Clear();
            World.Combatants(Fighters);
            foreach (var fighter in Fighters)
            {
                if (!(fighter is WorldCreature creature) || !creature || !creature.Burning) continue;
                Vector3 feet = creature.Position;
                Vector3 top = feet + Vector3.up * Mathf.Max(0, creature.BodyHeight - creature.CapsuleRadius);
                if (!CardZoneRegistry.TouchPoint(box, feet, top, creature.CapsuleRadius, burningTouchReach, out var point)) continue;
                Ignite(IgnitionSource.Fire, point);
                return;
            }
        }

        // ---------------- взрыв ----------------

        /// <summary>Бум: урон, огонь и отброс всем рядом, поджиг горючего, цепь соседних бочек; бочки больше нет.</summary>
        public void Explode()
        {
            if (!fuse.Blow()) return;
            Vector3 centre = Centre;
            StopFuse(false);
            if (World) World.Zones.Remove(this);
            if (solid) solid.enabled = false;
            if (model) model.gameObject.SetActive(false);
            var world = World;
            if (world)
            {
                world.Spawn(blastVfx, centre, Quaternion.identity, blastTint, blastScalePerMetre * numbers.radius);
                world.Play(blastSound, centre);
                bool hit = HitEveryone(world, centre);
                PushLooseBodies(centre);
                using (ListPool<ICardZone>.Get(out var nearby))
                {
                    world.Zones.Near(centre, numbers.radius, nearby, CardZoneKind.PowderBarrel);
                    DetonateNeighbours(nearby);
                }
                // Горючее вокруг: смола, ветвь-мост, газ, Чучело (бочки уже подожжены цепью выше).
                world.Zones.Ignite(centre, centre, numbers.radius, 0, IgnitionSource.Fire, this);
                var thicket = world.ObstacleNear(centre, numbers.radius, ObstacleKind.Thorns);
                if (thicket) thicket.React(CardAction.Fire, centre);
                Feel(world, centre, hit);
            }
            Destroy(gameObject);
        }

        bool HitEveryone(ExpeditionWorld world, Vector3 centre)
        {
            bool any = false;
            Fighters.Clear();
            world.Combatants(Fighters);
            foreach (var fighter in Fighters)
            {
                if (!Combat.IsAlive(fighter) || !fighter.Root) continue;
                if (!PowderBarrelRules.Reaches(centre, numbers.radius, fighter.Position, fighter.Centre)) continue;
                bool player = ReferenceEquals(fighter, world.Player);
                if (!player && !hurtAllies && fighter.Faction == Faction.Player) continue;
                // Игрок в Пузыре неуязвим к своей бочке: ни урона, ни отброса.
                if (player && CardBarriers.ShieldsPlayer(world, fighter.Position)) continue;
                var push = PowderBarrelRules.Push(centre, fighter.Centre, numbers.radius, numbers.knockback, pushAtEdge, lift);
                any = true;
                if (player)
                {
                    // Игроку — своё число в половинках сердца (Хрупкость и защита после удара — как у любого удара),
                    // без горения. Отброс — только если урон прошёл: в цепочке бочек защита после первого взрыва
                    // гасит и следующие толчки, импульсы не складываются.
                    bool hurt = fighter.TakeDamage(new DamageInfo(numbers.playerDamage, Vector3.zero, fighter.Centre, centre, DamageKind.Fire));
                    if (hurt && world.motor)
                    {
                        world.motor.ApplyNormalizedImpulse(push);
                        world.OnPlayerLaunched(push);
                    }
                }
                else if (fighter is WorldCreature creature)
                    creature.Damage(numbers.damage, push, creature.Centre, creatureBurnSeconds, DamageKind.Fire);
                else
                    fighter.TakeDamage(new DamageInfo(numbers.damage, push, fighter.Centre, centre, DamageKind.Fire));
            }
            return any;
        }

        /// <summary>
        /// Отбрасывает и вещи без здоровья: ящики, каменный куб, лут, другие бочки — сколько их ни будь в радиусе.
        /// Возвращает, скольким телам дан толчок.
        /// </summary>
        public int PushLooseBodies(Vector3 centre)
        {
            int pushed = 0;
            int count = Physics.OverlapSphereNonAlloc(centre, numbers.radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            // Буфер полон — значит, вещей могло быть больше: удвоить и спросить заново.
            while (count == overlaps.Length)
            {
                overlaps = new Collider[overlaps.Length * 2];
                count = Physics.OverlapSphereNonAlloc(centre, numbers.radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            }
            using (HashSetPool<Rigidbody>.Get(out var seen))
            {
                for (int i = 0; i < count; i++)
                {
                    var hit = overlaps[i];
                    var rb = hit ? hit.attachedRigidbody : null;
                    if (!rb || rb == body || rb.isKinematic || !seen.Add(rb) || Combat.Of(hit) != null) continue;
                    CardPhysics.Push(rb, PowderBarrelRules.Push(centre, rb.worldCenterOfMass, numbers.radius, numbers.knockback, pushAtEdge, lift));
                    pushed++;
                }
            }
            return pushed;
        }

        void Feel(ExpeditionWorld world, Vector3 centre, bool hitSomeone)
        {
            if (world.feel && world.viewCamera)
                world.feel.AddTrauma(PowderBarrelRules.Shake(shake, Vector3.Distance(world.viewCamera.transform.position, centre), shakeDistance));
            if (world.time && hitStop > 0 && (hitSomeone || world.viewCamera && Vector3.Distance(world.viewCamera.transform.position, centre) < numbers.radius * 2))
                world.time.HitStop(hitStop);
        }

        // ---------------- вид ----------------

        void StartFuse()
        {
            var world = World;
            if (!world) return;
            Vector3 tip = fuseTip ? fuseTip.position : Centre + Vector3.up * .5f;
            world.Play(igniteSound, tip);
            world.Play(fuseSound, tip);
            tipFlames = world.Spawn(fuseFlames, tip, Quaternion.identity, Color.white, fuseFlamesScale);
            if (tipFlames) { tipFlames.Follow(fuseTip ? fuseTip : transform, Vector3.zero); tipFlames.HoldFor(fuseSeconds + 1); }
            bodyFlames = world.Spawn(fuseFlames, transform.position, Quaternion.identity, Color.white, bodyFlamesScale);
            if (bodyFlames) { bodyFlames.Follow(transform, Vector3.up * height * .55f); bodyFlames.HoldFor(fuseSeconds + 1); }
        }

        void StopFuse(bool doused)
        {
            if (tipFlames) tipFlames.Release(doused ? .3f : .05f);
            if (bodyFlames) bodyFlames.Release(doused ? .3f : .05f);
            tipFlames = bodyFlames = null;
            if (model) model.localScale = Vector3.one;
            if (doused && World)
            {
                World.Spawn(fizzleVfx, Centre, Quaternion.identity, Color.white, 1);
                World.Play(fizzleSound, Centre);
            }
        }

        void Fizzle(Vector3 point)
        {
            if (!World || Time.time - lastFizzle < fizzleCooldown) return;
            lastFizzle = Time.time;
            World.Spawn(fizzleVfx, point, Quaternion.identity, Color.white, .7f);
            World.Play(fizzleSound, point);
        }

        // ---------------- контур ----------------

        /// <summary>Пока горит фитиль, на земле виден радиус взрыва (<see cref="CardAreaDisplay"/>).</summary>
        public bool TryGetOutline(out Vector3 centre, out float radius)
        {
            centre = transform.position;
            radius = numbers.radius;
            return fuse.Lit;
        }

        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();
            Gizmos.color = new Color(1, .45f, .1f, .5f);
            Gizmos.DrawWireSphere(Centre, numbers.radius);
        }
    }

    /// <summary>Числа взрыва одной бочки (по уровню карты), передаются бочке при появлении.</summary>
    public struct PowderBarrelBlast
    {
        /// <summary>Урон зверям, мимикам и союзникам, HP.</summary>
        public int damage;
        /// <summary>Урон игроку, половинки сердца.</summary>
        public int playerDamage;
        public float radius, knockback;
    }
}
