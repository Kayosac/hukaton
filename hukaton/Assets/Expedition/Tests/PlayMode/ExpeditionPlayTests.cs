using System.Collections;
using System.IO;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace Hakaton.ExpeditionTests
{
 public class ExpeditionPlayTests
 {
  ExpeditionWorld w;
  [UnitySetUp] public IEnumerator Open(){yield return SceneManager.LoadSceneAsync("DeckPrototype");w=Object.FindFirstObjectByType<ExpeditionWorld>();w.workshop.persistProgress=false;Assert.That(w.workshop.Model.TryRestore(w.workshop.config.CreateWorkshop().Save()),Is.True);w.workshop.RefreshWorkshop();yield return null;}
  IEnumerator Depart(){w.workshop.departButton.onClick.Invoke();yield return null;Assert.That(w.Running,Is.True);Assert.That(w.workshop.presentation.enabled,Is.False);Assert.That(w.hud.canvas.enabled,Is.True);Assert.That(w.control.HasModalOwner,Is.False);}
  IEnumerator Return(){while(!w.workshop.Model.Current.Get(DeckKind.Exploration).HasReturn){w.workshop.Model.Current.Switch(DeckKind.Exploration);w.thrower.Discard();yield return new WaitForSeconds(w.settings.discardCooldown+.03f);}w.workshop.Model.Current.Switch(DeckKind.Exploration);w.thrower.Begin();yield return new WaitForSeconds(w.settings.returnSeconds+.2f);Assert.That(w.Running,Is.False);Assert.That(w.workshop.presentation.enabled,Is.True);Assert.That(w.control.HasModalOwner,Is.True);}
  [UnityTest] public IEnumerator ReleaseCreatesPhysicalProjectileAndOnlyFirstCollisionAppliesEffect(){yield return Depart();w.motor.Teleport(new Vector3(0,.1f,0),Quaternion.Euler(65,0,0));yield return null;int total=w.workshop.Model.Current.Active.Total;w.thrower.Begin();Assert.That(w.workshop.Model.Current.Active.Total,Is.EqualTo(total));var p=w.thrower.Release();Assert.That(p,Is.Not.Null);Assert.That(w.workshop.Model.Current.PlayedCount,Is.EqualTo(1));Assert.That(w.thrower.Release(),Is.Null);float deadline=Time.time+3;while(w.Impacts==0 && Time.time<deadline)yield return null;Assert.That(w.Impacts,Is.EqualTo(1));Assert.That(w.workshop.Model.Current.PlayedCount,Is.EqualTo(1));LogAssert.NoUnexpectedReceived();}
  [UnityTest] public IEnumerator CancelAndSwitchPreserveIndependentHandsAndDiscardHasNoEffects(){yield return Depart();var trip=w.workshop.Model.Current;var hand=trip.Active.Hand.Select(c=>c.Id).ToArray();w.thrower.Begin();w.thrower.Preparation.Tick(.5f,true,120,1,200);w.thrower.Cancel();Assert.That(trip.PlayedCount,Is.Zero);w.thrower.SwitchDeck();w.thrower.SwitchDeck();Assert.That(trip.Active.Hand.Select(c=>c.Id),Is.EqualTo(hand));w.thrower.Discard();yield return new WaitForSeconds(1.5f);Assert.That(w.Impacts,Is.Zero);Assert.That(trip.Get(DeckKind.Combat).Hand.Count,Is.EqualTo(3));yield return Return();}
  CardCopy Copy(CardAction action){var def=w.workshop.config.cards.First(c=>c.action==action);return w.workshop.Model.Owned.First(c=>c.Spec.Id==def.id);}
  void Effect(CardAction action,Collider target,Vector3 point){var c=Copy(action);w.ApplyImpact(c,w.workshop.config.Find(c.Spec.Id),target,point,Vector3.up);}
  [UnityTest] public IEnumerator PhysicalEffectsOpenRoutesDamageCreaturesAndLaunchPlayer(){yield return Depart();var lake=w.obstacles.Single(o=>o.kind==ObstacleKind.Lake);var thorn=w.obstacles.Single(o=>o.kind==ObstacleKind.Thorns);Effect(CardAction.Freeze,lake.barrier,lake.transform.position);Assert.That(lake.Resolved,Is.True);Assert.That(lake.changed.GetComponent<Collider>().enabled,Is.True);Effect(CardAction.Fire,thorn.barrier,thorn.transform.position);Assert.That(thorn.Resolved,Is.True);var creature=w.creatures[1];int hp=creature.Health;Effect(CardAction.Fire,creature.GetComponent<Collider>(),creature.transform.position);Assert.That(creature.Health,Is.LessThan(hp));Effect(CardAction.Freeze,creature.GetComponent<Collider>(),creature.transform.position);Assert.That(creature.State,Is.EqualTo(CreatureState.Frozen));var ground=Physics.Raycast(w.motor.transform.position+Vector3.up,Vector3.down,out var hit,10)?hit.collider:lake.barrier;Effect(CardAction.Wind,ground,w.motor.transform.position);Assert.That(w.motor.VerticalSpeed,Is.GreaterThan(15));yield return null;LogAssert.NoUnexpectedReceived();}
  [UnityTest] public IEnumerator LootDepositsOnReturnAndNextOutingResetsWorldWithoutDestroyingCards(){yield return Depart();int owned=w.workshop.Model.Owned.Count;var loot=w.finds[0];loot.gameObject.SetActive(true);Assert.That(loot.PickUp(),Is.True);Assert.That(loot.PickUp(),Is.False);Effect(CardAction.Freeze,w.obstacles[0].barrier,w.obstacles[0].transform.position);yield return Return();Assert.That(w.workshop.Model.Goods.Sum(g=>g.count),Is.EqualTo(1));Assert.That(w.workshop.Model.Owned.Count,Is.EqualTo(owned));yield return Depart();Assert.That(w.obstacles.All(o=>!o.Resolved),Is.True);Assert.That(w.finds[0].Taken,Is.False);Assert.That(w.workshop.Model.Current.PlayedCount,Is.Zero);}
  [UnityTest] public IEnumerator DefeatLosesPartOfLootKeepsCollectionAndRestoresHealth(){yield return Depart();for(int i=0;i<4;i++)w.Collect("Test mushroom",12);int count=w.workshop.Model.Owned.Count;w.vitals.Hurt(100,Vector3.zero);yield return new WaitForSeconds(w.settings.returnSeconds+.2f);Assert.That(w.workshop.Model.IsAway,Is.False);Assert.That(w.workshop.Model.Goods.Sum(g=>g.count),Is.EqualTo(2));Assert.That(w.workshop.Model.Owned.Count,Is.EqualTo(count));Assert.That(w.vitals.HalfHearts,Is.EqualTo(w.settings.maximumHalfHearts));}
  [UnityTest] public IEnumerator CreatureTelegraphsBeforeDamageAndDropsAPhysicalTrophy()
  {
   yield return Depart();var creature=w.creatures[1];creature.navigator.Warp(new Vector3(-4,.05f,5));creature.body.position=new Vector3(-4,.05f,5);creature.body.rotation=Quaternion.identity;
   w.motor.Teleport(new Vector3(-4,.1f,6.5f),Quaternion.identity);yield return new WaitForSeconds(.2f);
   Assert.That(w.vitals.HalfHearts,Is.EqualTo(w.settings.maximumHalfHearts),"Warning/contact cannot deal instant damage");
   float deadline=Time.time+5;while(w.vitals.HalfHearts==w.settings.maximumHalfHearts && Time.time<deadline)yield return null;
   Assert.That(w.vitals.HalfHearts,Is.LessThan(w.settings.maximumHalfHearts),"Telegraphed strike must connect: "+creature.State+" creature="+creature.transform.position+" player="+w.motor.transform.position);
   creature.Damage(100,Vector3.zero,0);yield return null;
   var trophy=w.spawnedObjects.GetComponentsInChildren<WorldLoot>().Single();Assert.That(trophy.gameObject.activeSelf,Is.True);Assert.That(w.Loot,Is.Empty);
   Assert.That(trophy.PickUp(),Is.True);Assert.That(w.Loot.Count,Is.EqualTo(1));Assert.That(creature.gameObject.activeSelf,Is.False);
  }
  [UnityTest] public IEnumerator DashNeedsPhysicalProximityAfterItsWindup()
  {
   yield return Depart();var creature=w.creatures[2];creature.navigator.Warp(new Vector3(-4,.05f,5));creature.body.position=new Vector3(-4,.05f,5);creature.body.rotation=Quaternion.identity;
   w.motor.Teleport(new Vector3(-4,.1f,9),Quaternion.identity);
   float deadline=Time.time+4;while(creature.State!=CreatureState.Strike && Time.time<deadline)yield return null;
   Assert.That(creature.State,Is.EqualTo(CreatureState.Strike));Assert.That(w.vitals.HalfHearts,Is.EqualTo(w.settings.maximumHalfHearts),"Windup range is not a damage radius");
   yield return new WaitForSeconds(.6f);Assert.That(w.vitals.HalfHearts,Is.EqualTo(w.settings.maximumHalfHearts-creature.attackHalfHearts));
  }
  [UnityTest] public IEnumerator FireProjectileHitsCreatureAndMissDoesNotApplyAnEffect()
  {
   yield return Depart();var creature=w.creatures[1];creature.Freeze(5);var card=Copy(CardAction.Fire);int health=creature.Health;
   Vector3 target=creature.transform.position+Vector3.up*.8f;
   w.Launch(card,target-Vector3.forward*3,Vector3.forward*12,0);float deadline=Time.time+1;
   while(creature.Health==health && Time.time<deadline)yield return null;
   Assert.That(creature.Health,Is.LessThan(health));Assert.That(w.Impacts,Is.EqualTo(1));
   w.Launch(card,new Vector3(0,80,0),Vector3.up*20,0);yield return new WaitForSeconds(.2f);Assert.That(w.Impacts,Is.EqualTo(1));
  }
  [UnityTest] public IEnumerator MinimumPreparationLayoutFitsAllCardDescriptions()
  {
   var ui=w.workshop;var viewport=ui.GetComponent<DeckViewport>();viewport.enabled=false;viewport.content.sizeDelta=new Vector2(1232,768);Canvas.ForceUpdateCanvases();yield return null;
   foreach(var copy in ui.Model.Owned.GroupBy(c=>c.Spec.Id).Select(g=>g.First())){ui.SelectCopy(copy.Id);Canvas.ForceUpdateCanvases();ui.detailDescription.ForceMeshUpdate();Assert.That(ui.detailDescription.isTextOverflowing,Is.False,copy.Spec.Id);var textCorners=new Vector3[4];var buttonCorners=new Vector3[4];ui.detailDescription.rectTransform.GetWorldCorners(textCorners);((RectTransform)ui.upgradeButton.transform).GetWorldCorners(buttonCorners);Assert.That(textCorners[0].y,Is.GreaterThanOrEqualTo(buttonCorners[1].y),"Description overlaps upgrade at minimum size");}
   viewport.enabled=true;
  }
  [UnityTest] public IEnumerator CaptureActualOutingAndCharge(){yield return Capture("preparation");yield return Depart();yield return new WaitForSeconds(.7f);yield return Capture("lookout");w.motor.Teleport(new Vector3(7,.1f,0),Quaternion.Euler(10,25,0));yield return new WaitForSeconds(.4f);yield return Capture("lake");w.thrower.Begin();w.thrower.Preparation.Tick(.7f,false,0,1,200);w.thrower.Preparation.Tick(.05f,true,110,1,200);yield return Capture("throw");w.thrower.Cancel();w.motor.Teleport(new Vector3(-17,.1f,-1),Quaternion.Euler(0,0,0));yield return Capture("thorns");yield return Return();yield return Capture("returned");}
  [UnityTest] public IEnumerator ActualMouseInputChargesLocksLookMovesAndConsumesOnRelease()
  {
   yield return Depart();var previousKeyboard=Keyboard.current;var previousMouse=Mouse.current;
   var keyboard=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();
   var previousBehavior=InputSystem.settings.editorInputBehaviorInPlayMode;
   InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
   KeyboardState keys=new KeyboardState();MouseState mouseState=new MouseState();
   System.Action queue=()=>{InputSystem.QueueStateEvent(keyboard,keys);InputSystem.QueueStateEvent(mouse,mouseState);};
   System.Action current=()=>{keyboard.MakeCurrent();mouse.MakeCurrent();};
   InputSystem.onBeforeUpdate+=queue;InputSystem.onAfterUpdate+=current;
   try {
    w.control.SendMessage("OnApplicationFocus",true);w.control.CaptureCursor();yield return null;yield return null;
    mouseState=new MouseState().WithButton(MouseButton.Left,true);yield return null;yield return new WaitForSeconds(.2f);
    Assert.That(w.thrower.Preparation.Active,Is.True);float charge=w.thrower.Preparation.Charge;
    var yaw=w.motor.view.yawRoot.rotation;var pitch=w.motor.view.pitchPivot.localRotation;var pos=w.motor.transform.position;
    keys=new KeyboardState(Key.LeftShift,Key.W);mouseState=new MouseState{delta=new Vector2(55,0)}.WithButton(MouseButton.Left,true);
    yield return null;mouseState=new MouseState().WithButton(MouseButton.Left,true);yield return new WaitForSeconds(.2f);
    Assert.That(w.thrower.Preparation.Charge,Is.EqualTo(charge).Within(.06f));Assert.That(w.thrower.Preparation.Spin,Is.GreaterThan(0));
    Assert.That(Quaternion.Angle(yaw,w.motor.view.yawRoot.rotation),Is.LessThan(.1f));Assert.That(Quaternion.Angle(pitch,w.motor.view.pitchPivot.localRotation),Is.LessThan(.1f));
    Assert.That(Vector3.Distance(pos,w.motor.transform.position),Is.GreaterThan(.15f));Assert.That(w.workshop.Model.Current.PlayedCount,Is.Zero);
    mouseState=new MouseState();yield return null;yield return null;Assert.That(w.workshop.Model.Current.PlayedCount,Is.EqualTo(1));Assert.That(w.thrower.input.LookBlocked,Is.False);
    keys=new KeyboardState();mouseState=new MouseState().WithButton(MouseButton.Left,true);yield return null;yield return null;
    mouseState=new MouseState().WithButton(MouseButton.Right,true);yield return null;yield return null;
    Assert.That(w.thrower.Preparation.Active,Is.False);Assert.That(w.workshop.Model.Current.PlayedCount,Is.EqualTo(1));
   }finally{InputSystem.onBeforeUpdate-=queue;InputSystem.onAfterUpdate-=current;InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(mouse);if(previousKeyboard!=null)previousKeyboard.MakeCurrent();if(previousMouse!=null)previousMouse.MakeCurrent();InputSystem.settings.editorInputBehaviorInPlayMode=previousBehavior;}
  }
  IEnumerator Capture(string name){if(Application.isBatchMode)yield break;yield return new WaitForEndOfFrame();string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../output/expedition/"+name+".png"));ScreenCapture.CaptureScreenshot(path);yield return null;yield return null;}
 }
}
