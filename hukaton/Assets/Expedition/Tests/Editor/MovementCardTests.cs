using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// Cluster A «movement» cards: the mushroom's bounce formula, the updraft's lift, the Moon and Lightness rules on
    /// the motor, the Teleport's free-pose search (real physics queries, far above the valley) and Swap's conditions.
    /// </summary>
    public sealed class MovementCardTests
    {
        /// <summary>Test geometry is built here, far above anything in the open scene.</summary>
        static readonly Vector3 Far = new Vector3(700, 2500, -700);
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var item in created) if (item) Object.DestroyImmediate(item);
            created.Clear();
            Physics.SyncTransforms();
        }

        T Make<T>(T item) where T : Object { created.Add(item); return item; }

        GameObject Box(string name, Vector3 min, Vector3 max)
        {
            var go = Make(new GameObject(name));
            go.transform.position = Far + (min + max) * .5f;
            go.AddComponent<BoxCollider>().size = max - min;
            return go;
        }

        FirstPersonMotor BareMotor() => Make(new GameObject("Motor")).AddComponent<FirstPersonMotor>();

        // ---------- Mushroom trampoline ----------

        [TestCase(5f, .8f, 4f)]
        [TestCase(5f, 1f, 5f)]
        [TestCase(8f, .8f, 6.4f)]
        [TestCase(8f, 1f, 8f)]
        [TestCase(6f, .8f, 4.8f)]
        public void MushroomReturnsTheBoardsShareOfTheFall(float fall, float heightReturn, float bounce)
        {
            float landing = MushroomBounce.FallSpeed(fall);
            Assert.That(MushroomBounce.BounceHeight(landing, heightReturn, 1.5f, 10), Is.EqualTo(bounce).Within(.001f),
                "Board: 5 m → 4 / 5 m, 8 m → 6.4 / 8 m, 6 m → 4.8 m.");
            float up = MushroomBounce.LaunchSpeedFor(landing, heightReturn, 1.5f, 10);
            Assert.That(MushroomBounce.EquivalentHeight(up), Is.EqualTo(bounce).Within(.001f), "The launch speed climbs exactly that high at g 9.81.");
        }

        [Test]
        public void SmallHopsBounceTheMinimumAndBigFallsStopAtTen()
        {
            Assert.That(MushroomBounce.BounceHeight(0, .8f, 1.5f, 10), Is.EqualTo(1.5f), "A soft landing still launches 1.5 m.");
            Assert.That(MushroomBounce.BounceHeight(MushroomBounce.FallSpeed(1), .8f, 1.5f, 10), Is.EqualTo(1.5f));
            Assert.That(MushroomBounce.BounceHeight(MushroomBounce.FallSpeed(20), .8f, 1.5f, 10), Is.EqualTo(10).Within(.001f), "16 m is capped at 10 m.");
            Assert.That(MushroomBounce.BounceHeight(MushroomBounce.FallSpeed(30), 1, 1.5f, 10), Is.EqualTo(10).Within(.001f));
            Assert.That(MushroomBounce.BounceHeight(-MushroomBounce.FallSpeed(5), .8f, 1.5f, 10), Is.EqualTo(4).Within(.001f), "Sign of the speed does not matter.");
        }

        [Test]
        public void TheFormulaKeepsNormalGravitySoTheMoonCarriesTheBounceHigher()
        {
            float launch = MushroomBounce.LaunchSpeedFor(MushroomBounce.FallSpeed(20), .8f, 1.5f, 10);
            float moonHeight = launch * launch / (2 * MushroomBounce.DesignGravity * .25f);
            Assert.That(moonHeight, Is.EqualTo(40).Within(.01f), "Confirmed: under the Moon the combination goes above 10 m.");
            // Lightness limits the fall to 8 m/s: the bounce uses that actual speed.
            Assert.That(MushroomBounce.BounceHeight(8, .8f, 1.5f, 10), Is.EqualTo(.8f * 64 / (2 * 9.81f)).Within(.001f));
        }

        [Test]
        public void MushroomBouncesThePlayerOnAMotor()
        {
            var motor = BareMotor();
            float real = MushroomBounce.LaunchSpeedFor(MushroomBounce.FallSpeed(5), .8f, 1.5f, 10) * motor.ReferenceScale;
            motor.SetVerticalSpeed(real);
            Assert.That(motor.VerticalSpeed, Is.EqualTo(real));
            float realHeight = real * real / (2 * motor.gravity);
            Assert.That(realHeight, Is.EqualTo(4).Within(.01f), "Real gravity 25 with ReferenceScale climbs the design 4 m.");
        }

        // ---------- Updraft ----------

        [Test]
        public void UpdraftLiftsToFourAndHoversAtTheTop()
        {
            float v = 0;
            for (int i = 0; i < 50; i++) v = UpdraftColumn.Lift(v, UpdraftColumn.TargetSpeed(4, 5, 1.2f), 4, 28, .02f);
            Assert.That(v, Is.EqualTo(4).Within(.001f), "Up to 4 m/s, never more.");
            for (int i = 0; i < 50; i++) v = UpdraftColumn.Lift(v, UpdraftColumn.TargetSpeed(4, 0, 1.2f), 4, 28, .02f);
            Assert.That(v, Is.EqualTo(0).Within(.001f), "At the top the lift eases off: the body hovers.");
            Assert.That(UpdraftColumn.Lift(14, 4, 4, 28, .02f), Is.EqualTo(14), "A faster bounce through the column is not slowed.");
            Assert.That(UpdraftColumn.Lift(-12, 4, 4, 28, .02f), Is.GreaterThan(-12), "A fall into the column is braked.");
        }

        // ---------- Moon gravity and Lightness ----------

        [Test]
        public void MoonRepeatRefreshesTheTimeWithoutStackingOrShortening()
        {
            var motor = BareMotor();
            var moon = Make(ScriptableObject.CreateInstance<MoonGravityEffect>());
            moon.ApplyTo(motor, 1);
            Assert.That(motor.GravityScale, Is.EqualTo(.25f));
            Assert.That(motor.ModifierRemaining(MotorModifier.Gravity), Is.EqualTo(12).Within(.01f));
            moon.ApplyTo(motor, 1);
            Assert.That(motor.GravityScale, Is.EqualTo(.25f), "Never multiplied: not 1/16.");
            Assert.That(motor.ModifierRemaining(MotorModifier.Gravity), Is.EqualTo(12).Within(.01f), "Never added up.");
            moon.ApplyTo(motor, 3);
            Assert.That(motor.ModifierRemaining(MotorModifier.Gravity), Is.EqualTo(45).Within(.01f), "Level III: 45 s.");
            moon.ApplyTo(motor, 1);
            Assert.That(motor.ModifierRemaining(MotorModifier.Gravity), Is.EqualTo(45).Within(.01f), "A shorter copy does not cut it.");
            Assert.That(motor.ImpulseScale, Is.EqualTo(1), "The Moon changes only gravity.");
            Assert.That(float.IsPositiveInfinity(motor.FallSpeedLimit), Is.True);
            Assert.That(motor.MoveSpeedScale, Is.EqualTo(1));
        }

        [Test]
        public void LightnessAndMoonActTogetherWithoutMultiplying()
        {
            var motor = BareMotor();
            var moon = Make(ScriptableObject.CreateInstance<MoonGravityEffect>());
            var light = Make(ScriptableObject.CreateInstance<LightnessEffect>());
            light.ApplyTo(motor, 1);
            moon.ApplyTo(motor, 1);
            Assert.That(motor.GravityScale, Is.EqualTo(.25f));
            Assert.That(motor.ImpulseScale, Is.EqualTo(1.5f));
            Assert.That(motor.FallSpeedLimit, Is.EqualTo(8 * motor.ReferenceScale).Within(.001f), "8 design m/s, in real units.");
            Assert.That(motor.ModifierRemaining(MotorModifier.ReceivedImpulse), Is.EqualTo(6).Within(.01f));
            light.ApplyTo(motor, 2);
            Assert.That(motor.ModifierRemaining(MotorModifier.FallSpeedLimit), Is.EqualTo(12).Within(.01f), "Level II: 12 s.");
            Assert.That(motor.ImpulseScale, Is.EqualTo(1.5f), "Not 2.25.");
        }

        // ---------- Teleport: the free pose ----------

        static PlayerCapsulePlacement.Settings Capsule() => PlayerCapsulePlacement.Settings.For(.315f, 1.8f);

        [Test]
        public void TeleportOnOpenFloorStandsOnTheContact()
        {
            Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5));
            Physics.SyncTransforms();
            var s = Capsule();
            Assert.That(PlayerCapsulePlacement.TryFind(Far, Vector3.up, s, out var feet), Is.True);
            Assert.That(Vector3.Distance(feet, Far + Vector3.up * s.gap), Is.LessThan(.01f));
        }

        [Test]
        public void TeleportIntoAWallLeavesThePlayerInTheAirBesideIt()
        {
            Box("Wall", new Vector3(-1, 0, -3), new Vector3(0, 6, 3));
            Physics.SyncTransforms();
            var s = Capsule();
            Vector3 point = Far + new Vector3(0, 4, 0);
            Assert.That(PlayerCapsulePlacement.TryFind(point, Vector3.right, s, out var feet), Is.True);
            Assert.That(feet.x - Far.x, Is.EqualTo(s.radius + s.gap).Within(.01f), "Right beside the wall, minimal gap.");
            Assert.That(feet.y - Far.y, Is.EqualTo(4 - .9f).Within(.01f), "At the contact's height, in the air.");
        }

        [Test]
        public void TeleportIntoACeilingHangsJustBelowIt()
        {
            Box("Ceiling", new Vector3(-3, 5, -3), new Vector3(3, 6, 3));
            Physics.SyncTransforms();
            var s = Capsule();
            Assert.That(PlayerCapsulePlacement.TryFind(Far + Vector3.up * 5, Vector3.down, s, out var feet), Is.True);
            Assert.That(feet.y + s.height - Far.y, Is.EqualTo(5 - s.gap).Within(.01f));
        }

        [Test]
        public void TeleportNextToAWallShiftsAwayAlongTheFloor()
        {
            Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5));
            Box("Wall", new Vector3(.1f, 0, -5), new Vector3(1.1f, 3, 5));
            Physics.SyncTransforms();
            var s = Capsule();
            Assert.That(PlayerCapsulePlacement.TryFind(Far, Vector3.up, s, out var feet), Is.True);
            Assert.That(feet.x - Far.x, Is.LessThanOrEqualTo(.1f - s.radius + .001f), "Moved off the wall.");
            Assert.That(feet.y - Far.y, Is.LessThanOrEqualTo(s.maxRise + s.gap), "Not lifted.");
            Assert.That(PlayerCapsulePlacement.IsFree(feet, s), Is.True);
        }

        [Test]
        public void TeleportNeverLiftsOntoALedge()
        {
            // A 0.4 m slot between a tall wall and a 0.6 m step: no room on the floor; the step top is free but higher.
            Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5));
            Box("Wall", new Vector3(-3, 0, -5), new Vector3(-.5f, 4, 5));
            Box("Step", new Vector3(-.1f, 0, -5), new Vector3(3, .6f, 5));
            Physics.SyncTransforms();
            var s = Capsule();
            Assert.That(PlayerCapsulePlacement.TryFind(Far + new Vector3(-.3f, 0, 0), Vector3.up, s, out _), Is.False);
        }

        [Test]
        public void TeleportNeverPassesThroughAWall()
        {
            // The contact is in a 0.1 m crack; open space starts right behind a thin wall, within the search distance.
            Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5));
            Box("Block", new Vector3(-3, 0, -5), new Vector3(-.05f, 4, 5));
            Box("Thin wall", new Vector3(.05f, 0, -5), new Vector3(.15f, 4, 5));
            Physics.SyncTransforms();
            var s = Capsule();
            Assert.That(PlayerCapsulePlacement.IsFree(Far + new Vector3(.6f, .05f, 0), s), Is.True, "Behind the wall there is room…");
            Assert.That(PlayerCapsulePlacement.TryFind(Far, Vector3.up, s, out _), Is.False, "…but the player is not moved through it.");
        }

        // ---------- Swap ----------

        [Test]
        public void SwapTakesOnlyLooseLightNearbyNonLivingObjects()
        {
            var swap = Make(ScriptableObject.CreateInstance<SwapEffect>());
            var crate = Box("Crate", new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 1, .5f));
            var body = crate.AddComponent<Rigidbody>();
            body.mass = 1;
            Physics.SyncTransforms();
            Vector3 near = Far + new Vector3(8, 0, 0), far = Far + new Vector3(15, 0, 0);
            Assert.That(swap.Check(body, near, 1), Is.EqualTo(SwapEffect.Refusal.None));
            Assert.That(swap.Check(body, far, 1), Is.EqualTo(SwapEffect.Refusal.TooFar), "12 m base.");
            Assert.That(swap.Check(body, far, 2), Is.EqualTo(SwapEffect.Refusal.None), "20 m at level II.");
            body.mass = 2;
            Assert.That(swap.Check(body, near, 1), Is.EqualTo(SwapEffect.Refusal.None), "μ = 2 is still fine.");
            body.mass = 2.5f;
            Assert.That(swap.Check(body, near, 1), Is.EqualTo(SwapEffect.Refusal.TooHeavy));
            body.mass = 1;
            body.isKinematic = true;
            Assert.That(swap.Check(body, near, 1), Is.EqualTo(SwapEffect.Refusal.Fixed));
            body.isKinematic = false;
            crate.AddComponent<WorldCreature>();
            Assert.That(swap.Check(body, near, 1), Is.EqualTo(SwapEffect.Refusal.Living));
            Assert.That(swap.Check(null, near, 1), Is.EqualTo(SwapEffect.Refusal.NoObject));
        }

        // ---------- the authored assets ----------

        [Test]
        public void MovementEffectAssetsCarryTheBoardsNumbersAndPrefabs()
        {
            const string folder = "Assets/Game/Data/Effects/";
            var moon = AssetDatabase.LoadAssetAtPath<MoonGravityEffect>(folder + "Moon gravity.asset");
            Assert.That((moon.duration.i, moon.duration.ii, moon.duration.iii), Is.EqualTo((12f, 25f, 45f)));
            Assert.That(moon.gravityMultiplier, Is.EqualTo(.25f));
            Assert.That(moon.badge, Is.Not.Null);
            Assert.That(moon.badge.modifier, Is.EqualTo(MotorModifier.Gravity));

            var light = AssetDatabase.LoadAssetAtPath<LightnessEffect>(folder + "Lightness.asset");
            Assert.That(light.radius, Is.EqualTo(2.5f));
            Assert.That(light.impulseMultiplier, Is.EqualTo(1.5f));
            Assert.That(light.fallSpeedLimit, Is.EqualTo(8));
            Assert.That(light.badge, Is.Not.Null);

            var mushroom = AssetDatabase.LoadAssetAtPath<MushroomTrampolineEffect>(folder + "Mushroom trampoline.asset");
            Assert.That(mushroom.lifetimeSeconds, Is.EqualTo(15));
            Assert.That((mushroom.heightReturn.i, mushroom.heightReturn.ii, mushroom.heightReturn.iii), Is.EqualTo((.8f, .9f, 1f)));
            Assert.That((mushroom.minBounceHeight.i, mushroom.minBounceHeight.ii, mushroom.minBounceHeight.iii), Is.EqualTo((1.5f, 2.5f, 4f)));
            Assert.That(mushroom.capDiameter, Is.EqualTo(1.6f));
            Assert.That(mushroom.mushroomPrefab, Is.Not.Null);
            Assert.That(mushroom.mushroomPrefab.capCollider, Is.Not.Null);
            Assert.That(mushroom.maxSlope, Is.EqualTo(48), "Playtest: floors up to the player's slope limit, steeper grows a wall shelf.");
            Assert.That(mushroom.shelfPrefab, Is.Not.Null);
            Assert.That(mushroom.shelfPrefab.capCollider, Is.Not.Null);

            var updraft = AssetDatabase.LoadAssetAtPath<UpdraftEffect>(folder + "Updraft.asset");
            Assert.That((updraft.height.i, updraft.height.ii, updraft.height.iii), Is.EqualTo((6f, 10f, 15f)));
            Assert.That(updraft.radius, Is.EqualTo(1.5f));
            Assert.That(updraft.durationSeconds, Is.EqualTo(12));
            Assert.That(updraft.riseSpeed, Is.EqualTo(4));
            Assert.That(updraft.maxMass, Is.EqualTo(2));
            Assert.That(updraft.columnPrefab, Is.Not.Null);

            var teleport = AssetDatabase.LoadAssetAtPath<TeleportEffect>(folder + "Teleport.asset");
            Assert.That(teleport.arriveVfx, Is.Not.Null);
            Assert.That((ScriptableObject)teleport is Hakaton.Decks.Unity.ICardStatLine, Is.False, "Its levels add uses: the PC shows «Использований 1 · 2 · 3».");

            var swap = AssetDatabase.LoadAssetAtPath<SwapEffect>(folder + "Swap.asset");
            Assert.That((swap.range.i, swap.range.ii, swap.range.iii), Is.EqualTo((12f, 20f, 30f)));
            Assert.That(swap.maxMass, Is.EqualTo(2));
            Assert.That(swap.swapVfx, Is.Not.Null);
        }
    }
}
