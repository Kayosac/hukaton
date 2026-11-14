using System.Globalization;
using System.Text;
using Hakaton.Trading.Unity;
using UnityEngine;

namespace Hakaton.Trading.Tests
{
    /// <summary>
    /// Development probe for the editor bridge (<c>invoke Hakaton.Trading.Tests.HeldStockProbe.Run &lt;args&gt;</c>), in
    /// play mode: <c>info</c> — where the eye, the counter and the display slots are and the hand's state;
    /// <c>menu</c> — leaves the main menu as «Продолжить» does; <c>hold &lt;item id&gt;</c> — shows a catalogue item in the hand
    /// (the shop's stock is not touched: the save stays as it was) and stops the shop, so the hand is wanted without a
    /// captured cursor; <c>at &lt;x&gt; &lt;z&gt; [yaw]</c> — moves the shopkeeper; <c>suppress 0|1</c>; <c>slow &lt;seconds&gt;</c> — the slide's
    /// length for this play session (to catch it mid-way in a screenshot).
    /// Not used by the game or the tests.
    /// </summary>
    public static class HeldStockProbe
    {
        public static string Run(string argument)
        {
            if (!Application.isPlaying) return "not playing";
            var trade = Object.FindFirstObjectByType<TradingPrototype>();
            if (!trade || !trade.World) return "no shop";
            var world = trade.World;
            var held = world.HeldStock;
            var parts = (argument ?? "").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            string verb = parts.Length > 0 ? parts[0] : "info";
            var culture = CultureInfo.InvariantCulture;
            switch (verb)
            {
                case "menu":
                    foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                        if (behaviour.GetType().Name == "MainMenu")
                            behaviour.GetType().GetMethod("StartPlaying", System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.NonPublic)?.Invoke(behaviour, null);
                    break;
                case "hold":
                {
                    // By reflection: this assembly does not reference the loot catalogue's.
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
                    object catalog = typeof(TradingPrototype).GetField("lootCatalog", flags)?.GetValue(trade), found = null;
                    if (catalog == null || parts.Length < 2) return "no catalogue or no item id";
                    foreach (var item in (System.Collections.IEnumerable)catalog.GetType().GetField("items", flags).GetValue(catalog))
                        if (item != null && (string)item.GetType().GetField("id", flags).GetValue(item) == parts[1]) found = item;
                    if (found == null) return "unknown item";
                    trade.enabled = false;
                    typeof(HeldStockView).GetMethod("Show").Invoke(held, new[] { catalog, found });
                    held.Wanted = true;
                    break;
                }
                case "at":
                {
                    float x = float.Parse(parts[1], culture), z = float.Parse(parts[2], culture);
                    float yaw = parts.Length > 3 ? float.Parse(parts[3], culture) : world.PlayerRoot.eulerAngles.y;
                    world.Movement.Teleport(new Vector3(x, world.PlayerRoot.position.y, z), Quaternion.Euler(0, yaw, 0));
                    break;
                }
                case "slow":
                    held.slideSeconds = float.Parse(parts[1], culture);
                    break;
                case "suppress":
                    held.Suppressed = parts.Length > 1 && parts[1] == "1";
                    break;
            }
            var text = new StringBuilder();
            var eye = world.ViewCamera.transform.position;
            text.Append($"eye {eye.x:F2} {eye.y:F2} {eye.z:F2} yaw {world.ViewCamera.transform.eulerAngles.y:F0}; ");
            var counter = world.CounterPose.position;
            text.Append($"counter {counter.x:F2} {counter.z:F2}; slots");
            float fromCounter = float.MaxValue, fromEye = float.MaxValue;
            for (int i = 0; i < world.SlotAnchors.Length; i++)
            {
                var a = world.SlotAnchors[i].position;
                var b = world.SlotColliders[i].bounds;
                text.Append($" [{a.x:F2} {a.y:F2} {a.z:F2} size {b.size.x:F2}x{b.size.z:F2}]");
                fromCounter = Mathf.Min(fromCounter, HeldStockView.HorizontalDistance(counter, b));
                fromEye = Mathf.Min(fromEye, HeldStockView.HorizontalDistance(eye, b));
            }
            text.Append($"; counter->display {fromCounter:F2} m, eye->display {fromEye:F2} m; wanted {held.Wanted} near {held.NearDisplay} " +
                        $"suppressed {held.Suppressed} raised {held.Raised:F2} shown {world.HeldItem.activeSelf}; stock");
            foreach (var item in trade.Session.Inventory) text.Append(' ').Append(item.Definition.Id);
            return text.ToString();
        }
    }
}
