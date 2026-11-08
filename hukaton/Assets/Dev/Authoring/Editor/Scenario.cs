using System.Globalization;
using System.Linq;
using System.Text;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Expeditions;
using Hakaton.Game;
using Hakaton.Trading.Unity;
using UnityEngine;

namespace Hakaton.Authoring
{
    /// <summary>Play-mode automation for verification through the Editor bridge (invoke Hakaton.Authoring.Scenario.X).</summary>
    static class Scenario
    {
        static GameLoop Game => Object.FindAnyObjectByType<GameLoop>();
        static ExpeditionWorld World => Object.FindAnyObjectByType<ExpeditionWorld>();
        static float[] Numbers(string args) => (args ?? "").Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Select(a => float.Parse(a, CultureInfo.InvariantCulture)).ToArray();

        public static string Depart() => "depart=" + Game.TryDepart();

        /// <summary>Leaves through the shop door, as the player does: fade, departure, location title.</summary>
        public static string Door()
        {
            typeof(GameLoop).GetMethod("TryDepartFromDoor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .Invoke(Game, null);
            return "door used";
        }

        public static string Teleport(string args)
        {
            var n = Numbers(args);
            var world = World;
            world.motor.Teleport(new Vector3(n[0], n[1], n[2]), Quaternion.Euler(n.Length > 4 ? n[4] : 0, n.Length > 3 ? n[3] : 0, 0));
            return "at " + world.motor.transform.position;
        }

        public static string Look(string args)
        {
            var n = Numbers(args);
            var world = World;
            world.motor.view.ResetPose(Quaternion.Euler(n.Length > 1 ? n[1] : 0, n[0], 0));
            return "look " + n[0] + " " + (n.Length > 1 ? n[1] : 0);
        }

        /// <summary>args: "handIndex charge spin" — selects, charges and releases in one frame.</summary>
        public static string Throw(string args)
        {
            var n = Numbers(args);
            var world = World;
            var thrower = world.thrower;
            thrower.Select((int)n[0]);
            var card = thrower.Selected;
            if (card == null) return "no card";
            if (card.IsReturn) { world.ReturnToShop(false); return "return"; }
            thrower.Preparation.Begin();
            float charge = n.Length > 1 ? n[1] : 1, spin = n.Length > 2 ? n[2] : 0;
            thrower.Preparation.Tick(world.settings.chargeSeconds * charge, false, 0, world.settings.chargeSeconds, world.settings.gesturePixels);
            if (Mathf.Abs(spin) > 0)
                thrower.Preparation.Tick(.01f, true, spin * world.settings.gesturePixels, world.settings.chargeSeconds, world.settings.gesturePixels);
            var projectile = thrower.Release();
            return projectile ? $"threw {card.Spec.Name} charge={charge} spin={spin}" : "release failed";
        }

        /// <summary>args: "cardId charge spin" — switches deck if needed and throws the first copy of that card.</summary>
        public static string ThrowId(string args)
        {
            var parts = args.Split(' ');
            var world = World;
            var trip = world.workshop.Model.Current;
            foreach (var kind in new[] { DeckKind.Exploration, DeckKind.Combat })
            {
                var hand = trip.Get(kind).Hand;
                for (int i = 0; i < hand.Count; i++)
                    if (hand[i].Spec.Id == parts[0])
                    {
                        if (trip.ActiveKind != kind) world.thrower.SwitchDeck();
                        return Throw(i + " " + string.Join(" ", parts.Skip(1)));
                    }
            }
            return "card not in hand: " + parts[0];
        }

        /// <summary>Holds a charge without releasing, for screenshots of the wind-up.</summary>
        public static string Charge(string args)
        {
            var n = Numbers(args);
            var world = World;
            world.thrower.Select((int)n[0]);
            world.thrower.Preparation.Begin();
            world.thrower.Preparation.Tick(world.settings.chargeSeconds * (n.Length > 1 ? n[1] : 1), false, 0, world.settings.chargeSeconds, world.settings.gesturePixels);
            return "charging";
        }

        static float peak;
        /// <summary>Samples the player's height over the next seconds (call repeatedly); returns the max seen.</summary>
        public static string Peak(string reset) { if (reset == "reset") peak = float.MinValue; peak = Mathf.Max(peak, World.motor.transform.position.y); return "peak=" + peak.ToString("F2") + " now=" + World.motor.transform.position.y.ToString("F2"); }

        /// <summary>Aims the camera at a creature by display name (first alive match), with an optional pitch offset.</summary>
        public static string AimAt(string args)
        {
            var parts = args.Split(' ');
            var world = World;
            var target = world.creatures.FirstOrDefault(c => c.displayName == parts[0] && c.Alive && c.gameObject.activeInHierarchy);
            if (!target) return "no target";
            Vector3 eye = world.viewCamera.transform.position;
            Vector3 to = target.transform.position + Vector3.up * .7f - eye;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg + (parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 0);
            world.motor.view.ResetPose(Quaternion.Euler(pitch, yaw, 0));
            return $"aim {target.displayName} d={to.magnitude:F1} state={target.State}";
        }

        /// <summary>args: "Object name|distance|height|pitchOffset" — stands the player in front of a scene object and aims at it.</summary>
        public static string Approach(string args)
        {
            var parts = args.Split('|');
            var world = World;
            var target = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == parts[0]);
            if (!target) return "no object " + parts[0];
            var bounds = target.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            float distance = float.Parse(parts[1], CultureInfo.InvariantCulture);
            Vector3 from = world.motor.transform.position - bounds.center; from.y = 0;
            Vector3 stand = bounds.center + from.normalized * distance;
            if (Physics.Raycast(stand + Vector3.up * 30, Vector3.down, out var hit, 60, ~0, QueryTriggerInteraction.Ignore)) stand.y = hit.point.y + .05f;
            world.motor.Teleport(stand, Quaternion.LookRotation(-from));
            Vector3 eye = stand + Vector3.up * 1.6f;
            Vector3 to = bounds.center + Vector3.up * float.Parse(parts[2], CultureInfo.InvariantCulture) - eye;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg + float.Parse(parts[3], CultureInfo.InvariantCulture);
            world.motor.view.ResetPose(Quaternion.Euler(pitch, yaw, 0));
            return $"at {stand} facing {target.name} bounds={bounds.size}";
        }

