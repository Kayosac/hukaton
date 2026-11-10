using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Hakaton.EnemyDebug.Tests
{
    public class EnemyDebugAssetTests
    {
        const string Root = "Assets/EnemyDebug/";
        [Test]
        public void CatalogueIncludesEveryGameplayCreatureOnceAndFindsNewPrefabsWithoutRegistration()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<EnemyDebugCatalogue>(Root + "Data/Enemy Catalogue.asset");
            var all = catalogue.Discover();
            Assert.That(all.Length, Is.GreaterThanOrEqualTo(15));
            Assert.That(all.Distinct().Count(), Is.EqualTo(all.Length));
            Assert.That(all.Any(p => p.name == "Bee swarm"), Is.True);
            const string copy = "Assets/EnemyDebugDiscoveryTest.prefab";
            try
            {
                Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(all[0]), copy), Is.True);
                AssetDatabase.ImportAsset(copy);
                var added = catalogue.Discover();
                Assert.That(added.Length, Is.EqualTo(all.Length + 1));
                Assert.That(added.Any(p => AssetDatabase.GetAssetPath(p) == copy), Is.True);
            }
            finally { AssetDatabase.DeleteAsset(copy); }
            Assert.That(catalogue.Discover().Length, Is.EqualTo(all.Length));
        }

        [Test]
        public void SavedSceneContainsEditablePrefabInstancesAndBakedNavigationWithoutPreviewPhysics()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(Root + "Scenes/EnemyDebug.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(Root + "Scenes/EnemyDebug.unity", OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var sandbox = roots.SelectMany(g => g.GetComponentsInChildren<EnemyDebugSandbox>(true)).Single();
                Assert.That(sandbox.authoredPreviews.Length, Is.EqualTo(sandbox.catalogue.Discover().Length));
                Assert.That(sandbox.world.motor, Is.SameAs(sandbox.motor));
                Assert.That(sandbox.world.vitals.world, Is.SameAs(sandbox.world));
                Assert.That(sandbox.world.workshop.gameObject.activeSelf, Is.False);
                Assert.That(sandbox.world.workshop.config, Is.Not.Null);
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(sandbox), Is.True);
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(sandbox.motor), Is.True);
                Assert.That(roots.SelectMany(g => g.GetComponentsInChildren<NavMeshSurface>()).Single().navMeshData, Is.Not.Null);
                foreach (var preview in sandbox.authoredPreviews)
                {
                    Assert.That(PrefabUtility.IsPartOfPrefabInstance(preview), Is.True);
                    Assert.That(preview.GetComponentsInChildren<Collider>(true), Is.Empty);
                    Assert.That(preview.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                    Assert.That(preview.GetComponentsInChildren<NavMeshAgent>(true), Is.Empty);
                    Assert.That(preview.GetComponentsInChildren<WorldCreature>(true), Is.Empty);
                    Assert.That(preview.view.idle, Is.SameAs(preview.source.view.idle));
                    Assert.That(preview.transform.localScale, Is.EqualTo(preview.source.transform.localScale));
                }
                foreach (var transform in roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
                var canvas = sandbox.title.canvas;
                Assert.That(canvas.pixelPerfect, Is.True);
                Assert.That(canvas.GetComponent<CanvasScaler>().scaleFactor, Is.EqualTo(1));
                foreach (var label in canvas.GetComponentsInChildren<TMP_Text>(true))
                {
                    Assert.That(label.enableAutoSizing, Is.False);
                    Assert.That(label.fontSize % label.font.faceInfo.pointSize, Is.Zero);
                    Assert.That(label.transform.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(label.fontSharedMaterial.shader.name, Is.EqualTo("TextMeshPro/Mobile/Bitmap"));
                }
                Assert.That(EditorBuildSettings.scenes.Any(s => s.path == scene.path), Is.False, "Debug scene must not enter the game build by default.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
        }
    }
}
