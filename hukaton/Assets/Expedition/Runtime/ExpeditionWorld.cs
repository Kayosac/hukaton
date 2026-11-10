using System;
using System.Collections;
using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using Hakaton.Loot;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Owns one outing in the authored valley: resets the world, keeps the loot ledger, routes card impacts to
    /// their effects and runs the return / defeat transitions. Spawns only authored prefabs.
    /// Services for card effects (objects until the end of the outing, zones, fighters) are in ExpeditionWorld.Cards.cs.
    /// </summary>
    public sealed partial class ExpeditionWorld : MonoBehaviour, IExternalMotorControl
    {
        [Header("Game systems")]
        public DeckPrototype workshop;
        public ExpeditionSettings settings;
        public FirstPersonMotor motor;
        public PlayerControlGate control;
        public PlayerVitals vitals;
        public Camera viewCamera;
        public CardThrower thrower;
        public ExpeditionHud hud;
        public CardHandView hand;
        [Header("Feel")]
        public CameraFeel feel;
        public TimeFeel time;
        public SfxPlayer sfx;
        public ScreenFade fade;
        public ExpeditionAudio cues;
        public ExpeditionVfx vfx;
        [Header("Authored places and prefabs")]
        public Transform departurePose;
        public Transform shopPose;
        public Transform spawnedObjects;
        public CardProjectile projectilePrefab;
        public CardDebris debrisPrefab;
        public string locationName = "Тихая лощина";
        [Header("Loot")]
        [Tooltip("Every item the game knows: what finds and trophies are, how they look, rarity colours.")]
        public LootCatalog lootCatalog;
        [Tooltip("The pickup creatures' drops are spawned from (Assets/Loot/Prefabs/Loot drop.prefab).")]
        public WorldLoot lootDrop;
        [Header("World content")]
        [Tooltip("Optional: builds a new world for every outing (IOutingWorld) and fills the content lists below as it loads. " +
                 "Without it the authored content below is reset each outing.")]
        public MonoBehaviour outingWorld;
        [Tooltip("Optional: the ready-made location offered at the door beside the endless world (AuthoredOutingWorld).")]
        public AuthoredOutingWorld readyWorld;
        public List<WorldLoot> finds = new List<WorldLoot>();
        public List<WorldObstacle> obstacles = new List<WorldObstacle>();
        public List<WorldCreature> creatures = new List<WorldCreature>();
        public List<PhysicsProp> props = new List<PhysicsProp>();

        /// <summary>The world the current (or next) outing goes to: the endless one unless the ready place was chosen.</summary>
        public IOutingWorld OutingWorld => activeWorld ?? ChosenWorld;
        IOutingWorld ChosenWorld => NextPlace == OutingPlace.Ready && readyWorld ? readyWorld : outingWorld as IOutingWorld;
        IOutingWorld activeWorld;
        /// <summary>Chosen at the door before departing; read when the outing begins.</summary>
        public OutingPlace NextPlace { get; set; } = OutingPlace.Endless;
        public bool HasPlaceChoice => readyWorld && outingWorld is IOutingWorld;
        /// <summary>The title of the place the next outing goes to.</summary>
        public string NextLocationName => NextPlace == OutingPlace.Ready && readyWorld ? readyWorld.placeName : locationName;

        readonly List<LootSave> loot = new List<LootSave>();
        readonly List<LootSave> pickups = new List<LootSave>();
        public IReadOnlyList<LootSave> Loot => loot.AsReadOnly();
        /// <summary>Every find picked up on this outing, one entry per copy, in the order it was picked up.</summary>
        public IReadOnlyList<LootSave> Pickups => pickups.AsReadOnly();
        public int LootValue { get; private set; }
        public bool Returning { get; private set; }
        public bool Running => workshop.Model != null && workshop.Model.IsAway && !Returning;
        public int Impacts { get; private set; }
        public WorldLoot FocusedLoot { get; private set; }
        public string Notice { get; private set; }
        public float NoticeUntil { get; private set; }
        /// <summary>Raised for each item picked up.</summary>
        public event Action<LootItem> Collected;
        public event Action<Vector3> PlayerLaunched;
        /// <summary>Every card landing: the copy and what it hit (the tutorial watches its bush).</summary>
        public event Action<CardCopy, Collider, Vector3> Impacted;
        /// <summary>The Return card was used in the cave prologue and the screen is dark: the tutorial takes the player on.</summary>
        public event Action PrologueReturned;
        /// <summary>The cave prologue runs instead of an outing to the valley.</summary>
        public bool InPrologue { get; private set; }
        /// <summary>Where a fall into the prologue's water puts the player back when no shores are listed below.</summary>
        public Transform prologueShore;
        /// <summary>Safe spots on both banks of the prologue's water: a fall returns the player to the nearest one.</summary>
        public Transform[] prologueShores = new Transform[0];
        /// <summary>The cave's roots and water: searched instead of the valley's while the prologue runs.</summary>
        public WorldObstacle[] prologueObstacles = new WorldObstacle[0];
        /// <summary>The cave's creatures (mushrooms without trophies): spawned instead of the valley's while the prologue runs.</summary>
        public WorldCreature[] prologueCreatures = new WorldCreature[0];
        /// <summary>The creatures of the place the player is in: the cave's during the prologue, the valley's otherwise.</summary>
        public IReadOnlyList<WorldCreature> ActiveCreatures => InPrologue ? (IReadOnlyList<WorldCreature>)prologueCreatures : creatures;

        void Awake() { workshop.Departed += BeginOuting; }
        void OnDestroy() { if (workshop) workshop.Departed -= BeginOuting; }

        public void BeginOuting()
        {
            InPrologue = false;
            Returning = false;
            Impacts = 0;
            LootValue = 0;
            loot.Clear();
            pickups.Clear();
            ClearCardEffects();
            foreach (Transform child in spawnedObjects) Destroy(child.gameObject);
            Pose start;
            if (activeWorld != null) activeWorld.EndOuting();
            activeWorld = ChosenWorld;
            if (activeWorld != null) start = activeWorld.BeginOuting(this);
            else
            {
                start = new Pose(departurePose.position, departurePose.rotation);
                var random = new System.Random(workshop.Model.Journeys * 1637);
                foreach (var obstacle in obstacles) obstacle.ResetForOuting();
                foreach (var find in finds) find.ResetForOuting(random);
                foreach (var prop in props) prop.ResetForOuting();
                foreach (var creature in creatures) creature.ResetForOuting(random);
            }
            vitals.Restore();
            motor.Teleport(start.position, start.rotation);
            thrower.ResetSelection();
            control.CaptureCursor();
            hud.canvas.enabled = true;
            hud.BeginOuting();
            hand.Deal();
            if (time) time.ResetTime();
            if (sfx && cues) sfx.Loop(this, cues.ambience, viewCamera.transform.position);
        }

        void Update()
        {
            FocusedLoot = null;
            if (!Running) return;
            if (sfx && cues && !InPrologue) sfx.Loop(this, cues.ambience, viewCamera.transform.position);
            if (motor.transform.position.y < settings.killHeight) { vitals.Hurt(settings.maximumHalfHearts, Vector3.zero, null); return; }
            if (control.GameplayActive) FocusedLoot = FindFocusedLoot();
            if (FocusedLoot) FocusedLoot.SetFocused(true);
        }

        WorldLoot FindFocusedLoot()
        {
            var eye = viewCamera.transform;
            // Generous aim assist: a fat sphere cast, then the closest loot to the crosshair ray.
            var hits = Physics.SphereCastAll(eye.position, .35f, eye.forward, settings.interactionReach, ~(1 << 2), QueryTriggerInteraction.Ignore);
            WorldLoot best = null;
            float bestScore = float.MaxValue;
            foreach (var hit in hits)
            {
                var item = hit.collider.GetComponentInParent<WorldLoot>();
                if (!item || !item.Available) continue;
                Vector3 toItem = item.transform.position - eye.position;
                // Only a find in plain sight: nothing solid between the eye and the middle of it.
                Vector3 toMiddle = hit.collider.bounds.center - eye.position;
                if (Physics.Raycast(eye.position, toMiddle.normalized, out var blocker, Mathf.Max(0, toMiddle.magnitude - .05f), ~(1 << 2), QueryTriggerInteraction.Ignore)
                    && !blocker.collider.transform.IsChildOf(item.transform)) continue;
                float score = Vector3.Cross(eye.forward, toItem).magnitude + toItem.magnitude * .05f;
                if (score < bestScore) { bestScore = score; best = item; }
            }
            return best;
        }

        public bool TryPickUpFocused() => FocusedLoot && FocusedLoot.PickUp(viewCamera.transform);

        /// <summary>Credits one copy of the item to this outing's bag.</summary>
        public void Collect(LootItem item)
        {
            if (!Running || !item) return;
            if (lootCatalog && !lootCatalog.Contains(item))
                Debug.LogError($"Picked up '{item.name}', which is not in the loot catalogue: the shop will refuse this outing's delivery.", item);
            var entry = loot.Find(x => x.id == item.id);
            if (entry == null) loot.Add(new LootSave { id = item.id, name = item.displayName, value = item.price, count = 1 });
            else entry.count++;
            pickups.Add(new LootSave { id = item.id, name = item.displayName, value = item.price, count = 1 });
            LootValue += item.price;
            Play(cues ? cues.pickup : null);
            Collected?.Invoke(item);
        }

        /// <summary>A drop of <paramref name="item"/> at <paramref name="position"/>, alive until the outing ends.</summary>
        public WorldLoot SpawnLoot(LootItem item, Vector3 position)
        {
            if (!item) return null;
            if (!lootDrop)
            {
                Debug.LogError("ExpeditionWorld: assign the loot drop prefab.", this);
                return null;
            }
            var drop = Instantiate(lootDrop, position, Quaternion.identity, spawnedObjects);
            drop.world = this;
            drop.Setup(item);
            return drop;
        }

        public void Announce(string text, float seconds = 3) { Notice = text; NoticeUntil = Time.time + seconds; }

        // ---------- presentation helpers ----------

        public AudioSource Play(SfxCue cue, Vector3? position = null, float volume = 1, float pitch = 1)
            => sfx && cue ? sfx.Play(cue, position, volume, pitch) : null;

        public VfxBurst Spawn(VfxBurst prefab, Vector3 position, Quaternion rotation, Color tint, float scale = 1)
            => VfxBurst.Spawn(prefab, position, rotation, spawnedObjects, tint, scale);

        public Texture Artwork(CardCopy card)
        {
            var sprite = card.IsReturn ? workshop.config.returnArtwork : workshop.config.Find(card.Spec.Id)?.artwork;
            return sprite ? sprite.texture : null;
        }

        public Color Accent(CardCopy card)
        {
            if (card == null || card.IsReturn) return new Color(1f, .82f, .35f);
            var definition = workshop.config.Find(card.Spec.Id);
            return definition ? definition.accent : Color.white;
        }

        public CardProjectile Launch(CardCopy card, Vector3 position, Vector3 velocity, Vector2 spin, CardView source, Pose? handPose)
        {
            var projectile = Instantiate(projectilePrefab, position, Quaternion.LookRotation(velocity.normalized), spawnedObjects);
            projectile.Launch(this, card, source, velocity, spin, handPose);
            return projectile;
        }

        public CardDebris SpawnDebris(CardView source, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 spin, float seconds)
        {
            if (!debrisPrefab) return null;
            var debris = Instantiate(debrisPrefab, position, rotation, spawnedObjects);
            debris.view.CopyFrom(source);
            debris.view.transform.localScale = source.transform.lossyScale;
            debris.Fling(this, velocity, spin, seconds);
            return debris;
        }

        public void SpawnStuckCard(CardView source, Vector3 position, Quaternion rotation, Transform target, Color colour)
        {
            if (!debrisPrefab) return;
            var debris = Instantiate(debrisPrefab, position, rotation, spawnedObjects);
            debris.view.CopyFrom(source);
            debris.view.transform.localScale = source.transform.lossyScale;
            debris.Stick(this, target, 3.5f, .45f, colour, vfx ? vfx.cardBurn : null);
        }

        public WorldObstacle ObstacleNear(Vector3 point, float radius, ObstacleKind kind)
        {
            foreach (var obstacle in InPrologue ? (IEnumerable<WorldObstacle>)prologueObstacles : obstacles)
                if (obstacle.kind == kind && obstacle.DistanceTo(point) <= radius) return obstacle;
            return null;
        }

        // ---------- impacts ----------

        public void ApplyImpact(CardCopy copy, CardDefinition definition, Collider target, Vector3 point, Vector3 normal, Vector3 velocity,
            Vector3? throwOrigin = null)
        {
            if (!Running || !definition) return;
            Impacts++;
            var context = new ImpactContext(this, copy, definition, target, point, normal, velocity, throwOrigin);
            if (definition.effectAsset is CardEffect effect) effect.Apply(context);
            else Debug.LogWarning($"Card '{definition.id}' has no effect asset assigned.", definition);
            Impacted?.Invoke(copy, target, point);
        }

        public void OnPlayerLaunched(Vector3 push)
        {
            if (feel) { feel.PunchFov(55); feel.Kick(new Vector3(18, 0, 0)); feel.AddTrauma(.2f); }
            Play(cues ? cues.launch : null);
            PlayerLaunched?.Invoke(push);
        }

        // ---------- return and defeat ----------

        public void ReturnToShop(bool defeated) => ReturnToShop(defeated, false);

        /// <summary>Deep water: no damage over time, the player drowns at once — the view darkens while the body sinks.</summary>
        public void Drown()
        {
            if (InPrologue)
            {
                // The prologue has no death: a short dip into the dark and back on the nearest shore.
                if (!shoreTrip) StartCoroutine(BackToShore());
                return;
            }
            if (!Running || !vitals.Drain()) return;
            ReturnToShop(true, true);
        }

        bool shoreTrip;

        IEnumerator BackToShore()
        {
            shoreTrip = true;
            thrower.Cancel();
            control.SetBlocked(this, true);
            Play(cues ? cues.drownSplash : null);
            if (fade) yield return fade.FadeTo(1, .25f);
            var shore = NearestShore(motor.transform.position);
            if (shore) motor.Teleport(shore.position, shore.rotation);
            yield return new WaitForSecondsRealtime(.15f);
            control.SetBlocked(this, false);
            if (fade) yield return fade.FadeTo(0, .35f);
            shoreTrip = false;
        }

        /// <summary>The listed prologue shore closest to the point (on the ground plane), or the single fallback.</summary>
        public Transform NearestShore(Vector3 point)
        {
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (var shore in prologueShores)
            {
                if (!shore) continue;
                Vector3 offset = shore.position - point;
                offset.y = 0;
                if (offset.sqrMagnitude < bestDistance) { bestDistance = offset.sqrMagnitude; best = shore; }
            }
            return best ? best : prologueShore;
        }

        /// <summary>
        /// The cave prologue (board, «Туториал», 01–04): the outing the tutorial runs itself, starting at its own spot,
        /// with an empty hand the tutorial fills. No loot, no Return card, no death.
        /// </summary>
        public void BeginPrologue(Transform start)
        {
            InPrologue = true;
            Returning = false;
            Impacts = 0;
            LootValue = 0;
            loot.Clear();
            pickups.Clear();
            ClearCardEffects();
            foreach (Transform child in spawnedObjects) Destroy(child.gameObject);
            foreach (var obstacle in prologueObstacles) obstacle.ResetForOuting();
            // spawned like the valley's: each one at a random spot of its own
            var random = new System.Random(Environment.TickCount);
            foreach (var creature in prologueCreatures) if (creature) creature.ResetForOuting(random);
            vitals.Restore();
            motor.Teleport(start.position, start.rotation);
            thrower.ResetSelection();
            control.CaptureCursor();
            hud.canvas.enabled = true;
            hud.BeginOuting();
            hand.Deal();
            if (time) time.ResetTime();
        }

        /// <summary>Ends the prologue on the spot (the screen is already dark): clears it and moves the player to the shop.</summary>
        public void EndPrologue()
        {
            if (!InPrologue) return;
            workshop.Model.Current?.Defeat();
            thrower.Cancel();
            ClearCardEffects();
            hand.Hide();
            hud.canvas.enabled = false;
            motor.Teleport(shopPose.position, shopPose.rotation);
            vitals.Restore();
            if (sfx) sfx.StopLoop(this);
            control.SetBlocked(this, false);
            Returning = false;
            InPrologue = false;
        }

        void ReturnToShop(bool defeated, bool drowned)
        {
            if (!Running) return;
            // The prologue has no defeat; its Return card (from the goblin) ends the cave instead of an outing.
            if (InPrologue && defeated) return;
            if (!defeated && !workshop.Model.Current.ReturnHome()) return;
            if (defeated) workshop.Model.Current.Defeat();
            Returning = true;
            thrower.Cancel();
            // Drowning keeps the mouse: the player can still look around while sinking; the body is held by Sink.
            if (!drowned) control.SetBlocked(this, true);
            StartCoroutine(InPrologue ? PrologueReturn() : ReturnTransition(defeated, drowned));
        }

        /// <summary>The cave's Return card: the same fling and dark as at the end of an outing, then the tutorial moves on.</summary>
        IEnumerator PrologueReturn()
        {
            yield return FlingReturnCard();
            hand.Hide();
            if (fade) yield return fade.FadeTo(1, settings.returnFadeSeconds);
            if (time) time.ResetTime();
            if (!InPrologue) yield break;
            Returning = false;
            PrologueReturned?.Invoke();
        }

        /// <summary>The Return card is flung skyward and bursts.</summary>
        IEnumerator FlingReturnCard()
        {
            var eye = viewCamera.transform;
            if (hand.Take(-1, out var pose, out var view))
                SpawnDebris(view, pose.position, pose.rotation, eye.forward * 4 + Vector3.up * 9, new Vector3(0, 0, 900), .7f);
            Play(cues ? cues.returnCard : null);
            if (feel) { feel.Kick(new Vector3(-35, 0, 0)); feel.PunchFov(30); }
            yield return new WaitForSecondsRealtime(.3f);
            Spawn(vfx ? vfx.returnPoof : null, eye.position + eye.forward * 2.5f + Vector3.up * 1.5f, Quaternion.identity, new Color(1f, .82f, .35f));
        }

        /// <summary>The body sinks under water and the screen goes black at once; the mouse still looks around.</summary>
        IEnumerator Sink()
        {
            if (motor.ExternallyControlled) motor.RevokeExternalControl();
            bool sinking = motor.TryClaimExternalControl(this);
            Play(cues ? cues.drownSplash : null);
            // The cards slip out of the hand and fly off, carried along with the view.
            hand.ScatterAll(settings.drownCardsSpeed, settings.drownDarkenSeconds + .35f, viewCamera.transform);
            float time = 0, total = settings.drownDarkenSeconds + settings.drownSeconds;
            while (time < total)
            {
                time += Time.unscaledDeltaTime;
                if (fade) fade.Set(Ease.InOutSine(time / settings.drownDarkenSeconds), settings.drownColour);
                if (sinking && motor.ExternalOwner == (IExternalMotorControl)this)
                    motor.MoveExternally(Vector3.down * settings.drownSinkSpeed * Time.deltaTime);
                yield return null;
            }
        }

        Vector3 IExternalMotorControl.OnControlRevoked() => Vector3.zero;

        IEnumerator ReturnTransition(bool defeated, bool drowned)
        {
            if (drowned) { yield return Sink(); control.SetBlocked(this, true); }
            else if (defeated)
            {
                if (time) time.SlowMotion(.9f, .25f, .2f);
                if (feel) { feel.AddTrauma(.6f); feel.Kick(new Vector3(40, 0, 25)); }
                yield return new WaitForSecondsRealtime(.75f);
            }
            // The Return card is flung skyward and bursts — then the screen goes dark.
            else yield return FlingReturnCard();
            hand.Hide();
            if (fade && !drowned) yield return fade.FadeTo(1, settings.returnFadeSeconds);
            else if (fade) fade.Set(1, settings.drownColour);
            // A beat of black before the shop: never tear down the outing in the frame that finished the sinking.
            if (drowned) yield return new WaitForSecondsRealtime(.2f);
            if (time) time.ResetTime();
            if (sfx) sfx.StopLoop(this);

            int kept = 0, lost = 0;
            var retained = new List<LootSave>();
            foreach (var entry in loot)
            {
                int count = defeated ? Mathf.FloorToInt(entry.count * (1 - settings.defeatLootLoss)) : entry.count;
                kept += count;
                lost += entry.count - count;
                if (count > 0) retained.Add(new LootSave { id = entry.id, name = entry.name, count = count, value = entry.value });
            }
            // Nothing a card made outlives the outing: objects, zones, statuses, the player's modifiers.
            ClearCardEffects();
            motor.Teleport(shopPose.position, shopPose.rotation);
            activeWorld?.EndOuting();
            activeWorld = null;
            vitals.Restore();
            control.SetBlocked(this, false);
            hud.canvas.enabled = false;
            Returning = false;
            // Complete only after local cleanup: the integration callback disables this world root.
            workshop.CompleteOuting(drowned
                ? $"Вы утонули — глубокую воду лучше заморозить. Сохранено: {kept}; потеряно: {lost}."
                : defeated
                ? $"Вы вернулись после поражения. Сохранено: {kept}; потеряно: {lost}."
                : $"Вы дома. В запас лавки добавлено: {kept}.", retained, defeated);
            if (fade) fade.StartCoroutine(fade.FadeTo(0, settings.arriveFadeSeconds));
        }
    }
}
