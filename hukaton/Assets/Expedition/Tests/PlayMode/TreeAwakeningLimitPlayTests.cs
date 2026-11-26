#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using Hakaton.Expeditions.Events;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Expeditions.Tests.PlayMode
{
    /// <summary>
    /// Balance stage 9, «Оживление дерева»: at most two awakened trees live at once. The card wakes three giant wall trees
    /// of a forest maze chunk one after another (the card's own effect on each trunk): the third crumbles the first — its
    /// death, no loot — and the two newer ones stand. The places all three stood in stay open: the hidden wall tree is a
    /// way through the maze, as before. In the beast brawl lab (a running outing).
    /// </summary>
    public sealed class TreeAwakeningLimitPlayTests
    {
        const string CardPath = "Assets/Game/Data/tree_awakening.asset";
        const string MazePath = "Assets/WorldAssembly/Chunks/maze-dead/maze-dead forest.prefab";
        static readonly Vector3 Spot = new Vector3(30, 0, -75);

        ExpeditionWorld world;
        readonly List<GameObject> made = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator LoadLab()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Expedition/Scenes/BeastBrawlLab.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            world = Object.FindFirstObjectByType<ExpeditionWorld>();
            Assert.That(world, Is.Not.Null);
            float until = Time.time + 3;
            while (!world.Running && Time.time < until) yield return null;
            Assert.That(world.Running, "the lab opens an outing by itself");
            foreach (var showcase in Object.FindObjectsByType<BeastBrawl>(FindObjectsSortMode.None)) Object.Destroy(showcase.gameObject);
            made.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            foreach (var thing in made) if (thing) Object.Destroy(thing);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheThirdTreeCrumblesTheFirstAndEveryPlaceStaysOpen()
        {
            var definition = AssetDatabase.LoadAssetAtPath<CardDefinition>(CardPath);
            var effect = definition.effectAsset as TreeAwakeningEffect;
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.livingLimit, Is.EqualTo(2));

            // A forest maze chunk far from the lab's own things, the player standing in it.
            var maze = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MazePath), new Vector3(2000, 0, 2000), Quaternion.identity);
            made.Add(maze);
            Physics.SyncTransforms();
            var ground = maze.transform.Find("Ground");
            var giants = maze.transform.Find("Forest/Giant trees");
            Assert.That(giants, Is.Not.Null);
            var trees = giants.Cast<Transform>().Where(t => t.gameObject.activeSelf && !t.name.Contains("Hollow"))
                .Select(t => t.GetComponent<AwakenableTree>()).Where(t => t).Take(3).ToList();
            Assert.That(trees, Has.Count.EqualTo(3), "three wall trees");
            Vector3 centre = maze.transform.position + new Vector3(8, 0, 8);
            if (Physics.Raycast(centre + Vector3.up * 20, Vector3.down, out var hit, 40, ~(1 << 2), QueryTriggerInteraction.Ignore)) centre = hit.point;
            world.motor.Teleport(centre + Vector3.up * .05f, Quaternion.LookRotation(Vector3.forward));
            yield return null;

            List<string> Blocking(Vector3 foot) => Physics.OverlapCapsule(foot + Vector3.up * .78f, foot + Vector3.up * 1.52f, .28f, ~(1 << 4), QueryTriggerInteraction.Ignore)
                .Where(c => c.transform.IsChildOf(maze.transform) && !(ground && c.transform.IsChildOf(ground)))
                .Select(c => c.name).ToList();
            var feet = trees.Select(t => t.FootPosition).ToList();
            foreach (var foot in feet) Assert.That(Blocking(foot), Is.Not.Empty, "a wall tree stops a walker where it stands");

            var allies = new List<TreeAlly>();
            foreach (var tree in trees)
            {
                var collider = tree.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger);
                Vector3 point = collider.bounds.center;
                effect.Apply(new ImpactContext(world, null, definition, collider, point, Vector3.up, Vector3.forward));
                yield return null;
                var ally = world.Allies.All.OfType<TreeAlly>().Where(a => a && a.Origin == tree).OrderByDescending(a => a.AwakeningOrder).FirstOrDefault();
                Assert.That(ally, Is.Not.Null, "the card woke " + tree.name);
                Assert.That(tree.Standing, Is.False, "the standing tree is hidden for the outing");
                allies.Add(ally);
                yield return new WaitForSeconds(.3f);
            }

            Assert.That(allies[0].IsAwake, Is.False, "the third tree crumbles the first");
            Assert.That(allies[0].State, Is.EqualTo(TreeAllyState.Dying), "with the tree's own death");
            Assert.That(allies[1].IsAwake && allies[2].IsAwake, Is.True, "the two newer ones stand");
            Assert.That(world.Allies.All.OfType<TreeAlly>().Count(a => a && a.IsAwake), Is.EqualTo(2));
            Assert.That(world.Notice, Is.Not.EqualTo("Оживлённое дерево погибло"), "no «погибло» notice for a crumbled tree");

            float until = Time.time + 5;
            while (allies[0] && allies[0].gameObject.activeSelf && Time.time < until) yield return null;
            Assert.That(allies[0].gameObject.activeSelf, Is.False, "it fades away");
            Physics.SyncTransforms();
            for (int i = 0; i < trees.Count; i++)
            {
                Assert.That(trees[i].Standing, Is.False, "tree " + i + " stays hidden");
                Assert.That(Blocking(feet[i]), Is.Empty, "the place of tree " + i + " stays open");
            }
            Assert.That(allies[1].IsAwake && allies[2].IsAwake, Is.True);
        }
    }
}
#endif
