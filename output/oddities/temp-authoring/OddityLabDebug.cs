using System.Linq;
using Hakaton.Expeditions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.TempAuthoring
{
    /// <summary>TEMPORARY: drives the oddity lab from the bridge. Removed after checks.</summary>
    static class OddityLabDebug
    {
        public static string OpenScene(string path)
        {
            if (EditorApplication.isPlaying) return "playing";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "DIRTY " + SceneManager.GetSceneAt(i).path;
            EditorSceneManager.OpenScene(path);
            return "opened " + path;
        }

        static Oddity Find(string kind) => Object.FindObjectsByType<Oddity>(FindObjectsSortMode.None).First(o => o.kind.ToString().StartsWith(kind));
        static ExpeditionWorld World() => Object.FindFirstObjectByType<ExpeditionWorld>();

        public static string GoTo(string arg)
        {
            var parts = arg.Split(' ');
            var o = Find(parts[0]);
            float distance = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 2.2f;
            var world = World();
            Vector3 stand = o.transform.position + o.transform.forward * distance + Vector3.up * .05f;
            world.motor.Teleport(stand, Quaternion.LookRotation(-o.transform.forward));
            var view = Object.FindFirstObjectByType<Hakaton.Character.FirstPersonView>();
            float h = o.kind == Oddity.Kind.Payphone ? 1.3f : .65f;
            if (view) view.ResetPose(Quaternion.LookRotation(o.transform.position + Vector3.up * h - view.pitchPivot.position));
            return "at " + o.name;
        }

        public static string Use(string kind) => "used=" + Find(kind).Interact();

        public static string Report()
        {
            var world = World();
            return "running=" + world.Running + " hearts=" + world.vitals.HalfHearts + " gravity=" + world.motor.GravityScale.ToString("0.00")
                   + " notice=" + (Time.time < world.NoticeUntil ? world.Notice : "-") + " | " + string.Join(", ",
                Object.FindObjectsByType<Oddity>(FindObjectsSortMode.None).Select(o => o.kind + ":" + o.Current + (o.Answering ? "(answering)" : "") + " effect=" + (o.LastEffect ? o.LastEffect.name : "-")));
        }
    }
}
