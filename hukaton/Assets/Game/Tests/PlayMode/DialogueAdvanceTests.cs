#if UNITY_EDITOR
using System.Collections;
using Hakaton.Character;
using Hakaton.Tutorial;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// User rule 2026-11-13: wherever someone talks to the player, lines read on with E and with a left click alike;
    /// neither counts while a menu is in front, nor on the frame it closes. Own keyboard and mouse.
    /// </summary>
    public sealed class DialogueAdvanceTests
    {
        Keyboard keyboard;
        Mouse mouse;
        GameObject menu;
        InputSettings.EditorInputBehaviorInPlayMode focusRule;
        InputSettings.BackgroundBehavior background;
        InputSettings.UpdateMode update;

        [SetUp]
        public void AddDevices()
        {
            // The test's own devices reach the game whether or not the Game view has focus.
            focusRule = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            update = InputSystem.settings.updateMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            keyboard = InputSystem.AddDevice<Keyboard>("Dialogue test keyboard");
            keyboard.MakeCurrent();
            mouse = InputSystem.AddDevice<Mouse>("Dialogue test mouse");
            mouse.MakeCurrent();
            menu = new GameObject("Dialogue test menu");
        }

        [TearDown]
        public void RemoveDevices()
        {
            GamePause.Suspend(menu, false);
            Object.Destroy(menu);
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(mouse);
            InputSystem.settings.editorInputBehaviorInPlayMode = focusRule;
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.updateMode = update;
        }

        /// <summary>Polls in Update, as the conversations do.</summary>
        sealed class Listener : MonoBehaviour
        {
            public int heard;
            void Update() { if (DialogueAdvance.Pressed) heard++; }
        }

        IEnumerator Tap(InputDevice device, IInputStateTypeInfo down, IInputStateTypeInfo up)
        {
            QueueAny(device, down);
            for (int i = 0; i < 3; i++) yield return null;
            QueueAny(device, up);
            for (int i = 0; i < 3; i++) yield return null;
        }

        static void QueueAny(InputDevice device, IInputStateTypeInfo state)
        {
            if (state is KeyboardState k) InputSystem.QueueStateEvent(device, k);
            else if (state is MouseState m) InputSystem.QueueStateEvent(device, m);
        }

        IEnumerator TapE() => Tap(keyboard, new KeyboardState(Key.E), new KeyboardState());
        IEnumerator Click() => Tap(mouse, new MouseState().WithButton(MouseButton.Left, true), new MouseState());

        [UnityTest]
        public IEnumerator EAndALeftClickBothReadOn()
        {
            var listener = menu.AddComponent<Listener>();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(listener.heard, Is.Zero, "nothing pressed");
            yield return TapE();
            Assert.That(listener.heard, Is.EqualTo(1), "E reads on once");
            yield return Click();
            Assert.That(listener.heard, Is.EqualTo(2), "a left click reads on once");
        }

        [UnityTest]
        public IEnumerator AClickOnAMenuDoesNotReadOn()
        {
            var listener = menu.AddComponent<Listener>();
            for (int i = 0; i < 3; i++) yield return null;
            GamePause.Suspend(menu, true);
            yield return Click();
            yield return TapE();
            Assert.That(listener.heard, Is.Zero, "a menu is in front");
            GamePause.Suspend(menu, false);
            for (int i = 0; i < 3; i++) yield return null;
            yield return Click();
            Assert.That(listener.heard, Is.EqualTo(1), "once the menu is gone a click reads on");
        }
    }
}
#endif
