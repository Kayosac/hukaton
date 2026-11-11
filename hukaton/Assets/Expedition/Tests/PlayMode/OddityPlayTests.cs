#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Hakaton.Character;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Expeditions.Tests.PlayMode
{
    /// <summary>«Странное» (payphone, television) and the shared E interaction, in Assets/OddityArt/Scenes/OddityLab.unity.</summary>
    public sealed class OddityPlayTests
    {
        const string Data = "Assets/Game/Data/Oddities/";
        ExpeditionWorld world;
        FirstPersonView view;

        [UnitySetUp]
        public IEnumerator LoadLab()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/OddityArt/Scenes/OddityLab.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            world = Object.FindFirstObjectByType<ExpeditionWorld>();
            view = Object.FindFirstObjectByType<FirstPersonView>();
            float until = Time.time + 3;
            while (!world.Running && Time.time < until) yield return null;
            Assert.That(world.Running, "the lab opens an outing by itself");
            // The enemy polygon heals every hit at once; the heal test needs real damage.
            foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (b.GetType().Name == "EnemyDebugSandbox") Object.Destroy(b);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Release()
        {
            if (world && world.control) world.control.ReleaseCursor();
            yield return null;
        }

        static Oddity Lab(Oddity.Kind kind) => Object.FindObjectsByType<Oddity>(FindObjectsSortMode.None).First(o => o.kind == kind);

        static OddityTable Only(OddityEffect effect)
        {
            var table = ScriptableObject.CreateInstance<OddityTable>();
            table.entries = new[] { new OddityTable.Entry { effect = effect, weight = 1 } };
            return table;
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        IEnumerator Use(Oddity oddity)
        {
            Assert.That(oddity.CanInteract, Is.True);
            Assert.That(oddity.Interact(), Is.True);
            yield return Seconds(oddity.answerSeconds + .8f);
            Assert.That(oddity.Answering, Is.False);
        }

        [UnityTest]
        public IEnumerator HealGivesHalfHeartsBack()
        {
            var heal = AssetDatabase.LoadAssetAtPath<HealOddityEffect>(Data + "Heal.asset");
            var phone = Lab(Oddity.Kind.Payphone);
            phone.effects = Only(heal);
            Assert.That(world.vitals.Hurt(4, Vector3.zero, null), Is.True);
            int before = world.vitals.HalfHearts;
            yield return Use(phone);
            Assert.That(phone.LastEffect, Is.EqualTo(heal));
            Assert.That(world.vitals.HalfHearts, Is.EqualTo(Mathf.Min(world.vitals.MaximumHalfHearts, before + heal.halfHearts)));
        }

        [UnityTest]
        public IEnumerator MoonGravityWorksLikeTheCard()
        {
            var moon = AssetDatabase.LoadAssetAtPath<MoonGravityOddityEffect>(Data + "Moon gravity.asset");
            var tv = Lab(Oddity.Kind.Television);
            tv.effects = Only(moon);
            yield return Use(tv);
            Assert.That(tv.LastEffect, Is.EqualTo(moon));
            Assert.That(world.motor.HasModifier(MotorModifier.Gravity), Is.True);
            Assert.That(world.motor.GravityScale, Is.EqualTo(moon.card.gravityMultiplier).Within(1e-4));
        }

        [UnityTest]
        public IEnumerator LureBringsCreaturesWithinItsRadius()
        {
            var lure = AssetDatabase.LoadAssetAtPath<LureOddityEffect>(Data + "Lure creatures.asset");
            var prefab = AssetDatabase.LoadAssetAtPath<WorldCreature>("Assets/Game/Prefabs/Purple mushroom.prefab");
            Assert.That(prefab, Is.Not.Null);
            var phone = Lab(Oddity.Kind.Payphone);
            Vector3 player = world.motor.transform.position;
            var creature = Object.Instantiate(prefab, player + new Vector3(0, 0, -16), Quaternion.identity);
            creature.world = world;
            creature.ResetForOuting(null);
            world.creatures.Clear();
            world.creatures.Add(creature);
            yield return Seconds(1);
            Assert.That(lure.CountInReach(world), Is.EqualTo(1));
            phone.effects = Only(lure);
            yield return Use(phone);
            Assert.That(phone.LastEffect, Is.EqualTo(lure));
            Assert.That(creature.Investigating, Is.True, "the creature comes to look");
        }

        [UnityTest]
        public IEnumerator LureCardPullsCreaturesFromOutsideTheirTerritory()
        {
            var noisePrefab = AssetDatabase.LoadAssetAtPath<Hakaton.Expeditions.Cards.LureNoise>("Assets/Game/Prefabs/Cards/Lure/Lure.prefab");
            var prefab = AssetDatabase.LoadAssetAtPath<WorldCreature>("Assets/Game/Prefabs/Purple mushroom.prefab");
            Vector3 player = world.motor.transform.position;
            var creature = Object.Instantiate(prefab, player + new Vector3(0, 0, -40), Quaternion.identity);
            creature.world = world;
            creature.ResetForOuting(null);
            world.creatures.Clear();
            world.creatures.Add(creature);
            yield return Seconds(1);
            Assert.That(creature.InTerritory(player), Is.False, "the noise is far outside its own area");
            var noise = world.SpawnForOuting(noisePrefab, player, Quaternion.identity);
            noise.Setup(6, 50);
            yield return Seconds(.3f);
            Assert.That(creature.Investigating, Is.True, "the card's noise draws it out anyway");
            Assert.That(creature.InTerritory(player), Is.True, "it settles at the noise instead of running home");
        }

        [UnityTest]
        public IEnumerator LureCardLeavesAggressiveCreaturesAlone()
        {
            var noisePrefab = AssetDatabase.LoadAssetAtPath<Hakaton.Expeditions.Cards.LureNoise>("Assets/Game/Prefabs/Cards/Lure/Lure.prefab");
            var prefab = AssetDatabase.LoadAssetAtPath<WorldCreature>("Assets/Game/Prefabs/Goblin dog.prefab");
            Assert.That(prefab.temperament, Is.EqualTo(Temperament.Aggressive));
            Vector3 player = world.motor.transform.position;
            var creature = Object.Instantiate(prefab, player + new Vector3(0, 0, -40), Quaternion.identity);
            creature.world = world;
            creature.ResetForOuting(null);
            world.creatures.Clear();
            world.creatures.Add(creature);
            yield return Seconds(1);
            var noise = world.SpawnForOuting(noisePrefab, player + new Vector3(0, 0, -30), Quaternion.identity);
            noise.Setup(6, 50);
            yield return Seconds(.3f);
            Assert.That(creature.Investigating, Is.False, "an openly hostile beast does not answer the lure");
        }

        [UnityTest]
        public IEnumerator UsingItAgainDoesNothing()
        {
            var moon = AssetDatabase.LoadAssetAtPath<MoonGravityOddityEffect>(Data + "Moon gravity.asset");
            var phone = Lab(Oddity.Kind.Payphone);
            phone.effects = Only(moon);
            Assert.That(phone.Interact(), Is.True);
            Assert.That(phone.Interact(), Is.False, "not while it answers");
            yield return Seconds(phone.answerSeconds + .8f);
            var first = phone.LastEffect;
            world.motor.ClearModifier(MotorModifier.Gravity);
            Assert.That(phone.CanInteract, Is.False);
            Assert.That(phone.Interact(), Is.False);
            yield return Seconds(phone.answerSeconds + .3f);
            Assert.That(world.motor.HasModifier(MotorModifier.Gravity), Is.False, "nothing happens the second time");
            Assert.That(phone.LastEffect, Is.EqualTo(first));
            Assert.That(phone.Current, Is.EqualTo(Oddity.State.Used));
            Assert.That(phone.glow.enabled, Is.False, "silent and dark");
        }

        [UnityTest]
        public IEnumerator BothStatesComeBack()
        {
            foreach (var path in new[] { "Assets/Expedition/Prefabs/Oddities/Oddity Payphone.prefab", "Assets/Expedition/Prefabs/Oddities/Oddity Television.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<Oddity>(path);
                var used = Object.Instantiate(prefab, new Vector3(30, 0, 30), Quaternion.identity);
                used.world = world;
                used.Restore(Oddity.State.Used);
                yield return null;
                Assert.That(used.Capture(), Is.EqualTo(Oddity.State.Used), path);
                Assert.That(used.CanInteract, Is.False);
                Assert.That(used.Interact(), Is.False);
                Assert.That(used.glow.enabled, Is.False, "comes back silent");

                var fresh = Object.Instantiate(prefab, new Vector3(34, 0, 30), Quaternion.identity);
                fresh.world = world;
                fresh.Restore(Oddity.State.Untouched);
                yield return null;
                Assert.That(fresh.Capture(), Is.EqualTo(Oddity.State.Untouched), path);
                Assert.That(fresh.CanInteract, Is.True);
                Assert.That(fresh.glow.enabled, Is.True, "comes back calling");
                Object.Destroy(used.gameObject);
                Object.Destroy(fresh.gameObject);
            }
        }

        // ---------------- E: the shared interaction ----------------

        IEnumerator Aim(Vector3 stand, Vector3 target)
        {
            world.motor.Teleport(stand, Quaternion.LookRotation(Flat(target - stand)));
            for (int i = 0; i < 4; i++)
            {
                view.ResetPose(Quaternion.LookRotation(target - view.cameraTransform.position));
                yield return null;
            }
        }

        IEnumerator Capture()
        {
            world.control.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
            world.control.CaptureCursor();
            yield return null;
            Assert.That(world.control.GameplayActive, Is.True);
        }

        [UnityTest]
        public IEnumerator ChestsFindsAndOdditiesAllAnswerE()
        {
            yield return Capture();

            // a chest
            var chest = Object.FindObjectsByType<TreasureChest>(FindObjectsSortMode.None).First();
            yield return Aim(chest.transform.position - chest.transform.forward * 2f + Vector3.up * .05f, chest.transform.position + Vector3.up * .25f);
            Assert.That(world.FocusedChest, Is.EqualTo(chest));
            Assert.That(world.FocusedInteractable.Prompt, Is.EqualTo("E — открыть"));
            Assert.That(world.TryPickUpFocused(), Is.True);
            Assert.That(chest.Opened, Is.True);

            // a find lying on the ground
            var item = world.lootCatalog.items.First(i => i);
            Vector3 spot = chest.transform.position + new Vector3(-3, .4f, 0);
            var find = world.SpawnLoot(item, spot);
            yield return Seconds(.8f);
            yield return Aim(find.transform.position + new Vector3(0, 1.6f, 0) + Vector3.back * 1.6f - Vector3.up * 1.6f, find.transform.position);
            Assert.That(world.FocusedLoot, Is.EqualTo(find));
            Assert.That(world.TryPickUpFocused(), Is.True);
            Assert.That(find.Taken, Is.True);

            // a payphone
            var phone = Lab(Oddity.Kind.Payphone);
            phone.effects = Only(AssetDatabase.LoadAssetAtPath<MoonGravityOddityEffect>(Data + "Moon gravity.asset"));
            yield return Aim(phone.transform.position + phone.transform.forward * 1.8f + Vector3.up * .05f, phone.transform.position + Vector3.up * 1.3f);
            Assert.That(world.FocusedInteractable, Is.EqualTo(phone));
            Assert.That(world.FocusedInteractable.Prompt, Is.EqualTo("E — снять трубку"));
            Assert.That(world.TryPickUpFocused(), Is.True);
            Assert.That(phone.Current, Is.EqualTo(Oddity.State.Used));
            yield return null;
            Assert.That(world.FocusedInteractable, Is.Null, "a used one is no longer offered");
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .0001f ? v : Vector3.forward; }
    }
}
#endif
