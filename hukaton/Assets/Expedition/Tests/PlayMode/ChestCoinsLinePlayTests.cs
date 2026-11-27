#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Expeditions.Tests.PlayMode
{
    /// <summary>
    /// Balance stage 10: the first chest opened on an outing says once per game «Монеты из сундука идут в кошелёк, не в
    /// выручку.» A mimic's chest never says it, not even a frozen one opened for the plain payout. In the mimic lab
    /// (Assets/ChestArt/Scenes/MimicLab.unity), with a running outing.
    /// </summary>
    public sealed class ChestCoinsLinePlayTests
    {
        ExpeditionWorld world;

        [UnitySetUp]
        public IEnumerator LoadLab()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/ChestArt/Scenes/MimicLab.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            world = Object.FindFirstObjectByType<ExpeditionWorld>();
            Assert.That(world, Is.Not.Null);
            float until = Time.time + 3;
            while (!world.Running && Time.time < until) yield return null;
            Assert.That(world.Running, "the lab opens an outing by itself");
            Assert.That(world.InPrologue, Is.False);
        }

        TreasureChest Plain(Vector3 at)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<TreasureChest>("Assets/Expedition/Prefabs/Chests/Chest 2 Common.prefab");
            var chest = Object.Instantiate(prefab, at, Quaternion.identity);
            chest.world = world;
            return chest;
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        [UnityTest]
        public IEnumerator TheFirstChestSaysWhereItsCoinsGoOnceAndAMimicNever()
        {
            world.RestoreFirsts(null);
            Assert.That(world.Firsts.chestLineShown, Is.False);

            // A frozen mimic opens for the plain payout: no line.
            var mimic = Object.FindObjectsByType<ChestMimic>(FindObjectsSortMode.None).First(m => m.chest.tier.rank == 2);
            world.motor.Teleport(mimic.transform.position - mimic.transform.forward * 1.8f, Quaternion.LookRotation(mimic.transform.forward));
            yield return null;
            mimic.Freeze(10);
            Assert.That(mimic.chest.Open(), Is.True);
            yield return Seconds(3f);
            Assert.That(mimic.chest.Opened, Is.True);
            Assert.That(world.Notice, Is.Not.EqualTo(ExpeditionWorld.ChestCoinsLine), "not on a mimic");
            Assert.That(world.Firsts.chestLineShown, Is.False);

            // The first plain chest: the line, held its time.
            var at = world.motor.transform.position + world.motor.transform.right * 4;
            var first = Plain(at);
            yield return null;
            Assert.That(first.Open(), Is.True);
            float until = Time.time + 6;
            while (world.Notice != ExpeditionWorld.ChestCoinsLine && Time.time < until) yield return null;
            Assert.That(world.Notice, Is.EqualTo("Монеты из сундука идут в кошелёк, не в выручку."));
            Assert.That(world.Firsts.chestLineShown, Is.True, "marked for the save");
            Assert.That(world.SaveFirsts().chestLineShown, Is.True);
            yield return Seconds(ExpeditionWorld.FirstLineSeconds + .2f);

            // The second: nothing.
            world.Announce("другое", 1);
            var second = Plain(at + world.motor.transform.right * 3);
            yield return null;
            Assert.That(second.Open(), Is.True);
            yield return Seconds(3f);
            Assert.That(second.Opened, Is.True);
            Assert.That(world.Notice, Is.Not.EqualTo(ExpeditionWorld.ChestCoinsLine), "once per game");
            Assert.That(world.NoteChestOpened(), Is.False);
            Object.Destroy(first.gameObject);
            Object.Destroy(second.gameObject);
        }
    }
}
#endif
