using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Render real SpriteRenderers: world pixel density must not depend on the artwork/import.</summary>
    public sealed class WorldSpritePixelTests
    {
        const int Resolution = 256;
        readonly List<Object> owned = new List<Object>();
        Material material;
        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        [SetUp]
        public void Setup()
        {
            var shader = Shader.Find("Hukaton/Sprites/Hand Drawn Pixel");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            material = Own(new Material(shader));
            material.SetFloat("_PixelsPerUnit", 16);
            material.SetFloat("_Motion", 0);
            // These checks measure the world grid itself; the close-up split has its own tests below.
            material.SetFloat("_MaxRefine", 0);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var item in owned.AsEnumerable().Reverse()) if (item) Object.DestroyImmediate(item);
            owned.Clear();
        }

        SpriteRenderer Drawing(int size = 256, int columns = 1, int rows = 1, int column = 0, int row = 0)
        {
            var texture = Own(new Texture2D(size * columns, size * rows, TextureFormat.RGBA32, false, true));
            var pixels = Enumerable.Repeat(new Color32(255, 0, 255, 255), texture.width * texture.height).ToArray();
            // A smooth two-axis ramp makes every quantized cell observable, with poison-pink neighbours.
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                pixels[(row * size + y) * texture.width + column * size + x] =
                    new Color32((byte)(20 + 210f * (x + .5f) / size), (byte)(20 + 210f * (y + .5f) / size), 32, 255);
            texture.SetPixels32(pixels); texture.Apply();
            var rect = new Rect(column * size, row * size, size, size);
            var sprite = Own(Sprite.Create(texture, rect, new Vector2(.5f, .5f), size, 0, SpriteMeshType.FullRect));
            var go = Own(new GameObject("World pixel fixture"));
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetVector("_SpriteUVRect", new Vector4(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height));
            renderer.SetPropertyBlock(block);
            return renderer;
        }

        Color32[] Render(SpriteRenderer renderer, int pass = 0, float distance = 0)
        {
            var previousCamera = Shader.GetGlobalVector("_WorldSpaceCameraPos");
            var previousInverseView = Shader.GetGlobalMatrix("unity_MatrixInvV");
            var rt = RenderTexture.GetTemporary(Resolution, Resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var image = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true);
            try
            {
                using (var command = new CommandBuffer())
                {
                    command.SetRenderTarget(rt);
                    command.ClearRenderTarget(true, true, Color.clear);
                    var projection = distance > 0 ? Matrix4x4.Perspective(45, 1, .1f, 100) : Matrix4x4.Ortho(-2, 2, -2, 2, -10, 10);
                    var view = distance > 0 ? Matrix4x4.Translate(new Vector3(0, 0, -distance)) : Matrix4x4.identity;
                    command.SetViewProjectionMatrices(view, GL.GetGPUProjectionMatrix(projection, true));
                    command.SetGlobalMatrix("unity_MatrixInvV", view.inverse);
                    command.SetGlobalVector("_WorldSpaceCameraPos", new Vector4(0, 0, distance, 1));
                    command.DrawRenderer(renderer, material, 0, pass);
                    Graphics.ExecuteCommandBuffer(command);
                }
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0); image.Apply();
                return image.GetPixels32();
            }
            finally
            {
                Shader.SetGlobalVector("_WorldSpaceCameraPos", previousCamera);
                Shader.SetGlobalMatrix("unity_MatrixInvV", previousInverseView);
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static List<int> Runs(Color32[] image, bool vertical = false)
        {
            var runs = new List<int>();
            int previous = -1;
            for (int i = 0; i < Resolution; i++)
            {
                var c = image[vertical ? i * Resolution + Resolution / 2 : Resolution / 2 * Resolution + i];
                if (c.a == 0) continue;
                int value = vertical ? c.g : c.r;
                if (value != previous) { runs.Add(1); previous = value; }
                else runs[runs.Count - 1]++;
            }
            Assert.That(runs.Count, Is.GreaterThan(0), "Must measure actual rendered output.");
            return runs;
        }

        [TestCase(128, 1, 1, 0, 0)]
        [TestCase(512, 1, 1, 0, 0)]
        [TestCase(256, 3, 2, 2, 1)]
        public void TextureResolutionPpuAndAtlasPositionDoNotChangeWorldPixels(int size, int columns, int rows, int column, int row)
        {
            var image = Render(Drawing(size, columns, rows, column, row));
            Assert.That(Runs(image), Is.EqualTo(Enumerable.Repeat(4, 16)));
            Assert.That(Runs(image, true), Is.EqualTo(Enumerable.Repeat(4, 16)));
            Assert.That(image.Count(c => c.a > 0 && c.b > 100), Is.Zero, "No adjacent atlas cell may leak.");
        }

        [Test]
        public void ParentAndNonUniformScaleChangeCellCountButKeepSquareWorldPixels()
        {
            var renderer = Drawing();
            var parent = Own(new GameObject("Scaled parent"));
            renderer.transform.SetParent(parent.transform, false);
            parent.transform.localScale = new Vector3(2, .5f, 1);
            var image = Render(renderer);
            Assert.That(Runs(image), Is.EqualTo(Enumerable.Repeat(4, 32)));
            Assert.That(Runs(image, true), Is.EqualTo(Enumerable.Repeat(4, 8)));
        }

        [TestCase(.01f)]
        [TestCase(.18f)]
        [TestCase(2f)]
        [TestCase(100f)]
        public void CompensatedImportAndTransformScaleProduceIdenticalWorldPixels(float scale)
        {
            var renderer = Drawing();
            var original = Render(renderer);
            var sprite = renderer.sprite;
            renderer.sprite = Own(Sprite.Create(sprite.texture, sprite.rect, new Vector2(.5f, .5f),
                sprite.pixelsPerUnit * scale, 0, SpriteMeshType.FullRect));
            renderer.transform.localScale = Vector3.one * scale;
            Assert.That(Render(renderer), Is.EqualTo(original), "Same world size must ignore how PPU and Transform divide the scaling.");
        }

        Color32[] RenderThroughCamera(SpriteRenderer renderer, int outputSize = Resolution, bool perspective = false, bool overrideView = false, Vector3? frameCenter = null)
        {
            var cameraObject = Own(new GameObject("World pixel camera"));
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = !perspective;
            camera.orthographicSize = 2;
            camera.aspect = 1;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << 31;
            var centre = frameCenter ?? renderer.bounds.center;
            camera.transform.position = centre + new Vector3(0, 0, -10);
            if (perspective)
            {
                camera.transform.position = centre + new Vector3(1.5f, 1, -4);
                camera.transform.LookAt(centre);
            }
            if (overrideView)
            {
                // Exercise an overridden view independently of the camera's Transform hierarchy.
                var view = camera.worldToCameraMatrix;
                camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                camera.worldToCameraMatrix = view;
            }
            renderer.gameObject.layer = 31;
            var rt = RenderTexture.GetTemporary(outputSize, outputSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var image = new Texture2D(outputSize, outputSize, TextureFormat.RGBA32, false, true);
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, outputSize, outputSize), 0, 0); image.Apply();
                return image.GetPixels32();
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        [TestCase(.18f)]
        [TestCase(2f)]
        [TestCase(100f)]
        public void CameraRenderingKeepsCompensatedScaleIndependent(float scale)
        {
            var renderer = Drawing();
            var original = RenderThroughCamera(renderer);
            // The game's renderer also applies full-screen dithering, so compare complete frames.
            Assert.That(original.Count(c => c.a > 0), Is.GreaterThan(1000));
            var sprite = renderer.sprite;
            renderer.sprite = Own(Sprite.Create(sprite.texture, sprite.rect, new Vector2(.5f, .5f),
                sprite.pixelsPerUnit * scale, 0, SpriteMeshType.FullRect));
            renderer.transform.localScale = Vector3.one * scale;
            Assert.That(RenderThroughCamera(renderer), Is.EqualTo(original));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PresentationCameraViewOverrideDoesNotChangeTheWorldGrid(bool useTutorialBush)
        {
            var renderer = Drawing();
            if (useTutorialBush)
            {
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Content/Enemies/Bush.png");
                renderer.transform.localScale = Vector3.one * .21976472f;
                material.SetFloat("_PixelsPerUnit", 184.875f);
            }
            renderer.transform.position = new Vector3(644, 1, 7);
            var ordinary = RenderThroughCamera(renderer, 1024, perspective: true);
            var presentation = RenderThroughCamera(renderer, 1024, perspective: true, overrideView: true);
            int changed = ordinary.Zip(presentation, (a, b) => a.Equals(b) ? 0 : 1).Sum();
            Assert.That(changed, Is.Zero, $"The identical camera view changed {changed} pixels when only its Transform differed.");
        }

        [TestCase(0, false)]
        [TestCase(644, false)]
        [TestCase(644, true)]
        public void TutorialBushKeepsTheSameWorldGridWithScaleBakedIntoItsSprite(float worldX, bool perspective)
        {
            var source = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Content/Enemies/Bush.png");
            var renderer = Own(new GameObject("Tutorial bush comparison")).AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = material;
            material.SetFloat("_PixelsPerUnit", 184.875f);
            renderer.sprite = source;
            // As CaveDrawnProp does: the bush is a sub-rect of a power-of-two texture.
            var atlasRect = source.textureRect;
            var block = new MaterialPropertyBlock();
            block.SetVector("_SpriteUVRect", new Vector4(atlasRect.x / source.texture.width, atlasRect.y / source.texture.height,
                atlasRect.width / source.texture.width, atlasRect.height / source.texture.height));
            renderer.SetPropertyBlock(block);
            renderer.transform.position = new Vector3(worldX, 1, 7);
            const float scale = .21976472f;
            renderer.transform.localScale = Vector3.one * scale;
            var worldSize = renderer.bounds.size;
            var centre = renderer.bounds.center;
            var original = RenderThroughCamera(renderer, 1024, perspective, frameCenter: centre);
            var rect = source.textureRect;
            var pivot = source.pivot / source.rect.size;
            // Unity can downsize the imported texture while retaining the original sprite's world bounds.
            float effectivePpu = rect.height / source.bounds.size.y;
            renderer.sprite = Own(Sprite.Create(source.texture, rect, pivot, effectivePpu / scale, 0, SpriteMeshType.FullRect));
            renderer.transform.localScale = Vector3.one;
            // SpriteRenderer adds Z padding to its flat bounds; only the drawn XY size matters.
            Assert.That(Vector2.Distance(renderer.bounds.size, worldSize), Is.LessThan(0.000001f));
            var baked = RenderThroughCamera(renderer, 1024, perspective, frameCenter: centre);
            var changed = original.Zip(baked, (a, b) => a.Equals(b) ? 0 : 1).Sum();
            // Recreating an imported sprite rounds its mesh coordinates differently. Allow <0.05%
            // raster/sample boundary differences (the bush is authored at 100 texels/m, so texel edges
            // meet this test's 184.875 grid more often); the view-override regression above remains bit-exact.
            Assert.That(changed / (float)original.Length, Is.LessThan(.0005f),
                $"Bush at x={worldX}: {changed} changed pixels after baking only Transform scale.");
        }

        [Test]
        public void TranslatingAndMirroringKeepsTheGridAttachedToTheSprite()
        {
            var renderer = Drawing();
            var original = Render(renderer);
            renderer.transform.position = new Vector3(.125f, 0, 0); // Eight output pixels.
            var translated = Render(renderer);
            for (int y = 0; y < Resolution; y++) for (int x = 0; x < Resolution - 8; x++)
                Assert.That(translated[y * Resolution + x + 8], Is.EqualTo(original[y * Resolution + x]));
            renderer.transform.position = Vector3.zero;
            renderer.flipX = true;
            var flipped = Render(renderer);
            for (int y = 0; y < Resolution; y++) for (int x = 0; x < Resolution; x++)
                Assert.That(flipped[y * Resolution + x], Is.EqualTo(original[y * Resolution + Resolution - 1 - x]));
        }

        [Test]
        public void PerspectiveDistanceChangesScreenSizeWithoutChangingWorldGrid()
        {
            var renderer = Drawing();
            var near = Runs(Render(renderer, distance: 2));
            var far = Runs(Render(renderer, distance: 4));
            Assert.That(near.Count, Is.EqualTo(16), "Near row runs: " + string.Join(",", near));
            Assert.That(far.Count, Is.EqualTo(16), "Far row runs: " + string.Join(",", far));
            Assert.That(near.Sum(), Is.EqualTo(far.Sum() * 2).Within(2));
        }

        // A 1/16 m cell seen through the 45-degree test view is 81.5 / distance px of a 1080-line view.
        [TestCase(4f, 16)] // 20 px: under the 25 px limit, the world grid holds.
        [TestCase(2f, 32)] // 41 px: split once.
        [TestCase(1f, 64)] // 81 px: split twice.
        public void CloseUpSplitsTheWorldGridByPowersOfTwo(float distance, int cells)
        {
            material.SetFloat("_MaxRefine", 4);
            material.SetFloat("_MaxCellPixels", 25);
            var renderer = Drawing();
            var image = Render(renderer, distance: distance);
            var across = Runs(image);
            var down = Runs(image, vertical: true);
            // Near 1 m the 1 m drawing overflows the 256 px view: count only the cells inside it.
            int visible = distance < 2 ? across.Count : cells;
            Assert.That(across.Count, Is.EqualTo(visible), "Row runs: " + string.Join(",", across));
            Assert.That(down.Count, Is.EqualTo(across.Count), "The whole drawing is on one grid.");
            if (distance >= 2) Assert.That(across.Max() - across.Min(), Is.LessThanOrEqualTo(1), "Equal cells: " + string.Join(",", across));
            else Assert.That(across.Where((run, i) => i > 0 && i < across.Count - 1).All(run => System.Math.Abs(run - across[1]) <= 1), Is.True);
        }

        [Test]
        public void CloseUpSplitKeepsEveryWorldGridLine()
        {
            material.SetFloat("_MaxCellPixels", 25);
            var renderer = Drawing();
            material.SetFloat("_MaxRefine", 0);
            var world = Runs(Render(renderer, distance: 2));
            material.SetFloat("_MaxRefine", 4);
            var split = Runs(Render(renderer, distance: 2));
            Assert.That(world.Count, Is.EqualTo(16));
            Assert.That(split.Count, Is.EqualTo(32));
            for (int i = 0; i < world.Count; i++)
                Assert.That(split[2 * i] + split[2 * i + 1], Is.EqualTo(world[i]).Within(1), "World cell " + i);
        }

        [Test]
        public void TheSharedMaterialSplitsCloseUpCells()
        {
            var shared = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/HandDrawnSprite.mat");
            Assert.That(shared.GetFloat("_MaxRefine"), Is.GreaterThanOrEqualTo(3), "The shop counter and held items need the split.");
            Assert.That(shared.GetFloat("_MaxCellPixels"), Is.InRange(1.5f, 4f));
        }

        [Test]
        public void SceneFogCoversSprites()
        {
            var renderer = Drawing();
            bool fog = RenderSettings.fog; var mode = RenderSettings.fogMode;
            var colour = RenderSettings.fogColor; float density = RenderSettings.fogDensity;
            try
            {
                RenderSettings.fog = false;
                var clear = RenderThroughCamera(renderer, perspective: true);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = .3f;
                RenderSettings.fogColor = Color.blue;
                var fogged = RenderThroughCamera(renderer, perspective: true);
                var drawn = Enumerable.Range(0, clear.Length).Where(i => clear[i].a > 0).ToArray();
                Assert.That(drawn.Length, Is.GreaterThan(1000));
                // About 4.3 m away at density 0.3: the drawing must be mostly fog colour, as the world is.
                Assert.That(drawn.Average(i => fogged[i].b - clear[i].b), Is.GreaterThan(100));
                Assert.That(drawn.Average(i => clear[i].r - fogged[i].r), Is.GreaterThan(50));
            }
            finally
            {
                RenderSettings.fog = fog; RenderSettings.fogMode = mode;
                RenderSettings.fogColor = colour; RenderSettings.fogDensity = density;
            }
        }

        [Test]
        public void BothShaderPassesRenderTheSameGrid()
        {
            var renderer = Drawing();
            Assert.That(Render(renderer, 1), Is.EqualTo(Render(renderer, 0)));
        }

        [Test]
        public void NpcsCreaturesLootAndMaterialVariantsShareOneEditableDensity()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Bee swarm.prefab");
            var creature = prefab.GetComponent<WorldCreature>();
            var shared = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/HandDrawnSprite.mat");
            // Calibration is recorded separately; later creature-size or density tuning stays independent.
            Assert.That(shared.GetFloat("_PixelsPerUnit"), Is.GreaterThan(0));
            Assert.That(creature.view.spriteRenderer.sharedMaterial, Is.SameAs(shared));
            Assert.That(shared.HasProperty("_PixelSize"), Is.False);
            foreach (var path in new[] { "Assets/Character/Prefabs/SpriteNpc.prefab", "Assets/Loot/Prefabs/Loot sprite.prefab" })
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<SpriteRenderer>(true)
                    .Any(r => r.sharedMaterial == shared), Is.True, path);
            foreach (var path in new[] { "Assets/Rendering/Materials/HandDrawnSprite Bubble.mat", "Assets/Content/GoblinTrader/SuspensionRope.mat" })
            {
                var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.That(variant.parent, Is.SameAs(shared));
                Assert.That(variant.IsPropertyOverriden("_PixelsPerUnit"), Is.False, path);
            }
        }

        [Test]
        public void TheGoblinTraderAloneDrawsOnATwiceFinerGrid()
        {
            // User-approved exception: the small trader goblin keeps detail on its own grid.
            var shared = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/HandDrawnSprite.mat");
            var goblin = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/HandDrawnSprite Goblin.mat");
            Assert.That(goblin.parent, Is.SameAs(shared));
            Assert.That(goblin.IsPropertyOverriden("_PixelsPerUnit"), Is.True);
            Assert.That(goblin.GetFloat("_PixelsPerUnit"), Is.EqualTo(shared.GetFloat("_PixelsPerUnit") * 2).Within(1e-4f));
            foreach (var path in new[] { "Assets/Tutorial/Prefabs/CounterGoblin.prefab", "Assets/ShopBlockout/Prefabs/GoblinCeilingRig.prefab" })
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<SpriteRenderer>(true)
                    .Any(r => r.sharedMaterial == goblin && r.sprite && r.sprite.texture.name.Contains("Goblin")), Is.True, path);
        }
    }
}
