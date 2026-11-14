#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Hakaton.Character;
using Hakaton.Trading.Unity;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// A close look at an item on the display (DisplayItemInspector) in the real shop: it lifts a copy while the shelf
    /// original hides, holds the player, and puts everything back; it ends at once when the item leaves the display.
    /// The player's own save files are moved aside for the run and put back afterwards.
    /// </summary>
    public sealed class DisplayInspectionPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        internal const string Curio = "mantel_clock", Drawing = "agaric_cap";
        readonly Dictionary<string, byte[]> playerSaves = new Dictionary<string, byte[]>();
        string saveFolder;

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
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
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Inspection test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        [UnityTest]
        public IEnumerator AModelOnTheDisplayIsLiftedTurnedAndPutBack()
        {
            var trade = Prepare();
            var inspector = trade.Inspector;
            Assert.That(inspector, Is.Not.Null, "The shop makes its inspector itself.");
            Assert.That(inspector.CanInspect(0), Is.True, "A curio model can be looked at.");
            Assert.That(inspector.CanInspect(1), Is.True, "A drawing too.");
            Assert.That(inspector.CanInspect(2), Is.False, "Nor an empty slot.");
            yield return Aim(trade, 0);

            var original = trade.DisplayedObject(0);
            Vector3 shelfPosition = original.transform.position;
            Quaternion shelfRotation = original.transform.rotation;
            var control = trade.World.Control;
            Assert.That(inspector.TryBegin(0), Is.True);
            Assert.That(original.activeSelf, Is.False, "The shelf original hides while its copy is held.");
            Assert.That(control.HasModalOwner, Is.True, "Movement and look are held.");
            Assert.That(control.PauseAllowed, Is.False, "Escape belongs to the inspection, not the pause menu.");
            Assert.That(trade.World.HeldStock.Suppressed, Is.True, "The held stock is lowered.");
            Assert.That(inspector.TryBegin(0), Is.False, "One look at a time.");

            yield return WaitFor(() => false, .8f);
            var copy = Copy(inspector);
            Assert.That(copy, Is.Not.Null);
            var eye = trade.World.ViewCamera;
            var bounds = WorldBounds(copy);
            var middle = eye.WorldToViewportPoint(bounds.center);
            Assert.That(middle.x, Is.EqualTo(.5f).Within(.06f), "In the middle of the view.");
            Assert.That(middle.y, Is.EqualTo(.5f).Within(.06f), "In the middle of the view.");
            float low = 1, high = 0;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                float y = eye.WorldToViewportPoint(point).y;
                low = Mathf.Min(low, y);
                high = Mathf.Max(high, y);
            }
            Assert.That(high - low, Is.InRange(.3f, .85f), "A comfortable share of the view (its box seen from the eye).");
            Assert.That(middle.z, Is.LessThan(.55f), "Close in front of the eye.");
            Assert.That(original.transform.position, Is.EqualTo(shelfPosition), "The shelf original never moves.");
            yield return null;
            var hud = trade.hud;
            Assert.That(hud.selectedItemTitle.text, Is.EqualTo(inspector.Item.Definition.Name), "The card names the item.");
            Assert.That(hud.contextPrompt.text, Is.EqualTo(DisplayItemInspector.Hint), "The hint says how to turn it.");

            inspector.Return();
            yield return WaitFor(() => !inspector.Active, 1);
            Assert.That(inspector.Active, Is.False, "Back on the shelf.");
            Assert.That(original.activeSelf, Is.True, "The original is shown again.");
            Assert.That(original.transform.position, Is.EqualTo(shelfPosition));
            Assert.That(original.transform.rotation, Is.EqualTo(shelfRotation));
            Assert.That(Copy(inspector), Is.Null, "The copy is gone.");
            Assert.That(control.HasModalOwner, Is.False, "Control is back.");
            Assert.That(trade.World.HeldStock.Suppressed, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ADrawingIsLookedAtAsAFlatCardThatTurns()
        {
            var trade = Prepare();
            var inspector = trade.Inspector;
            yield return Aim(trade, 1);
            var original = trade.DisplayedObject(1);
            Assert.That(inspector.TryBegin(1), Is.True);
            Assert.That(original.activeSelf, Is.False);
            yield return WaitFor(() => false, .8f);
            var copy = Copy(inspector);
            Assert.That(copy, Is.Not.Null);
            var view = copy.GetComponentInChildren<Hakaton.Loot.LootSpriteView>(true);
            Assert.That(view.faceCamera, Is.False, "The copy no longer turns to the camera by itself.");
            var picture = copy.GetComponentInChildren<SpriteRenderer>();
            Assert.That(picture.sharedMaterial, Is.SameAs(original.GetComponentInChildren<SpriteRenderer>(true).sharedMaterial),
                "The shared hand-drawn material, no copy of it.");
            Assert.That(picture.sharedMaterial.shader.name, Does.Contain("Hand Drawn"));
            var eye = trade.World.ViewCamera;
            Assert.That(Vector3.Dot(picture.transform.forward, eye.transform.forward), Is.GreaterThan(.9f),
                $"Face to the eye. picture {picture.transform.forward} root {copy.transform.forward} eye {eye.transform.forward} original {original.transform.rotation.eulerAngles} picture local {picture.transform.localRotation.eulerAngles}");
            var middle = eye.WorldToViewportPoint(WorldBounds(copy).center);
            Assert.That(middle.x, Is.EqualTo(.5f).Within(.06f));
            Assert.That(middle.y, Is.EqualTo(.5f).Within(.06f));
            // The drawing's own rectangle (with its transparent margins) as the eye sees it.
            var box = picture.sprite.bounds;
            float low = 1, high = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                var point = picture.transform.TransformPoint(new Vector3((corner & 1) == 0 ? box.min.x : box.max.x,
                    (corner & 2) == 0 ? box.min.y : box.max.y, 0));
                float y = eye.WorldToViewportPoint(point).y;
                low = Mathf.Min(low, y);
                high = Mathf.Max(high, y);
            }
            Assert.That(high - low, Is.InRange(.25f, .85f), "The same share of the view as a model.");
            inspector.Return();
            yield return WaitFor(() => !inspector.Active, 1);
            Assert.That(original.activeSelf, Is.True);
            Assert.That(trade.World.Control.HasModalOwner, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheLookEndsAtOnceWhenTheItemLeavesTheDisplay()
        {
            var trade = Prepare();
            var inspector = trade.Inspector;
            yield return Aim(trade, 0);
            Assert.That(inspector.TryBegin(0), Is.True);
            yield return WaitFor(() => false, .2f);
            Assert.That(trade.Session.RemoveItem(0), Is.True);
            yield return null;
            yield return null;
            Assert.That(inspector.Active, Is.False);
            Assert.That(Copy(inspector), Is.Null);
            Assert.That(trade.World.Control.HasModalOwner, Is.False);
            Assert.That(trade.World.HeldStock.Suppressed, Is.False);

            // Someone else taking control (the tutorial's lines) flies it back.
            yield return Aim(trade, 0);
            trade.Session.PlaceItem(IndexOf(trade, Curio), 0);
            trade.RefreshPresentation();
            yield return null;
            yield return Aim(trade, 0);
            Assert.That(inspector.TryBegin(0), Is.True);
            var owner = new GameObject("Another window");
            trade.World.Control.SetBlocked(owner, true);
            yield return WaitFor(() => !inspector.Active, 1);
            Assert.That(inspector.Active, Is.False);
            Assert.That(trade.DisplayedObject(0).activeSelf, Is.True);
            trade.World.Control.SetBlocked(owner, false);
            Object.Destroy(owner);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The shop after the tutorial with a curio on slot 0 and a drawing on slot 1.</summary>
        internal static TradingPrototype Prepare()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            game.SkipTutorial();
            var trade = Object.FindAnyObjectByType<TradingPrototype>();
            var session = trade.Session;
            for (int i = 0; i < session.Slots.Count; i++) if (session.Slots[i] != null) session.RemoveItem(i);
            session.SeedInventory(session.FindDefinition(Curio), 2);
            session.SeedInventory(session.FindDefinition(Drawing), 1);
            Assert.That(session.PlaceItem(IndexOf(trade, Curio), 0), Is.True);
            Assert.That(session.PlaceItem(IndexOf(trade, Drawing), 1), Is.True);
            trade.RefreshPresentation();
            return trade;
        }

        static int IndexOf(TradingPrototype trade, string id)
        {
            var stock = trade.Session.Inventory;
            for (int i = 0; i < stock.Count; i++) if (stock[i].Definition.Id == id) return i;
            return -1;
        }

        /// <summary>Stands the shopkeeper in front of the slot, looking at it (as the aim would before R).</summary>
        internal static IEnumerator Aim(TradingPrototype trade, int slot)
        {
            yield return null;
            var world = trade.World;
            var anchor = world.SlotAnchors[slot];
            var shown = trade.DisplayedObject(slot);
            var view = world.Movement.GetComponentInChildren<FirstPersonView>() ?? Object.FindAnyObjectByType<FirstPersonView>();
            Vector3 eyeOffset = world.ViewCamera.transform.position - world.PlayerRoot.position;
            Vector3 target = shown ? WorldBounds(shown).center : anchor.position;
            // The customers' side first (the display faces them with its -X), then all round.
            for (int i = 0; i < 16; i++)
            {
                float turn = i * 22.5f;
                Vector3 away = Quaternion.Euler(0, turn, 0) * Vector3.ProjectOnPlane(-anchor.right, Vector3.up).normalized;
                Vector3 feet = new Vector3(target.x, world.PlayerRoot.position.y, target.z) + away * 1.1f;
                Vector3 eye = feet + eyeOffset;
                var look = Quaternion.LookRotation(target - eye);
                if (!Physics.Raycast(eye, target - eye, out var hit, 3, ~(1 << 2), QueryTriggerInteraction.Collide)) continue;
                if (!shown || !hit.collider.transform.IsChildOf(shown.transform)) continue;
                world.Movement.Teleport(feet, Quaternion.Euler(0, look.eulerAngles.y, 0));
                if (view) view.ResetPose(look);
                break;
            }
            // Let the view settle on the new pose (look smoothing, the step after a teleport).
            yield return WaitFor(() => false, .3f);
        }

        internal static GameObject Copy(DisplayItemInspector inspector)
        {
            var rig = GameObject.Find("Display item inspection");
            if (!rig) return null;
            var holder = rig.transform.Find("Item");
            return holder && holder.childCount > 0 && rig.activeInHierarchy ? holder.GetChild(0).gameObject : null;
        }

        internal static Bounds WorldBounds(GameObject shown)
        {
            var renderers = shown.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        internal static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
