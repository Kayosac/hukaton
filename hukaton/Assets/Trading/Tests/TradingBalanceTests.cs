using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Hakaton.Trading.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Hakaton.Trading.Tests
{
    /// <summary>
    /// Trade balance (stage 2): 20 000 random visitors on the real <see cref="TradingSession"/>, with the trade numbers the
    /// game scene gives its <see cref="TradingPrototype"/> (read from Hakaton.unity and passed through
    /// <see cref="TradingPrototype.CreateTradeSettings"/>, as the game does). The strategies are those of the balance model
    /// (output/balance-audit-2026-11-22/model/demo_sim.py), the price is 100 coins.
    /// </summary>
    public sealed class TradingBalanceTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        const int Customers = 20000, Seed = 20261124, Price = 100;
        static readonly ItemDefinition Ware = new ItemDefinition("ware", "Товар", "Трофеи", Price);
        /// <summary>The stage 2 trade numbers: the scene must keep the prototype's defaults for them.</summary>
        static readonly string[] StageFields =
        {
            nameof(TradingPrototype.minCeilingRatio), nameof(TradingPrototype.maxCeilingRatio),
            nameof(TradingPrototype.minOfferCeilingRatio), nameof(TradingPrototype.maxOfferCeilingRatio),
            nameof(TradingPrototype.askerFirstCounterRatio)
        };

        // Option indices of the price buttons: +0, +5, +15, +25, +50%.
        const int Plus0 = 0, Plus5 = 1, Plus15 = 2, Plus25 = 3, Plus50 = 4;

        static TradeSettings sceneSettings;
        static readonly Dictionary<string, Tally> results = new Dictionary<string, Tally>();

        // ---------- the scene's numbers ----------

        /// <summary>Top-level scalar fields of the scene's only TradingPrototype component, as written in the YAML.</summary>
        static Dictionary<string, string> SceneFields(TradingPrototype probe)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(probe)));
            Assert.That(guid, Is.Not.Empty, "TradingPrototype script guid");
            string[] lines = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(Application.dataPath), ScenePath));
            Dictionary<string, string> fields = null;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("  m_Script: ") || !lines[i].Contains("guid: " + guid + ",")) continue;
                Assert.That(fields, Is.Null, "The game scene has one TradingPrototype.");
                fields = new Dictionary<string, string>();
                for (int j = i + 1; j < lines.Length && !lines[j].StartsWith("--- "); j++)
                {
                    string line = lines[j];
                    if (line.Length < 3 || line[0] != ' ' || line[1] != ' ' || line[2] == ' ' || line[2] == '-') continue;
                    int colon = line.IndexOf(": ", StringComparison.Ordinal);
                    if (colon > 2) fields[line.Substring(2, colon - 2)] = line.Substring(colon + 2).Trim();
                }
            }
            Assert.That(fields, Is.Not.Null, "The game scene has a TradingPrototype.");
            return fields;
        }

        /// <summary>Puts the scene's values into the prototype's scalar fields; fields the scene lacks keep their defaults (as in Unity).</summary>
        static void ApplySceneValues(TradingPrototype prototype, Dictionary<string, string> scene)
        {
            foreach (var field in typeof(TradingPrototype).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!scene.TryGetValue(field.Name, out string text)) continue;
                if (field.FieldType == typeof(float)) field.SetValue(prototype, float.Parse(text, CultureInfo.InvariantCulture));
                else if (field.FieldType == typeof(int)) field.SetValue(prototype, int.Parse(text, CultureInfo.InvariantCulture));
                else if (field.FieldType == typeof(bool)) field.SetValue(prototype, text != "0");
            }
        }

        static T WithProbe<T>(Func<TradingPrototype, T> use)
        {
            // In Edit Mode the component's Awake does not run: it is only a holder of the Inspector fields.
            var holder = new GameObject("TradingBalanceProbe") { hideFlags = HideFlags.HideAndDontSave };
            try { return use(holder.AddComponent<TradingPrototype>()); }
            finally { Object.DestroyImmediate(holder); }
        }

        static TradeSettings SceneSettings()
        {
            return sceneSettings ??= WithProbe(probe =>
            {
                ApplySceneValues(probe, SceneFields(probe));
                return probe.CreateTradeSettings();
            });
        }

        // ---------- playing visitors ----------

        sealed class Tally
        {
            public long Revenue;
            public int Sold, WalkedAway, AskerCounters, CountersBelowBase;
            public double Yield => Revenue * 100.0 / ((double)Customers * Price);
            public double WalkedShare => WalkedAway * 100.0 / Customers;
            public override string ToString() =>
                $"{Yield:0.0}% of base, walked away {WalkedShare:0.0}%, asker counters {AskerCounters}, below base {CountersBelowBase}";
        }

        /// <summary>One visitor at the counter: the moves of a strategy, with the asker's counters checked as they come.</summary>
        sealed class Visit
        {
            readonly TradingSession session;
            readonly Tally tally;
            public readonly TradeDeal Deal;
            public readonly bool Asks;
            public Visit(TradingSession session, Tally tally)
            {
                this.session = session; this.tally = tally;
                Deal = session.CurrentDeal; Asks = session.CurrentCustomer.AsksPrice;
            }
            public bool Open => session.CurrentDeal == Deal;
            public bool HasCounter => Deal.N.HasValue;
            public bool LastOffer => Deal.IsLastOffer;
            public bool Offer(int option)
            {
                if (!session.Offer(Deal.Id, Deal.Revision, option)) return false;
                if (Asks && Open && Deal.N.HasValue)
                {
                    tally.AskerCounters++;
                    if (Deal.N.Value < Deal.R) tally.CountersBelowBase++;
                }
                return true;
            }
            public void Accept() { if (!session.Accept(Deal.Id, Deal.Revision)) Assert.Fail("accept the counter"); }
            /// <summary>The model's way out when a button is disabled: take the counter, or ask the price itself.</summary>
            public void Settle() { if (HasCounter) Accept(); else Offer(Plus0); }
        }

        static void SellAtOnce(Visit v) { if (v.Asks) v.Offer(Plus0); else v.Accept(); }

        static Action<Visit> AskOnce(int option) => v =>
        {
            if (!v.Offer(option)) { v.Settle(); return; }
            if (v.Open) v.Accept();
        };

        static Action<Visit> AskInTurn(params int[] options) => v =>
        {
            foreach (int option in options)
            {
                if (!v.Open) return;
                if (v.LastOffer) { v.Accept(); return; }
                v.Offer(option);
            }
            if (v.Open) v.Settle();
        };

        static Action<Visit> AskRepeatedly(int option) => v =>
        {
            for (int round = 0; v.Open; round++)
            {
                if (round >= 50) Assert.Fail("the haggle must end");
                if (v.LastOffer) { v.Accept(); return; }
                if (!v.Offer(option)) { v.Settle(); return; }
            }
        };

        static readonly (string name, Action<Visit> play)[] Strategies =
        {
            ("sell at once", SellAtOnce),
            ("+5% once, accept", AskOnce(Plus5)),
            ("+15% once, accept", AskOnce(Plus15)),
            ("+25% once, accept", AskOnce(Plus25)),
            ("+50% once, accept", AskOnce(Plus50)),
            ("+50% then +15%, accept", AskInTurn(Plus50, Plus15)),
            ("+15% repeatedly", AskRepeatedly(Plus15)),
            ("+25% repeatedly", AskRepeatedly(Plus25)),
        };

        static Tally Play(string name)
        {
            if (results.TryGetValue(name, out var cached)) return cached;
            var play = Array.Find(Strategies, s => s.name == name).play;
            var session = new TradingSession(SceneSettings(), Seed);
            var tally = new Tally();
            for (int served = 0; served < Customers; served++)
            {
                if (session.IsOpen && session.DisplayCount < 3) session.Close();
                if (!session.IsOpen)
                {
                    // Restock the display between visitors: the shop never runs out, and the clock is never ticked.
                    int missing = session.Slots.Count - session.DisplayCount;
                    session.SeedInventory(Ware, missing);
                    for (int slot = 0; slot < session.Slots.Count; slot++)
                        if (session.Slots[slot] == null && !session.PlaceItem(0, slot)) Assert.Fail("restock the display");
                    if (!session.Open()) Assert.Fail("open the shop");
                }
                else if (!session.AdvanceCustomer()) Assert.Fail("the next visitor comes");
                if (session.CurrentDeal == null) Assert.Fail("an ordinary visitor with a deal");
                int sales = session.SalesCount, departures = session.DepartureSequence;
                var visit = new Visit(session, tally);
                play(visit);
                // Plain checks in the loop: NUnit constraints cost more than the visit itself.
                if (visit.Open || session.DepartureSequence != departures + 1) Assert.Fail(name + ": the visit ends once");
                if (session.SalesCount > sales) { tally.Sold++; tally.Revenue += session.LastSalePrice; }
                else if (session.LastDeparture.Outcome == CustomerOutcome.WalkedAway) tally.WalkedAway++;
            }
            results[name] = tally;
            return tally;
        }

        // ---------- tests ----------

        [Test]
        public void SceneKeepsThePrototypeDefaultsOfTheStageTradeNumbers()
        {
            WithProbe(probe =>
            {
                var scene = SceneFields(probe);
                foreach (string name in StageFields)
                {
                    var field = typeof(TradingPrototype).GetField(name);
                    float fallback = (float)field.GetValue(probe);
                    var range = field.GetCustomAttribute<UnityEngine.RangeAttribute>();
                    if (range != null)
                        Assert.That(fallback, Is.InRange(range.min, range.max), name + ": the Inspector range admits the default");
                    // A field the scene does not list yet (new field, scene not re-saved) gets the default in Unity.
                    if (!scene.TryGetValue(name, out string text)) continue;
                    Assert.That(float.Parse(text, CultureInfo.InvariantCulture), Is.EqualTo(fallback).Within(1e-5f),
                        name + ": the game scene and the TradingPrototype default must agree");
                }
                return 0;
            });
        }

        [Test]
        public void SellingAtOnceYieldsNinetyThreeToNinetySevenPercentOfBase()
        {
            var tally = Play("sell at once");
            Assert.That(tally.Yield, Is.InRange(93.0, 97.0), tally.ToString());
            Assert.That(tally.WalkedAway, Is.Zero, tally.ToString());
        }

        [Test]
        public void OneAskOfPlusTwentyFiveThenTheCounterYieldsOneHundredThreeToSevenPercent()
        {
            var tally = Play("+25% once, accept");
            Assert.That(tally.Yield, Is.InRange(103.0, 107.0), tally.ToString());
        }

        [Test]
        public void AskingPlusTwentyFiveAgainAndAgainDrivesAwayThirtySevenToFortySevenPercent()
        {
            var tally = Play("+25% repeatedly");
            Assert.That(tally.WalkedShare, Is.InRange(37.0, 47.0), tally.ToString());
        }

        [Test]
        public void AskersCounterIsNeverBelowTheBasePrice()
        {
            var watch = Stopwatch.StartNew();
            var report = new StringBuilder("Trade balance, scene numbers, " + Customers + " visitors, price " + Price + ":\n");
            int counters = 0, below = 0;
            foreach (var (name, _) in Strategies)
            {
                var tally = Play(name);
                counters += tally.AskerCounters; below += tally.CountersBelowBase;
                report.Append("  ").Append(name).Append(": ").Append(tally).Append('\n');
            }
            report.Append("  (").Append(watch.ElapsedMilliseconds).Append(" ms for the strategies not played before)");
            Debug.Log(report.ToString());
            Assert.That(counters, Is.GreaterThan(Customers), "askers drew counters in the run");
            Assert.That(below, Is.Zero, report.ToString());
        }
    }
}
