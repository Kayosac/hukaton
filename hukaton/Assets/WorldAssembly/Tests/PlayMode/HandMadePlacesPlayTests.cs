using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// The hand-made places (the Old Apiary, the tutorial cave) have the endless world's loose things: every one, woken
    /// and tossed a little, stays in the place — none shoots off (woken inside something), none sinks through the floor —
    /// and they lie still again.
    /// </summary>
    public sealed class HandMadePlacesPlayTests
    {
        [UnityTest]
        public IEnumerator LooseThingsOfTheTutorialCaveStayInTheCave()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/TutorialCave/Scenes/TutorialCave.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return Shake("the cave", 40);
        }

        [UnityTest]
        public IEnumerator LooseThingsOfTheOldApiaryStayInTheApiary()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Apiary test"));
            var place = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OldApiary/Prefabs/Old Apiary.prefab"));
            place.SetActive(true);
            yield return Shake("the apiary", 150);
            Object.Destroy(place);
        }

        /// <summary>Photos (Logs/world-plans/places-*.png) of the thickest heap of loose things in each place blown apart.</summary>
        [UnityTest]
        public IEnumerator PhotographAHeapBlownApartInTheCaveAndTheApiary()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/TutorialCave/Scenes/TutorialCave.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return new WaitForSeconds(1);
            yield return BlowUpTheThickestHeap("cave");
            SceneManager.SetActiveScene(SceneManager.CreateScene("Apiary test"));
            foreach (var root in SceneManager.GetSceneByPath("Assets/TutorialCave/Scenes/TutorialCave.unity").GetRootGameObjects()) root.SetActive(false);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.45f, .47f, .55f);
            var place = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OldApiary/Prefabs/Old Apiary.prefab"));
            place.SetActive(true);
            yield return new WaitForSeconds(1);
            yield return BlowUpTheThickestHeap("apiary");
            Object.Destroy(place);
        }

        static IEnumerator BlowUpTheThickestHeap(string place)
        {
            var props = Object.FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None).Where(p => p.dormant && p.isActiveAndEnabled && p.body).ToList();
            // The most things that break (crates, pots, bottles, hives) together.
            var breaking = props.Where(p => p.TryGetComponent<Breakable>(out _)).ToList();
            var centre = breaking.Select(p => p.body.position)
                .OrderByDescending(c => breaking.Count(q => (q.body.position - c).sqrMagnitude < 3f * 3f) * 10
                    + props.Count(q => (q.body.position - c).sqrMagnitude < 2.5f * 2.5f)).First();
            int near = props.Count(q => (q.body.position - centre).sqrMagnitude < 2.5f * 2.5f);
            // From the side with the most room, so the eye is not inside a rock.
            Vector3 eye = centre + new Vector3(-3, 2, -3);
            float best = 0;
            for (int i = 0; i < 12; i++)
            {
                var way = Quaternion.Euler(0, i * 30, 0) * Vector3.forward;
                var from = centre + Vector3.up * 2.6f;
                float free = Physics.Raycast(from, way, out var wall, 5.5f, ~(1 << 4), QueryTriggerInteraction.Ignore) ? wall.distance - .4f : 5.5f;
                if (free > best) { best = free; eye = from + way * free; }
            }
            var cam = new GameObject("Photo camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.55f, .65f, .75f);
            cam.fieldOfView = 70;
            cam.nearClipPlane = .05f;
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "world-plans");
            System.IO.Directory.CreateDirectory(dir);
            yield return Photo(cam, dir, $"places-{place}-0-still", eye, centre);
            int moved = PropPhysics.Burst(centre + Vector3.down * .3f, 3, 7, 1.2f, null, 2, 10, new CardBlow("strike", 2));
            yield return new WaitForSeconds(.35f);
            yield return Photo(cam, dir, $"places-{place}-1-flying", eye, centre);
            yield return new WaitForSeconds(2.5f);
            yield return Photo(cam, dir, $"places-{place}-2-after", eye, centre);
            Debug.Log($"[World] {place}: heap of {near} at {centre}, burst moved {moved}, eye {eye}");
            Object.Destroy(cam.gameObject);
            Assert.That(moved, Is.GreaterThan(2), $"things blown about in the {place}");
        }

        static IEnumerator Photo(Camera cam, string dir, string name, Vector3 eye, Vector3 look)
        {
            yield return new WaitForEndOfFrame();
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            var rt = new RenderTexture(640, 360, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
        }

        static IEnumerator Shake(string place, int atLeast)
        {
            yield return new WaitForSeconds(1);
            var props = Object.FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None)
                .Where(p => p.dormant && p.body && p.isActiveAndEnabled && p.body.isKinematic).ToList();
            Assert.That(props.Count, Is.GreaterThan(atLeast), $"dormant loose things in {place}");
            var problems = new List<string>();
            int woken = 0;
            for (int start = 0; start < props.Count; start += 30)
            {
                var batch = props.Skip(start).Take(30).Where(p => p && p.isActiveAndEnabled).ToList();
                var before = batch.ToDictionary(p => p, p => p.body.position);
                foreach (var p in batch)
                    if (PropPhysics.Ready(p.body)) { woken++; p.body.AddForce(Vector3.up * 1.2f + Random.insideUnitSphere * .6f, ForceMode.VelocityChange); }
                yield return new WaitForSeconds(2.5f);
                foreach (var p in batch)
                {
                    if (!p || !p.isActiveAndEnabled) continue;
                    var delta = p.body.position - before[p];
                    float across = new Vector2(delta.x, delta.z).magnitude;
                    if (across > 6) problems.Add($"{p.name} shot {across:0.0} m across, {delta.y:0.0} m up, from {before[p]}");
                    if (delta.y < -3) problems.Add($"{p.name} fell {-delta.y:0.0} m from {before[p]}");
                }
                foreach (var p in batch) if (p && !p.body.isKinematic) p.Rest();
            }
            Debug.Log($"[World] {place}: woke {woken} of {props.Count} loose things, {problems.Count} problems\n" + string.Join("\n", problems));
            Assert.That(woken, Is.GreaterThan(props.Count / 2));
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
