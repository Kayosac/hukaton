using System.Reflection;
using Hakaton.Character;
using Hakaton.ShopBlockout;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Backroom.Editor
{
    /// <summary>Play-mode shortcuts for trying the goblin's table: stand at him with his table open, buy, turn the card, put it away.</summary>
    public static class GoblinPlayTools
    {
        static void KeepSaveUntouched()
        {
            // Only the running copy of the scene: outside Play mode this would switch saving off in the open scene
            // without marking it dirty, and every later Play would start a new game in the cave.
            if (!Application.isPlaying) return;
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                var field = behaviour.GetType().GetField("persistProgress");
                if (field != null && field.FieldType == typeof(bool)) field.SetValue(behaviour, false);
            }
        }

        static GoblinCardShop Shop => Object.FindFirstObjectByType<ShopBackroomServices>()?.cardShop;

        [MenuItem("Tools/Hakaton/Goblin/Stand at the goblin and open his table")]
        public static string StandAndOpen()
        {
            KeepSaveUntouched();
            var services = Object.FindFirstObjectByType<ShopBackroomServices>();
            var goblin = services ? services.goblin : null;
            var shop = Shop;
            if (!goblin || !shop) return "no goblin";
            // +1000 coins through the cheat (the game loop lives in an assembly this one does not see).
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (behaviour.GetType().Name == "GameLoop") behaviour.GetType().GetMethod("AddThousandCoins")?.Invoke(behaviour, null);
            if (goblin.IsStowed && shop.TryCall(12345)) goblin.Summon();
            for (int i = 0; i < 40 && !goblin.IsDeployed; i++) goblin.Advance(.1f);
            var motor = Object.FindFirstObjectByType<FirstPersonMotor>();
            var view = Object.FindFirstObjectByType<FirstPersonView>();
            var target = goblin.goblinRoot.position;
            var flat = goblin.transform.forward;
            flat.y = 0;
            var stand = target + flat.normalized * 1.3f;
            if (Physics.Raycast(stand + Vector3.up * 1.5f, Vector3.down, out var floor, 6)) stand.y = floor.point.y + .05f;
            if (motor) { motor.TeleportKeepingVelocity(stand); motor.SetVelocity(Vector3.zero); }
            if (view) view.ResetPose(Quaternion.LookRotation(target + Vector3.up * .2f - (stand + Vector3.up * 1.5f)));
            bool open = shop.Open();
            var next = typeof(GoblinCardShop).GetMethod("NextLine", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < 6 && shop.Visits.IntroductionRead < shop.introduction.Length; i++) next.Invoke(shop, null);
            return $"deployed {goblin.IsDeployed} open {open} offer {shop.Visits.Offer != null}";
        }

        [MenuItem("Tools/Hakaton/Goblin/Buy the first card")]
        public static string BuyFirst()
        {
            var shop = Shop;
            if (!shop || !shop.IsOpen) return "not open";
            KeepSaveUntouched();
            for (int i = 0; i < shop.cards.Length; i++)
                if (!shop.Visits.Offer.IsSold(i)) return $"slot {i}: {shop.Choose(i)} inspecting {shop.Inspecting}";
            return "nothing left";
        }

        static CardInspect Inspected => Shop ? typeof(GoblinCardShop).GetField("inspected", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Shop) as CardInspect : null;

        /// <summary>Turns the card at the eyes as if dragged this many pixels right and up.</summary>
        public static string Turn(string pixels)
        {
            var card = Inspected;
            if (!card) return "no card";
            var parts = (string.IsNullOrEmpty(pixels) ? "240 0" : pixels).Split(' ');
            card.Turn(new Vector2(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0));
            card.Release();
            return "turned";
        }

        [MenuItem("Tools/Hakaton/Goblin/Put the card away")]
        public static string PutAway()
        {
            if (!Inspected) return "no card";
            // As a click would: the shop's own send-off, with its burst and sounds.
            typeof(GoblinCardShop).GetMethod("SendAway", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Shop, null);
            return "away";
        }

        static CardInspect preview;
        static Camera stage;

        /// <summary>
        /// The bought card's show without the shop or the save: "up" lifts a blind card to the eyes in front of the
        /// main camera, "away" sends it into the bag with the table's own burst and sounds.
        /// </summary>
        public static string Preview(string step)
        {
            if (step == "end")
            {
                if (preview) Object.Destroy(preview.gameObject);
                if (stage) Object.Destroy(stage.gameObject);
                return "ended";
            }
            // A camera of its own, high above everything and drawn last, so the show is seen whatever the game is doing.
            if (!stage)
            {
                stage = new GameObject("Goblin preview camera").AddComponent<Camera>();
                stage.transform.SetPositionAndRotation(new Vector3(0, 900, 0), Quaternion.identity);
                stage.clearFlags = CameraClearFlags.SolidColor;
                stage.backgroundColor = new Color(.32f, .24f, .18f);
                stage.fieldOfView = 60;
                stage.nearClipPlane = .05f;
                stage.depth = 1000;
            }
            var camera = stage;
            if (step == "away")
            {
                if (!preview) return "no card";
                preview.Dismiss(() => Debug.Log("[Goblin] launched"));
                return "away";
            }
            if (preview) Object.Destroy(preview.gameObject);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Cards/Card debris.prefab");
            var front = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/heavy-strike.png");
            var back = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Decks/Art/Backs/PenusCardBack.png");
            preview = CardInspect.Launch(prefab, front, back, 1, camera, new Vector2(.5f, .45f), .05f, -6, true, 0, null, null);
            return preview ? "up" : "failed";
        }

        [MenuItem("Tools/Hakaton/Goblin/Buy the first card", true)]
        [MenuItem("Tools/Hakaton/Goblin/Put the card away", true)]
        [MenuItem("Tools/Hakaton/Goblin/Stand at the goblin and open his table", true)]
        static bool InPlay() => Application.isPlaying;
    }
}
