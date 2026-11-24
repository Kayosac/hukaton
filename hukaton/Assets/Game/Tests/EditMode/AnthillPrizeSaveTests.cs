using System;
using System.IO;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// The first anthill prize chest opened is saved with the game (until then every anthill is small). Uses an isolated
    /// temporary directory; never reads or changes the player's save.
    /// </summary>
    public sealed class AnthillPrizeSaveTests
    {
        string directory, path;

        [SetUp]
        public void CreateTemporarySaveDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "HakatonAnthillPrizeTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "game.json");
        }

        [TearDown]
        public void RemoveOnlyThisTestsFiles()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void TheFirstOpenedPrizeChestSurvivesSaveAndLoadAndOlderSavesHaveNone()
        {
            Assert.That(new GameSave().anthillPrizeOpened, Is.False, "a new game has opened no anthill prize chest");
            Assert.That(GameSaveStore.Save(path, new GameSave { anthillPrizeOpened = true }, out var error), Is.True, error);
            Assert.That(GameSaveStore.Load(path, s => s != null, out var loaded, out var warning), Is.True, warning);
            Assert.That(loaded.anthillPrizeOpened, Is.True);

            // A save written before the flag existed: no prize opened yet, the next anthills are small.
            var older = JsonUtility.FromJson<GameSave>("{\"version\":1,\"coins\":0,\"queenSlain\":true}");
            Assert.That(older.queenSlain, Is.True);
            Assert.That(older.anthillPrizeOpened, Is.False);
        }

        [Test]
        public void TheProgressIsNotedOnceAndRestoredSilently()
        {
            bool was = DungeonProgress.AnthillPrizeOpened;
            int changes = 0;
            void Count() => changes++;
            DungeonProgress.Changed += Count;
            try
            {
                DungeonProgress.Restore(false);
                Assert.That(DungeonProgress.AnthillPrizeOpened, Is.False);
                DungeonProgress.NoteAnthillPrizeOpened();
                DungeonProgress.NoteAnthillPrizeOpened();
                Assert.That(DungeonProgress.AnthillPrizeOpened, Is.True);
                Assert.That(changes, Is.EqualTo(1), "noted once: the save is marked dirty once");
                DungeonProgress.Restore(false);
                DungeonProgress.Restore(true);
                Assert.That(changes, Is.EqualTo(1), "restoring from a save is not a change");
                Assert.That(DungeonProgress.AnthillPrizeOpened, Is.True);
            }
            finally
            {
                DungeonProgress.Changed -= Count;
                DungeonProgress.Restore(was);
            }
        }
    }
}
