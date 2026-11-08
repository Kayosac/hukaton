using System.Globalization;
using System.Linq;
using System.Text;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Expeditions;
using Hakaton.Game;
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
