#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Hakaton.Expeditions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.TempAuthoring
{
    /// <summary>TEMPORARY: plays the oddity scenario in its lab and writes frames for a GIF. Removed afterwards.</summary>
    public sealed class OddityGif : MonoBehaviour
    {
        static readonly string Folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "oddity-gif");

        public static string OpenScene(string path)
        {
            if (EditorApplication.isPlaying) return "playing";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "DIRTY " + SceneManager.GetSceneAt(i).path;
            EditorSceneManager.OpenScene(path);
            return "opened";
        }

        public static string Record()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            if (Directory.Exists(Folder)) foreach (var f in Directory.GetFiles(Folder)) File.Delete(f);
            Directory.CreateDirectory(Folder);
            new GameObject("Oddity gif").AddComponent<OddityGif>();
            return "recording";
        }

        public static string Status() => File.Exists(Path.Combine(Folder, "done.txt")) ? "done " + Directory.GetFiles(Folder, "*.png").Length
            : "running " + (Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.png").Length : 0);

        ExpeditionWorld world;
        Hakaton.Character.FirstPersonView view;
        Transform look;
        Vector3 lookOffset;
        bool capturing = true;
        int frame;
        readonly StringBuilder notices = new StringBuilder();

        static OddityTable Only(OddityEffect effect)
        {
            var t = ScriptableObject.CreateInstance<OddityTable>();
            t.entries = new[] { new OddityTable.Entry { effect = effect, weight = 1 } };
            return t;
        }

        IEnumerator Start()
        {
            world = FindFirstObjectByType<ExpeditionWorld>();
            view = FindFirstObjectByType<Hakaton.Character.FirstPersonView>();
            foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (b.GetType().Name == "EnemyDebugSandbox") Destroy(b);
            foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (b && b.GetType().Name == "EnemyDebugPreview") b.gameObject.SetActive(false);
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t => t.text.Contains("ОТЛАДКА"))) canvas.enabled = false;
            var phone = FindObjectsByType<Oddity>(FindObjectsSortMode.None).First(o => o.kind == Oddity.Kind.Payphone);
            var tv = FindObjectsByType<Oddity>(FindObjectsSortMode.None).First(o => o.kind == Oddity.Kind.Television);
            phone.effects = Only(AssetDatabase.LoadAssetAtPath<LureOddityEffect>("Assets/Game/Data/Oddities/Lure creatures.asset"));
            tv.effects = Only(AssetDatabase.LoadAssetAtPath<HealOddityEffect>("Assets/Game/Data/Oddities/Heal.asset"));

            // A creature far behind the payphone, grazing; it will come when the phone is answered.
            var prefab = AssetDatabase.LoadAssetAtPath<WorldCreature>("Assets/Game/Prefabs/Goblin dog.prefab");
            var creature = Instantiate(prefab, phone.transform.position - phone.transform.forward * 13 + phone.transform.right * 2.5f, Quaternion.Euler(0, 0, 0));
            creature.world = world;
            creature.ResetForOuting(null);
            world.creatures.Clear();
            world.creatures.Add(creature);

            // 1) walking up to the ringing payphone
            Vector3 front = phone.transform.forward;
            Vector3 from = phone.transform.position + front * 6 + Vector3.up * .05f, to = phone.transform.position + front * 2.3f + Vector3.up * .05f;
            look = phone.transform; lookOffset = Vector3.up * 1.2f;
            world.motor.Teleport(from, Quaternion.LookRotation(-front));
            yield return null;
            StartCoroutine(Capture());
            float t = 0;
            while (t < 2.2f)
            {
                t += Time.deltaTime;
                world.motor.Teleport(Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, t / 2.2f)), Quaternion.LookRotation(-front));
                yield return null;
            }
            yield return new WaitForSeconds(.6f);
            phone.Interact();                                  // E
            yield return new WaitForSeconds(phone.answerSeconds + .6f);
            look = creature.transform; lookOffset = Vector3.up * .5f; // the dog comes
            yield return new WaitForSeconds(4f);

            // 2) the television (cut): hurt first, so the heal shows
            world.vitals.Hurt(5, Vector3.zero, null);
            Vector3 tvFront = tv.transform.forward;
            world.motor.Teleport(tv.transform.position + tvFront * 2.1f + Vector3.up * .05f, Quaternion.LookRotation(-tvFront));
            creature.gameObject.SetActive(false);
            look = tv.transform; lookOffset = Vector3.up * .62f;
            yield return new WaitForSeconds(1.6f);
            tv.Interact();                                     // E
            yield return new WaitForSeconds(tv.answerSeconds + 2.6f);
            capturing = false;
            yield return null;
            File.WriteAllText(Path.Combine(Folder, "notices.txt"), notices.ToString());
            File.WriteAllText(Path.Combine(Folder, "done.txt"), frame.ToString());
            Destroy(gameObject);
        }

        void LateUpdate()
        {
            if (!look || !view) return;
            var wanted = Quaternion.LookRotation(look.position + lookOffset - view.pitchPivot.position);
            view.ResetPose(Quaternion.Slerp(view.pitchPivot.rotation, wanted, Time.deltaTime * 5));
        }

        IEnumerator Capture()
        {
            const float step = 1 / 12f;
            float next = Time.time;
            while (capturing)
            {
                yield return new WaitForEndOfFrame();
                if (Time.time < next) continue;
                next += step;
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(Folder, $"f{frame:0000}.png"), shot.EncodeToPNG());
                Destroy(shot);
                string notice = Time.time < world.NoticeUntil ? world.Notice : "";
                notices.Append(frame).Append('\t').Append(notice).Append('\t').Append(world.vitals.HalfHearts).Append('\n');
                frame++;
            }
        }
    }
}
#endif
