using System.Linq;
using Hakaton.Decks;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Hakaton.ExpeditionTests
{
 public class ExpeditionRulesTests
 {
  [Test] public void ModifierFreezesChargeAndOneGestureCannotBeReversedOrReopened(){var p=new ThrowPreparation();p.Begin();p.Tick(.4f,false,0,1,200);p.Tick(.5f,true,80,1,200);Assert.That(p.Charge,Is.EqualTo(.4f).Within(.001));Assert.That(p.Spin,Is.EqualTo(.4f));p.Tick(.1f,true,-80,1,200);p.Tick(.1f,false,0,1,200);p.Tick(.1f,true,200,1,200);Assert.That(p.Spin,Is.EqualTo(.4f));Assert.That(p.GestureUsed,Is.True);Assert.That(p.Charge,Is.EqualTo(.5f).Within(.001));p.Cancel();Assert.That(p.Active,Is.False);Assert.That(p.Spin,Is.Zero);}
  [Test] public void FullyChargedThrowWaitsForRelease(){var p=new ThrowPreparation();p.Begin();p.Tick(10,false,0,1,200);Assert.That(p.Charge,Is.EqualTo(1));Assert.That(p.Active,Is.True);}
  [Test] public void SavedSceneHasWorldPlayerPrefabsAndAllDependencies(){var setup=EditorSceneManager.GetSceneManagerSetup();try{var scene=EditorSceneManager.OpenScene("Assets/Decks/Scenes/DeckPrototype.unity");var roots=scene.GetRootGameObjects();var world=roots.SelectMany(r=>r.GetComponentsInChildren<ExpeditionWorld>()).Single();Assert.That(world.finds.Length,Is.GreaterThanOrEqualTo(10));Assert.That(world.obstacles.Length,Is.EqualTo(2));Assert.That(world.creatures.Length,Is.EqualTo(3));Assert.That(world.projectilePrefab,Is.Not.Null);Assert.That(PrefabUtility.GetPrefabAssetType(world.motor.gameObject),Is.EqualTo(PrefabAssetType.Variant));Assert.That(world.control,Is.SameAs(world.workshop.control));Assert.That(world.hand.cards.Length,Is.EqualTo(5));Assert.That(world.hud.canvas.pixelPerfect,Is.True);Assert.That(world.hud.canvas.enabled,Is.False);Assert.That(world.hud.chargeFill.sprite,Is.Not.Null);Assert.That(world.hud.staminaFill.sprite,Is.Not.Null);Assert.That(world.hud.heartFills.All(h=>h.sprite!=null),Is.True);foreach(var tr in roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)))Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(tr.gameObject),Is.Zero,tr.name);foreach(var c in world.creatures){Assert.That(c.world,Is.SameAs(world));Assert.That(c.trophyPrefab,Is.Not.Null);}Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>("Assets/Expedition/Data/Quiet Hollow NavMesh.asset"),Is.Not.Null);}finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}}
 }
}
