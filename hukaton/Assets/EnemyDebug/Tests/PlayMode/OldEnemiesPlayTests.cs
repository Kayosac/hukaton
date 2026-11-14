#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.EnemyDebug.Tests.PlayMode
{
    /// <summary>The refreshed forest creatures (docs/enemies/old-enemies-refresh.md), checked in the debug scene.</summary>
    public class OldEnemiesPlayTests
    {
        EnemyDebugSandbox sandbox;

        [UnitySetUp]
        public IEnumerator LoadSavedScene()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/EnemyDebug/Scenes/EnemyDebug.unity", new LoadSceneParameters(LoadSceneMode.Single));
            sandbox = Object.FindFirstObjectByType<EnemyDebugSandbox>();
            yield return Wait(.3f);
            Assert.That(sandbox.world.Running, Is.True);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (sandbox) sandbox.ClearLive();
            foreach (var puddle in Object.FindObjectsByType<EnemyPuddle>(FindObjectsSortMode.None)) Object.Destroy(puddle.gameObject);
            yield return null;
        }

        static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

        WorldCreature Spawn(string prefab)
        {
            var preview = sandbox.Previews.FirstOrDefault(p => p.source && p.source.name == prefab);
            Assert.That(preview, Is.Not.Null, $"the debug scene shows {prefab}");
            return sandbox.Spawn(preview);
        }

        IEnumerator Stand(WorldCreature creature, float distance)
        {
            // In front of it, where it looks: a calm one does not see behind itself.
            var dir = creature.transform.forward;
            dir.y = 0;
            var at = creature.Position + dir.normalized * distance;
            if (NavMesh.SamplePosition(at, out var hit, 2, NavMesh.AllAreas)) at = hit.position;
            sandbox.motor.Teleport(at + Vector3.up * .1f, Quaternion.LookRotation(creature.Position - at));
            yield return null;
        }

        Vector3 Player => sandbox.motor.transform.position;
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static bool Calm(WorldCreature c) => c.State == CreatureState.Idle || c.State == CreatureState.Wander || c.State == CreatureState.Graze;

        [UnityTest]
        public IEnumerator GoblinDogsComeAsAPackAndSpreadRoundThePlayer()
        {
            var leader = Spawn("Goblin dog");
            Assert.That(leader.maximumHealth, Is.EqualTo(4));
            Assert.That(leader.Squad, Is.Not.Null, "goblin dogs come in packs");
            var pack = leader.Squad.Members.ToList();
            yield return Stand(leader, 9);
            float widest = 0;
            float until = Time.time + 6;
            while (Time.time < until && widest < 90)
            {
                yield return null;
                var ways = pack.Where(d => d && d.Alive && !Calm(d)).Select(d => Flat(d.Position - Player)).Where(v => v.magnitude > .5f).ToList();
                for (int i = 0; i < ways.Count; i++)
                    for (int j = i + 1; j < ways.Count; j++) widest = Mathf.Max(widest, Vector3.Angle(ways[i], ways[j]));
            }
            Assert.That(widest, Is.GreaterThanOrEqualTo(90), "the pack comes at the player from different sides");
            Assert.That(pack.Count(d => !Calm(d)), Is.EqualTo(pack.Count), "one noticed — the whole pack attacks");
        }

        [UnityTest]
        public IEnumerator BuddingTrollDropsABudAtHalfAndAQuarter()
        {
            var troll = Spawn("Budding troll");
            Assert.That(troll.maximumHealth, Is.EqualTo(16));
            int Buds() => sandbox.world.creatures.Count(c => c && c.Alive && c.name.StartsWith("Budding troll bud"));
            troll.Damage(7, Vector3.zero, troll.Centre, 0);
            yield return null;
            Assert.That(Buds(), Is.EqualTo(0), "above half: no bud yet");
            troll.Damage(1, Vector3.zero, troll.Centre, 0);
            yield return null;
            Assert.That(Buds(), Is.EqualTo(1), "at half a bud falls off");
            troll.Damage(4, Vector3.zero, troll.Centre, 0);
            yield return null;
            Assert.That(Buds(), Is.EqualTo(2), "at a quarter another");
            var bud = sandbox.world.creatures.First(c => c && c.name.StartsWith("Budding troll bud"));
            Assert.That(bud.maximumHealth, Is.EqualTo(4));
            Assert.That(bud.view.height * bud.Size, Is.LessThan(troll.view.height * troll.Size * .5f), "a bud is small");
            Assert.That(Calm(bud), Is.False, "a bud goes for the player at once");
        }

        [UnityTest]
        public IEnumerator HostileBushWaitsTillThePlayerIsClose()
        {
            var bush = Spawn("Hostile bush");
            Assert.That(bush.ambush.enabled && bush.ambush.pounceRange <= 3, Is.True);
            var start = bush.Position;
            yield return Stand(bush, 6);
            yield return Wait(2);
            Assert.That(bush.Lurking, Is.True, "at six metres it still pretends to be a bush");
            Assert.That(Flat(bush.Position - start).magnitude, Is.LessThan(1));
            yield return Stand(bush, 2.4f);
            float until = Time.time + 4;
            while (Time.time < until && sandbox.ReceivedDamage < 2) yield return null;
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThanOrEqualTo(2), "close up it springs and hits for a heart");
        }

        [UnityTest]
        public IEnumerator RatPumpkinLiesStillLikeAPumpkin()
        {
            var rat = Spawn("Rat pumpkin");
            yield return Stand(rat, 5);
            yield return Wait(1.5f);
            Assert.That(rat.Lurking, Is.True, "at five metres it is just a pumpkin");
            yield return Stand(rat, 2.2f);
            float until = Time.time + 4;
            while (Time.time < until && sandbox.ReceivedDamage < 1) yield return null;
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThanOrEqualTo(1), "it jumps at the player");
        }

        [UnityTest]
        public IEnumerator HostileBushWearsAForestBushUntilItSprings()
        {
            var bush = Spawn("Hostile bush");
            yield return Stand(bush, 6);
            yield return Wait(.5f);
            Assert.That(bush.Disguised, Is.True, "lying in wait it is a bush");
            Assert.That(bush.view.gameObject.activeSelf, Is.False, "its drawing is hidden");
            var worn = bush.ambush.disguises.Single(d => d.activeSelf);
            Assert.That(worn.name, Does.StartWith("FA_VG0"), "it wears one of the forest's own bushes");
            Assert.That(worn.GetComponentsInChildren<Collider>().Length, Is.Zero, "the look has no body of its own");
            yield return Stand(bush, 2.4f);
            float until = Time.time + 3;
            while (Time.time < until && bush.Disguised) yield return null;
            Assert.That(bush.Disguised, Is.False, "close up it throws the disguise off");
            Assert.That(bush.view.gameObject.activeSelf, Is.True);
            Assert.That(worn.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator RatPumpkinLiesInAPatchOfRealPumpkins()
        {
            var rat = Spawn("Rat pumpkin");
            yield return Stand(rat, 6);
            yield return Wait(.5f);
            Assert.That(rat.Disguised, Is.True, "lying in wait it is a pumpkin");
            Assert.That(rat.view.gameObject.activeSelf, Is.False);
            var meshes = rat.ambush.disguises.Select(d => d.GetComponentInChildren<MeshFilter>(true).sharedMesh).ToList();
            Assert.That(meshes.Distinct().Count(), Is.EqualTo(3), "three kinds of pumpkin");
            int pumpkins = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Count(m => meshes.Contains(m.sharedMesh) && m.gameObject.activeInHierarchy);
            Assert.That(pumpkins, Is.GreaterThanOrEqualTo(4), "its own pumpkin among at least three real ones");
        }

        [UnityTest]
        public IEnumerator ClownVanishesAndDivesFromTheSide()
        {
            var clown = Spawn("Floating clown emoji");
            yield return Stand(clown, 9);
            float until = Time.time + 6;
            while (Time.time < until && !clown.Blinking) yield return null;
            Assert.That(clown.Blinking, Is.True, "it vanishes in a puff");
            Assert.That(clown.GetComponent<CapsuleCollider>().enabled, Is.False, "gone: nothing to hit");
            until = Time.time + 2;
            while (Time.time < until && clown.Blinking) yield return null;
            Assert.That(Flat(clown.Position - Player).magnitude, Is.LessThan(4), "it reappears right by the player");
            var look = Flat(sandbox.motor.transform.forward);
            Assert.That(Vector3.Angle(look, Flat(clown.Position - Player)), Is.GreaterThan(60), "beside or behind, not in front");
            until = Time.time + 3;
            while (Time.time < until && sandbox.ReceivedDamage < 1) yield return null;
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThanOrEqualTo(1), "and dives at once");
        }

        [UnityTest]
        public IEnumerator RoosterBatsCardsAwayInAFight()
        {
            var rooster = Spawn("Rooster baseball");
            yield return null;
            int calmHits = Enumerable.Range(0, 100).Count(_ => rooster.TryDeflectCard(rooster.Centre));
            Assert.That(calmHits, Is.EqualTo(0), "calm, it does not swing at cards");
            rooster.Damage(0, Vector3.zero, rooster.Centre, 0);
            yield return null;
            int hits = Enumerable.Range(0, 400).Count(_ => rooster.TryDeflectCard(rooster.Centre));
            Assert.That(hits, Is.InRange(100, 180), "in a fight about a third of the cards are batted away");
        }

        [UnityTest]
        public IEnumerator WormBurrowsAndComesBackWhenThePlayerLeaves()
        {
            var worm = Spawn("Kind little worm");
            yield return Stand(worm, 1.4f);
            float until = Time.time + 3;
            while (Time.time < until && !worm.Burrowed) yield return null;
            Assert.That(worm.Burrowed, Is.True, "it dives into the ground");
            yield return Wait(1);
            Assert.That(worm.GetComponent<CapsuleCollider>().enabled, Is.False, "under the ground it has no body to hit");
            worm.Damage(1, Vector3.zero, worm.Centre, 0);
            Assert.That(worm.Alive, Is.True, "nothing reaches it there");
            yield return Stand(worm, 20);
            until = Time.time + 6;
            while (Time.time < until && worm.Burrowed) yield return null;
            Assert.That(worm.Burrowed, Is.False, "it comes back out once the player has gone");
            yield return Wait(.6f);
            Assert.That(worm.GetComponent<CapsuleCollider>().enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator BurrowedWormPeeksOutWhenThePlayerStandsStill()
        {
            var worm = Spawn("Kind little worm");
            yield return Stand(worm, 1.4f);
            float until = Time.time + 3;
            while (Time.time < until && !worm.Burrowed) yield return null;
            Assert.That(worm.Burrowed, Is.True);
            until = Time.time + 6;
            while (Time.time < until && !worm.Peeking) yield return null;
            Assert.That(worm.Peeking, Is.True, "the player stands still: it peeks out to see whether he has gone");
            yield return Wait(.6f);
            Assert.That(worm.GetComponent<CapsuleCollider>().enabled, Is.True, "peeking, it can be hit");
        }

        [UnityTest]
        public IEnumerator WormsHaveTheirOwnNerve()
        {
            var a = Spawn("Kind little worm");
            yield return null;
            var distances = Enumerable.Range(0, 12).Select(_ => { a.ResetForOuting(null); return (float)typeof(WorldCreature).GetField("burrowNerve", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(a); }).ToList();
            Assert.That(distances.Max() - distances.Min(), Is.GreaterThan(2), "some dive at once, some only when the player is right by them");
        }

        [UnityTest]
        public IEnumerator ClownBlinksAwayFromACardAndFightsOn()
        {
            var clown = Spawn("Floating clown emoji");
            yield return Stand(clown, 7);
            clown.Damage(0, Vector3.zero, clown.Centre, 0);
            yield return Wait(.3f);
            var before = clown.Position;
            clown.DodgeCard((clown.Position - Player).normalized * 25);
            Assert.That(clown.Blinking, Is.True, "it vanishes as the card comes");
            Assert.That(clown.GetComponent<CapsuleCollider>().enabled, Is.False, "the card meets nothing");
            float until = Time.time + 2;
            while (Time.time < until && clown.Blinking) yield return null;
            Assert.That(clown.Blinking, Is.False);
            Assert.That(Flat(clown.Position - before).magnitude, Is.GreaterThan(2.5f), "back a few metres aside");
            Assert.That(clown.State, Is.EqualTo(CreatureState.Chase), "and the fight goes on");
        }

        [UnityTest]
        public IEnumerator MantisHopsAsideFromACard()
        {
            var mantis = Spawn("Mantis man");
            Assert.That(mantis.maximumHealth, Is.EqualTo(6));
            yield return Stand(mantis, 7);
            mantis.Damage(0, Vector3.zero, mantis.Centre, 0);
            yield return Wait(.2f);
            var before = mantis.Position;
            mantis.DodgeCard((mantis.Position - Player).normalized * 25);
            yield return Wait(.35f);
            var moved = Flat(mantis.Position - before);
            var across = Vector3.Cross(Vector3.up, Flat(mantis.Position - Player).normalized);
            Assert.That(Mathf.Abs(Vector3.Dot(moved, across)), Is.GreaterThan(1.5f), "a quick hop aside");
        }

        [UnityTest]
        public IEnumerator SpiderWebSpreadsAsItFlies()
        {
            var spider = Spawn("Giant swamp spider");
            yield return Stand(spider, 11);
            EnemyProjectile web = null;
            float until = Time.time + 8;
            while (Time.time < until && !web) { yield return null; web = EnemyProjectile.Active.FirstOrDefault(p => p && p.Shooter == spider); }
            Assert.That(web, Is.Not.Null);
            yield return Wait(.35f);
            Assert.That(web.Growth, Is.GreaterThan(1.4f), "the web opens out in flight");
        }

        [UnityTest]
        public IEnumerator GhostDivesRightThroughThePlayer()
        {
            var ghost = Spawn("Sheet ghost");
            yield return Stand(ghost, 8);
            var from = Flat(ghost.Position - Player).normalized;
            bool through = false;
            float until = Time.time + 8;
            while (Time.time < until && !through)
            {
                yield return null;
                if (Vector3.Dot(Flat(ghost.Position - Player), from) < -.8f) through = true;
            }
            Assert.That(through, Is.True, "its dive carries it through the player and out the other side");
        }

        [UnityTest]
        public IEnumerator FlyAgaricIsBigAndSpitsOften()
        {
            var agaric = Spawn("Raging fly agaric");
            Assert.That(agaric.size, Is.GreaterThanOrEqualTo(2));
            Assert.That(agaric.ranged.cooldown, Is.LessThanOrEqualTo(1.3f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator HeartsShowTheHitThenFillUp()
        {
            Assert.That(sandbox.world.hud, Is.Not.Null, "the expedition HUD with its hearts is in the debug scene");
            var vitals = sandbox.world.vitals;
            int full = vitals.MaximumHalfHearts;
            vitals.Hurt(1, Vector3.zero, null);
            yield return null;
            Assert.That(vitals.HalfHearts, Is.EqualTo(full - 1), "the hit stays on the hearts for a while");
            yield return Wait(sandbox.refillSeconds + .5f);
            Assert.That(vitals.HalfHearts, Is.EqualTo(full), "then they fill up again");
        }

        [UnityTest]
        public IEnumerator LiveCopyAppearsInFrontOfItsPreview()
        {
            var preview = sandbox.Previews.First(p => p.source && p.source.name == "Budding troll");
            var troll = sandbox.Spawn(preview);
            yield return null;
            Assert.That(Flat(troll.Position - preview.transform.position).magnitude, Is.GreaterThan(1.5f), "not inside the drawing");
        }

        [UnityTest]
        public IEnumerator DisguisedBushAppearsAsABushAmongBushes()
        {
            var bush = Spawn("Hostile bush");
            yield return Wait(.3f);
            Assert.That(bush.Disguised, Is.True, "it appears in disguise");
            int looks = sandbox.liveRoot.GetComponentsInChildren<Transform>().Count(t => t.parent == sandbox.liveRoot && t.name.StartsWith("FA_VG0"));
            Assert.That(looks, Is.GreaterThanOrEqualTo(3), "among real bushes, to judge the disguise");
        }

        [UnityTest]
        public IEnumerator GrippersSnapOntoTheScreenWhileTheyHold()
        {
            foreach (var name in new[] { "Swamp mutant", "Giant swamp leech" })
            {
                sandbox.ClearLive();
                yield return null;
                var gripper = Spawn(name);
                var picture = gripper.grab.screenPicture;
                Assert.That(picture, Is.Not.Null, name + " has a picture for the player's screen");
                Assert.That(picture.gameObject.activeSelf, Is.False, "nothing on the screen before the grip");
                yield return Stand(gripper, 2);
                float until = Time.time + 6;
                while (Time.time < until && !gripper.Holding) yield return null;
                Assert.That(gripper.Holding, Is.True, name + " grabs");
                yield return Wait(.2f);
                Assert.That(picture.gameObject.activeSelf, Is.True, "up close on the screen while it holds");
                Assert.That(gripper.view.spriteRenderer.enabled, Is.False, "its drawing in the world is hidden meanwhile");
                var eye = sandbox.world.viewCamera.transform;
                Assert.That(Vector3.Distance(picture.transform.position, eye.position), Is.LessThan(1), "right in front of the eyes");
                until = Time.time + 4;
                while (Time.time < until && gripper.Holding) yield return null;
                yield return Wait(.3f);
                Assert.That(picture.gameObject.activeSelf, Is.False, "gone when it lets go");
                Assert.That(gripper.view.spriteRenderer.enabled, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator SnailShowsItsHiddenDrawingInTheShell()
        {
            var snail = Spawn("Snail frog mount");
            Assert.That(snail.shellPicture, Is.Not.Null);
            yield return null;
            snail.Damage(1, Vector3.zero, snail.Centre, 0);
            yield return null;
            yield return null;
            Assert.That(snail.view.spriteRenderer.sprite, Is.SameAs(snail.shellPicture), "hidden in the shell, the frog peeking out");
            yield return Wait(2.3f);
            Assert.That(snail.view.spriteRenderer.sprite, Is.Not.SameAs(snail.shellPicture));
        }

        [UnityTest]
        public IEnumerator LeshyRidesAtTheCameraWhenItRams()
        {
            var leshy = Spawn("Teen leshy skater");
            Assert.That(leshy.ramPicture, Is.Not.Null);
            yield return Stand(leshy, 9);
            leshy.Damage(0, Vector3.zero, leshy.Centre, 0);
            bool rode = false;
            float until = Time.time + 6;
            while (Time.time < until && !rode) { yield return null; rode = leshy.State == CreatureState.Strike && leshy.view.spriteRenderer.sprite == leshy.ramPicture; }
            Assert.That(rode, Is.True, "it rides at the camera on its board");
        }

        [UnityTest]
        public IEnumerator BudIsTheSmallTroll()
        {
            var bud = Spawn("Budding troll bud");
            yield return null;
            Assert.That(bud.view.idle.name, Does.StartWith("BuddingTrollSmall"), "its own drawing, not a shrunk grown troll");
            var troll = sandbox.Previews.First(p => p.source && p.source.name == "Budding troll").source;
            Assert.That(bud.view.height * bud.size, Is.InRange(troll.view.height * troll.size / 4, troll.view.height * troll.size / 2), "a third of the grown one");
        }

        [UnityTest]
        public IEnumerator SnailHidesInItsShellAfterAHit()
        {
            var snail = Spawn("Snail frog mount");
            Assert.That(snail.maximumHealth, Is.EqualTo(4));
            yield return null;
            snail.Damage(1, Vector3.zero, snail.Centre, 0);
            Assert.That(snail.Health, Is.EqualTo(3));
            Assert.That(snail.InShell, Is.True, "a hit sends it into the shell");
            snail.Damage(1, Vector3.zero, snail.Centre, 0);
            Assert.That(snail.Health, Is.EqualTo(3), "in the shell nothing hurts it");
            yield return Wait(2.2f);
            Assert.That(snail.InShell, Is.False, "it comes out after two seconds");
            snail.Damage(1, Vector3.zero, snail.Centre, 0);
            Assert.That(snail.Health, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator LeshyRamsRightThroughThePlayer()
        {
            var leshy = Spawn("Teen leshy skater");
            yield return Stand(leshy, 9);
            leshy.Damage(0, Vector3.zero, leshy.Centre, 0);
            var from = Flat(leshy.Position - Player).normalized;
            bool through = false;
            float until = Time.time + 6;
            while (Time.time < until && !(through && sandbox.ReceivedDamage >= 2))
            {
                yield return null;
                if (Vector3.Dot(Flat(leshy.Position - Player), from) < -1) through = true;
            }
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThanOrEqualTo(2), "the ram hits for a heart");
            Assert.That(through, Is.True, "it runs on past the player");
        }

        [UnityTest]
        public IEnumerator DwarfThrowsMugsThatLeavePuddles()
        {
            var dwarf = Spawn("Drunken dwarf robot");
            Assert.That(dwarf.maximumHealth, Is.EqualTo(20));
            Assert.That(dwarf.staggerAngle, Is.GreaterThan(0), "it staggers");
            yield return Stand(dwarf, 9);
            float until = Time.time + 8;
            while (Time.time < until && !Object.FindObjectsByType<EnemyPuddle>(FindObjectsSortMode.None).Any()) yield return null;
            Assert.That(Object.FindObjectsByType<EnemyPuddle>(FindObjectsSortMode.None).Any(), Is.True, "a thrown mug leaves a puddle");
        }

        [UnityTest]
        public IEnumerator BeesStayCalmByTheBeekeeperUntilHeIsHurt()
        {
            var keeper = Spawn("Zombie beekeeper");
            var bee = Spawn("Bee swarm");
            yield return null;
            bee.Resume(bee.Health, keeper.Position + Vector3.right * 2.5f);
            yield return Stand(keeper, 6);
            yield return Wait(2.5f);
            Assert.That(Calm(bee), Is.True, "by the beekeeper the bees leave the player be");
            keeper.Damage(1, Vector3.zero, keeper.Centre, 0);
            yield return Wait(.3f);
            Assert.That(Calm(bee), Is.False, "hurt him and the bees go for the player");
        }

        [UnityTest]
        public IEnumerator BeeMutantCallsTwoSwarmsAtHalfHealth()
        {
            var mutant = Spawn("Enraged bee mutant");
            int Swarms() => sandbox.world.creatures.Count(c => c && c.Alive && c.name.StartsWith("Bee swarm"));
            int before = Swarms();
            mutant.Damage(7, Vector3.zero, mutant.Centre, 0);
            yield return null;
            Assert.That(Swarms(), Is.EqualTo(before + 2), "at half health it calls two swarms");
            Assert.That(sandbox.world.creatures.First(c => c && c.name.StartsWith("Bee swarm")).maximumHealth, Is.EqualTo(1));
        }
    }
}
#endif
