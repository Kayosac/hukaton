using System.Collections;
using System.IO;
using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>
    /// Writes every teaser frame (the Game view, overlay UI included) as a numbered PNG and leaves Play mode after the
    /// last one. The Game view must be set to the output resolution (1920 x 1080): use Hakaton → Teaser → Render frames.
    /// Editor only.
    /// </summary>
    public sealed class TeaserRecorder : MonoBehaviour
    {
        public TeaserTimeline timeline;
        [Tooltip("Record whenever Play starts. Usually left off: the Render frames menu records one run.")]
        public bool record;

        /// <summary>Editor session flag set by the Render frames menu for exactly one Play run.</summary>
        public const string RecordOnceKey = "Hakaton.Teaser.RecordOnce";
        /// <summary>Optional editor session list of frames ("150,300-310") for a quick check render of that run only;
        /// they are written to the output folder + "-check" and Play stops after the last one.</summary>
        public const string CheckFramesKey = "Hakaton.Teaser.CheckFrames";
        [Tooltip("Folder relative to the Unity project folder.")]
        public string outputFolder = "Logs/teaser-frames";
        [Tooltip("Save every Nth frame (1 = all) for quick checks.")]
        [Min(1)] public int recordEvery = 1;

        IEnumerator Start()
        {
#if UNITY_EDITOR
            bool once = UnityEditor.SessionState.GetBool(RecordOnceKey, false);
            UnityEditor.SessionState.SetBool(RecordOnceKey, false);
            string check = UnityEditor.SessionState.GetString(CheckFramesKey, "");
            UnityEditor.SessionState.SetString(CheckFramesKey, "");
            if (!(record || once) || !timeline) yield break;
            var only = new System.Collections.Generic.HashSet<int>();
            foreach (var part in check.Split(','))
            {
                var ends = part.Split('-');
                if (!int.TryParse(ends[0], out int a)) continue;
                int b = ends.Length > 1 && int.TryParse(ends[1], out int e) ? e : a;
                for (int f = a; f <= b; f++) only.Add(f);
            }
            int lastFrame = timeline.totalFrames - 1;
            if (only.Count > 0) lastFrame = Mathf.Min(lastFrame, System.Linq.Enumerable.Max(only));
            string folder = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), outputFolder + (only.Count > 0 ? "-check" : "")));
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, "frame_*.png")) File.Delete(old);
            var log = new System.Text.StringBuilder();
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                int frame = timeline.Frame;
                if (frame < 0) continue;
                if (frame > lastFrame) break;
                if (only.Count > 0 ? !only.Contains(frame) : frame % recordEvery != 0) continue;
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(folder, $"frame_{frame:0000}.png"), shot.EncodeToPNG());
                if (frame % 30 == 0 || frame < 3)
                    log.AppendLine($"frame {frame} size {shot.width}x{shot.height} time {Time.time:F4} unscaled {Time.unscaledTime:F4} dt {Time.deltaTime:F4} udt {Time.unscaledDeltaTime:F4}");
                Destroy(shot);
            }
            File.WriteAllText(Path.Combine(folder, "capture-log.txt"), log.ToString());
            UnityEditor.EditorApplication.isPlaying = false;
#else
            yield break;
#endif
        }
    }
}
