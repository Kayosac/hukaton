using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Shared services for card effects (see tmp/new-cards/API.md): objects that live until the end of the outing,
    /// the registry of card zones, reactions (ignite), vision, fighters. Everything a card leaves in the world is
    /// cleaned up on the way home (return or defeat) and again before the next outing.
    /// </summary>
    public sealed partial class ExpeditionWorld
    {
        readonly List<GameObject> outingObjects = new List<GameObject>();
        readonly List<Action> outingRestores = new List<Action>();
        readonly List<ICardStatus> statuses = new List<ICardStatus>();

        /// <summary>Card zones of this outing (tar, rain, fog, updraft…). Cleared when the outing ends.</summary>
        public CardZoneRegistry Zones { get; } = new CardZoneRegistry();

        /// <summary>Fighters added by cards (awakened trees). Cleared when the outing ends.</summary>
        public CombatantRegistry Allies { get; } = new CombatantRegistry();

        /// <summary>The player as a fighter.</summary>
        public ICombatant Player => vitals;

        /// <summary>Raised after everything cards created this outing was removed (home, defeat, before a new outing).</summary>
        public event Action OutingCleared;

        // ---------- objects until the end of the outing ----------

        /// <summary>
        /// Instantiates an authored prefab that lives until the end of the outing. Parent: <paramref name="parent"/>
        /// (a moving cube, so the object follows it) or the world's spawned-objects root. Every
        /// <see cref="IOutingSpawned"/> in the instance is handed the world. Destroyed automatically when the outing ends.
        /// </summary>
        public T SpawnForOuting<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
        {
            if (!prefab) { Debug.LogWarning("SpawnForOuting: no prefab assigned.", this); return null; }
            var instance = Instantiate(prefab, position, rotation, parent ? parent : spawnedObjects);
            Bind(instance.gameObject);
            return instance;
        }

        /// <inheritdoc cref="SpawnForOuting{T}"/>
        public GameObject SpawnForOuting(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (!prefab) { Debug.LogWarning("SpawnForOuting: no prefab assigned.", this); return null; }
            var instance = Instantiate(prefab, position, rotation, parent ? parent : spawnedObjects);
            Bind(instance);
            return instance;
        }

        void Bind(GameObject instance)
        {
            TrackForOuting(instance);
            foreach (var spawned in instance.GetComponentsInChildren<IOutingSpawned>(true)) spawned.OnSpawned(this);
        }

        /// <summary>Destroys this object when the outing ends (for objects made some other way).</summary>
        public void TrackForOuting(GameObject instance)
        {
            if (instance && !outingObjects.Contains(instance)) outingObjects.Add(instance);
        }

        /// <summary>Hides an authored scene object until the outing ends (the tree that came alive), then shows it again.</summary>
        public void HideForOuting(GameObject sceneObject)
        {
            if (!sceneObject || !sceneObject.activeSelf) return;
            sceneObject.SetActive(false);
            RestoreAtOutingEnd(() => { if (sceneObject) sceneObject.SetActive(true); });
        }

        /// <summary>Runs once when the outing ends (latest registered first): undo a change to an authored object.</summary>
        public void RestoreAtOutingEnd(Action restore)
        {
            if (restore != null) outingRestores.Add(restore);
        }

        /// <summary>A status on a body (wet, light) to clear when the outing ends. Status components call this themselves.</summary>
        public void TrackStatus(ICardStatus status)
        {
            if (status != null && !statuses.Contains(status)) statuses.Add(status);
        }

        /// <summary>Removes everything cards left in the world and every card modifier on the player.</summary>
        void ClearCardEffects()
        {
            if (motor)
            {
                motor.RevokeExternalControl();
                motor.ClearModifiers();
            }
            foreach (var status in statuses)
                if (status != null && !(status is Object unityObject && !unityObject)) status.ClearStatus();
            statuses.Clear();
            foreach (var instance in outingObjects)
                if (instance) Destroy(instance);
            outingObjects.Clear();
            for (int i = outingRestores.Count - 1; i >= 0; i--)
            {
                try { outingRestores[i](); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
            outingRestores.Clear();
            Zones.Clear();
            Allies.Clear();
            OutingCleared?.Invoke();
        }

        // ---------- prompts of card objects (cluster C: the rope) ----------

        string cardPrompt;
        int cardPromptFrame = -10;
        bool cardPromptFocus;

        /// <summary>
        /// Prompt of the card-made object the player can use right now («E  Схватиться» at a rope), or null. Shown by
        /// the HUD when no loot is focused. Owners call <see cref="ShowCardPrompt"/> every frame while it applies.
        /// </summary>
        public string CardPrompt => Time.frameCount - cardPromptFrame <= 1 ? cardPrompt : null;

        /// <summary>True while <see cref="CardPrompt"/> is for a focused target (the reticle lights up like for loot).</summary>
        public bool CardTargetFocused => CardPrompt != null && cardPromptFocus;

        /// <summary>Shows <paramref name="text"/> as the interaction prompt this frame (call every frame; it lapses by itself).</summary>
        public void ShowCardPrompt(string text, bool focusedTarget = true)
        {
            cardPrompt = text;
            cardPromptFocus = focusedTarget;
            cardPromptFrame = Time.frameCount;
        }

        // ---------- reactions and senses ----------

        /// <summary>
        /// Sets alight whatever can burn at an impact: the <see cref="IIgnitable"/> the hit collider belongs to, and
        /// ignitable zones whose bounds are within <paramref name="reach"/> of the point. Returns how many caught fire.
        /// Used by Fire; Lightning calls it with <see cref="IgnitionSource.Lightning"/>.
        /// </summary>
        public int Ignite(Collider target, Vector3 point, float reach, IgnitionSource source)
        {
            int count = 0;
            var direct = target ? target.GetComponentInParent<IIgnitable>() : null;
            if (direct != null && direct.Ignite(source, point)) count++;
            var nearby = new List<ICardZone>();
            Zones.Near(point, reach, nearby);
            foreach (var zone in nearby)
                if (zone is IIgnitable ignitable && !ReferenceEquals(ignitable, direct) && ignitable.Ignite(source, point)) count++;
            return count;
        }

        /// <summary>
        /// Is the line of sight between two points hidden by a card zone (<see cref="IVisionBlocker"/>, the fog)?
        /// Geometry is not checked here: creatures raycast separately.
        /// </summary>
        public bool IsVisionBlocked(Vector3 from, Vector3 to)
        {
            var zones = Zones.All;
            for (int i = 0; i < zones.Count; i++)
                if (zones[i] is IVisionBlocker blocker && blocker.BlocksVision(from, to)) return true;
            return false;
        }

        // ---------- fighters ----------

        /// <summary>Appends every live fighter: the player, the scene's active creatures and the registered allies.</summary>
        public void Combatants(List<ICombatant> results)
        {
            if (vitals) results.Add(vitals);
            foreach (var creature in creatures)
                if (creature && creature.isActiveAndEnabled && creature.Alive) results.Add(creature);
            foreach (var ally in Allies.All)
                if (ally.Alive) results.Add(ally);
        }

        /// <summary>The nearest live fighter within <paramref name="range"/> of the point that passes the filter, or null.</summary>
        public ICombatant NearestCombatant(Vector3 point, float range, Predicate<ICombatant> filter = null)
        {
            var all = new List<ICombatant>();
            Combatants(all);
            ICombatant best = null;
            float bestDistance = range;
            foreach (var fighter in all)
            {
                if (filter != null && !filter(fighter)) continue;
                float distance = Vector3.Distance(fighter.Centre, point);
                if (distance <= bestDistance) { best = fighter; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>Damage to the player from a card effect, in units of ½ heart (burning tar, a chain of lightning).</summary>
        public bool HurtPlayer(int halfHearts, Vector3 knockback, Vector3? source, DamageKind kind = DamageKind.Other)
            => vitals && vitals.TakeDamage(new DamageInfo(halfHearts, knockback, vitals.Centre, source, kind));
    }
}
