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
        void Feed() { if (InputState.currentUpdateType == InputUpdateType.Dynamic) InputSystem.QueueStateEvent(keyboard,held); }
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
    }
}

#endif
