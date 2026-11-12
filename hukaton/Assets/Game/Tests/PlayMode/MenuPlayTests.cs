using System.Collections;
using System.Collections.Generic;
using System.IO;
using Hakaton.Character;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>The main menu over the real game, the pause and the exit rules, on the saved Hakaton scene.</summary>
    public sealed class MenuPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        readonly Dictionary<string, byte[]> playerSaves = new Dictionary<string, byte[]>();
        string saveFolder;
        GameLoop game;
        MainMenu main;
        PauseMenu pause;

        [UnitySetUp]
        public IEnumerator LoadWithoutASave()
        {
            saveFolder = Path.Combine(Application.persistentDataPath, "Hakaton");
            playerSaves.Clear();
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) { playerSaves[file] = File.ReadAllBytes(file); File.Delete(file); }
            MainMenu.SkipNextLaunch = false;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
            game = Object.FindAnyObjectByType<GameLoop>();
            main = Object.FindAnyObjectByType<MainMenu>();
            pause = Object.FindAnyObjectByType<PauseMenu>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Menu test teardown"));
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.name != "Menu test teardown") yield return SceneManager.UnloadSceneAsync(scene);
            }
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
            Time.timeScale = 1;
            AudioListener.pause = false;
        }

        [UnityTest]
        public IEnumerator TheMenuLiesOverTheCaveAndHandsOverControlOnTheSpot()
        {
            Assert.That(main.IsOpen, Is.True, "A launch opens on the main menu.");
            Assert.That(game.InCave && game.FreshStart && !game.LoadedFromSave, Is.True, "No save: the cave waits behind the menu.");
            Assert.That(main.continueButton.interactable, Is.False, "Nothing to continue.");
            Assert.That(GamePause.Suspended, Is.True, "Gameplay waits for the menu.");
            Assert.That(game.player.control.GameplayActive, Is.False);
            foreach (var hud in main.gameplayHud) Assert.That(hud.alpha, Is.Zero, hud.name + " is hidden under the menu.");
            var eye = game.player.view.pitchPivot;
            Vector3 position = eye.position;
            Quaternion rotation = eye.rotation;

            if (MenuProgress.TutorialCompleted)
            {
                main.newGameButton.onClick.Invoke();
                Assert.That(main.dialog.IsOpen && main.dialog.skipRow.activeSelf, Is.True, "A finished tutorial may be skipped.");
                main.dialog.confirmButton.onClick.Invoke();
            }
            else main.newGameButton.onClick.Invoke();
            yield return null;
            Assert.That(main.IsOpen, Is.False);
            Assert.That(GamePause.Suspended, Is.False);
            Assert.That(game.player.control.HasModalOwner, Is.False);
            Assert.That(Vector3.Distance(eye.position, position), Is.LessThan(.05f), "The camera stays where the menu showed it (only the body settles).");
            Assert.That(Quaternion.Angle(eye.rotation, rotation), Is.LessThan(.5f));
            yield return new WaitForSecondsRealtime(.5f);
            foreach (var hud in main.gameplayHud) Assert.That(hud.alpha, Is.EqualTo(1).Within(.001f), hud.name + " is back.");
        }

        [UnityTest]
        public IEnumerator TheMenuMusicPlaysOnceAndFadesOutWhenPlayBegins()
        {
            var music = main.music;
            Assert.That(music && music.clip, Is.True, "The menu prefab wires its track.");
            Assert.That(music.isPlaying, Is.True, "The track starts with the menu.");
            Assert.That(music.loop, Is.False, "The menu track plays once.");
            Assert.That(music.outputAudioMixerGroup && music.outputAudioMixerGroup.name == "Music", Is.True,
                "The settings' music slider controls it.");
            float volume = music.volume;
            main.newGameButton.onClick.Invoke();
            if (main.dialog.IsOpen) main.dialog.confirmButton.onClick.Invoke();
            yield return null;
            Assert.That(main.IsOpen, Is.False);
            yield return new WaitForSecondsRealtime(main.musicFadeOutSeconds * .5f);
            Assert.That(music.isPlaying && music.volume < volume, Is.True, "It fades rather than cutting off.");
            yield return new WaitForSecondsRealtime(main.musicFadeOutSeconds * .5f + .3f);
            Assert.That(music.isPlaying, Is.False, "Gone once play begins.");
        }

        [UnityTest]
        public IEnumerator PauseFreezesTheWorldAndLeavingTheCaveAsksFirst()
        {
            main.newGameButton.onClick.Invoke();
            if (main.dialog.IsOpen) main.dialog.confirmButton.onClick.Invoke();
            yield return WaitFor(() => game.player.control.PauseAllowed && !game.fade.Busy && game.fade.Amount < .01f, 5);

            pause.Open();
            Assert.That(pause.IsOpen && GamePause.Paused, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(AudioListener.pause, Is.True);
            Assert.That(game.player.control.GameplayActive, Is.False);

            pause.settingsButton.onClick.Invoke();
            Assert.That(pause.settings.IsOpen, Is.True);
            pause.settings.Back();
            Assert.That(pause.IsOpen && !pause.settings.IsOpen, Is.True, "Settings go back to the pause.");

            pause.menuButton.onClick.Invoke();
            Assert.That(pause.dialog.IsOpen, Is.True, "Leaving the cave loses it: the menu asks first.");
            pause.dialog.cancelButton.onClick.Invoke();
            Assert.That(pause.IsOpen && !pause.dialog.IsOpen, Is.True, "«Остаться» stays in the pause.");

            pause.Resume();
            Assert.That(GamePause.Paused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(AudioListener.pause, Is.False);
            Assert.That(game.player.control.HasModalOwner, Is.False);
        }

        [UnityTest]
        public IEnumerator SettingsApplyAtOnce()
        {
            float master = GameSettings.Master, sensitivity = GameSettings.Sensitivity;
            bool showFps = GameSettings.ShowFps;
            var counter = Object.FindAnyObjectByType<FpsCounter>();
            Assert.That(counter && counter.canvas && counter.label && counter.readout, Is.True, "The saved scene carries the wired FPS counter.");
            Assert.That(counter.clearOf && counter.clearOfLayout, Is.True, "It knows the shop's shift clock it sits under.");
            Assert.That(main.settings.showFps, Is.Not.Null, "The settings window has the «Счётчик FPS» box.");
            try
            {
                GameSettings.Master = .37f;
                Assert.That(AudioListener.volume, Is.EqualTo(.37f).Within(.001f));
                GameSettings.Sensitivity = 2;
                Assert.That(game.player.view.sensitivityScale, Is.EqualTo(2).Within(.001f));
                GameSettings.Sensitivity = 99;
                Assert.That(GameSettings.Sensitivity, Is.EqualTo(GameSettings.MaxSensitivity));

                GameSettings.ShowFps = false;
                Assert.That(counter.canvas.enabled, Is.False, "Off: the readout is hidden.");
                Assert.That(counter.isActiveAndEnabled, Is.True, "The counter itself stays active to hear the setting.");
                main.settings.showFps.SetIsOnWithoutNotify(false);
                main.settings.showFps.isOn = true;
                Assert.That(GameSettings.ShowFps, Is.True, "The box writes the setting.");
                Assert.That(counter.canvas.enabled, Is.True, "Ticking «Счётчик FPS» shows the readout at once.");
                Assert.That(counter.canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(counter.canvas.sortingOrder, Is.GreaterThan(main.canvas.sortingOrder).And.GreaterThan(pause.canvas.sortingOrder),
                    "Above the menus.");
                yield return new WaitForSecondsRealtime(counter.refreshSeconds + .2f);
                Assert.That(counter.label.text, Does.Match(@"^\d+ FPS$"), "Real frames counted in real time, under the paused menu too.");
                // In screen pixels from the top: the clock hangs from the top of the shop HUD's layout, the readout from
                // the top of its own (whole-number) one.
                var screen = new Vector2(Screen.width, Screen.height);
                var clock = counter.clearOf;
                float clockBottom = (-clock.anchoredPosition.y + clock.rect.height * clock.pivot.y) * counter.clearOfLayout.ScaleFor(screen);
                float readoutTop = -counter.readout.anchoredPosition.y * counter.layout.ScaleFor(screen);
                Assert.That(readoutTop, Is.GreaterThan(clockBottom), "The readout sits below the shift clock's footprint.");
                main.settings.showFps.isOn = false;
                Assert.That(GameSettings.ShowFps || counter.canvas.enabled, Is.False, "Unticking hides it again.");
            }
            finally
            {
                GameSettings.Master = master;
                GameSettings.Sensitivity = sensitivity;
                GameSettings.ShowFps = showFps;
            }
            yield return null;
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(condition(), Is.True, "Timed out");
        }
    }
}