        /// <summary>args: "Effect name|scale|distance" — spawns a Game/Prefabs/Effects/Retro effect on the ground ahead.</summary>
        public static string SpawnFx(string args)
        {
            var parts = args.Split('|');
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Hakaton.Feel.VfxBurst>($"Assets/Game/Prefabs/Effects/Retro/{parts[0]}.prefab");
            if (!prefab) return "no effect " + parts[0];
            var eye = World.viewCamera.transform;
            Vector3 flat = eye.forward; flat.y = 0; flat.Normalize();
            Vector3 point = eye.position + flat * float.Parse(parts[2], CultureInfo.InvariantCulture);
            if (Physics.Raycast(point + Vector3.up * 5, Vector3.down, out var hit, 20, ~0, QueryTriggerInteraction.Ignore)) point = hit.point;
            World.Spawn(prefab, point, Quaternion.LookRotation(Vector3.up), Color.white, float.Parse(parts[1], CultureInfo.InvariantCulture));
            return "spawned " + parts[0] + " at " + point;
        }

        /// <summary>Reports which scene bindings of the shop are missing.</summary>
        public static string Diag()
        {
            var trade = Object.FindAnyObjectByType<TradingPrototype>(FindObjectsInactive.Include);
            var w = trade.World;
            var b = new StringBuilder($"trade enabled={trade.enabled} session={(trade.Session != null)} world={(bool)w} hud={(bool)trade.hud}\n");
            if (w)
                foreach (var f in w.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    var v = f.GetValue(w);
                    if (v is Object o && !o) b.Append("NULL ").AppendLine(f.Name);
                    else if (v is System.Array a) foreach (var item in a) if (item is Object io && !io) { b.Append("NULL item in ").AppendLine(f.Name); break; }
                }
            if (w && w.ShopView) b.AppendLine("shopView configured=" + w.ShopView.IsConfigured);
            return b.ToString();
        }

        static TradingPrototype Trade => Object.FindAnyObjectByType<TradingPrototype>();

        /// <summary>args: "itemId count ..." — adds stock and fills the display.</summary>
        public static string Stock(string args)
        {
            var parts = args.Split(' ');
            var session = Trade.Session;
            for (int i = 0; i + 1 < parts.Length; i += 2)
                session.SeedInventory(session.FindDefinition(parts[i]), int.Parse(parts[i + 1]));
            for (int slot = 0; slot < session.Slots.Count && session.Inventory.Count > 0; slot++)
                if (session.Slots[slot] == null) session.PlaceItem(0, slot);
            Trade.RefreshPresentation();
            return $"stock={session.Inventory.Count} display={session.DisplayCount}";
        }

