#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.EnemyDebug.Tests.PlayMode
{
    /// <summary>
    /// Новые атакующие карты на настоящих бойцах отладочной сцены: своя бочка и свой Камнепад ранят игрока его числом
    /// (половинки сердца), без горения, а в Пузыре — никак; вороны прерывают атаку настоящего зверя, а улетев,
    /// отпускают его охотиться; Струя прямым попаданием ранит и мочит, всплеском только мочит, мочит бочку и гасит
    /// смолу; горящая смола поджигает всё горючее рядом (настоящий SpreadFire); бочку поджигают горящий зверь вплотную
    /// и горящая трава, она ранит союзников, мокнет в воде и забывает фитиль, когда её прячут с чанком.
    /// </summary>
    public class AttackCardsPlayTests
    {
        EnemyDebugSandbox sandbox;
        ExpeditionWorld world;
        readonly List<GameObject> made = new List<GameObject>();
        // Пустое место арены (плоскость 160×160 м), вдали от витрины зверей (x −15…15, z 0…70).
        static readonly Vector3 Spot = new Vector3(50, 0, -30);

        [UnitySetUp]
        public IEnumerator LoadSavedScene()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/EnemyDebug/Scenes/EnemyDebug.unity", new LoadSceneParameters(LoadSceneMode.Single));
            sandbox = Object.FindFirstObjectByType<EnemyDebugSandbox>();
            yield return Wait(.3f);
            world = sandbox.world;
            Assert.That(world.Running, Is.True);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var go in made) if (go) Object.Destroy(go);
            made.Clear();
            if (sandbox) sandbox.ClearLive();
            yield return null;
        }

        static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

        static DeckPrototypeConfig Config => AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");

        static T Effect<T>(string id) where T : CardEffect
        {
            var effect = Config.Find(id).effectAsset as T;
            Assert.That(effect, Is.Not.Null, id);
            return effect;
        }

        ImpactContext Hit(string id, int level, Collider target, Vector3 point, Vector3 normal, Vector3 velocity)
        {
            var definition = Config.Find(id);
            var copy = new Expedition(new CardCopy[0], 3, 1).GrantToHand(definition.ToSpec(), level);
            return new ImpactContext(world, copy, definition, target, point, normal, velocity, world.motor.transform.position);
        }

        Vector3 PlayerAt(Vector3 at)
        {
            sandbox.motor.Teleport(at + Vector3.up * .05f, Quaternion.identity);
            return at;
        }

        Vector3 PlayerFeet => sandbox.motor.transform.position;

        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }

        WorldCreature Beast(string name, Vector3 at)
        {
            var preview = sandbox.Previews.FirstOrDefault(p => p.source && p.source.name == name);
            Assert.That(preview, Is.Not.Null, name);
            var creature = sandbox.Spawn(preview);
            creature.Resume(creature.maximumHealth, at);
            return creature;
        }

        static Collider Body(Component creature) => creature.GetComponentsInChildren<Collider>().First(c => !c.isTrigger && c.enabled);

        static Collider Ground(Vector3 at)
        {
            Assert.That(Physics.Raycast(at + Vector3.up * 3, Vector3.down, out var hit, 10, ~(1 << 2), QueryTriggerInteraction.Ignore), Is.True);
            return hit.collider;
        }

        PowderBarrel Barrel(Vector3 at, int level = 1)
        {
            var effect = Effect<PowderBarrelEffect>("powder_barrel");
            var barrel = world.SpawnForOuting(effect.barrelPrefab, at + Vector3.up * .02f, Quaternion.identity);
            barrel.Setup(new PowderBarrelBlast
            {
                damage = effect.damage.At(level), playerDamage = effect.playerDamage.At(level),
                radius = effect.radius.At(level), knockback = effect.knockback.At(level)
            });
            made.Add(barrel.gameObject);
            return barrel;
        }

        TarPuddleZone Tar(Vector3 at, float radius)
        {
            var effect = Effect<TarPuddleEffect>("tar_puddle");
            var tar = world.SpawnForOuting(effect.puddlePrefab, at, Quaternion.identity);
            tar.Setup(radius, effect.speedMultiplier, effect.burnSeconds, effect.burnDamagePerSecond);
            made.Add(tar.gameObject);
            return tar;
        }

        ScarecrowDecoy Scarecrow(Vector3 at)
        {
            var effect = Effect<ScarecrowEffect>("scarecrow");
            var decoy = world.SpawnForOuting(effect.scarecrowPrefab, at, Quaternion.identity);
            var back = world.hand ? world.hand.cardBack : null;
            decoy.Setup(back, back, 1, Color.white, effect.Numbers(1));
            made.Add(decoy.gameObject);
            return decoy;
        }

        BubbleDome Bubble(Vector3 at)
        {
            var dome = world.SpawnForOuting(Effect<BubbleEffect>("bubble").domePrefab, at, Quaternion.identity);
            dome.Setup(20, 3);
            made.Add(dome.gameObject);
            return dome;
        }

        RockfallZone Rockfall(Vector3 at, int level)
        {
            var effect = Effect<RockfallEffect>("rockfall");
            var zone = world.SpawnForOuting(effect.zonePrefab, at, Quaternion.identity);
            zone.Setup(new RockfallStrike
            {
                damage = effect.damage.At(level), playerDamage = effect.playerDamage.At(level), radius = effect.radius.At(level),
                // Обвал зовёт сам тест (HitEveryoneInside): срок далеко, чтобы зона не упала сама.
                delay = 60, smallStunSeconds = effect.smallStunSeconds, smallMaxMass = effect.smallMaxMass,
                hurtAllies = effect.hurtAllies, knockback = effect.knockback, lift = effect.lift
            });
            made.Add(zone.gameObject);
            return zone;
        }

        // ---------------- Пороховая бочка ----------------

        [UnityTest]
        public IEnumerator OwnBarrelHurtsThePlayerByHisOwnHalfHeartsAndNeverSetsHimAlight()
        {
            foreach (var (level, expected) in new[] { (1, 2), (3, 4) })
            {
                PlayerAt(Spot);
                var barrel = Barrel(Spot + new Vector3(1.4f, 0, 0), level);
                yield return Wait(1);
                int before = sandbox.ReceivedDamage;
                Vector3 start = PlayerFeet;
                Assert.That(barrel.Ignite(IgnitionSource.Fire, barrel.Centre), Is.True);
                yield return Wait(barrel.fuseSeconds + .5f);
                Assert.That(barrel == null, Is.True, "взорвалась");
                Assert.That(sandbox.ReceivedDamage - before, Is.EqualTo(expected), $"уровень {level}: урон игроку — его число в половинках сердца");
                Assert.That(world.statuses.Has(PlayerStatusKind.Burn), Is.False, "взрыв не поджигает игрока");
                Assert.That(Flat(PlayerFeet, start), Is.GreaterThan(.5f), "но отбрасывает");
                yield return Wait(1);
            }
        }

        [UnityTest]
        public IEnumerator AChainOfBarrelsHurtsAndThrowsThePlayerOnce()
        {
            PlayerAt(Spot);
            var first = Barrel(Spot + new Vector3(1.4f, 0, 0));
            var second = Barrel(Spot + new Vector3(1.4f, 0, 1.2f));
            yield return Wait(1);
            int before = sandbox.ReceivedDamage;
            first.Ignite(IgnitionSource.Fire, first.Centre);
            yield return Wait(first.fuseSeconds + second.chainDelay + .5f);
            Assert.That(first == null && second == null, Is.True, "цепь: обе взорвались");
            Assert.That(sandbox.ReceivedDamage - before, Is.EqualTo(2), "второй взрыв попал в защиту после удара: ни урона, ни второго толчка");
        }

        [UnityTest]
        public IEnumerator InsideTheBubbleThePlayerIsSafeFromHisOwnBarrelAndRockfall()
        {
            PlayerAt(Spot);
            var dome = Bubble(Spot);
            var barrel = Barrel(Spot + new Vector3(1.4f, 0, 0), 3);
            yield return Wait(1);
            Assert.That(dome.ShieldsPlayer(PlayerFeet), Is.True);
            int before = sandbox.ReceivedDamage;
            Vector3 start = PlayerFeet;
            barrel.Ignite(IgnitionSource.Fire, barrel.Centre);
            yield return Wait(barrel.fuseSeconds + .5f);
            Assert.That(barrel == null, Is.True, "бочка взорвалась");
            Assert.That(sandbox.ReceivedDamage, Is.EqualTo(before), "в Пузыре своя бочка не ранит");
            Assert.That(Flat(PlayerFeet, start), Is.LessThan(.15f), "и не отбрасывает");

            var zone = Rockfall(Spot, 3);
            Assert.That(zone.HitEveryoneInside(zone.transform.position), Is.False, "в зоне только игрок, и тот в Пузыре");
            yield return null;
            Assert.That(sandbox.ReceivedDamage, Is.EqualTo(before), "свой Камнепад в Пузыре не ранит");
        }

        [UnityTest]
        public IEnumerator BlastHurtsAndSetsAlightAlliesAndBeasts()
        {
            PlayerAt(Spot + new Vector3(0, 0, -12));
            var scarecrow = Scarecrow(Spot + new Vector3(1.5f, 0, 0));
            var dog = Beast("Goblin dog", Spot + new Vector3(-1.5f, 0, 0));
            dog.Distract(30);
            var barrel = Barrel(Spot);
            yield return Wait(.5f);
            int ally = scarecrow.Health, beast = dog.Health;
            barrel.Ignite(IgnitionSource.Fire, barrel.Centre);
            yield return Wait(barrel.fuseSeconds + .3f);
            Assert.That(scarecrow.Health, Is.LessThan(ally), "порох не разбирает сторон: союзник ранен");
            Assert.That(scarecrow.Burning, Is.True, "и горит");
            Assert.That(beast - dog.Health, Is.GreaterThanOrEqualTo(5), "зверь — звериным числом (5 на уровне I, уязвимому к огню — больше)");
            Assert.That(dog.Burning, Is.True, "и горит");
        }

        [UnityTest]
        public IEnumerator ABurningBeastRightBesideTheBarrelSetsItOff()
        {
            PlayerAt(Spot + new Vector3(0, 0, -12));
            var barrel = Barrel(Spot);
            var dog = Beast("Goblin dog", Spot + new Vector3(4, 0, 0));
            dog.Distract(30);
            dog.Damage(0, Vector3.zero, dog.Centre, 8);
            yield return Wait(.5f);
            Assert.That(dog.Burning, Is.True);
            Assert.That(barrel.Lit, Is.False, "горящий зверь в 4 м не поджигает");
            // Вплотную: бочка сама проверяет горящих зверей рядом (TouchedByBurningCreature).
            float gap = barrel.burningTouchReach * .5f + dog.CapsuleRadius + barrel.Bounds.extents.x;
            dog.Resume(dog.Health, Spot + new Vector3(gap, 0, 0));
            dog.Distract(30);
            dog.Damage(0, Vector3.zero, dog.Centre, 8);
            float until = Time.time + 1;
            while (Time.time < until && barrel && !barrel.Lit) yield return null;
            Assert.That(!barrel || barrel.Lit, Is.True, "горящий зверь вплотную поджёг порох");
        }

        [UnityTest]
        public IEnumerator HiddenWithItsChunkTheBarrelForgetsItsFuse()
        {
            PlayerAt(Spot + new Vector3(0, 0, -12));
            var barrel = Barrel(Spot);
            yield return Wait(.3f);
            barrel.Ignite(IgnitionSource.Fire, barrel.Centre);
            yield return null;
            // Как WorldStreamer.HideLoose: чанк выгрузился — бочка спрятана.
            barrel.gameObject.SetActive(false);
            Assert.That(barrel.State, Is.EqualTo(BarrelFuseState.Idle), "фитиль погас");
            yield return Wait(barrel.fuseSeconds + .5f);
            barrel.gameObject.SetActive(true);
            yield return Wait(.3f);
            Assert.That(barrel != null, Is.True, "вернулась целой, а не взорвалась сразу");
            Assert.That(barrel.State, Is.EqualTo(BarrelFuseState.Idle));
            Assert.That(world.Zones.All.Contains(barrel), Is.True, "снова зона: огонь её найдёт");
            Assert.That(barrel.Ignite(IgnitionSource.Fire, barrel.Centre), Is.True, "и снова поджигается");
            yield return Wait(barrel.fuseSeconds + .3f);
            Assert.That(barrel == null, Is.True);
        }

        [UnityTest]
        public IEnumerator BarrelInLakeWaterGetsWetAndItsFuseGoesOut()
        {
            PlayerAt(Spot + new Vector3(0, 0, -12));
            Lake(Spot, 6, 1);
            var dry = Barrel(Spot + new Vector3(8, 0, 0));
            var standing = Barrel(Spot + new Vector3(-1, 0, 0));
            var lit = Barrel(Spot + new Vector3(16, 0, 0));
            yield return Wait(.6f);
            Assert.That(standing.Wet, Is.True, "стоит на дне под поверхностью — мокнет, как бойцы в воде");
            Assert.That(dry.Wet, Is.False, "рядом с озером — сухая");
            Assert.That(standing.Ignite(IgnitionSource.Fire, standing.Centre), Is.False, "мокрая не горит");
            // Фитиль уже горит, а бочка в воде (под ней появилось озеро): вода гасит фитиль.
            Assert.That(lit.Ignite(IgnitionSource.Fire, lit.Centre), Is.True);
            var pond = Lake(lit.transform.position, 2, 1);
            pond.SoakWaders();
            Assert.That(lit.State, Is.EqualTo(BarrelFuseState.Idle), "вода гасит горящий фитиль");
            Assert.That(lit.Wet, Is.True);
        }

        /// <summary>Озеро без коллайдеров: поверхность воды на высоте <paramref name="level"/>, квадрат стороной <paramref name="size"/>.</summary>
        WorldObstacle Lake(Vector3 centre, float size, float level)
        {
            var go = new GameObject("Test lake");
            made.Add(go);
            go.transform.position = centre;
            var water = new GameObject("Water");
            water.transform.SetParent(go.transform, false);
            float h = size * .5f;
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(-h, level, -h), new Vector3(-h, level, h), new Vector3(h, level, h), new Vector3(h, level, -h) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            water.AddComponent<MeshFilter>().sharedMesh = mesh;
            water.AddComponent<MeshRenderer>();
            var lake = go.AddComponent<WorldObstacle>();
            lake.kind = ObstacleKind.Lake;
            lake.intact = water;
            lake.world = world;
            return lake;
        }

        [UnityTest]
        public IEnumerator BurningGrassAndABurningThicketSetTheBarrelOff()
        {
            PlayerAt(Spot + new Vector3(0, 0, -12));
            // Горящая трава (Burnable поля BurnableField): огонь доходит до бочки вплотную.
            var field = new GameObject("Test burnable field").AddComponent<Hakaton.OldApiary.BurnableField>();
            made.Add(field.gameObject);
            var grass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            made.Add(grass);
            grass.name = "Test grass";
            grass.transform.position = Spot + new Vector3(0, .3f, 0);
            grass.transform.localScale = new Vector3(.6f, .6f, .6f);
            var burnable = grass.AddComponent<Hakaton.OldApiary.Burnable>();
            burnable.field = field;
            burnable.seconds = .6f;
            field.burnables = new[] { burnable };
            field.world = world;
            var barrel = Barrel(Spot + new Vector3(.3f + .34f + .1f, 0, 0));
            var far = Barrel(Spot + new Vector3(6, 0, 0));
            yield return Wait(.3f);
            Assert.That(barrel.Lit, Is.False);
            Assert.That(field.Ignite(burnable, grass.transform.position), Is.True, "трава загорелась");
            bool lit = false;
            float until = Time.time + 1;
            while (Time.time < until && !lit) { lit = !barrel || barrel.Lit; yield return null; }
            Assert.That(lit, Is.True, "горящая трава подожгла бочку");
            Assert.That(far && !far.Lit, Is.True, "дальнюю — нет");

            // Сгорающие колючие заросли.
            Vector3 at = Spot + new Vector3(-10, 0, 0);
            var thicket = new GameObject("Test thicket");
            made.Add(thicket);
            thicket.transform.position = at;
            var intact = GameObject.CreatePrimitive(PrimitiveType.Cube);
            intact.transform.SetParent(thicket.transform, false);
            intact.transform.localScale = new Vector3(3, 1.2f, .6f);
            intact.transform.localPosition = Vector3.up * .6f;
            var thorns = thicket.AddComponent<WorldObstacle>();
            thorns.kind = ObstacleKind.Thorns;
            thorns.intact = intact;
            thorns.barrier = intact.GetComponent<Collider>();
            thorns.world = world;
            var byThorns = Barrel(at + new Vector3(1, 0, .3f + .34f + .15f));
            var farThorns = Barrel(at + new Vector3(0, 0, 4));
            yield return Wait(.3f);
            Assert.That(thorns.React(CardAction.Fire, at), Is.True, "заросли загорелись");
            Assert.That(byThorns.Lit, Is.True, "сгорающие заросли подожгли бочку рядом");
            Assert.That(farThorns.Lit, Is.False);
            yield return Wait(1.5f);
        }

        // ---------------- горящая смола ----------------

        [UnityTest]
        public IEnumerator BurningTarSetsAlightEverythingThatBurnsBesideIt()
        {
            PlayerAt(Spot + new Vector3(0, 0, -14));
            const float r = 1;
            // Каждая цель — у своей лужи, лужи далеко друг от друга: поджигает только SpreadFire своей лужи.
            Vector3 a = Spot + new Vector3(-24, 0, 10), b = Spot + new Vector3(-12, 0, 10), c = Spot + new Vector3(0, 0, 10),
                d = Spot + new Vector3(12, 0, 10), e = Spot + new Vector3(24, 0, 10);
            var tarA = Tar(a, r);
            var bridgeEffect = Effect<BranchBridgeEffect>("branch_bridge");
            var bridge = world.SpawnForOuting(bridgeEffect.branchPrefab, a + new Vector3(r + .1f, .5f, 0), Quaternion.LookRotation(Vector3.right));
            made.Add(bridge.gameObject);
            bridge.Grow(3, bridgeEffect.width, bridgeEffect.thickness);
            var tarB = Tar(b, r);
            // Вне огня (ноги дальше радиуса лужи), но вплотную к нему: зажжёт только перенос огня.
            var scarecrow = Scarecrow(b + new Vector3(r + .25f, 0, 0));
            var tarC = Tar(c, r);
            var vent = new GameObject("Test gas vent").AddComponent<GasVent>();
            made.Add(vent.gameObject);
            vent.transform.position = c + new Vector3(r + .45f, 0, 0);
            vent.radius = .4f;
            vent.height = 1.5f;
            vent.OnSpawned(world);
            var tarD = Tar(d, r);
            var neighbour = Tar(d + new Vector3(2 * r + .2f, 0, 0), r);
            var tarE = Tar(e, r);
            var barrel = Barrel(e + new Vector3(r + .3f, 0, 0));
            yield return Wait(.6f);
            Assert.That(bridge.Burning || scarecrow.Burning || !vent.Full || neighbour.Burning || barrel.Lit, Is.False, "пока смола не горит — ничего не горит");
            foreach (var tar in new[] { tarA, tarB, tarC, tarD, tarE })
                Assert.That(tar.Ignite(IgnitionSource.Fire, tar.transform.position), Is.True);
            bool barrelLit = false;
            float until = Time.time + .6f;
            while (Time.time < until)
            {
                if (barrel && barrel.Lit) barrelLit = true;
                yield return null;
            }
            Assert.That(bridge.Burning, Is.True, "ветвь-мост");
            Assert.That(scarecrow.Burning || !scarecrow.Alive, Is.True, "Чучело");
            Assert.That(vent.Full, Is.False, "болотный газ вспыхнул");
            Assert.That(neighbour.Burning, Is.True, "соседняя лужа");
            Assert.That(barrelLit || !barrel, Is.True, "пороховая бочка");
        }

        // ---------------- Вороньё ----------------

        static bool Striking(WorldCreature creature) => creature.State == CreatureState.Windup || creature.State == CreatureState.Strike;

        static bool Hunting(WorldCreature creature) => Striking(creature) || creature.State == CreatureState.Chase;

        [UnityTest]
        public IEnumerator CrowsBreakOffARealBeastsAttackAndLetItHuntAgain()
        {
            // Пёс у своего дома (там, где его ставит отладочная сцена): вдали от дома он бы шёл домой, а не нападал.
            var preview = sandbox.Previews.First(p => p.source && p.source.name == "Goblin dog");
            var dog = sandbox.Spawn(preview);
            Vector3 front = dog.Position + Vector3.back * 3.5f;
            sandbox.motor.Teleport(front + Vector3.up * .05f, Quaternion.LookRotation(dog.Position - front));
            float until = Time.time + 8;
            while (Time.time < until && !Striking(dog)) yield return null;
            Assert.That(Striking(dog), Is.True, $"пёс нападает: {dog.State}");

            Effect<CrowsEffect>("crows").Apply(Hit("crows", 1, Body(dog), dog.Centre, Vector3.back, Vector3.forward * 12));
            var flock = CrowFlock.On(dog);
            Assert.That(flock, Is.Not.Null, "вороны сели на пса");
            yield return null;
            yield return null;
            Assert.That(dog.Distracted, Is.True);
            Assert.That(dog.State, Is.EqualTo(CreatureState.Recover), "атака прервана");
            int before = sandbox.ReceivedDamage, health = dog.Health;
            bool struck = false;
            while (Time.time < flock.EndAt - .05f)
            {
                if (Striking(dog)) struck = true;
                yield return null;
            }
            Assert.That(struck, Is.False, "пока клюют — ни замаха, ни удара");
            Assert.That(sandbox.ReceivedDamage, Is.EqualTo(before), "игрок цел");
            yield return Wait(.4f);
            Assert.That(dog.Health, Is.EqualTo(health - 4), "клевки: 1 HP в секунду, 4 с");
            Assert.That(dog.Distracted, Is.False, "вороны улетели — отвлечение снято");
            until = Time.time + 6;
            while (Time.time < until && !Hunting(dog)) yield return null;
            Assert.That(Hunting(dog), Is.True, $"и пёс снова охотится: {dog.State}");
        }

        // ---------------- Струя ----------------

        [UnityTest]
        public IEnumerator WaterJetWoundsAndWetsOnADirectHitAndOnlyWetsWithASplash()
        {
            PlayerAt(Spot + new Vector3(0, 0, -8));
            var effect = Effect<WaterJetEffect>("water_jet");
            var dog = Beast("Goblin dog", Spot);
            dog.Distract(30);
            var other = Beast("Purple mushroom", Spot + new Vector3(5, 0, 0));
            yield return Wait(.3f);
            int health = dog.Health;
            effect.Apply(Hit("water_jet", 1, Body(dog), dog.Centre, Vector3.back, Vector3.forward * 12));
            Assert.That(dog.Health, Is.EqualTo(health - effect.damage.At(1)), "прямое попадание ранит (2 на уровне I)");
            Assert.That(WetStatus.IsWet(dog), Is.True, "и мочит");
            int otherHealth = other.Health;
            Vector3 beside = other.Position + new Vector3(.5f, .02f, 0);
            effect.Apply(Hit("water_jet", 1, Ground(beside), beside, Vector3.up, Vector3.down * 8));
            Assert.That(WetStatus.IsWet(other), Is.True, "всплеск мочит стоящего рядом");
            Assert.That(other.Health, Is.EqualTo(otherHealth), "но не ранит");
        }

        [UnityTest]
        public IEnumerator WaterJetSoaksTheBarrelAndPutsOutBurningTar()
        {
            PlayerAt(Spot + new Vector3(0, 0, -8));
            var effect = Effect<WaterJetEffect>("water_jet");
            var barrel = Barrel(Spot);
            var lit = Barrel(Spot + new Vector3(6, 0, 0));
            var tar = Tar(Spot + new Vector3(-6, 0, 0), 1.2f);
            yield return Wait(.4f);
            effect.Apply(Hit("water_jet", 1, barrel.solid, barrel.Centre, Vector3.back, Vector3.forward * 12));
            Assert.That(barrel.Wet, Is.True, "прямое попадание мочит бочку");
            Assert.That(barrel.Ignite(IgnitionSource.Fire, barrel.Centre), Is.False, "мокрая не горит");
            Assert.That(lit.Ignite(IgnitionSource.Fire, lit.Centre), Is.True);
            Vector3 beside = lit.transform.position + new Vector3(-.6f, .02f, 0);
            effect.Apply(Hit("water_jet", 1, Ground(beside), beside, Vector3.up, Vector3.down * 8));
            Assert.That(lit.State, Is.EqualTo(BarrelFuseState.Idle), "всплеск рядом гасит горящий фитиль");
            Assert.That(lit.Wet, Is.True);
            Assert.That(tar.Ignite(IgnitionSource.Fire, tar.transform.position), Is.True);
            Assert.That(tar.Burning, Is.True);
            Vector3 middle = tar.transform.position + Vector3.up * .02f;
            effect.Apply(Hit("water_jet", 1, Ground(middle), middle, Vector3.up, Vector3.down * 8));
            Assert.That(tar.Burning, Is.False, "всплеск гасит горящую смолу");
            Assert.That(tar.Phase, Is.EqualTo(TarPhase.Tar), "смола остаётся и снова поджигается");
            yield return null;
        }

        // ---------------- Камнепад ----------------

        [UnityTest]
        public IEnumerator RockfallHurtsThePlayerByHisNumberAndKnocksDownOnlySmallBeasts()
        {
            PlayerAt(Spot);
            var mushroom = Beast("Purple mushroom", Spot + new Vector3(1.5f, 0, 1));
            var dog = Beast("Goblin dog", Spot + new Vector3(-1.5f, 0, 1));
            dog.Distract(30);
            yield return null;
            Assert.That(mushroom.body.mass, Is.LessThanOrEqualTo(1), "гриб — мелкий");
            Assert.That(dog.body.mass, Is.GreaterThan(1), "пёс — нет");
            int before = sandbox.ReceivedDamage, small = mushroom.Health, big = dog.Health;
            var zone = Rockfall(Spot, 1);
            Assert.That(zone.HitEveryoneInside(zone.transform.position), Is.True);
            yield return null;
            yield return null;
            Assert.That(sandbox.ReceivedDamage - before, Is.EqualTo(1), "уровень I: игроку — 1 половинка сердца");
            Assert.That(mushroom.Health, Is.EqualTo(Mathf.Max(0, small - 3)), "зверям — 3");
            Assert.That(dog.Health, Is.EqualTo(big - 3));
            if (mushroom.Alive) Assert.That(mushroom.State, Is.EqualTo(CreatureState.Stunned), "мелкий сбит с ног");
            Assert.That(dog.State, Is.Not.EqualTo(CreatureState.Stunned), "крупный — нет");
            yield return Wait(1);
            before = sandbox.ReceivedDamage;
            var heavy = Rockfall(Spot, 3);
            heavy.HitEveryoneInside(heavy.transform.position);
            yield return null;
            Assert.That(sandbox.ReceivedDamage - before, Is.EqualTo(3), "уровень III: 3 половинки");
        }
    }
}
#endif
