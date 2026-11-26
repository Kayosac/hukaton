#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.EnemyDebug.Tests.PlayMode
{
    /// <summary>
    /// Combat balance of the demo (stage 8), measured live in the debug scene: every attack leaves the player an answer.
    /// Honey and web slow the player for a short while only; the numbers are read from real hits and real walking.
    /// </summary>
    public class CombatBalancePlayTests
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
            held = new KeyboardState();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.onBeforeUpdate += Feed; InputSystem.onAfterUpdate += SelectDevices;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/EnemyDebug/Scenes/EnemyDebug.unity", new LoadSceneParameters(LoadSceneMode.Single));
            sandbox = Object.FindFirstObjectByType<EnemyDebugSandbox>();
            yield return null;
            sandbox.control.SendMessage("OnApplicationFocus", true); sandbox.control.CaptureCursor();
            yield return Wait(.3f);
            Assert.That(sandbox.world.Running, Is.True);
            Assert.That(sandbox.world.statuses, Is.Not.Null, "the debug scene puts debuffs on the player");
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
        }

        void SelectDevices() { if (InputState.currentUpdateType == InputUpdateType.Dynamic) { keyboard.MakeCurrent(); mouse.MakeCurrent(); } }
        static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        static EnemyProjectileProfile Shot(string name)
        {
            var profile = AssetDatabase.LoadAssetAtPath<EnemyProjectileProfile>($"Assets/Game/Data/Projectiles/{name}.asset");
            Assert.That(profile, Is.Not.Null, name);
            return profile;
        }

        /// <summary>Open ground of the debug scene: in front of the goblin dog's drawing, walking along +Z.</summary>
        Vector3 Start
        {
            get
            {
                var preview = sandbox.Previews.First(p => p.source && p.source.name == "Goblin dog");
                return preview.transform.position + new Vector3(0, .05f, -2);
            }
        }

        float measured;

        /// <summary>Holds W from a standstill and measures the walking speed over the last half second, m/s.</summary>
        IEnumerator Walk()
        {
            held = new KeyboardState(Key.W);
            yield return Wait(.3f);
            var from = sandbox.motor.transform.position;
            float since = Time.time;
            yield return Wait(.5f);
            measured = Flat(from, sandbox.motor.transform.position) / (Time.time - since);
            held = new KeyboardState();
        }

        /// <summary>A real shot of this profile at the player from 5 m; returns once its debuff is on.</summary>
        IEnumerator HitPlayer(EnemyProjectileProfile profile)
        {
            sandbox.motor.view.Guide(null);
            sandbox.motor.Teleport(Start, Quaternion.identity);
            yield return Wait(.4f);
            Vector3 target = sandbox.motor.transform.position + Vector3.up * 1.1f;
            Vector3 from = target + new Vector3(0, .3f, 5);
            EnemyProjectileFlight.Launch(from, target, profile.speed, profile.FlightArc, out var velocity, out float gravity);
            var shot = sandbox.world.SpawnForOuting(profile.prefab, from, Quaternion.LookRotation(velocity));
            shot.Launch(sandbox.world, profile, null, sandbox.world.Player, velocity, gravity);
            float until = Time.time + 3;
            while (Time.time < until && !sandbox.world.statuses.Has(PlayerStatusKind.Slow)) yield return null;
            Assert.That(sandbox.world.statuses.Has(PlayerStatusKind.Slow), Is.True, profile.name + " slows the player");
        }

        float slowStarted;

        /// <summary>Seconds the Slow lasts from the hit (measured frame by frame until it is gone).</summary>
        IEnumerator SlowEnds()
        {
            float until = Time.time + 10;
            while (Time.time < until && sandbox.world.statuses.Has(PlayerStatusKind.Slow)) yield return null;
            measured = Time.time - slowStarted;
        }

        IEnumerator SlowOf(string name, System.Action<float, float, float> report)
        {
            var profile = Shot(name);
            // The usual walk first, on the same ground.
            sandbox.motor.view.Guide(null);
            sandbox.motor.Teleport(Start, Quaternion.identity);
            yield return Wait(.4f);
            yield return Walk();
            float normal = measured;
            yield return Wait(.4f);

            yield return HitPlayer(profile);
            slowStarted = Time.time;
            yield return Wait(.15f);
            yield return Walk();
            float slowed = measured;
            yield return SlowEnds();
            report(normal, slowed, measured);
        }

        [UnityTest]
        public IEnumerator HoneyHalvesThePlayersSpeedForASecondAndAHalf()
        {
            float normal = 0, slowed = 0, seconds = 0;
            yield return SlowOf("Honey glob", (n, s, d) => { normal = n; slowed = s; seconds = d; });
            Debug.Log($"[stage-8] honey: walk {normal:0.00} m/s, under honey {slowed:0.00} m/s ({slowed / normal:P0}), slow lasted {seconds:0.00} s");
            Assert.That(normal, Is.GreaterThan(3), "the usual walk was measured");
            Assert.That(slowed / normal, Is.EqualTo(.5f).Within(.06f), "under honey the player walks at half speed");
            Assert.That(seconds, Is.EqualTo(1.5f).Within(.12f), "the honey wears off after 1.5 s");
        }

        [UnityTest]
        public IEnumerator WebSlowsThePlayerForTwoAndAHalfSeconds()
        {
            float normal = 0, slowed = 0, seconds = 0;
            yield return SlowOf("Web", (n, s, d) => { normal = n; slowed = s; seconds = d; });
            Debug.Log($"[stage-8] web: walk {normal:0.00} m/s, in the web {slowed:0.00} m/s ({slowed / normal:P0}), slow lasted {seconds:0.00} s");
            Assert.That(normal, Is.GreaterThan(3), "the usual walk was measured");
            Assert.That(slowed / normal, Is.EqualTo(.5f).Within(.06f), "the web slows as before: half speed");
            Assert.That(seconds, Is.EqualTo(2.5f).Within(.12f), "the web wears off after 2.5 s");
        }
    }
}
#endif