        /// <summary>Runs a token outing with one find so the shift timer resets (the domain's own rule).</summary>
        public static string FreshShift()
        {
            var s = Trade.Session;
            long id = s.BeginExpedition();
            s.CollectLoot(id, s.FindDefinition("common_find"));
            s.CompleteExpedition(id);
            return $"time={s.RemainingSeconds:F0} {s.LastMessage}";
        }

        public static string Open() { bool ok = Trade.Session.Open(); return "open=" + ok + " " + Trade.Session.LastMessage; }

        public static string Haggle(string args)
        {
            var session = Trade.Session;
            var deal = session.CurrentDeal;
            if (deal == null && session.CurrentCustomer != null)
            {
                var customer = session.CurrentCustomer;
                for (int i = 0; i < session.Slots.Count; i++)
                    if (session.Slots[i] != null && session.Slots[i].Definition.Category == customer.RequestedCategory)
                    { Trade.SelectCustomerItem(i, customer.Id); break; }
                return "picked for category " + customer.RequestedCategory + ": " + session.CurrentDeal?.Item.Definition.Name;
            }
            if (deal == null) return "no customer";
            if (args == "accept") Trade.AcceptPrice(deal.Id, deal.Revision);
            else Trade.OfferPrice(deal.Id, deal.Revision, int.Parse(args));
            var after = session.CurrentDeal;
            return after == null ? $"done: {session.LastMessage} gold={session.Gold}" : $"R={after.R} B={after.B} N={after.N} patience={after.Patience} mood={after.Mood} «{after.Reaction}»";
        }

        public static string Shop()
        {
            var s = Trade.Session;
            var d = s.CurrentDeal;
            return $"open={s.IsOpen} time={s.RemainingSeconds:F0} gold={s.Gold} revenue={s.GrossRevenue} customer={s.CurrentCustomer?.Name} ready={Trade.CustomerReady} deal={(d == null ? "-" : $"{d.Item.Definition.Name} R={d.R} B={d.B} N={d.N} p={d.Patience} {d.Mood}")}";
        }

        public static string Workshop(string mode)
        {
            var w = World.workshop;
            if (mode == "close") { w.CloseEmbedded(); return "closed"; }
            return "open=" + w.OpenEmbedded(mode == "market") + " " + w.feedback.text;
        }

        public static string Switch() { World.thrower.SwitchDeck(); return "deck=" + World.workshop.Model.Current.ActiveKind; }
        public static string Discard() { World.thrower.Discard(); return "discarded"; }
        public static string Hurt(string args) { World.vitals.Hurt((int)Numbers(args)[0], Vector3.back * 3, World.motor.transform.position + World.motor.transform.forward * 2); return "hp=" + World.vitals.HalfHearts; }
        public static string TimeScale(string args) { Time.timeScale = Numbers(args)[0]; return "ok"; }

        public static string State()
        {
            var b = new StringBuilder();
            var game = Game;
            var world = World;
            b.AppendLine($"phase={game.Phase} running={world.Running} returning={world.Returning} pos={world.motor.transform.position} hp={world.vitals.HalfHearts}");
            var trip = world.workshop.Model.Current;
            if (trip != null)
                foreach (var kind in new[] { DeckKind.Exploration, DeckKind.Combat })
                {
                    var deck = trip.Get(kind);
                    b.AppendLine($"{kind}{(trip.ActiveKind == kind ? "*" : "")}: hand=[{string.Join(",", deck.Hand.Select(c => c.Spec.Name))}] stock={deck.StockCount}");
                }
            b.AppendLine("selected=" + world.thrower.Selected?.Spec.Name + " loot=" + string.Join(",", world.Loot.Select(l => l.name + "x" + l.count)));
            foreach (var c in world.creatures) b.AppendLine($"  {c.displayName} {c.State} hp={c.Health} at {c.transform.position} active={c.gameObject.activeSelf}");
            foreach (var o in world.obstacles) b.AppendLine($"  {o.kind} resolved={o.Resolved}");
            return b.ToString();
        }
    }
}
