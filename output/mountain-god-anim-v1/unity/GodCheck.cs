// TEMPORARY play check for the god encounter (bridge: invoke GodCheck.Open / Edge / Mouth / Finger / Next / Status / Look / Free).
using System.IO;
using System.Linq;
using Hakaton.GodMountains;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GodCheck
{
    static GodEncounter Enc => Object.FindFirstObjectByType<GodEncounter>();

    /// <summary>Opens the god-mountains scene alone, but never over unsaved edits (no save dialog).</summary>
    public static string Open()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.isDirty) return "NOT OPENED: unsaved scene " + s.name + " " + s.path;
        }
        EditorSceneManager.OpenScene("Assets/GodMountains/Scenes/GodMountains.unity", OpenSceneMode.Single);
        return "opened";
    }

    public static void Edge()
    {
        var e = Enc;
        Vector3 dir = e.lip.position - e.edgeLine.position; dir.y = 0;
        e.motor.Teleport(e.edgeLine.position + dir.normalized * .6f + Vector3.up * .2f, Quaternion.LookRotation(dir));
        Log("edge");
    }

    /// <summary>Stands the player at the mouth of the slot, facing the god (the first view of him).</summary>
    public static void Mouth()
    {
        var e = Enc;
        Vector3 dir = e.lip.position - e.mouth.position; dir.y = 0;
        e.motor.Teleport(e.mouth.position + Vector3.up * .2f, Quaternion.LookRotation(dir));
        Log("mouth");
    }

    public static void Finger()
    {
        var e = Enc;
        Vector3 dir = e.lip.position - e.edgeLine.position; dir.y = 0; dir.Normalize();
        var start = e.lip.position + dir * 5 + Vector3.up * 60;
        var hits = Physics.RaycastAll(start, Vector3.down, 200);
        var hit = hits.Where(h => e.fingerColliders.Contains(h.collider)).OrderBy(h => h.distance).FirstOrDefault();
        if (!hit.collider) { Log("finger MISSED " + string.Join(",", hits.Select(h => h.collider.name))); return; }
        e.motor.Teleport(hit.point + Vector3.up * .05f, Quaternion.LookRotation(dir));
        Log($"finger hit {hit.collider.name} at {hit.point}");
    }

    public static void Next() { Enc.Continue(); Log("next"); }
    public static void Status() => Log("status");

    /// <summary>Turns the view to a world point "x,y,z" or to a bone of the god by name.</summary>
    public static void Look(string target)
    {
        var e = Enc;
        Vector3 p;
        var bone = e.god.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == target);
        if (bone) p = bone.position;
        else
        {
            var v = target.Split(',').Select(float.Parse).ToArray();
            p = new Vector3(v[0], v[1], v[2]);
        }
        e.view.Guide(p);
        Log("look " + target);
    }

    public static void Free() { Enc.view.Guide(null); Log("free"); }

    static void Log(string tag)
    {
        var e = Enc;
        var info = e.god.GetCurrentAnimatorClipInfo(0);
        string clip = info.Length > 0 ? info[0].clip.name : "-";
        File.AppendAllText("Logs/bridge/god-check.txt",
            $"{tag}: t={Time.time:F1} stage={e.Stage} clip={clip} player={e.motor.transform.position:F2} grounded={e.motor.IsGrounded} fingertip={e.finger.position:F2}\n");
    }
}
