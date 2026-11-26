using System;
using System.IO;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// The outings' once-per-game marks (the forgiven first drowning…) go through the save file and back. Uses an isolated
    /// temporary directory; never reads or changes the player's save.
    /// </summary>
    public sealed class OutingFirstsSaveTests
    {
        string directory, path;

        [SetUp]
        public void CreateTemporarySaveDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "HakatonOutingFirstsTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "game.json");
        }

        [TearDown]
        public void RemoveOnlyThisTestsFiles()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void TheForgivenDrowningSurvivesSaveAndLoad()
        {
            var save = new GameSave { outingFirsts = new OutingFirstsSave { drownForgiven = true } };
            Assert.That(GameSaveStore.Save(path, save, out var error), Is.True, error);
            Assert.That(GameSaveStore.Load(path, _ => true, out var loaded, out var warning), Is.True, warning);
            Assert.That(loaded.outingFirsts, Is.Not.Null);
            Assert.That(loaded.outingFirsts.drownForgiven, Is.True);
        }

        [Test]
        public void TheWorldLinesSurviveSaveAndLoadAndOldSavesHaveNotSeenThem()
        {
            var save = new GameSave { outingFirsts = new OutingFirstsSave { wilderRingLineShown = true, mimicLineShown = true } };
            Assert.That(GameSaveStore.Save(path, save, out var error), Is.True, error);
            Assert.That(GameSaveStore.Load(path, _ => true, out var loaded, out var warning), Is.True, warning);
            Assert.That(loaded.outingFirsts.wilderRingLineShown && loaded.outingFirsts.mimicLineShown, Is.True);
            var older = JsonUtility.FromJson<OutingFirstsSave>("{\"drownForgiven\":true,\"anthillLineShown\":true}");
            Assert.That(older.wilderRingLineShown || older.mimicLineShown, Is.False, "a save from before the marks: not shown yet");
            Assert.That(older.drownForgiven && older.anthillLineShown, Is.True);
        }

        [Test]
        public void ANewGameHasNotDrownedYet()
        {
            Assert.That(new OutingFirstsSave().drownForgiven, Is.False);
            var older = JsonUtility.FromJson<GameSave>("{\"version\":1,\"coins\":0}");
            Assert.That(older.outingFirsts == null || !older.outingFirsts.drownForgiven, Is.True,
                "A save from before the mark has no forgiven drowning recorded.");
        }
    }
}
