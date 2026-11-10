using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Creatures' projectiles: the flight passes through the aim point, the shot check sees obstacles, the shooters are set up.</summary>
    public sealed class EnemyProjectileTests
    {
        static Vector3 Simulate(Vector3 from, Vector3 velocity, float gravity, float seconds, float step)
        {
            Vector3 position = from;
            int steps = Mathf.RoundToInt(seconds / step);
            for (int i = 0; i < steps; i++) EnemyProjectileFlight.Advance(ref position, ref velocity, gravity, step);
            return position;
        }

        [Test]
        public void StraightShotFliesALineThroughTheAimPointAtItsSpeed()
        {
            Vector3 from = new Vector3(0, 1.5f, 0), to = new Vector3(3, 1, 9);
            EnemyProjectileFlight.Launch(from, to, 12, 0, out var velocity, out float gravity);
            Assert.That(gravity, Is.Zero);
            Assert.That(new Vector2(velocity.x, velocity.z).magnitude, Is.EqualTo(12).Within(1e-3f));
            float time = EnemyProjectileFlight.FlightTime(from, to, 12);
            Assert.That(Vector3.Distance(Simulate(from, velocity, gravity, time, time / 50), to), Is.LessThan(1e-3f));
        }

        [Test]
        public void ArcRisesByItsHeightAndLandsOnTheAimPointWhateverTheStep()
        {
            Vector3 from = new Vector3(0, 2, 0), to = new Vector3(0, 0, 8);
            EnemyProjectileFlight.Launch(from, to, 8, 1.3f, out var velocity, out float gravity);
            float time = EnemyProjectileFlight.FlightTime(from, to, 8);
            Assert.That(gravity, Is.GreaterThan(0));
            Vector3 middle = EnemyProjectileFlight.PositionAt(from, velocity, gravity, time * .5f);
            Assert.That(middle.y - (from.y + to.y) * .5f, Is.EqualTo(1.3f).Within(1e-3f), "The middle of the arc rises by the arc height.");
            Assert.That(Vector3.Distance(Simulate(from, velocity, gravity, time, time / 40), to), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(Simulate(from, velocity, gravity, time, time / 7), to), Is.LessThan(1e-3f), "Exact steps: no drift with a coarse step.");
        }

        [Test]
        public void PointBlankLobKeepsAShortButSaneFlight()
        {
            EnemyProjectileFlight.Launch(Vector3.zero, new Vector3(0, 0, .05f), 10, 1, out var velocity, out float gravity);
            Assert.That(EnemyProjectileFlight.FlightTime(Vector3.zero, new Vector3(0, 0, .05f), 10), Is.EqualTo(EnemyProjectileFlight.MinimumFlightTime));
            Assert.That(float.IsNaN(velocity.x) || float.IsInfinity(gravity), Is.False);
        }

        [Test]
        public void HomingTurnsNoFasterThanItsRate()
        {
            Vector3 velocity = Vector3.forward * 10;
            Vector3 steered = EnemyProjectileFlight.Steer(velocity, Vector3.right, 90, .5f);
            Assert.That(Vector3.Angle(velocity, steered), Is.EqualTo(45).Within(.01f));
            Assert.That(steered.magnitude, Is.EqualTo(10).Within(1e-3f));
            Assert.That(EnemyProjectileFlight.Steer(velocity, Vector3.right, 0, 1), Is.EqualTo(velocity));
        }

        [Test]
        public void SegmentDistanceFindsCrossingsAndGaps()
        {
            float d = EnemyProjectileFlight.SegmentDistance(new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(0, -1, .5f), new Vector3(0, 1, .5f), out float s, out float t);
            Assert.That(d, Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(s, Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(t, Is.EqualTo(.5f).Within(1e-4f));
            d = EnemyProjectileFlight.SegmentDistance(Vector3.zero, Vector3.forward, new Vector3(0, 0, 3), new Vector3(0, 0, 4), out s, out t);
            Assert.That(d, Is.EqualTo(2).Within(1e-4f));
            Assert.That(s, Is.EqualTo(1));
            Assert.That(t, Is.EqualTo(0));
        }

        [Test]
        public void ShotCheckSeesARockButNotTheShooterOrTheTarget()
        {
            var shooter = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                // Far from anything the open scene may hold.
                Vector3 origin = new Vector3(5000, 500, 5000);
                shooter.transform.position = origin + new Vector3(0, 1, 0);
                target.transform.position = origin + new Vector3(0, 1, 10);
                rock.transform.position = origin + new Vector3(40, 1, 5);
                Physics.SyncTransforms();
                Vector3 from = origin + new Vector3(0, 1.2f, 0), to = origin + new Vector3(0, 1, 10);
                Assert.That(EnemyProjectileFlight.IsPathClear(from, to, 12, 0, .2f, shooter.transform, target.transform), Is.True,
                    "Starting inside the shooter and ending in the target must not count as blocked.");
                rock.transform.position = origin + new Vector3(0, 1, 5);
                Physics.SyncTransforms();
                Assert.That(EnemyProjectileFlight.IsPathClear(from, to, 12, 0, .2f, shooter.transform, target.transform), Is.False);
                // A lob goes over the rock.
                Assert.That(EnemyProjectileFlight.IsPathClear(from, to, 8, 3, .2f, shooter.transform, target.transform), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(shooter);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(rock);
            }
        }

        static WorldCreature LoadCreature(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Prefabs/{name}.prefab");
            Assert.That(prefab, Is.Not.Null, name);
            return prefab.GetComponent<WorldCreature>();
        }

        static void AssertProjectileAsset(EnemyProjectileProfile profile)
        {
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.prefab, Is.Not.Null, profile.name + ": prefab");
            Assert.That(profile.prefab.picture, Is.Not.Null, profile.name + ": picture");
            Assert.That(profile.prefab.picture.sprite, Is.Not.Null, profile.name + ": sprite");
            Assert.That(profile.prefab.picture.sharedMaterial ? profile.prefab.picture.sharedMaterial.name : null, Is.EqualTo("HandDrawnSprite"));
            Assert.That(profile.prefab.trails, Is.Not.Empty, profile.name + ": Retro Arsenal trail");
            Assert.That(profile.prefab.trails, Has.All.Not.Null);
            Assert.That(profile.impact, Is.Not.Null, profile.name + ": impact effect");
            Assert.That(profile.muzzleFlash, Is.Not.Null, profile.name + ": muzzle flash");
            Assert.That(profile.shotDown, Is.Not.Null, profile.name + ": shot-down effect");
            Assert.That(profile.fireCue && profile.impactCue, Is.True, profile.name + ": sounds");
            Assert.That(profile.cardsShootDown && profile.hitsCreatures, Is.True);
        }

        [Test]
        public void BeeSwarmLobsAFanOfHoneyAndStillStingsUpClose()
        {
            var bee = LoadCreature("Bee swarm");
            Assert.That(bee.HasRangedAttack, Is.True);
            var r = bee.ranged;
            AssertProjectileAsset(r.projectile);
            Assert.That(r.projectile.path, Is.EqualTo(EnemyProjectileProfile.Path.Arc));
            Assert.That(r.projectile.slowSeconds, Is.GreaterThan(0));
            Assert.That(r.projectile.slowScale, Is.LessThan(1));
            Assert.That(r.projectiles, Is.GreaterThan(1), "A fan of globs.");
            Assert.That(r.closeCombat, Is.EqualTo(CloseCombat.Strike));
            Assert.That(r.minRange, Is.GreaterThan(bee.attackRange), "Shoots from afar, stings up close.");
            Assert.That(r.range, Is.LessThanOrEqualTo(bee.sightRange));
        }

        [Test]
        public void RoosterThrowsBaseballsFromAfarAndBatsUpClose()
        {
            var rooster = LoadCreature("Rooster baseball");
            Assert.That(rooster.temperament, Is.EqualTo(Temperament.Peaceful), "Still peaceful: it only fights back.");
            Assert.That(rooster.HasRangedAttack, Is.True);
            var r = rooster.ranged;
            AssertProjectileAsset(r.projectile);
            Assert.That(r.projectile.path, Is.EqualTo(EnemyProjectileProfile.Path.Straight));
            Assert.That(r.closeCombat, Is.EqualTo(CloseCombat.Strike));
            Assert.That(r.minRange, Is.GreaterThan(rooster.attackRange));
            Assert.That(r.retreatDistance, Is.GreaterThan(rooster.attackRange), "Backs off before you reach bat range.");
            Assert.That(r.range, Is.LessThanOrEqualTo(rooster.sightRange));
        }

        [Test]
        public void CreaturesWithoutAProjectileHaveNoRangedAttack()
        {
            foreach (var name in new[] { "Goblin dog", "Budding troll", "Hostile bush" })
                Assert.That(LoadCreature(name).HasRangedAttack, Is.False, name);
        }
    }
}
