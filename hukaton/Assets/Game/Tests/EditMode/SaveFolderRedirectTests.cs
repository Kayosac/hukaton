using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// Tests never use the player's own save folder: <see cref="GameSaveFolder"/> redirects it to a temporary folder and
    /// back, and every test that loads the game scene uses that redirect instead of touching the player's files.
    /// </summary>
    public sealed class SaveFolderRedirectTests
    {
        [TearDown]
        public void NeverLeaveARedirect() => GameSaveFolder.EndTemporaryForTests();

        [Test]
        public void TheRedirectPointsAtAFreshTemporaryFolderAndIsUndoneCompletely()
        {
            string player = Path.GetFullPath(GameSaveFolder.PlayerFolder);
            Assert.That(GameSaveFolder.IsRedirected, Is.False);
            Assert.That(Path.GetFullPath(GameSaveFolder.Current), Is.EqualTo(player), "Without a test the save is the player's.");

            string folder = GameSaveFolder.UseTemporaryForTests("redirect test");
            Assert.That(GameSaveFolder.IsRedirected, Is.True);
            Assert.That(Path.GetFullPath(GameSaveFolder.Current), Is.EqualTo(Path.GetFullPath(folder)));
            Assert.That(Path.GetFullPath(folder), Does.StartWith(Path.GetFullPath(GameSaveFolder.TemporaryRoot)));
            Assert.That(Path.GetFullPath(folder), Does.Not.StartWith(player));
            Assert.That(Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any(), Is.True, "A new, empty folder.");
            File.WriteAllText(Path.Combine(folder, "hakaton-v1.json"), "{}");

            string second = GameSaveFolder.UseTemporaryForTests("redirect test");
            Assert.That(second, Is.Not.EqualTo(folder), "Every test gets its own folder.");
            Assert.That(Directory.Exists(folder), Is.False, "A folder left by an earlier test is cleared away.");

            GameSaveFolder.EndTemporaryForTests();
            Assert.That(GameSaveFolder.IsRedirected, Is.False);
            Assert.That(Path.GetFullPath(GameSaveFolder.Current), Is.EqualTo(player));
            Assert.That(Directory.Exists(second), Is.False, "The temporary folder goes with the redirect.");
        }

        [Test]
        public void EveryTestThatLoadsTheGameSavesIntoATemporaryFolder()
        {
            string self = nameof(SaveFolderRedirectTests) + ".cs";
            var sources = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories)
                .Where(p => p.Replace('\\', '/').Contains("/Tests/") && Path.GetFileName(p) != self).ToArray();
            Assert.That(sources, Is.Not.Empty);
            // Tests that run the game (load its scene in Play mode); a test that only reads the scene file saves nothing.
            var gameScene = sources.Where(p => { string t = File.ReadAllText(p); return t.Contains("Game/Scenes/Hakaton.unity") && t.Contains("LoadScene"); }).ToArray();
            Assert.That(gameScene, Is.Not.Empty);
            foreach (string path in gameScene)
            {
                string text = File.ReadAllText(path);
                Assert.That(text, Does.Contain("GameSaveFolder.UseTemporaryForTests("), path + " loads the game without a temporary save folder.");
                Assert.That(text, Does.Contain("GameSaveFolder.EndTemporaryForTests()"), path + " never ends its temporary save folder.");
            }
            foreach (string path in sources)
                Assert.That(File.ReadAllText(path), Does.Not.Contain("Application.persistentDataPath"),
                    path + " reaches into the player's own folder; use GameSaveFolder.UseTemporaryForTests instead.");
        }
    }
}
