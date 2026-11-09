using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Economy.Unity;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using Hakaton.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Hakaton.OwnerFixesTemp
{
    /// <summary>Temporary: builds the wet badge assets and runs the Play checks of the owner's four decisions. Delete after use.</summary>
    [InitializeOnLoad]
    static class OwnerFixes
    {
        const string PendingKey = "Hakaton.OwnerFixesTemp.Pending";

        static OwnerFixes()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        /// <summary>Enters Play and starts the scenario as soon as Play mode is entered (no inbox round trip).</summary>
        public static string PlayAndRun(string which)
        {
            if (EditorApplication.isPlaying) return "already playing";
            SessionState.SetString(PendingKey, which);
            EditorApplication.isPlaying = true;
            return "play requested, pending " + which;
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            string pending = SessionState.GetString(PendingKey, "");
            if (pending.Length == 0) return;
            SessionState.EraseString(PendingKey);
            Run(pending);
        }

        static readonly string LogPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge", "own-scenario.txt");
        static readonly string ShotDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge", "shots");

        const string WetPng = "Assets/Game/Prefabs/Cards/RainCloud/Wet badge.png";
        const string MoonPng = "Assets/Game/Prefabs/Cards/MoonGravity/Moon badge.png";
        const string MoonBadge = "Assets/Game/Prefabs/Cards/MoonGravity/Moon gravity badge.prefab";
        const string WetBadge = "Assets/Game/Prefabs/Cards/RainCloud/Wet badge.prefab";
        const string RainCloud = "Assets/Game/Prefabs/Cards/RainCloud/Rain cloud.prefab";

        // ---------- editor state ----------

        public static string SceneState()
        {
            var scene = EditorSceneManager.GetActiveScene();
            return $"scene={scene.path} dirty={scene.isDirty} playing={EditorApplication.isPlaying}";
        }

        public static string OpenScene(string path)
        {
            if (EditorApplication.isPlaying) return "playing";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty) return "active scene is dirty, not switching: " + scene.path;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return "opened " + path;
        }

        // ---------- assets ----------

        public static string BuildAssets()
        {
            var log = new List<string>();
            var source = (TextureImporter)AssetImporter.GetAtPath(MoonPng);
            var target = (TextureImporter)AssetImporter.GetAtPath(WetPng);
            if (!source || !target) return "FAILED importers " + (bool)source + " " + (bool)target;
            var settings = new TextureImporterSettings();
            source.ReadTextureSettings(settings);
            target.SetTextureSettings(settings);
            target.textureCompression = source.textureCompression;
            target.maxTextureSize = source.maxTextureSize;
            foreach (var platform in new[] { "Standalone", "WebGL", "iPhone", "Android" })
            {
                var p = source.GetPlatformTextureSettings(platform);
                if (p.overridden) target.SetPlatformTextureSettings(p);
            }
            target.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(WetPng);
            log.Add("sprite " + (sprite ? sprite.rect.ToString() : "MISSING"));
            if (!sprite) return string.Join("; ", log);

            if (!AssetDatabase.LoadAssetAtPath<GameObject>(WetBadge))
                log.Add("copy " + AssetDatabase.CopyAsset(MoonBadge, WetBadge));
            var root = PrefabUtility.LoadPrefabContents(WetBadge);
            try
            {
                root.name = "Wet badge";
                var badge = root.GetComponent<PlayerStatusBadge>();
                badge.source = PlayerStatusBadge.Source.Wet;
                badge.aura = null;
                badge.tint = new Color(.75f, .88f, 1f);
                badge.icon.sprite = sprite;
                badge.icon.color = Color.white;
                if (badge.ring) badge.ring.color = new Color(.55f, .78f, 1f, 1f);
                var back = root.transform.Find("Ring back");
                if (back && back.TryGetComponent<Image>(out var backImage)) backImage.color = new Color(.2f, .3f, .45f, .45f);
                PrefabUtility.SaveAsPrefabAsset(root, WetBadge);
                log.Add("badge saved");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            var cloud = PrefabUtility.LoadPrefabContents(RainCloud);
            try
            {
                var zone = cloud.GetComponentInChildren<RainCloudZone>(true);
                zone.wetBadge = AssetDatabase.LoadAssetAtPath<PlayerStatusBadge>(WetBadge);
                PrefabUtility.SaveAsPrefabAsset(cloud, RainCloud);
                log.Add("rain cloud badge=" + (zone.wetBadge ? zone.wetBadge.name : "null"));
            }
            finally { PrefabUtility.UnloadPrefabContents(cloud); }
            AssetDatabase.SaveAssets();
            return string.Join("; ", log);
        }

        // ---------- scenario runner ----------

        static readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();
        static ExpeditionWorld world;
        static GameLoop loop;

        public static string Run(string which)
        {
            if (!EditorApplication.isPlaying) return "not playing";
            File.WriteAllText(LogPath, "");
            Stack.Clear();
            IEnumerator scenario = which switch
            {
                "jump" => Scenario(JumpSizes()),
                "all" => Scenario(All()),
                "full" => Scenario(Full()),
                "cube" => Scenario(CubeOnly()),
                _ => null
            };
            if (scenario == null) return "unknown " + which;
            Stack.Push(scenario);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return "started " + which;
        }

        public static string Status() => File.Exists(LogPath) ? File.ReadAllText(LogPath) : "-";

        static void Tick()
        {
            if (Stack.Count == 0) { EditorApplication.update -= Tick; return; }
            if (!EditorApplication.isPlaying) { Finish(); return; }
            try
            {
                var top = Stack.Peek();
                if (top.MoveNext()) { if (top.Current is IEnumerator nested) Stack.Push(nested); }
                else
                {
                    Stack.Pop();
                    if (Stack.Count == 0) { Log("DONE"); Finish(); }
                }
            }
            catch (Exception e) { Log("EXCEPTION " + e); Finish(); EditorApplication.isPlaying = false; }
        }

        static void Finish()
        {
            Time.captureDeltaTime = 0;
            Stack.Clear();
            EditorApplication.update -= Tick;
            try { RestoreInput(); } catch (Exception) { }
        }

        static void Log(string line) => File.AppendAllText(LogPath, $"{Time.time,7:0.00}  {line}\n");
        static string V(Vector3 v) => $"({v.x:0.00} {v.y:0.00} {v.z:0.00})";

        static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        static void Shot(string name)
        {
            Directory.CreateDirectory(ShotDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, "own-" + name + ".png"));
            Log("shot own-" + name);
        }

        static CardDefinition Def(string id) => AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/" + id + ".asset");

        static int copyIds = -7000;
        static CardCopy Copy(CardDefinition definition, int level) => (CardCopy)Activator.CreateInstance(typeof(CardCopy),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { copyIds--, definition.ToSpec(), level }, null);

        static bool Ground(Vector3 at, out RaycastHit hit)
            => Physics.Raycast(at + Vector3.up * 3, Vector3.down, out hit, 12, ~(1 << 2), QueryTriggerInteraction.Ignore);

        static IEnumerator Scenario(IEnumerator body)
        {
            yield return Setup();
            if (!world || !world.Running) { Log("not running, abort"); EditorApplication.isPlaying = false; yield break; }
            yield return body;
        }

        static IEnumerator Setup()
        {
            yield return Wait(.5f);
            loop = Object.FindAnyObjectByType<GameLoop>();
            if (!loop) { Log("no GameLoop"); yield break; }
            loop.persistProgress = false;
            world = loop.expedition;
            var model = world.workshop.Model;
            if (model == null) { Log("BROKEN PLAY STATE: workshop model is null (scripts reloaded during Play?)"); world = null; yield break; }
            Log($"setup away={model.IsAway} coins={model.Coins} canDepart={model.CanDepart() ?? "ok"}");
            if (!model.IsAway)
            {
                if (model.CanDepart() != null)
                {
                    var wallet = Object.FindAnyObjectByType<CoinWalletHost>();
                    if (wallet) wallet.Wallet.Credit(500);
                    string error = model.Buy("strike", out var copy);
                    Log("buy strike: " + (error ?? "ok"));
                    if (copy != null) Log("toggle: " + (model.Toggle(copy.Id) ?? "ok"));
                }
                bool departed = false;
                for (int i = 0; i < 40 && !departed; i++)
                {
                    departed = loop.TryDepart();
                    if (!departed) yield return Wait(.25f);
                }
                Log("depart: " + departed);
            }
            float until = Time.time + 8;
            while (!world.Running && Time.time < until) yield return null;
            yield return Wait(2.5f);
            var m = world.motor;
            var cc = m.characterController;
            Log($"running={world.Running} player={V(m.transform.position)} jumpHeight={m.jumpHeight} gravity={m.gravity} refG={m.referenceGravity} " +
                $"cc r={cc.radius} h={cc.height} step={cc.stepOffset} skin={cc.skinWidth} accel={m.acceleration} air={m.airControl} walk={m.walkSpeed} hearts={world.vitals.HalfHearts}");
        }

        // ---------- forced gameplay input (the real jump path of the motor) ----------

        static FieldInfo moveField, jumpField, focusedField, capturedField;
        static bool inputForced;

        static bool ForceGameplay()
        {
            var motor = world.motor;
            var gate = motor.control;
            focusedField ??= typeof(PlayerControlGate).GetField("focused", BindingFlags.Instance | BindingFlags.NonPublic);
            capturedField ??= typeof(PlayerControlGate).GetField("captured", BindingFlags.Instance | BindingFlags.NonPublic);
            focusedField.SetValue(gate, true);
            capturedField.SetValue(gate, true);
            Cursor.lockState = CursorLockMode.Locked;
            if (motor.input.enabled) motor.input.enabled = false;
            inputForced = true;
            return gate.GameplayActive;
        }

        static void SetInput(Vector2 move, bool jump)
        {
            var input = world.motor.input;
            moveField ??= typeof(FirstPersonInput).GetField("<Move>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            jumpField ??= typeof(FirstPersonInput).GetField("<JumpPressed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            moveField.SetValue(input, move);
            jumpField.SetValue(input, jump);
        }

        static void RestoreInput()
        {
            if (!inputForced || !world) return;
            SetInput(Vector2.zero, false);
            world.motor.input.enabled = true;
            focusedField.SetValue(world.motor.control, Application.isFocused);
            capturedField.SetValue(world.motor.control, false);
            Cursor.lockState = CursorLockMode.None;
            inputForced = false;
        }

        static void Place(Vector3 feet, Vector3 facing)
        {
            var motor = world.motor;
            motor.TeleportKeepingVelocity(feet + Vector3.up * .05f);
            motor.SetVelocity(Vector3.zero);
            facing.y = 0;
            if (facing.sqrMagnitude > 1e-4f) motor.view.ResetPose(Quaternion.LookRotation(facing.normalized));
        }

        static bool FlatSpot(Vector3 around, float half, out Vector3 point, out Collider ground)
        {
            point = default;
            ground = null;
            int slope = 0, uneven = 0, blocked = 0, none = 0;
            for (int i = 0; i < 160; i++)
            {
                float a = i * 37 * Mathf.Deg2Rad, r = 2 + i * .4f;
                var p = around + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                if (!Physics.Raycast(p + Vector3.up * 30, Vector3.down, out var hit, 60, ~(1 << 2), QueryTriggerInteraction.Ignore)) { none++; continue; }
                if (Vector3.Angle(hit.normal, Vector3.up) > 8) { slope++; continue; }
                float min = hit.point.y, max = hit.point.y;
                bool ok = true;
                for (int k = 0; k < 12 && ok; k++)
                {
                    var q = hit.point + new Vector3(Mathf.Cos(k * .5236f), 0, Mathf.Sin(k * .5236f)) * (half + 3);
                    if (!Ground(q, out var g)) ok = false;
                    else { min = Mathf.Min(min, g.point.y); max = Mathf.Max(max, g.point.y); }
                }
                if (!ok || max - min > .12f) { uneven++; continue; }
                if (Physics.CheckBox(hit.point + Vector3.up * (half + 1.3f), new Vector3(half + 1, half + 1.1f, half + 1), Quaternion.identity,
                        ~(1 << 2), QueryTriggerInteraction.Ignore)) { blocked++; continue; }
                point = hit.point;
                ground = hit.collider;
                Log($"flat spot after {i + 1} tries: {V(point)} on {ground.name} (slope {slope}, uneven {uneven}, blocked {blocked}, none {none})");
                return true;
            }
            Log($"no flat spot: slope {slope}, uneven {uneven}, blocked {blocked}, none {none}");
            return false;
        }

        // ---------- 1. stone cube: a step for a normal jump ----------

        /// <summary>Holds W (when facing is set) and presses Space once the gap to the face drops to jumpGap (0 = at once).</summary>
        static IEnumerator Jump(Vector3 facing, Action<float> apex, Func<float> gap = null, float jumpGap = float.PositiveInfinity)
        {
            var motor = world.motor;
            float startY = motor.transform.position.y, maxY = startY;
            bool forced = ForceGameplay();
            int frame = 0, jumpFrame = -1;
            float until = Time.time + 2.6f, gapAtJump = -1;
            while (Time.time < until)
            {
                ForceGameplay();
                bool press = false;
                if (jumpFrame < 0 && (gap == null || gap() <= jumpGap)) { jumpFrame = frame; gapAtJump = gap != null ? gap() : -1; }
                if (jumpFrame >= 0 && frame - jumpFrame < 2) press = true;
                SetInput(Vector2.up * (facing.sqrMagnitude > 0 ? 1 : 0), press);
                maxY = Mathf.Max(maxY, motor.transform.position.y);
                frame++;
                yield return null;
            }
            SetInput(Vector2.zero, false);
            RestoreInput();
            Log($"  forced={forced} frames={frame} dt={Time.deltaTime:0.000} jumped at frame {jumpFrame} gap {gapAtJump:0.00}");
            apex(maxY - startY);
        }

        /// <summary>mode: touch (standing against the face), stand (standing 0.3 m away), walk (walking from 1.6 m, jump 0.45 m before the face).</summary>
        static IEnumerator TryCube(StoneCubeBody cube, string label, bool shoot, string mode = "stand")
        {
            var motor = world.motor;
            Vector3 dir = cube.transform.forward;
            dir.y = 0;
            dir.Normalize();
            float side = cube.Side, r = motor.characterController.radius;
            Vector3 face = cube.transform.position - dir * side * .5f;
            float startGap = mode == "touch" ? .03f : mode == "stand" ? .3f : 2.2f;
            float jumpGap = mode.StartsWith("walk") && mode.Length > 4 ? float.Parse(mode.Substring(4), System.Globalization.CultureInfo.InvariantCulture) : .45f;
            Vector3 stand = face - dir * (r + startGap);
            if (Ground(stand, out var g)) stand = g.point;
            Place(stand, dir);
            yield return Wait(.6f);
            float groundY = motor.transform.position.y;
            float top = cube.box.bounds.max.y;
            if (shoot) { motor.view.ResetPose(Quaternion.LookRotation((dir + Vector3.down * .35f).normalized)); yield return Wait(.2f); Shot(label + "-before"); yield return Wait(.2f); motor.view.ResetPose(Quaternion.LookRotation(dir)); }
            float apex = 0;
            Func<float> gap = () => Vector3.Dot(face - motor.transform.position, dir) - r;
            yield return Jump(dir, a => apex = a, gap, mode.StartsWith("walk") ? jumpGap : float.PositiveInfinity);
            yield return Wait(.3f);
            bool onTop = motor.transform.position.y > top - .08f;
            Log($"{label} [{mode}]: side={side:0.00} top-above-feet={top - groundY:0.00} apex={apex:0.00} feet-now={motor.transform.position.y - groundY:0.00} " +
                $"ground={(motor.GroundCollider ? motor.GroundCollider.name : "-")} ON_TOP={onTop}");
            if (shoot)
            {
                motor.view.ResetPose(Quaternion.LookRotation((dir + Vector3.down * .9f).normalized));
                yield return Wait(.25f);
                Shot(label + "-after");
                yield return Wait(.2f);
            }
        }

        static IEnumerator JumpSizes()
        {
            var effect = AssetDatabase.LoadAssetAtPath<StoneCubeEffect>("Assets/Game/Data/Effects/Stone cube.asset");
            var motor = world.motor;
            if (!FlatSpot(motor.transform.position, 1.2f, out var spot, out _)) { Log("no flat spot"); yield break; }
            foreach (float side in new[] { .9f, 1.0f, 1.1f })
                foreach (string mode in new[] { "stand", "walk0.6", "walk0.9", "walk1.2", "walk1.5" })
                {
                    var cube = world.SpawnForOuting(effect.cubePrefab, spot + Vector3.up * (side * .5f + .02f), Quaternion.Euler(0, 20, 0));
                    cube.Configure(side, effect.mass);
                    yield return Wait(.5f);
                    yield return TryCube(cube, "side " + side.ToString("0.00"), false, mode);
                    Place(spot + Vector3.right * 4, Vector3.forward);
                    Object.Destroy(cube.gameObject);
                    yield return Wait(.3f);
                }
        }

        // ---------- all four decisions ----------

        static IEnumerator CubeOnly()
        {
            // The editor runs in the background at a low frame rate: simulate a steady 60 fps like the real game.
            Time.captureDeltaTime = 1f / 60;
            Log("captureDeltaTime 1/60");
            yield return JumpSizes();
            yield return CubeCard();
            yield return Wait(.5f);
            EditorApplication.isPlaying = false;
        }

        static IEnumerator Full()
        {
            yield return JumpSizes();
            yield return All();
            yield return Wait(.5f);
            EditorApplication.isPlaying = false;
        }

        static IEnumerator All()
        {
            yield return CubeCard();
            yield return LightningOnTar();
            yield return Wet();
            yield return HealAlly();
        }

        static IEnumerator CubeCard()
        {
            Log("=== 1. stone cube card ===");
            var def = Def("stone_cube");
            var effect = (StoneCubeEffect)def.effectAsset;
            var motor = world.motor;
            if (!FlatSpot(motor.transform.position, 1.2f, out var spot, out var ground)) { Log("no flat spot"); yield break; }
            foreach (int level in new[] { 1, 2 })
            {
                Place(spot - Vector3.forward * 4, Vector3.forward);
                yield return Wait(.3f);
                var before = new HashSet<StoneCubeBody>(Object.FindObjectsByType<StoneCubeBody>(FindObjectsSortMode.None));
                world.ApplyImpact(Copy(def, level), def, ground, spot, Vector3.up, (Vector3.forward * 2 + Vector3.down).normalized * 12);
                yield return Wait(.6f);
                var cube = Object.FindObjectsByType<StoneCubeBody>(FindObjectsSortMode.None).FirstOrDefault(c => !before.Contains(c));
                if (!cube) { Log("card made no cube, notice=" + world.Notice); yield break; }
                Log($"card level {level}: side={cube.Side:0.00} (asset {effect.sizeBase}/{effect.sizeUpgraded}) mass={cube.body.mass}");
                yield return TryCube(cube, "card L" + level, false, "walk0.9");
                yield return TryCube(cube, "card L" + level, level == 1, "walk1.2");
                Place(spot + Vector3.right * 4, Vector3.forward);
                Object.Destroy(cube.gameObject);
                yield return Wait(.4f);
            }
        }

        static Collider SolidCollider(Component body)
            => body.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.enabled && !c.isTrigger);

        static string Healths(WorldCreature except)
            => string.Join(",", world.creatures.Where(c => c && c.isActiveAndEnabled && c != except).Select(c => c.Health))
               + $" player={world.vitals.HalfHearts}";

        static TarPuddleZone NearestTar(Vector3 point)
            => Object.FindObjectsByType<TarPuddleZone>(FindObjectsSortMode.None).OrderBy(t => (t.transform.position - point).sqrMagnitude).FirstOrDefault();

        static IEnumerator Strike(CardDefinition bolt, WorldCreature creature, string label)
        {
            var collider = SolidCollider(creature);
            var bounds = collider.bounds;
            var point = new Vector3(creature.Position.x, bounds.max.y - .05f, creature.Position.z);
            int before = creature.Health;
            string others = Healths(creature);
            world.ApplyImpact(Copy(bolt, 1), bolt, collider, point, Vector3.up, Vector3.down * 12);
            Log($"{label}: strike at {point.y - creature.Position.y:0.00} m above the feet; creature {before}->{creature.Health}; others before [{others}] after [{Healths(creature)}]");
            yield return null;
        }

        static IEnumerator LightningOnTar()
        {
            Log("=== 2. lightning on a fighter in tar ===");
            var tar = Def("tar_puddle");
            var bolt = Def("lightning");
            var creatures = world.creatures.Where(c => c && c.isActiveAndEnabled && c.Alive).OrderByDescending(c => c.Health).ToList();
            if (creatures.Count == 0) { Log("no creature"); yield break; }
            var creature = creatures[0];
            Log($"creature {creature.name} health={creature.Health} at {V(creature.Position)}");
            Vector3 side = Vector3.Cross(Vector3.up, (creature.Position - world.motor.transform.position).normalized);
            if (side.sqrMagnitude < .1f) side = Vector3.right;
            // Control: a puddle whose edge is 2.6 m from the creature stays unlit.
            if (Ground(creature.Position + side * 4.6f, out var far))
            {
                world.ApplyImpact(Copy(tar, 1), tar, far.collider, far.point, far.normal, Vector3.down * 8);
                yield return null;
                var control = NearestTar(far.point);
                yield return Strike(bolt, creature, "control strike (tar 4.6 m away)");
                Log($"control puddle burning={control && control.Burning}");
            }
            if (!creature.Alive && creatures.Count > 1) creature = creatures[1];
            yield return Wait(.5f);
            // The puddle right under the creature.
            Vector3 feet = creature.Position;
            if (!Ground(feet, out var under)) { Log("no ground under creature"); yield break; }
            world.ApplyImpact(Copy(tar, 1), tar, under.collider, under.point, under.normal, Vector3.down * 8);
            yield return null;
            var puddle = NearestTar(under.point);
            Log($"puddle under creature: on tar={puddle && puddle.OnTar(creature.Position)} burning={puddle && puddle.Burning}");
            // The player watches from 6 m.
            Vector3 away = creature.Position - side * 6;
            if (Ground(away, out var watch)) Place(watch.point, creature.Position - watch.point);
            yield return Strike(bolt, creature, "strike on the creature in tar");
            Log($"puddle under creature burning={puddle && puddle.Burning} phase={(puddle ? puddle.Phase.ToString() : "-")}");
            yield return Wait(.5f);
            world.motor.view.ResetPose(Quaternion.LookRotation((puddle.transform.position - world.motor.view.cameraTransform.position).normalized));
            yield return Wait(.3f);
            Shot("lightning-tar");
            yield return Wait(.3f);
        }

        static IEnumerator Wet()
        {
            Log("=== 3. wet badge ===");
            var rain = Def("rain_cloud");
            var motor = world.motor;
            if (!FlatSpot(motor.transform.position + Vector3.forward * 12, 2f, out var spot, out var ground)) { Log("no flat spot"); yield break; }
            Place(spot, Vector3.forward);
            yield return Wait(.3f);
            world.ApplyImpact(Copy(rain, 1), rain, ground, spot, Vector3.up, Vector3.down * 8);
            yield return Wait(1.2f);
            var badge = Object.FindObjectsByType<PlayerStatusBadge>(FindObjectsSortMode.None).FirstOrDefault(b => b.source == PlayerStatusBadge.Source.Wet);
            var wet = motor.GetComponent<WetStatus>();
            string notice = Time.time < world.NoticeUntil ? world.Notice : "";
            Log($"under rain: badge={(badge ? badge.name : "NONE")} icon={(badge && badge.icon.sprite ? badge.icon.sprite.name : "-")} fill={(badge ? badge.ring.fillAmount : -1):0.00} " +
                $"wetRemaining={(wet ? wet.Remaining : -1):0.0} notice='{notice}' badges={Object.FindObjectsByType<PlayerStatusBadge>(FindObjectsSortMode.None).Length}");
            motor.view.ResetPose(Quaternion.LookRotation(Vector3.forward + Vector3.up * .5f));
            yield return Wait(.2f);
            Shot("wet-under-rain");
            yield return Wait(.2f);
            if (Ground(spot + Vector3.back * 9, out var dry)) Place(dry.point, Vector3.forward);
            yield return Wait(4f);
            Log($"4 s after leaving: badge={(badge ? "shown" : "gone")} fill={(badge ? badge.ring.fillAmount : -1):0.00} wetRemaining={(wet ? wet.Remaining : -1):0.0}");
            Shot("wet-after-4s");
            yield return Wait(.3f);
        }

        static IEnumerator HealAlly()
        {
            Log("=== 4. heal the ally tree ===");
            var treeCard = Def("tree_awakening");
            var heal = Def("heal");
            var motor = world.motor;
            var tree = Object.FindObjectsByType<AwakenableTree>(FindObjectsSortMode.None).Where(t => t.Standing)
                .OrderBy(t => (t.transform.position - motor.transform.position).sqrMagnitude).FirstOrDefault();
            if (!tree) { Log("no tree"); yield break; }
            var trunk = SolidCollider(tree);
            Vector3 toTree = tree.FootPosition - motor.transform.position;
            toTree.y = 0;
            Vector3 dir = toTree.sqrMagnitude > .01f ? toTree.normalized : Vector3.forward;
            if (Ground(tree.FootPosition - dir * 5, out var stand)) Place(stand.point, dir);
            yield return Wait(.3f);
            world.ApplyImpact(Copy(treeCard, 1), treeCard, trunk, trunk.bounds.center, -dir, dir * 12);
            yield return Wait(2f);
            var ally = world.Allies.All.OfType<TreeAlly>().LastOrDefault();
            if (!ally) { Log("no ally, notice=" + world.Notice); yield break; }
            Log($"ally health={ally.Health}/{ally.maximumHealth}");
            ally.TakeDamage(new DamageInfo(5, Vector3.zero, ally.Centre, null, DamageKind.Other));
            Log($"after 5 damage: {ally.Health}");
            yield return Wait(.5f);
            Vector3 aside = Vector3.Cross(Vector3.up, dir);
            if (Ground(ally.Position + aside * 1.6f, out var near))
            {
                world.ApplyImpact(Copy(heal, 1), heal, near.collider, near.point, near.normal, Vector3.down * 8);
                Log($"base Heal on the ground 1.6 m from the roots: {ally.Health} (expected 9)");
            }
            motor.view.ResetPose(Quaternion.LookRotation((ally.Centre - motor.view.cameraTransform.position).normalized));
            yield return Wait(.25f);
            Shot("heal-ally");
            yield return Wait(.5f);
            var body = SolidCollider(ally);
            world.ApplyImpact(Copy(heal, 2), heal, body, ally.Centre, -dir, dir * 12);
            Log($"upgraded Heal, direct hit on the trunk: {ally.Health} (expected 12, max {ally.maximumHealth})");
            world.ApplyImpact(Copy(heal, 2), heal, body, ally.Centre, -dir, dir * 12);
            Log($"Heal at full health: {ally.Health}");
            ally.TakeDamage(new DamageInfo(3, Vector3.zero, ally.Centre, null, DamageKind.Other));
            if (Ground(ally.Position + aside * 6.5f, out var far))
            {
                world.ApplyImpact(Copy(heal, 1), heal, far.collider, far.point, far.normal, Vector3.down * 8);
                Log($"Heal 6.5 m away after 3 damage: {ally.Health} (expected 9)");
            }
            // The player is healed as before.
            world.vitals.Hurt(2, Vector3.zero, null);
            int hurt = world.vitals.HalfHearts;
            yield return Wait(.9f);
            if (Ground(motor.transform.position, out var feet))
                world.ApplyImpact(Copy(heal, 1), heal, feet.collider, feet.point, feet.normal, Vector3.down * 8);
            Log($"player: {hurt} -> {world.vitals.HalfHearts} half hearts (max {world.vitals.MaximumHalfHearts})");
            yield return Wait(.3f);
        }
    }
}
