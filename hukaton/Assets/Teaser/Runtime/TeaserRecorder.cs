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
        [Tooltip("Folder relative to the Unity project folder.")]
        public string outputFolder = "Logs/teaser-frames";
        [Tooltip("Save every Nth frame (1 = all) for quick checks.")]
        [Min(1)] public int recordEvery = 1;

        IEnumerator Start()
        {
#if UNITY_EDITOR
            bool once = UnityEditor.SessionState.GetBool(RecordOnceKey, false);
            UnityEditor.SessionState.SetBool(RecordOnceKey, false);
            if (!(record || once) || !timeline) yield break;
            string folder = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), outputFolder));
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, "frame_*.png")) File.Delete(old);
            var log = new System.Text.StringBuilder();
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                int frame = timeline.Frame;
                if (frame < 0) continue;
                if (frame >= timeline.totalFrames) break;
                if (frame % recordEvery != 0) continue;
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
