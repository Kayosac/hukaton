using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Backroom.Editor
{
    /// <summary>
    /// The brazier's campfire: makes its prefab, wires it into the brazier, and renders still frames of
    /// the brazier with the fire burning into output/campfire-preview (to judge the look without entering Play).
    /// </summary>
    public static class CampfireTools
    {
        const string PrefabPath = "Assets/Backroom/Prefabs/Campfire.prefab";
        const string BrazierPath = "Assets/Backroom/Prefabs/Brazier.prefab";
        const string HeartPath = "Assets/Plugins/Retro Arsenal/Prefabs/Environment/Fire/Torch/FireTorch.prefab";
        const string TonguePath = "Assets/Game/Prefabs/Effects/Retro/Candle flame.prefab";

        [MenuItem("Tools/Hakaton/Campfire/Build assets")]
        public static void BuildAssets()
        {
            var root = new GameObject("Campfire");
            var fire = root.AddComponent<Campfire>();
            fire.heart = AssetDatabase.LoadAssetAtPath<GameObject>(HeartPath);
            fire.tongue = AssetDatabase.LoadAssetAtPath<GameObject>(TonguePath);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var brazierRoot = PrefabUtility.LoadPrefabContents(BrazierPath);
            var brazier = brazierRoot.GetComponentInChildren<Brazier>(true);
            brazier.campfirePrefab = prefab.GetComponent<Campfire>();
            PrefabUtility.SaveAsPrefabAsset(brazierRoot, BrazierPath);
            PrefabUtility.UnloadPrefabContents(brazierRoot);
            AssetDatabase.SaveAssets();
            Debug.Log("[Campfire] assets built and wired into the brazier");
        }

        [MenuItem("Tools/Hakaton/Campfire/Render preview")]
        public static void RenderPreview()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../output/campfire-preview"));
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();

            var preview = new PreviewRenderUtility();
            try
            {
                var brazierAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BrazierPath);
                var root = Object.Instantiate(brazierAsset);
                preview.AddSingleGO(root);
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
                var brazier = root.GetComponentInChildren<Brazier>(true);
                var firePoint = brazier.firePoint;
                var fire = Object.Instantiate(brazier.campfirePrefab, firePoint, false);
                fire.transform.localScale = Vector3.one * brazier.flameSize;
                fire.Build();

                var bounds = new Bounds(firePoint.position, Vector3.zero);
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (!r.transform.IsChildOf(fire.transform)) bounds.Encapsulate(r.bounds);
                report.AppendLine($"brazier bounds {bounds.center} size {bounds.size}");
                report.AppendLine($"fire point {firePoint.position} lossy scale {firePoint.lossyScale}");

                var camera = preview.camera;
                camera.fieldOfView = 32;
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 50;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.07f, .06f, .08f);
                preview.lights[0].intensity = .7f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40, 30, 0);
                preview.lights[1].intensity = .3f;
                preview.ambientColor = new Color(.25f, .22f, .25f);

                var systems = fire.GetComponentsInChildren<ParticleSystem>(true)
                    .Where(s => !s.transform.parent || !s.transform.parent.GetComponentInParent<ParticleSystem>()).ToArray();
                float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                var shots = new[]
                {
                    ("wide", bounds.center, size * 2.1f, 15f),
                    ("close", firePoint.position + Vector3.up * size * .12f, size * 1.0f, 25f),
                    ("side", firePoint.position + Vector3.up * size * .12f, size * 1.0f, 8f),
                };
                // Frames: catching (0.35), burning (1), a card flaring in it (burn jump).
                var strengths = new[] { .35f, 1f, brazier.burnJump };
                for (int frame = 0; frame < 3; frame++)
                {
                    fire.Strength = strengths[frame];
                    fire.Apply();
                    for (int i = 0; i < systems.Length; i++)
                        systems[i].Simulate(2f + frame * .17f + i * .11f, true, true);
                    foreach (var (name, target, distance, pitch) in shots)
                    {
                        float yaw = name == "side" ? 70 : 0;
                        var rotation = Quaternion.Euler(pitch, yaw, 0);
                        camera.transform.rotation = rotation;
                        camera.transform.position = target - rotation * Vector3.forward * distance;
                        preview.BeginStaticPreview(new Rect(0, 0, 800, 800));
                        preview.Render(true);
                        var texture = preview.EndStaticPreview();
                        File.WriteAllBytes(Path.Combine(folder, $"{name}_{frame}.png"), texture.EncodeToPNG());
                        Object.DestroyImmediate(texture);
                    }
                }
            }
            finally
            {
                preview.Cleanup();
            }
            File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
            Debug.Log("[Campfire] preview written to " + folder + "\n" + report);
        }
    }
}
