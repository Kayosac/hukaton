#if UNITY_EDITOR
// TEMPORARY: walks the god-mountains blockout with the real player and keyboard input. Delete after use.
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Hakaton.Character;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class GodMountainsWalkPlayTests
{
    const string ScenePath = "Assets/GodMountains/Scenes/GodMountains.unity";
    Keyboard keyboard;
    Mouse mouse;
    KeyboardState held;
    InputSettings.UpdateMode previousUpdateMode;
    InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
    InputSettings.BackgroundBehavior previousBackgroundBehavior;

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

    [UnityTest, Timeout(900000)]
    public IEnumerator WalkTheWholeRoute()
    {
        previousUpdateMode = InputSystem.settings.updateMode;
        previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        held = new KeyboardState();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        InputSystem.onBeforeUpdate += Queue;
        InputSystem.onAfterUpdate += Select;
        var log = new StringBuilder();
        int problems = 0;
        try
        {
            var op = EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            while (!op.isDone) yield return null;
            var scene = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(scene);
            yield return null;
            var player = scene.GetRootGameObjects().First(g => g.name.StartsWith("Игрок"));
            var motor = player.GetComponent<FirstPersonMotor>();
            var control = player.GetComponent<PlayerControlGate>();
            var view = player.GetComponentInChildren<FirstPersonView>();
            control.SendMessage("OnApplicationFocus", true);
            control.CaptureCursor();
            float settle = Time.time + .5f;
            while (Time.time < settle) yield return null;
            log.AppendLine($"старт: GameplayActive={control.GameplayActive} grounded={motor.IsGrounded} pos={player.transform.position}");
            var root = scene.GetRootGameObjects().First(g => g.name == "God mountains — level").transform;
            var route = root.Find("Маршрут (узлы N00–N32)").Cast<Transform>().ToList();
            float walkStart = Time.time;
            // Passes: along the axis there and back, then hugging the left and then the right side of the corridor.
            // Widths: the node's corridor; at N20/N21 the narrow passage through the frame decides.
            var widths = new float[] { 0, 1.4f, 1.6f, 1.5f, 1.8f, 1.4f, 1.6f, 1.6f, 1.6f, 1.6f, 1.6f, 1.6f, 1.6f, 1.6f, 1.6f, 1.6f,
                1.4f, 1.2f, 1f, 1.6f, 1.4f, 1.4f, 3f, 2.8f, 3.2f, 1.1f, 1.7f, 1.6f, 1.6f, 1.8f, 2f, 14f, 14f };
            var targets = new System.Collections.Generic.List<(string name, Vector3 pos, Vector3 axis, bool jump)>();
            for (int i = 0; i < route.Count; i++) targets.Add((route[i].name, route[i].position, route[i].position, false));
            for (int i = route.Count - 2; i >= 0; i--) targets.Add((route[i].name, route[i].position, route[i].position, false));
            foreach (float side in new[] { 1f, -1f })
            {
                targets.Add(("старт", route[0].position, route[0].position, true));
                for (int i = 1; i < route.Count; i++)
                {
                    Vector3 prev = route[Mathf.Max(0, i - 1)].position, next = route[Mathf.Min(route.Count - 1, i + 1)].position;
                    var along = Flat(next - prev).normalized;
                    var left = new Vector3(-along.z, 0, along.x);
                    float half = widths[Mathf.Min(i, widths.Length - 1)] * .5f;
                    float offset = Mathf.Min(1.2f, Mathf.Max(0, half - .35f));
                    targets.Add(((side > 0 ? "лев " : "прав ") + route[i].name, route[i].position + left * offset * side, route[i].position, false));
                }
            }
            for (int i = 1; i < targets.Count; i++)
            {
                var target = targets[i].pos;
                if (targets[i].jump)
                {
                    held = new KeyboardState();
                    motor.Teleport(target + Vector3.up * .05f, Quaternion.LookRotation(Flat(targets[i + 1].pos - target).normalized));
                    float pause = Time.time + .4f;
                    while (Time.time < pause) yield return null;
                    continue;
                }
                float t0 = Time.time, lastCheck = Time.time;
                var lastPos = motor.transform.position;
                string trouble = null;
                float fastest = 0;
                while (true)
                {
                    var pos = motor.transform.position;
                    fastest = Mathf.Max(fastest, motor.PlanarVelocity.magnitude);
                    if (Flat(pos - target).magnitude < .5f) break;
                    if (pos.y < target.y - 6) { trouble = "упал"; break; }
                    if (Time.time - t0 > 25) { trouble = "не дошёл за 25 с"; break; }
                    view.Guide(new Vector3(target.x, pos.y + 1.6f, target.z));
                    float yawError = Vector3.Angle(Flat(motor.transform.forward), Flat(target - pos));
                    held = yawError < 30 ? new KeyboardState(Key.W) : new KeyboardState();
                    if (Time.time - lastCheck > 1.5f)
                    {
                        if ((pos - lastPos).magnitude < .3f) { trouble = "застрял"; break; }
                        lastPos = pos;
                        lastCheck = Time.time;
                    }
                    yield return null;
                }
                var p = motor.transform.position;
                var l = root.InverseTransformPoint(p);
                if (fastest > 7f) { problems++; log.AppendLine($"{targets[i - 1].name}→{targets[i].name}: БРОСОК {fastest:F1} м/с около ({l.x:F1}; {l.z:F1}; h {l.y:F2})"); }
                if (trouble != null)
                {
                    problems++;
                    var near = Physics.OverlapCapsule(p + Vector3.up * .3f, p + Vector3.up * 1.5f, .5f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(c => c.gameObject != player).Select(c => c.name).Distinct();
                    log.AppendLine($"{targets[i - 1].name}→{targets[i].name}: {trouble} в ({l.x:F1}; {l.z:F1}; h {l.y:F2}), grounded={motor.IsGrounded}, рядом: {string.Join(", ", near)}");
                    held = new KeyboardState();
                    var axis = targets[i].axis;
                    var next = i + 1 < targets.Count ? targets[i + 1].axis : axis + Vector3.forward;
                    motor.Teleport(axis + Vector3.up * .05f, Quaternion.LookRotation(Flat(next - axis).sqrMagnitude > .01f ? Flat(next - axis).normalized : Vector3.forward));
                    float wait = Time.time + .4f;
                    while (Time.time < wait) yield return null;
                }
                else log.AppendLine($"{targets[i - 1].name}→{targets[i].name}: ок за {Time.time - t0:F1} с, h {l.y:F2}");
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge", "god-walk.txt"), log.ToString());
            }
            log.AppendLine($"весь путь: {Time.time - walkStart:F0} с, проблем {problems}");
            held = new KeyboardState();
            view.Guide(null);
        }
        finally
        {
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge", "god-walk.txt"), log.ToString());
            InputSystem.onBeforeUpdate -= Queue;
            InputSystem.onAfterUpdate -= Select;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.updateMode = previousUpdateMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
        }
        Assert.That(problems, Is.Zero, log.ToString());
    }

    void Queue()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        InputSystem.QueueStateEvent(keyboard, held);
    }

    void Select()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        keyboard.MakeCurrent();
        mouse.MakeCurrent();
    }
}
#endif
