using System;
using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Expeditions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.EnemyDebug
{
    [DefaultExecutionOrder(100)]
    public sealed class EnemyDebugSandbox : MonoBehaviour
    {
        [Header("Saved scene dependencies")]
        public EnemyDebugCatalogue catalogue;
        public EnemyDebugPreview previewPrefab;
        public Transform previewRoot, liveRoot, startPose;
        public EnemyDebugPreview[] authoredPreviews = Array.Empty<EnemyDebugPreview>();
        public ExpeditionWorld world;
        public FirstPersonMotor motor;
        public PlayerControlGate control;
        public Camera viewCamera;
        public TMP_Text title, summary, focusText;
        [Header("Grid and interaction")]
        [Min(1)] public int columns = 4;
        [Min(3)] public float spacing = 10;
        public Vector3 gridOrigin = new Vector3(-15, 0, 0);
        [Min(1)] public float focusReach = 14;
        [Tooltip("E on a creature that only comes in squads (Patrol) brings a whole squad of this many, walking its route.")]
        [Min(1)] public int squadSize = 4;
        [Tooltip("A live copy appears this far in front of its preview (towards the player), not inside the drawing.")]
        [Min(0)] public float spawnAhead = 3;
        [Tooltip("A creature in disguise (the hostile bush) appears among this many copies of its look, to judge the disguise.")]
        [Min(0)] public int disguiseDressing = 4;
        [Header("Hearts")]
        [Tooltip("The hearts show the hits; this many seconds after the last one, or when only this many half hearts are left, they fill up again — the debug player never falls.")]
        [Min(0)] public float refillSeconds = 3;
        [Min(1)] public int refillAtHalfHearts = 2;

        readonly List<EnemyDebugPreview> previews = new List<EnemyDebugPreview>();
        readonly List<WorldCreature> live = new List<WorldCreature>();
        public IReadOnlyList<EnemyDebugPreview> Previews => previews;
        public IReadOnlyList<WorldCreature> LiveCreatures => live;
        public EnemyDebugPreview Focused { get; private set; }
        public int ReceivedDamage { get; private set; }
        float lastHurt = -1;

        void Start()
        {
            // A dedicated, inactive DeckPrototype is only the existing lazy model host. No shop UI or saves run here.
            world.workshop.Model.DepartPrologue();
            world.vitals.Changed += OnHealthChanged;
            SynchronizePreviews();
            motor.Teleport(startPose.position, startPose.rotation);
            control.CaptureCursor();
            RefreshHud();
        }

        void OnDestroy() { if (world && world.vitals) world.vitals.Changed -= OnHealthChanged; }

        void OnHealthChanged(int delta)
        {
            if (delta >= 0) return;
            ReceivedDamage -= delta;
            lastHurt = Time.time;
            // Nearly out (or out): straight back to full, so the debug player never falls.
            if (world.vitals.HalfHearts <= refillAtHalfHearts) world.vitals.Heal(world.vitals.MaximumHalfHearts);
        }

        public Vector3 GridPosition(int index) => gridOrigin + new Vector3(index % Mathf.Max(1, columns) * spacing, 0,
            index / Mathf.Max(1, columns) * spacing);

        void SynchronizePreviews()
        {
            var available = catalogue.Discover();
            var unused = new List<EnemyDebugPreview>(authoredPreviews);
            foreach (var source in available)
            {
                if (!source) continue;
                var preview = unused.Find(item => item && item.source == source);
                if (preview) unused.Remove(preview);
                else preview = Instantiate(previewPrefab, previewRoot);
                preview.transform.SetPositionAndRotation(GridPosition(previews.Count), Quaternion.Euler(0, 180, 0));
                preview.gameObject.SetActive(true);
                preview.Bind(source);
                previews.Add(preview);
            }
            foreach (var stale in unused) if (stale) { stale.gameObject.SetActive(false); Destroy(stale.gameObject); }
        }

        void LateUpdate()
        {
            if (lastHurt >= 0 && Time.time - lastHurt > refillSeconds)
            {
                lastHurt = -1;
                world.vitals.Heal(world.vitals.MaximumHalfHearts);
            }
            if (motor.transform.position.y < -4)
            {
                motor.Teleport(startPose.position, startPose.rotation);
                world.vitals.Restore();
            }
            Focused = control.GameplayActive ? FindFocused(new Ray(viewCamera.transform.position, viewCamera.transform.forward)) : null;
            var keyboard = Keyboard.current;
            if (control.GameplayActive && !control.CapturedThisFrame && keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame) ClearLive();
                if (Focused)
                {
                    if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame) Focused.PlayPose(CreaturePose.Default);
                    if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) Focused.PlayPose(CreaturePose.Alarm);
                    if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) Focused.PlayPose(CreaturePose.Hurt);
                    if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) Focused.PlayPose(CreaturePose.Attack);
                    if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) Focused.PlayPose(CreaturePose.Death);
                    if (keyboard.eKey.wasPressedThisFrame) Spawn(Focused);
                }
            }
            RefreshHud();
        }

        public EnemyDebugPreview FindFocused(Ray ray)
        {
            EnemyDebugPreview best = null;
            float nearest = focusReach;
            foreach (var preview in previews)
                if (preview.Intersect(ray, nearest, out float distance)) { best = preview; nearest = distance; }
            if (best && Physics.Raycast(ray, nearest - .03f, ~(1 << 2), QueryTriggerInteraction.Ignore)) return null;
            return best;
        }

        public WorldCreature Spawn(EnemyDebugPreview preview)
        {
            if (!preview || !preview.source) return null;
            var origin = SpawnPoint(preview);
            var rotation = preview.transform.rotation;
            if (!preview.source.patrol.enabled) return SpawnOne(preview.source, origin, rotation);
            // A squad creature comes as a squad, side by side, and walks its route round the spot as in the world.
            var group = new List<WorldCreature>();
            for (int i = 0; i < squadSize; i++)
            {
                float a = i * 2.4f;
                var at = origin + (i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (1.4f + .45f * i));
                group.Add(SpawnOne(preview.source, at, rotation));
            }
            PatrolSquad.Form(group, origin, live.Count * 7919 + 17);
            return group[0];
        }

        /// <summary>
        /// In front of the preview, towards the player, on the ground — not inside the drawing. A creature in disguise
        /// keeps out of its spring range, so it appears in disguise.
        /// </summary>
        Vector3 SpawnPoint(EnemyDebugPreview preview)
        {
            var at = preview.transform.position;
            var toPlayer = motor.transform.position - at;
            toPlayer.y = 0;
            float ahead = spawnAhead;
            var source = preview.source;
            if (source.ambush.enabled && source.ambush.disguises.Length > 0)
                ahead = Mathf.Min(ahead, Mathf.Max(0, toPlayer.magnitude - source.ambush.pounceRange - 3));
            if (toPlayer.sqrMagnitude > .01f) at += toPlayer.normalized * Mathf.Min(ahead, toPlayer.magnitude * .5f);
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            return at;
        }

        /// <summary>
        /// A creature in disguise without a patch of its own (the hostile bush) gets a few copies of its look round it
        /// and settles by one, as it would among the forest's bushes.
        /// </summary>
        void DressDisguise(WorldCreature creature)
        {
            var looks = creature.ambush.disguises;
            if (!creature.ambush.enabled || looks.Length == 0 || creature.ambush.patch.Length > 0 || disguiseDressing <= 0) return;
            var random = new System.Random(live.Count * 17 + 3);
            var placed = new List<Component>();
            var toPlayer = motor.transform.position - creature.Position;
            toPlayer.y = 0;
            toPlayer = toPlayer.sqrMagnitude > .01f ? toPlayer.normalized : Vector3.forward;
            for (int i = 0; i < disguiseDressing; i++)
            {
                // Round it but not on the way in from the player.
                float turn = Mathf.Lerp(50, 310, disguiseDressing == 1 ? .5f : i / (float)(disguiseDressing - 1));
                var at = creature.Position + Quaternion.Euler(0, turn, 0) * toPlayer * (2.4f + (float)random.NextDouble() * 1.4f);
                var look = looks[random.Next(looks.Length)];
                if (!look) continue;
                var copy = Instantiate(look, at, Quaternion.Euler(0, (float)random.NextDouble() * 360, 0), liveRoot);
                copy.name = look.name;
                copy.transform.localScale = look.transform.lossyScale * (1 + (float)random.NextDouble() * .25f);
                copy.SetActive(true);
                placed.Add(copy.transform);
            }
            creature.HideIn(placed, new List<Vector3>(), true);
        }

        WorldCreature SpawnOne(WorldCreature source, Vector3 position, Quaternion rotation)
        {
            // Instantiate the ORIGINAL gameplay prefab, never the presentation-only preview.
            var creature = Instantiate(source, position, rotation, liveRoot);
            creature.name = source.name + " [live]";
            creature.world = world;
            creature.ResetForOuting(null);
            // An ambusher that hides among props like its disguise (the rat pumpkin's patch) brings them along.
            creature.LayOutPatch(position, liveRoot, new System.Random(live.Count * 31 + 5), new List<Vector3> { position });
            DressDisguise(creature);
            live.Add(creature);
            world.creatures.Clear();
            world.creatures.AddRange(live);
            return creature;
        }

        public void ClearLive()
        {
            if (world.thrower) world.thrower.Cancel();
            foreach (Transform child in liveRoot) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            live.Clear();
            world.creatures.Clear();
            EnemyProjectile.ClearAll();
            foreach (var preview in previews) preview.PlayPose(CreaturePose.Default);
            ReceivedDamage = 0;
            world.vitals.Restore();
        }

        void RefreshHud()
        {
            if (title) title.text = "ВРАГИ / ОТЛАДКА";
            int active = 0;
            foreach (var creature in live) if (creature && creature.gameObject.activeSelf) active++;
            if (summary) summary.text = $"Виды: {previews.Count}   Копии: {active}   Получено урона: {ReceivedDamage}\nR — убрать копии и сбросить позы\nКарта без лимита: ЛКМ — зарядить и бросить, T — другая карта";
            if (!focusText) return;
            focusText.gameObject.SetActive(Focused);
            if (Focused)
            {
                string alarm = Focused.source.temperament == Temperament.Timid ? "испуг" : "агрессия";
                string attack = Focused.HasAttack ? "3 атака" : "3 нет атаки";
                string shoots = (Focused.source.HasRangedAttack ? "   стреляет" : "") + (Focused.source.patrol.enabled ? $"   отряд ×{squadSize}" : "");
                focusText.text = $"{Focused.source.displayName} / {Focused.PoseLabel}{shoots}\n1 {alarm}   2 урон   {attack}   4 смерть\n0 исходное   E живая копия";
            }
        }
    }
}
