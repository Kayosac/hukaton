#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hakaton.Expeditions;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Hakaton.EnemyDebug.Tests.PlayMode
{
    public class HumanoidSpeechPlayTests
    {
        static readonly string[] Speakers = { "Teen leshy skater", "Zombie beekeeper", "Rooster baseball" };
        readonly List<CreatureSpeechProfile> temporaryProfiles = new List<CreatureSpeechProfile>();
        EnemyDebugSandbox sandbox;
        Keyboard keyboard, previousKeyboard;
        Mouse mouse, previousMouse;
        InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;
        InputSettings.BackgroundBehavior previousBackground;
        InputSettings.UpdateMode previousUpdate;
        float previousTimeScale;

        [UnitySetUp]
        public IEnumerator LoadSavedScene()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1;
            previousKeyboard = Keyboard.current;
            previousMouse = Mouse.current;
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousUpdate = InputSystem.settings.updateMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.onBeforeUpdate += Feed;
            InputSystem.onAfterUpdate += SelectDevices;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/EnemyDebug/Scenes/EnemyDebug.unity", new LoadSceneParameters(LoadSceneMode.Single));
            sandbox = Object.FindFirstObjectByType<EnemyDebugSandbox>();
            Assert.That(sandbox, Is.Not.Null);
            yield return null;
            sandbox.control.SendMessage("OnApplicationFocus", true);
            sandbox.control.CaptureCursor();
            yield return Wait(.1f);
            Assert.That(sandbox.world.Running, Is.True);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1;
            if (sandbox) { if (sandbox.world.time) sandbox.world.time.SetPaused(false); sandbox.ClearLive(); sandbox.control.ReleaseCursor(); }
            InputSystem.onBeforeUpdate -= Feed;
            InputSystem.onAfterUpdate -= SelectDevices;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.updateMode = previousUpdate;
            foreach (var profile in temporaryProfiles) if (profile) Object.Destroy(profile);
            temporaryProfiles.Clear();
            yield return null;
            Time.timeScale = previousTimeScale;
        }

        void Feed()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState());
        }

        void SelectDevices()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            keyboard.MakeCurrent();
            mouse.MakeCurrent();
        }

        static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        static IEnumerator Until(Func<bool> condition, string failure, float timeout = 3)
        {
            float deadline = Time.time + timeout;
            while (!condition() && Time.time < deadline) yield return null;
            Assert.That(condition(), Is.True, failure);
        }

        HumanoidSpeech Spawn(string sourceName)
        {
            var preview = sandbox.Previews.Single(p => p.source.name == sourceName);
            sandbox.motor.Teleport(preview.transform.position + new Vector3(0, .05f, -6), Quaternion.identity);
            var creature = sandbox.Spawn(preview);
            var speech = creature.GetComponent<HumanoidSpeech>();
            Assert.That(speech, Is.Not.Null, sourceName + " must carry the saved speech component.");
            Assert.That(speech.creature, Is.SameAs(creature));
            Assert.That(speech.profile, Is.Not.Null);
            Assert.That(speech.bubble, Is.Not.Null);
            Assert.That(speech.bubble.spritePlane, Is.SameAs(creature.view));
            Assert.That(speech.profile.reactionDistance, Is.EqualTo(4), "The saved gameplay profile uses the agreed four-metre radius.");
            Assert.That(speech.profile.requireLineOfSight, Is.True);

            // Only the runtime copy is accelerated; the authored texts, bindings and gameplay assets are untouched.
            var profile = Object.Instantiate(speech.profile);
            temporaryProfiles.Add(profile);
            profile.initialDelay = Vector2.zero;
            profile.displaySeconds = .35f;
            profile.encounterFollowupDelay = .1f;
            profile.reencounterDelay = .2f;
            profile.reactionPause = new Vector2(.12f, .12f);
            profile.minimumReadSeconds = .05f;
            profile.reactionMemorySeconds = 2;
            speech.profile = profile;
            creature.wanderSpeed = 0; // Sample a fixed radius while retaining the real calm AI and sprite presentation.
            speech.ResetSpeech();
            return speech;
        }

        void StandAt(HumanoidSpeech speech, float distance)
        {
            sandbox.motor.Teleport(speech.creature.transform.position + new Vector3(0, .05f, -distance), Quaternion.identity);
        }

        static bool Shows(HumanoidSpeech speech, string[] pool) => speech.bubble.IsVisible && pool.Contains(speech.bubble.Text);

        object CameraGuide()
        {
            var field = sandbox.motor.view.GetType().GetField("guide", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Keep this snapshot aligned with FirstPersonView's camera guide field.");
            return field.GetValue(sandbox.motor.view);
        }

        [UnityTest]
        public IEnumerator AllSavedHumanoidsGreetThenRemarkOnceAndHideOutsideFourMetres()
        {
            foreach (string source in Speakers)
            {
                var speech = Spawn(source);
                StandAt(speech, 4.5f);
                yield return Wait(.15f);
                Assert.That(speech.bubble.IsVisible, Is.False, source + " must remain silent beyond the radius.");

                StandAt(speech, 3);
                yield return Until(() => Shows(speech, speech.profile.greetings), source + " must greet the nearby player.");
                yield return Until(() => Shows(speech, speech.profile.neutral), source + " must follow the greeting with one neutral remark.");
                yield return Wait(speech.profile.displaySeconds + .6f);
                Assert.That(speech.bubble.IsVisible, Is.False, source + " must not repeat ambient lines while the player stays nearby.");

                StandAt(speech, 4.5f);
                yield return Wait(speech.profile.reencounterDelay + .1f);
                StandAt(speech, 3);
                yield return Until(() => Shows(speech, speech.profile.greetings), source + " must allow a new encounter after leaving.");
                StandAt(speech, 4.5f);
                yield return null;
                yield return null;
                Assert.That(speech.bubble.IsVisible, Is.False, source + " must hide an active line immediately on leaving the radius.");
                sandbox.ClearLive();
                yield return null;
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RealDamageAndAttackSpeakWithoutTakingControlOrGuidingTheCamera()
        {
            var speech = Spawn("Zombie beekeeper");
            var creature = speech.creature;
            creature.speed = 0; // The test chooses Chase and then Windup by moving the player, not by changing AI states.
            StandAt(speech, 3);
            object guide = CameraGuide();
            bool hadOwner = sandbox.control.HasModalOwner;
            bool gameplayActive = sandbox.control.GameplayActive;
            yield return Until(() => Shows(speech, speech.profile.greetings), "The encounter must start before provocation.");
            creature.Damage(1, Vector3.zero, creature.Centre, 0);
            yield return Until(() => Shows(speech, speech.profile.aggression), "A real hit must produce an aggression line during Chase.");
            string aggression = speech.bubble.Text;
            StandAt(speech, 1.8f);
            yield return Wait(.15f);
            Assert.That(speech.bubble.Text, Is.EqualTo(aggression), "An attack transition must not replace a combat line before its reading time.");
            yield return Until(() => Shows(speech, speech.profile.attack), "The real attack sequence must eventually speak after the reaction cooldown.", 4);
            Assert.That(CameraGuide(), Is.EqualTo(guide), "Barks must not change the camera guide target.");
            Assert.That(sandbox.control.HasModalOwner, Is.EqualTo(hadOwner), "Barks must not claim player controls.");
            Assert.That(sandbox.control.GameplayActive, Is.EqualTo(gameplayActive));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DisableOutingResetAndDeathClearTheAuthoredBubble()
        {
            var speech = Spawn("Teen leshy skater");
            StandAt(speech, 3);
            yield return Until(() => Shows(speech, speech.profile.greetings), "The speaker must start its first encounter.");
            speech.enabled = false;
            Assert.That(speech.bubble.IsVisible, Is.False, "Disabling speech must clear it synchronously.");
            speech.enabled = true;
            yield return Until(() => Shows(speech, speech.profile.greetings), "Re-enabled speech must begin cleanly.");

            speech.creature.ResetForOuting(new System.Random(17));
            Assert.That(speech.bubble.IsVisible, Is.False, "The creature's outing-reset event must clear the previous line.");
            StandAt(speech, 3);
            yield return Until(() => Shows(speech, speech.profile.greetings), "The next outing must have a fresh greeting.");
            speech.creature.Damage(speech.creature.maximumHealth, Vector3.zero, speech.creature.Centre, 0);
            yield return null;
            yield return null;
            Assert.That(speech.creature.Alive, Is.False);
            Assert.That(speech.bubble.IsVisible, Is.False, "Dead creatures must not leave speech floating above the corpse.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SolidWallHidesSpeechAndDoesNotRestartTheGreeting()
        {
            var speech = Spawn("Zombie beekeeper");
            StandAt(speech, 3);
            yield return Until(() => Shows(speech, speech.profile.greetings), "The clear encounter must greet.");
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                blocker.name = "Speech test occluder";
                blocker.transform.position = (speech.creature.transform.position + sandbox.motor.transform.position) * .5f + Vector3.up * 2;
                blocker.transform.localScale = new Vector3(3, 4, .25f);
                Physics.SyncTransforms();
                yield return Wait(.18f);
                Assert.That(speech.bubble.IsVisible, Is.False, "A solid wall must block actual speech visibility.");
                blocker.SetActive(false);
                Physics.SyncTransforms();
                yield return Until(() => Shows(speech, speech.profile.neutral), "Regaining sight should continue with the follow-up, not another greeting.");
            }
            finally { Object.Destroy(blocker); }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PausingPreservesTheCurrentLineAndDoesNotAdvanceTheEncounter()
        {
            var speech = Spawn("Rooster baseball");
            StandAt(speech, 3);
            yield return Until(() => Shows(speech, speech.profile.greetings), "The greeting must be visible before pausing.");
            string line = speech.bubble.Text;
            float pausedAt = Time.time;
            Assert.That(sandbox.world.time, Is.Not.Null);
            sandbox.world.time.SetPaused(true);
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(Time.time, Is.EqualTo(pausedAt).Within(.001f));
            Assert.That(speech.bubble.IsVisible, Is.True);
            Assert.That(speech.bubble.Text, Is.EqualTo(line), "Realtime during pause must not consume display time or start the follow-up.");
            sandbox.world.time.SetPaused(false);
            yield return Until(() => Shows(speech, speech.profile.neutral), "Resuming must continue the same encounter.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BubbleFollowsTheExactSpritePlaneWithoutSquashingOrMirroringText()
        {
            foreach (string source in Speakers)
            {
                var speech = Spawn(source);
                var bubble = speech.bubble;
                var view = speech.creature.view;
                Assert.That(bubble.bubbleCanvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
                foreach (float size in new[] { .7f, 1.8f })
                foreach (bool flip in new[] { false, true })
                {
                    view.Size = size;
                    view.Present(CreaturePose.Default, Vector3.right, new Vector3(.12f, .21f, 0), new Vector2(.72f, 1.31f), 17);
                    view.spriteRenderer.flipX = flip;
                    bubble.Show("Ёжик / Hello");
                    bubble.FollowSprite();
                    Transform canvas = bubble.bubbleCanvas.transform;
                    Vector3 expected = view.transform.TransformPoint(new Vector3(0, view.height + bubble.headGap, 0));
                    Assert.That(Vector3.Distance(canvas.position, expected), Is.LessThan(.0001f), source);
                    Assert.That(Quaternion.Angle(canvas.rotation, view.transform.rotation), Is.LessThan(.01f), source);
                    Assert.That(Vector3.Distance(canvas.lossyScale, Vector3.one * bubble.metresPerPixel), Is.LessThan(.0001f), "Canvas world size must be independent of body size and squash.");
                    Assert.That(Vector3.Dot(canvas.right, view.transform.right), Is.GreaterThan(.999f), "Sprite flipX must not mirror the bubble.");
                    foreach (var label in canvas.GetComponentsInChildren<TMP_Text>(true))
                    {
                        Assert.That(label.transform.localScale, Is.EqualTo(Vector3.one));
                        Assert.That(label.transform.lossyScale.x, Is.GreaterThan(0));
                        Assert.That(label.enableAutoSizing, Is.False);
                    }
                }
                sandbox.ClearLive();
                yield return null;
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
