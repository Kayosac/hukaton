using System.Globalization;
using System.Text;
using Hakaton.Trading.Unity;
using UnityEngine;

namespace Hakaton.Trading.Tests
{
    /// <summary>
    /// Development probe for the editor bridge (<c>invoke Hakaton.Trading.Tests.HeldStockProbe.Run &lt;args&gt;</c>), in
    /// play mode: <c>info</c> — where the eye, the counter and the display slots are and the hand's state;
    /// <c>hold &lt;item id&gt;</c> — puts the item in stock and hand, and stops the shop so the hand is wanted without
    /// a captured cursor; <c>at &lt;x&gt; &lt;z&gt; &lt;yaw&gt;</c> — moves the shopkeeper; <c>suppress 0|1</c>.
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
                case "hold":
                {
                    if (!trade.enabled) trade.enabled = true;
                    var definition = trade.Session.FindDefinition(parts.Length > 1 ? parts[1] : "");
                    if (definition == null) return "unknown item";
                    trade.Session.SeedInventory(definition, 1);
                    trade.SelectStock(definition.Id);
                    trade.RefreshPresentation();
                    trade.enabled = false;
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
                        $"suppressed {held.Suppressed} raised {held.Raised:F2} shown {world.HeldItem.activeSelf}");
            return text.ToString();
        }
    }
}
