using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hakaton.DialogueUI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Hakaton.Trading.Tests
{
    public sealed class AxisTiledImageTests
    {
        readonly List<Object> created = new List<Object>();
        Sprite sprite;

        [SetUp]
        public void CreateEdgeSprite()
        {
            var texture = Keep(new Texture2D(128, 64));
            texture.wrapMode = TextureWrapMode.Clamp;
            // A subrect also verifies that trailing tiles stay within their sprite's UVs.
            sprite = Keep(Sprite.Create(texture, new Rect(16, 8, 64, 32), Vector2.one * .5f,
                100, 0, SpriteMeshType.FullRect));
        }

        [TearDown]
        public void Cleanup()
        {
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i]) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [TestCase(AxisTiledImage.TilingAxis.Horizontal)]
        [TestCase(AxisTiledImage.TilingAxis.Vertical)]
        public void ExtraCrossAxisFractionDoesNotRepeatTheOutline(AxisTiledImage.TilingAxis axis)
        {
            // Pixel adjustment can increase the apparent thickness beyond the sprite's
            // native thickness. Stock Tiled then emits an unwanted second row/column.
            var size = axis == AxisTiledImage.TilingAxis.Horizontal
                ? new Vector2(150, 32.25f) : new Vector2(64.25f, 75);
            var stock = MakeImage<Image>(size);
            using (var oldMesh = Populate(stock))
                Assert.That(oldMesh.currentVertCount, Is.EqualTo(24), "Reproduces the stock two-axis seam.");

            var image = MakeImage<AxisTiledImage>(size);
            image.Axis = axis;
            image.color = new Color(.2f, .4f, .6f, .8f);
            using (var mesh = Populate(image))
            {
                Assert.That(mesh.currentVertCount, Is.EqualTo(12), "Only three tiles along the edge.");
                CheckQuads(image, mesh);
                var last = Vertex(mesh, mesh.currentVertCount - 2);
                var uv = DataUtility.GetOuterUV(sprite);
                float expected = axis == AxisTiledImage.TilingAxis.Horizontal
                    ? Mathf.Lerp(uv.x, uv.z, 22f / 64) : Mathf.Lerp(uv.y, uv.w, 11f / 32);
                Assert.That(axis == AxisTiledImage.TilingAxis.Horizontal ? last.uv0.x : last.uv0.y,
                    Is.EqualTo(expected).Within(.00001f), "The last tile clips instead of stretching the repeated axis.");
            }
        }

        [TestCase(AxisTiledImage.TilingAxis.Horizontal, .75f, false)]
        [TestCase(AxisTiledImage.TilingAxis.Horizontal, 1f, false)]
        [TestCase(AxisTiledImage.TilingAxis.Horizontal, 1.25f, false)]
        [TestCase(AxisTiledImage.TilingAxis.Horizontal, 2f, false)]
        [TestCase(AxisTiledImage.TilingAxis.Vertical, .75f, false)]
        [TestCase(AxisTiledImage.TilingAxis.Vertical, 1f, false)]
        [TestCase(AxisTiledImage.TilingAxis.Vertical, 1.25f, false)]
        [TestCase(AxisTiledImage.TilingAxis.Vertical, 2f, false)]
        [TestCase(AxisTiledImage.TilingAxis.Horizontal, .75f, true)]
        [TestCase(AxisTiledImage.TilingAxis.Horizontal, 1.125f, true)]
        [TestCase(AxisTiledImage.TilingAxis.Horizontal, 1.25f, true)]
        [TestCase(AxisTiledImage.TilingAxis.Horizontal, 2f, true)]
        [TestCase(AxisTiledImage.TilingAxis.Vertical, .75f, true)]
        [TestCase(AxisTiledImage.TilingAxis.Vertical, 1.125f, true)]
        [TestCase(AxisTiledImage.TilingAxis.Vertical, 1.25f, true)]
        [TestCase(AxisTiledImage.TilingAxis.Vertical, 2f, true)]
        public void PixelPerfectCanvasKeepsOneFullThickness(AxisTiledImage.TilingAxis axis, float scale, bool scaleParent)
        {
            var canvasObject = Keep(new GameObject("Pixel rounding regression", typeof(RectTransform), typeof(Canvas)));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.scaleFactor = scaleParent ? 1 : scale;
            var parent = Keep(new GameObject("Reference UI scale", typeof(RectTransform))).GetComponent<RectTransform>();
            parent.SetParent(canvas.transform, false);
            // Production ReferenceUiScale keeps the canvas at one and scales a
            // shared parent, so cover that path separately from CanvasScaler.
            parent.localScale = Vector3.one * (scaleParent ? scale : 1);
            var image = MakeImage<AxisTiledImage>(axis == AxisTiledImage.TilingAxis.Horizontal
                ? new Vector2(150.3f, 32) : new Vector2(64, 75.3f));
            image.Axis = axis;
            image.transform.SetParent(parent, false);
            image.rectTransform.anchoredPosition = new Vector2(.3f, .7f);
            Canvas.ForceUpdateCanvases();
            using (var mesh = Populate(image)) CheckQuads(image, mesh);
        }

        [TestCase(AxisTiledImage.TilingAxis.Horizontal)]
        [TestCase(AxisTiledImage.TilingAxis.Vertical)]
        public void SmallerThanOneTileClipsAndEmptyRectClears(AxisTiledImage.TilingAxis axis)
        {
            var image = MakeImage<AxisTiledImage>(new Vector2(.25f, .25f));
            image.Axis = axis;
            using (var mesh = Populate(image))
            {
                Assert.That(mesh.currentVertCount, Is.EqualTo(4));
                CheckQuads(image, mesh);
            }
            image.rectTransform.sizeDelta = Vector2.zero;
            using (var mesh = Populate(image)) Assert.That(mesh.currentVertCount, Is.Zero);
            image.rectTransform.sizeDelta = new Vector2(150, 32);
            image.fillCenter = false;
            using (var mesh = Populate(image)) Assert.That(mesh.currentVertCount, Is.Zero);
        }

        [TestCase("Assets/Content/UI/Dialogue/Prefabs/StitchedLinenButton.prefab")]
        [TestCase("Assets/Content/UI/Dialogue/Prefabs/StitchedLinenWindow.prefab")]
        [TestCase("Assets/Game/Prefabs/UI/Outing choice.prefab")]
        public void SavedLinenPrefabsUseTheCorrectAxisOnEveryEdge(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var edges = prefab.GetComponentsInChildren<Image>(true).Where(image => image.sprite &&
                (image.sprite.name == "TopEdge" || image.sprite.name == "BottomEdge" ||
                 image.sprite.name == "LeftEdge" || image.sprite.name == "RightEdge")).ToArray();
            Assert.That(edges, Is.Not.Empty);
            foreach (var edge in edges)
            {
                Assert.That(edge, Is.InstanceOf<AxisTiledImage>(), edge.name + " must not repeat across its thickness.");
                var expected = edge.sprite.name == "TopEdge" || edge.sprite.name == "BottomEdge"
                    ? AxisTiledImage.TilingAxis.Horizontal : AxisTiledImage.TilingAxis.Vertical;
                Assert.That(((AxisTiledImage)edge).Axis, Is.EqualTo(expected), edge.name);
                Assert.That(edge.type, Is.EqualTo(Image.Type.Tiled), edge.name);
            }
        }

        T Keep<T>(T value) where T : Object { created.Add(value); return value; }

        T MakeImage<T>(Vector2 size) where T : Image
        {
            var go = Keep(new GameObject("Edge", typeof(RectTransform), typeof(CanvasRenderer)));
            var image = go.AddComponent<T>();
            image.sprite = sprite;
            image.type = Image.Type.Tiled;
            image.rectTransform.sizeDelta = size;
            return image;
        }

        static VertexHelper Populate(Image image)
        {
            var mesh = new VertexHelper();
            image.GetType().GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(VertexHelper) }, null).Invoke(image, new object[] { mesh });
            return mesh;
        }

        static UIVertex Vertex(VertexHelper mesh, int index)
        {
            var vertex = new UIVertex();
            mesh.PopulateUIVertex(ref vertex, index);
            return vertex;
        }

        static void CheckQuads(AxisTiledImage image, VertexHelper mesh)
        {
            Assert.That(mesh.currentVertCount, Is.GreaterThan(0));
            var rect = image.GetPixelAdjustedRect();
            var uv = DataUtility.GetOuterUV(image.sprite);
            bool horizontal = image.Axis == AxisTiledImage.TilingAxis.Horizontal;
            float previousEnd = horizontal ? rect.xMin : rect.yMin;
            for (int i = 0; i < mesh.currentVertCount; i += 4)
            {
                var lower = Vertex(mesh, i);
                var upper = Vertex(mesh, i + 2);
                Assert.That(horizontal ? lower.position.x : lower.position.y, Is.EqualTo(previousEnd).Within(.0001f));
                previousEnd = horizontal ? upper.position.x : upper.position.y;
                Assert.That(horizontal ? lower.position.y : lower.position.x,
                    Is.EqualTo(horizontal ? rect.yMin : rect.xMin).Within(.0001f));
                Assert.That(horizontal ? upper.position.y : upper.position.x,
                    Is.EqualTo(horizontal ? rect.yMax : rect.xMax).Within(.0001f));
                Assert.That(horizontal ? lower.uv0.y : lower.uv0.x,
                    Is.EqualTo(horizontal ? uv.y : uv.x).Within(.00001f));
                Assert.That(horizontal ? upper.uv0.y : upper.uv0.x,
                    Is.EqualTo(horizontal ? uv.w : uv.z).Within(.00001f));
                Assert.That(lower.color, Is.EqualTo((Color32)image.color));
            }
            Assert.That(previousEnd, Is.EqualTo(horizontal ? rect.xMax : rect.yMax).Within(.0001f), "No missing final strip.");
        }
    }
}
