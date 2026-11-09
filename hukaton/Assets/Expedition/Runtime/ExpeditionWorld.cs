using System;
using System.Collections;
using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
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
        [Header("World content")]
        public WorldLoot[] finds;
        public WorldObstacle[] obstacles;
        public WorldCreature[] creatures;
        public PhysicsProp[] props;

        readonly List<LootSave> loot = new List<LootSave>();
        public IReadOnlyList<LootSave> Loot => loot.AsReadOnly();
        public int LootValue { get; private set; }
        public bool Returning { get; private set; }
        public bool Running => workshop.Model != null && workshop.Model.IsAway && !Returning;
        public int Impacts { get; private set; }
        public WorldLoot FocusedLoot { get; private set; }
        public string Notice { get; private set; }
        public float NoticeUntil { get; private set; }
        /// <summary>Raised for each item picked up: display name, value.</summary>
        public event Action<string, int> Collected;
        public event Action<Vector3> PlayerLaunched;

        void Awake() { workshop.Departed += BeginOuting; }
        void OnDestroy() { if (workshop) workshop.Departed -= BeginOuting; }

        public void BeginOuting()
        {
            Returning = false;
            Impacts = 0;
            LootValue = 0;
            loot.Clear();
            ClearCardEffects();
            foreach (Transform child in spawnedObjects) Destroy(child.gameObject);
            var random = new System.Random(workshop.Model.Journeys * 1637);
            foreach (var obstacle in obstacles) obstacle.ResetForOuting();
            foreach (var find in finds) find.ResetForOuting(random);
            foreach (var prop in props) prop.ResetForOuting();
            foreach (var creature in creatures) creature.ResetForOuting(random);
            vitals.Restore();
            motor.Teleport(departurePose.position, departurePose.rotation);
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
            if (sfx && cues) sfx.Loop(this, cues.ambience, viewCamera.transform.position);
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
                float score = Vector3.Cross(eye.forward, toItem).magnitude + toItem.magnitude * .05f;
                if (score < bestScore) { bestScore = score; best = item; }
            }
            return best;
        }

        public bool TryPickUpFocused() => FocusedLoot && FocusedLoot.PickUp(viewCamera.transform);

        public void Collect(string itemId, string displayName, int value)
        {
            if (!Running) return;
            var entry = loot.Find(x => !string.IsNullOrEmpty(itemId) ? x.id == itemId : string.IsNullOrEmpty(x.id) && x.name == displayName && x.value == value);
            if (entry == null) loot.Add(new LootSave { id = itemId, name = displayName, value = value, count = 1 });
            else entry.count++;
            LootValue += value;
            Play(cues ? cues.pickup : null);
            Collected?.Invoke(displayName, value);
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
            foreach (var obstacle in obstacles)
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
            if (!Running || !vitals.Drain()) return;
            ReturnToShop(true, true);
        }

        void ReturnToShop(bool defeated, bool drowned)
        {
            if (!Running) return;
            if (!defeated && !workshop.Model.Current.ReturnHome()) return;
            if (defeated) workshop.Model.Current.Defeat();
            Returning = true;
            thrower.Cancel();
            control.SetBlocked(this, true);
            StartCoroutine(ReturnTransition(defeated, drowned));
        }

        /// <summary>The body sinks under water; nothing else steers it (a rope lets go).</summary>
        IEnumerator Sink()
        {
            if (motor.ExternallyControlled) motor.RevokeExternalControl();
            bool sinking = motor.TryClaimExternalControl(this);
            Play(cues ? cues.playerHurt : null);
            var view = motor.view;
            float startPitch = view ? SignedPitch(view) : 0;
            float time = 0, nextTurn = 0, goalYaw = 0, goalPitch = 0, yaw = 0, pitch = 0, yawSpeed = 0, pitchSpeed = 0;
            int side = UnityEngine.Random.value < .5f ? -1 : 1;
            while (time < settings.drownSeconds)
            {
                float dt = Time.unscaledDeltaTime;
                time += dt;
                float k = Mathf.Clamp01(time / settings.drownDarkenSeconds);
                if (fade) fade.Set(Mathf.Lerp(0, settings.drownDarkness, Ease.OutCubic(k)), settings.drownColour);
                // Thrashing: the head swings left and right, up and down — real, smooth turns of the view.
                // A new direction is picked before the last one is reached, so the motion never stops dead.
                if (view && dt > 0)
                {
                    if (time >= nextTurn)
                    {
                        side = -side;
                        goalYaw = side * UnityEngine.Random.Range(settings.drownTurnYaw.x, settings.drownTurnYaw.y);
                        goalPitch = UnityEngine.Random.Range(settings.drownTurnPitch.x, settings.drownTurnPitch.y) - startPitch;
                        nextTurn = time + UnityEngine.Random.Range(settings.drownTurnSeconds.x, settings.drownTurnSeconds.y);
                    }
                    float newYaw = Mathf.SmoothDamp(yaw, goalYaw, ref yawSpeed, settings.drownTurnSmoothing, Mathf.Infinity, dt);
                    float newPitch = Mathf.SmoothDamp(pitch, goalPitch, ref pitchSpeed, settings.drownTurnSmoothing, Mathf.Infinity, dt);
                    view.Turn(newYaw - yaw, newPitch - pitch);
                    yaw = newYaw;
                    pitch = newPitch;
                }
                if (sinking && motor.ExternalOwner == (IExternalMotorControl)this)
                    motor.MoveExternally(Vector3.down * settings.drownSinkSpeed * Time.deltaTime);
                yield return null;
            }
        }

        static float SignedPitch(Hakaton.Character.FirstPersonView view)
        {
            float x = view.pitchPivot.localEulerAngles.x;
            return x > 180 ? x - 360 : x;
        }

        Vector3 IExternalMotorControl.OnControlRevoked() => Vector3.zero;

        IEnumerator ReturnTransition(bool defeated, bool drowned)
        {
            var eye = viewCamera.transform;
            if (drowned) yield return Sink();
            else if (defeated)
            {
                if (time) time.SlowMotion(.9f, .25f, .2f);
                if (feel) { feel.AddTrauma(.6f); feel.Kick(new Vector3(40, 0, 25)); }
                yield return new WaitForSecondsRealtime(.75f);
            }
            else
            {
                // The Return card is flung skyward and bursts — then the screen goes dark.
                if (hand.Take(-1, out var pose, out var view))
                    SpawnDebris(view, pose.position, pose.rotation, eye.forward * 4 + Vector3.up * 9, new Vector3(0, 0, 900), .7f);
                Play(cues ? cues.returnCard : null);
                if (feel) { feel.Kick(new Vector3(-35, 0, 0)); feel.PunchFov(30); }
                yield return new WaitForSecondsRealtime(.3f);
                Spawn(vfx ? vfx.returnPoof : null, eye.position + eye.forward * 2.5f + Vector3.up * 1.5f, Quaternion.identity, new Color(1f, .82f, .35f));
            }
            hand.Hide();
            if (fade) yield return fade.FadeTo(1, settings.returnFadeSeconds);
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
