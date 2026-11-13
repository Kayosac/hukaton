#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// The player's debuffs in the real scene (board «Дебаффы»): each one lands with its badge, the rules hold
    /// (refresh, Fragile, Rot, water puts out fire, the cure, no debuffs under immortality) and the outing's end clears them.
    /// </summary>
    public sealed class DebuffPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        readonly Dictionary<string, byte[]> playerSaves = new Dictionary<string, byte[]>();
        string saveFolder;
        GameLoop game;
        ExpeditionWorld world;
        PlayerStatuses statuses;

        [UnitySetUp]
        public IEnumerator DepartOnAnOuting()
        {
            saveFolder = Path.Combine(Application.persistentDataPath, "Hakaton");
            playerSaves.Clear();
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder))
                {
                    playerSaves[file] = File.ReadAllBytes(file);
                    File.Delete(file);
                }
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
            game = Object.FindAnyObjectByType<GameLoop>();
            game.SkipTutorial();
            world = game.expedition;
            statuses = world.statuses;
            Assert.That(game.TryDepart(), Is.True);
            yield return WaitFor(() => world.Running, 5);
            Assert.That(world.Running, Is.True);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Debuff test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        [UnityTest]
        public IEnumerator EveryDebuffLandsWithItsBadgeAndTheOutingsEndClearsThemAll()
        {
            Assert.That(statuses, Is.Not.Null, "The scene wires the player's debuffs.");
            Assert.That(statuses.definitions.Select(d => d.kind).Distinct().Count(), Is.EqualTo(12), "One definition per debuff.");
            foreach (var definition in statuses.definitions)
                Assert.That(statuses.Apply(definition), Is.True, definition.displayName);
            yield return null;
            yield return null;
            Assert.That(statuses.Count, Is.EqualTo(12), "Any number at once.");
            var badges = world.hud.statusRow.GetComponentsInChildren<PlayerStatusBadge>().Where(b => b.source == PlayerStatusBadge.Source.Status).ToArray();
            Assert.That(badges.Length, Is.EqualTo(12), "A badge per debuff.");
            Assert.That(world.motor.MoveSpeedScale, Is.LessThan(1), "Slow slows the player.");
            Assert.That(statuses.deafFilter.enabled, Is.True, "Deafen muffles the sound.");
            Assert.That(world.viewCamera.projectionMatrix, Is.Not.EqualTo(Matrix4x4.Perspective(world.viewCamera.fieldOfView,
                world.viewCamera.aspect, world.viewCamera.nearClipPlane, world.viewCamera.farClipPlane)), "Warp bends the view.");
            Assert.That(world.vitals.MaximumHalfHearts, Is.EqualTo(world.settings.maximumHalfHearts - 2), "Rot takes a heart off the maximum.");
            yield return new WaitForSeconds(.5f);

            world.ReturnToShop(false);
            yield return WaitFor(() => game.Phase == GamePhase.Shop && !world.Returning, 10);
            Assert.That(statuses.Count, Is.Zero, "The outing's end clears every debuff.");
            Assert.That(statuses.deafFilter.enabled, Is.False);
            Assert.That(world.vitals.RotHalfHearts, Is.Zero);
            Assert.That(world.motor.StatusSpeedScale, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RulesOfFragileRotRefreshFireAndCure()
        {
            var vitals = world.vitals;
            // A repeat only refreshes the timer.
            statuses.Apply(PlayerStatusKind.Fragile, 4);
            yield return new WaitForSeconds(.3f);
            statuses.Apply(PlayerStatusKind.Fragile, 4);
            Assert.That(statuses.Remaining(PlayerStatusKind.Fragile), Is.GreaterThan(3.9f).And.LessThanOrEqualTo(4.01f));

            // Fragile: double damage, no protection after the hit.
            int hearts = vitals.HalfHearts;
            Assert.That(vitals.Hurt(1, Vector3.zero, null), Is.True);
            Assert.That(vitals.HalfHearts, Is.EqualTo(hearts - 2));
            Assert.That(vitals.Invulnerable, Is.False);
            statuses.Remove(PlayerStatusKind.Fragile);
            vitals.Heal(vitals.MaximumHalfHearts);

            // Rot: another heart per hit, one heart always stays.
            for (int i = 0; i < 6; i++) statuses.Apply(PlayerStatusKind.Rot);
            Assert.That(vitals.MaximumHalfHearts, Is.EqualTo(2));
            Assert.That(vitals.HalfHearts, Is.LessThanOrEqualTo(2));
            Assert.That(statuses.IsPermanent(PlayerStatusKind.Rot), Is.True);
            vitals.ClearRot();
            vitals.Heal(vitals.MaximumHalfHearts);

            // Fire: water puts it out, and a wet player does not catch fire.
            Assert.That(statuses.Apply(PlayerStatusKind.Burn), Is.True);
            WetStatus.Soak(world.motor, world, 2);
            yield return null;
            Assert.That(statuses.Has(PlayerStatusKind.Burn), Is.False, "Water puts the fire out.");
            Assert.That(statuses.Apply(PlayerStatusKind.Burn), Is.False, "A wet player does not catch fire.");
            world.motor.GetComponentInParent<WetStatus>().Dry();

            // Poison ticks and the cure ends it.
            Assert.That(statuses.Apply(PlayerStatusKind.Poison, 10), Is.True);
            hearts = vitals.HalfHearts;
            yield return new WaitForSeconds(statuses.Definition(PlayerStatusKind.Poison).interval + .2f);
            Assert.That(vitals.HalfHearts, Is.LessThan(hearts), "Poison takes hearts.");
            statuses.Healed();
            Assert.That(statuses.Has(PlayerStatusKind.Poison), Is.False, "The cure ends Poison.");

            // Immortality keeps them off.
            vitals.Immortal = true;
            Assert.That(statuses.Apply(PlayerStatusKind.Mark), Is.False);
            vitals.Immortal = false;

            // Leaky pocket: coins leave the purse.
            world.CollectCoins(5, Color.yellow);
            Assert.That(world.LoseCoins(3), Is.EqualTo(3));
            Assert.That(world.Coins, Is.EqualTo(2));
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
