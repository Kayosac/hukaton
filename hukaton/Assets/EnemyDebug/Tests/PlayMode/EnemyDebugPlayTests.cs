#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.EnemyDebug.Tests.PlayMode
{
    public class EnemyDebugPlayTests
    {
        EnemyDebugSandbox sandbox;
        Keyboard keyboard, previousKeyboard;
        Mouse mouse, previousMouse;
        KeyboardState held;
        MouseState mouseState;
        InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;
        InputSettings.BackgroundBehavior previousBackground;
        InputSettings.UpdateMode previousUpdate;
        [UnitySetUp]
        public IEnumerator LoadSavedScene()
        {
            previousKeyboard = Keyboard.current; previousMouse = Mouse.current;
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousUpdate = InputSystem.settings.updateMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            held = new KeyboardState(); mouseState = new MouseState();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.onBeforeUpdate += Feed; InputSystem.onAfterUpdate += SelectDevices;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/EnemyDebug/Scenes/EnemyDebug.unity", new LoadSceneParameters(LoadSceneMode.Single));
            sandbox = Object.FindFirstObjectByType<EnemyDebugSandbox>();
            yield return null;
            sandbox.control.SendMessage("OnApplicationFocus", true); sandbox.control.CaptureCursor();
            yield return Wait(.2f);
            Assert.That(sandbox.world.Running, Is.True);
            Assert.That(sandbox.Previews.Count, Is.EqualTo(sandbox.catalogue.Discover().Length));
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            held = new KeyboardState();
            if (sandbox) { sandbox.ClearLive(); sandbox.control.ReleaseCursor(); }
            InputSystem.onBeforeUpdate -= Feed; InputSystem.onAfterUpdate -= SelectDevices;
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.updateMode = previousUpdate;
            yield return null;
        }
        void Feed()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            InputSystem.QueueStateEvent(keyboard, held);
            InputSystem.QueueStateEvent(mouse, mouseState);
        }
        void SelectDevices() { if (InputState.currentUpdateType == InputUpdateType.Dynamic) { keyboard.MakeCurrent(); mouse.MakeCurrent(); } }
        static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
        IEnumerator Press(Key key)
        {
            held = new KeyboardState(key); yield return null; yield return null;
            held = new KeyboardState(); yield return null; yield return null;
        }
        IEnumerator Aim(EnemyDebugPreview preview)
        {
            sandbox.motor.Teleport(preview.transform.position + new Vector3(0,.05f,-7),Quaternion.identity);
            sandbox.motor.view.Guide(preview.transform.position + Vector3.up * preview.source.view.height * preview.transform.localScale.y * .5f);
            yield return Wait(.8f);
            Assert.That(sandbox.Focused, Is.SameAs(preview));
        }

        [UnityTest]
        public IEnumerator KeyboardPosesSpawnAndClearUseSavedSourcesAndRespectModalOwnership()
        {
            var preview = sandbox.Previews.First(p => p.source.name == "Goblin dog");
            yield return Aim(preview);
            foreach (var pair in new[] { (Key.Digit1,CreaturePose.Alarm), (Key.Digit2,CreaturePose.Hurt), (Key.Digit3,CreaturePose.Attack), (Key.Digit4,CreaturePose.Death), (Key.Digit0,CreaturePose.Default) })
            {
                yield return Press(pair.Item1);
                Assert.That(preview.SelectedPose, Is.EqualTo(pair.Item2));
                Assert.That(preview.view.Pose, Is.EqualTo(pair.Item2));
            }
            sandbox.control.SetBlocked(sandbox, true);
            yield return Press(Key.E);
            Assert.That(sandbox.LiveCreatures, Is.Empty);
            sandbox.control.SetBlocked(sandbox, false);
            yield return null;
            yield return Press(Key.E);
            Assert.That(sandbox.LiveCreatures.Count, Is.EqualTo(1));
            var creature = sandbox.LiveCreatures[0];
            Assert.That(creature.world, Is.SameAs(sandbox.world));
            Assert.That(Vector3.Distance(creature.transform.position,preview.transform.position), Is.LessThan(.5f));
            Assert.That(creature.body.isKinematic, Is.False);
            Assert.That(creature.navigator.isOnNavMesh, Is.True);
            Assert.That(creature.view.idle, Is.SameAs(preview.source.view.idle));
            Assert.That(preview.GetComponentsInChildren<Collider>(), Is.Empty);
            yield return Press(Key.R);
            Assert.That(sandbox.LiveCreatures, Is.Empty);
            yield return null;
            Assert.That(sandbox.liveRoot.childCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator EveryPrefabSpawnsOnTheGridWithNavigationAndPreviewNeverBlocksWalking()
        {
            foreach (var preview in sandbox.Previews)
            {
                var instance = sandbox.Spawn(preview);
                Assert.That(instance.transform.position.x, Is.EqualTo(preview.transform.position.x).Within(.01f));
                Assert.That(instance.transform.position.z, Is.EqualTo(preview.transform.position.z).Within(.01f));
                Assert.That(instance.navigator.isOnNavMesh, Is.True, preview.source.name);
                Assert.That(instance.Health, Is.EqualTo(instance.maximumHealth));
                if (!preview.HasAttack) Assert.That(preview.PlayPose(CreaturePose.Attack), Is.False);
            }
            yield return Wait(.2f);
            sandbox.ClearLive(); yield return null;
            var item = sandbox.Previews.First(p => p.source.name == "Goblin dog");
            sandbox.motor.view.Guide(null);
            sandbox.motor.Teleport(item.transform.position + new Vector3(0,.05f,-2),Quaternion.identity);
            held = new KeyboardState(Key.W);
            yield return Wait(1.5f);
            held = new KeyboardState();
            Assert.That(sandbox.motor.transform.position.z, Is.GreaterThan(item.transform.position.z + 1));
            Assert.That(sandbox.motor.IsGrounded, Is.True);
            var to = sandbox.viewCamera.transform.position - item.transform.position; to.y=0;
            item.view.Present(CreaturePose.Default,-to,Vector3.zero,Vector2.one,0);
            Assert.That(item.view.ShowingBack, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator ThrowStrike()
        {
            Assert.That(sandbox.world.thrower.Selected, Is.Not.Null);
            mouseState = new MouseState { buttons = 1 };
            yield return Wait(.75f);
            Assert.That(sandbox.world.thrower.Preparation.Active, Is.True);
            mouseState = new MouseState();
            yield return null; yield return null;
            Assert.That(sandbox.world.thrower.HasThrown, Is.True);
        }

        [UnityTest]
        public IEnumerator InfiniteStrikeKillsLiveCreatureAndReturnsTheSameCardAfterEveryHit()
        {
            var infinite = sandbox.viewCamera.GetComponentInChildren<EnemyDebugStrikeCard>();
            Assert.That(infinite, Is.Not.Null);
            var trip = sandbox.world.workshop.Model.Current;
            var card = infinite.Card;
            Assert.That(card.Spec.Id, Is.EqualTo("strike"));
            Assert.That(card.UsesLeft, Is.EqualTo(int.MaxValue));
            Assert.That(trip.Deck.Hand.Count, Is.EqualTo(1));
            var preview = sandbox.Previews.First(p => p.source.name == "Goblin dog");
            sandbox.motor.Teleport(preview.transform.position + new Vector3(0,.05f,-3), Quaternion.identity);
            var creature = sandbox.Spawn(preview);
            creature.Freeze(30); // Stationary target; actual input, projectile sweep, impact and death remain unchanged.
            int damage = infinite.definition.power.At(card.Level);
            int throws = Mathf.CeilToInt(creature.maximumHealth / (float)damage);
            for (int i = 0; i < throws; i++)
            {
                sandbox.motor.view.Guide(creature.GetComponent<Collider>().bounds.center + Vector3.up * .15f);
                yield return Wait(.5f);
                int health = creature.Health;
                yield return ThrowStrike();
                float deadline = Time.time + 3;
                while (creature.Health == health && Time.time < deadline) yield return null;
                Assert.That(creature.Health, Is.EqualTo(Mathf.Max(0, health - damage)), "The real Strike impact must use normal card damage.");
                yield return null; yield return null;
                Assert.That(trip.Deck.Hand.Single().Id, Is.EqualTo(card.Id));
                Assert.That(card.UsesLeft, Is.EqualTo(int.MaxValue));
                Assert.That(trip.Deck.Held, Is.Empty);
                Assert.That(trip.Deck.Discard, Is.Empty);
            }
            Assert.That(creature.Alive, Is.False);
            Assert.That(creature.State, Is.EqualTo(CreatureState.Dying));
            Assert.That(creature.view.Pose, Is.EqualTo(CreaturePose.Death));
            yield return Wait(creature.corpseSeconds + .9f);
            Assert.That(creature.gameObject.activeSelf, Is.False);
            Assert.That(preview.isActiveAndEnabled, Is.True);
            Assert.That(preview.SelectedPose, Is.EqualTo(CreaturePose.Default));
            Assert.That(sandbox.world.thrower.Selected.Id, Is.EqualTo(card.Id));
            Assert.That(sandbox.world.Running, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator StrikeSurvivesMissCancelAndResetWhileFlying()
        {
            var thrower = sandbox.world.thrower;
            var trip = sandbox.world.workshop.Model.Current;
            int id = thrower.Selected.Id;
            sandbox.motor.view.Guide(sandbox.motor.transform.position + new Vector3(0,-2,4));
            yield return Wait(.5f);
            yield return ThrowStrike();
            float deadline = Time.time + 4;
            while (trip.Deck.Hand.Count == 0 && Time.time < deadline) yield return null;
            Assert.That(trip.Deck.Hand.Single().Id, Is.EqualTo(id), "A miss at the ground must return the card.");
            Assert.That(sandbox.world.Impacts, Is.GreaterThan(0));

            mouseState = new MouseState { buttons = 1 }; yield return Wait(.15f);
            Assert.That(thrower.Preparation.Active, Is.True);
            mouseState = new MouseState { buttons = 3 }; yield return null; yield return null;
            Assert.That(thrower.Preparation.Active, Is.False, "RMB cancels charge.");
            mouseState = new MouseState(); yield return null; yield return null;
            Assert.That(trip.Deck.Hand.Single().Id, Is.EqualTo(id));

            sandbox.motor.view.Guide(sandbox.motor.transform.position + new Vector3(0,15,5));
            yield return Wait(.6f);
            yield return ThrowStrike();
            Assert.That(trip.Deck.Held.Count, Is.EqualTo(1));
            yield return Press(Key.R);
            yield return null;
            Assert.That(trip.Deck.Hand.Single().Id, Is.EqualTo(id), "R must settle the destroyed projectile instead of losing the only card.");
            Assert.That(trip.Deck.Held, Is.Empty);
            Assert.That(trip.Deck.Discard, Is.Empty);
            Assert.That(trip.Deck.HasReturn, Is.False);
            Assert.That(thrower.Preparation.Active, Is.False);
            Assert.That(sandbox.liveRoot.GetComponentsInChildren<CardProjectile>(), Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator LiveAggressiveAndTimidCreaturesUseTheirRealWorldBehaviour()
        {
            var dogPreview = sandbox.Previews.First(p => p.source.name == "Goblin dog");
            sandbox.motor.Teleport(dogPreview.transform.position + new Vector3(0,.05f,-5),Quaternion.identity);
            var dog = sandbox.Spawn(dogPreview);
            float initial = Vector3.Distance(dog.Position,sandbox.motor.transform.position);
            bool attacked = false;
            float until = Time.time + 7;
            while (Time.time < until)
            {
                attacked |= dog.State == CreatureState.Windup || dog.State == CreatureState.Strike || sandbox.ReceivedDamage > 0;
                if (attacked && sandbox.ReceivedDamage > 0) break;
                yield return null;
            }
            Assert.That(attacked, Is.True, "Real hostile AI must reach the attack sequence.");
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThan(0), "Real damage and knockback must run.");
            Assert.That(sandbox.world.vitals.HalfHearts, Is.EqualTo(sandbox.world.vitals.MaximumHalfHearts));
            Assert.That(sandbox.world.Running, Is.True);
            sandbox.ClearLive(); yield return null;
            var timid = sandbox.Previews.First(p => p.source.name == "Purple mushroom");
            sandbox.motor.Teleport(timid.transform.position + new Vector3(0,.05f,-3),Quaternion.identity);
            var mushroom = sandbox.Spawn(timid);
            var start = mushroom.Position;
            bool fled = false;
            until = Time.time + 3;
            while(Time.time < until) { fled |= mushroom.State == CreatureState.Flee; yield return null; }
            Assert.That(fled, Is.True);
            Assert.That(Vector3.Distance(start,mushroom.Position), Is.GreaterThan(1));
            LogAssert.NoUnexpectedReceived();
        }
        IEnumerator StandInFront(EnemyDebugPreview preview, float distance)
        {
            sandbox.motor.view.Guide(null);
            Vector3 ahead = preview.transform.forward; ahead.y = 0;
            ahead = ahead.sqrMagnitude > .01f ? ahead.normalized : Vector3.back;
            sandbox.motor.Teleport(preview.transform.position + ahead * distance + Vector3.up * .05f, Quaternion.LookRotation(-ahead));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ShootersFireFromAfarHitThePlayerAndOtherCreaturesAndShotsCanBeKnockedDown()
        {
            // Bee swarm: aims, then lobs a fan of honey; the splash hurts and slows.
            var beePreview = sandbox.Previews.First(p => p.source.name == "Bee swarm");
            yield return StandInFront(beePreview, 8);
            var bee = sandbox.Spawn(beePreview);
            bool aimed = false, slowed = false;
            int volley = 0;
            float until = Time.time + 9;
            while (Time.time < until)
            {
                aimed |= bee.State == CreatureState.Aim;
                volley = Mathf.Max(volley, EnemyProjectile.Active.Count(p => p && p.Shooter == bee));
                slowed |= sandbox.motor.MoveSpeedScale < 1;
                if (sandbox.ReceivedDamage > 0 && slowed) break;
                yield return null;
            }
            Assert.That(aimed, Is.True, "The shot is telegraphed by aiming.");
            Assert.That(volley, Is.EqualTo(bee.ranged.projectiles), "One shot is a fan of globs.");
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThan(0), "Honey hits the player.");
            Assert.That(slowed, Is.True, "Honey slows the player down.");
            sandbox.ClearLive(); yield return null;
            Assert.That(EnemyProjectile.Active, Is.Empty, "R clears shots in flight too.");

            // Rooster: peaceful until hurt, then throws baseballs from afar.
            var roosterPreview = sandbox.Previews.First(p => p.source.name == "Rooster baseball");
            yield return StandInFront(roosterPreview, 9);
            var rooster = sandbox.Spawn(roosterPreview);
            yield return Wait(1.2f);
            Assert.That(EnemyProjectile.Active, Is.Empty, "Peaceful: it does not shoot unprovoked.");
            rooster.Damage(0, Vector3.zero, rooster.Centre, 0);
            bool threw = false;
            until = Time.time + 9;
            while (Time.time < until && !(threw && sandbox.ReceivedDamage > 0))
            {
                threw |= EnemyProjectile.Active.Any(p => p && p.Shooter == rooster);
                yield return null;
            }
            Assert.That(threw, Is.True, "Provoked, it throws.");
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThan(0), "The baseball hits the player.");
            var profile = rooster.ranged.projectile;
            sandbox.ClearLive(); yield return null;

            // A shot hits another creature on its way.
            var dogPreview = sandbox.Previews.First(p => p.source.name == "Goblin dog");
            var dog = sandbox.Spawn(dogPreview);
            dog.Freeze(30);
            yield return null;
            Vector3 target = dog.GetComponent<Collider>().bounds.center;
            Vector3 from = target - dog.transform.forward * 6;
            EnemyProjectileFlight.Launch(from, target, profile.speed, profile.FlightArc, out var velocity, out float gravity);
            var shot = sandbox.world.SpawnForOuting(profile.prefab, from, Quaternion.LookRotation(velocity));
            shot.Launch(sandbox.world, profile, null, dog, velocity, gravity);
            int health = dog.Health;
            until = Time.time + 2;
            while (Time.time < until && dog.Health == health) yield return null;
            Assert.That(dog.Health, Is.EqualTo(health - profile.damage), "Creatures are hit by stray shots.");

            // Knocked out of the air: the shot ends with its effect.
            var second = sandbox.world.SpawnForOuting(profile.prefab, from + Vector3.up * 6, Quaternion.identity);
            second.Launch(sandbox.world, profile, null, dog, Vector3.forward * profile.speed, 0);
            yield return null;
            second.ShootDown();
            Assert.That(second.Ended, Is.True);
            yield return null;
            Assert.That(EnemyProjectile.Active.Contains(second), Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

#endif
