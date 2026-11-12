// TEMPORARY play check for the god encounter (bridge: invoke GodCheck.Edge / Finger / Next / Report while playing).
using System.IO;
using System.Linq;
using Hakaton.GodMountains;
using UnityEngine;
public static class GodCheck
{
    static GodEncounter Enc => Object.FindFirstObjectByType<GodEncounter>();
    public static void Edge()
    {
        var e = Enc;
        Vector3 dir = e.lip.position - e.edgeLine.position; dir.y = 0;
        e.motor.Teleport(e.edgeLine.position + dir.normalized * .6f + Vector3.up * .2f, Quaternion.LookRotation(dir));
        Report("edge");
    }
    public static void Finger()
    {
        var e = Enc;
        Vector3 dir = e.lip.position - e.edgeLine.position; dir.y = 0; dir.Normalize();
        var start = e.lip.position + dir * 5 + Vector3.up * 60;
        var hits = Physics.RaycastAll(start, Vector3.down, 200);
        var hit = hits.Where(h => e.fingerColliders.Contains(h.collider)).OrderBy(h => h.distance).FirstOrDefault();
        if (!hit.collider) { Report("finger MISSED " + string.Join(",", hits.Select(h => h.collider.name))); return; }
        e.motor.Teleport(hit.point + Vector3.up * .05f, Quaternion.LookRotation(dir));
        Report($"finger hit {hit.collider.name} at {hit.point}");
    }
    public static void Next() { Enc.Continue(); Report("next"); }
    public static void Report() => Report("report");
    static void Report(string tag)
    {
        var e = Enc;
        var info = e.god.GetCurrentAnimatorClipInfo(0);
        string clip = info.Length > 0 ? info[0].clip.name : "-";
        File.AppendAllText("Logs/bridge/god-check.txt",
            $"{tag}: t={Time.time:F1} stage={e.Stage} clip={clip} player={e.motor.transform.position:F2} grounded={e.motor.IsGrounded} fingertip={e.finger.position:F2}\n");
    }
}
