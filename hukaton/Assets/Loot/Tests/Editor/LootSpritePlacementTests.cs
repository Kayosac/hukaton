using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hakaton.Loot.Tests
{
    public sealed class LootSpritePlacementTests
    {
        const string CataloguePath = "Assets/Loot/Loot catalog.asset";
        const string DisplayCasePath = "Assets/ShopBlockout/Prefabs/DisplayCase.prefab";
        const byte VisibleAlpha = 128;

        static LootCatalog Catalogue()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<LootCatalog>(CataloguePath);
            Assert.That(catalogue, Is.Not.Null);
            return catalogue;
        }

        static LootItem[] SpriteItems(LootCatalog catalogue)
        {
            var items = catalogue.items.Where(item => item && item.icon && !item.ShelfModel).ToArray();
            Assert.That(items, Is.Not.Empty, "The catalogue should exercise real sprite-only merchandise.");
            return items;
        }

        [Test]
        public void CatalogueSpritePivotsSitAtTheVisibleBottomOfTheirArtwork()
        {
            foreach (var item in SpriteItems(Catalogue()))
            {
                float bottom = VisibleBottom(item.icon);
                Assert.That(item.icon.pivot.y, Is.EqualTo(bottom).Within(.01f),
                    $"{item.id}: the pivot must follow the visible bottom, not the transparent canvas edge.");
            }
        }

        [Test]
        public void SpriteViewsRestOnTheirOriginWithoutChangingTheConfiguredCanvasHeight()
        {
            var catalogue = Catalogue();
            Assert.That(catalogue.spriteView, Is.Not.Null);
            var view = Object.Instantiate(catalogue.spriteView);
            try
            {
                view.faceCamera = false;
                view.transform.SetPositionAndRotation(new Vector3(2, 1.7f, -3), Quaternion.Euler(0, 37, 0));
                foreach (var item in SpriteItems(catalogue))
                {
                    view.Show(item.icon, item.iconHeight);
                    var drawing = view.spriteRenderer.transform;
                    float bottom = (VisibleBottom(item.icon) - item.icon.pivot.y) / item.icon.pixelsPerUnit;
                    float bottomWorldY = drawing.TransformPoint(new Vector3(0, bottom, 0)).y;
                    Assert.That(bottomWorldY, Is.EqualTo(view.transform.position.y).Within(.0001f),
                        $"{item.id}: visible artwork floats above or sinks below the placement origin.");
                    Assert.That(view.spriteRenderer.bounds.size.y, Is.EqualTo(item.iconHeight).Within(.0001f),
                        $"{item.id}: correcting the ground pivot must preserve the existing canvas height.");
                }
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void AllSixDisplayAnchorsRestOnTheirOwnSlotPads()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DisplayCasePath);
            Assert.That(prefab, Is.Not.Null);
            var display = Object.Instantiate(prefab);
            try
            {
                var anchors = display.GetComponentsInChildren<Transform>(true)
                    .Where(child => child.name == "Item visual").ToArray();
                Assert.That(anchors.Length, Is.EqualTo(6));
                foreach (var anchor in anchors)
                {
                    var pad = anchor.parent.Find("Slot pad");
                    Assert.That(pad, Is.Not.Null, anchor.parent.name);
                    var renderer = pad.GetComponent<Renderer>();
                    Assert.That(renderer, Is.Not.Null, anchor.parent.name);
                    Assert.That(anchor.position.y, Is.EqualTo(renderer.bounds.max.y).Within(.001f),
                        $"{anchor.parent.name}: merchandise must rest on this pad, not above it.");
                }
            }
            finally { Object.DestroyImmediate(display); }
        }

        [Test]
        public void ShelfModelsUseTheSameGroundOriginAsSpriteMerchandise()
        {
            var catalogue = Catalogue();
            var parent = new GameObject("Shelf model test anchor");
            try
            {
                parent.transform.position = new Vector3(2, 1.7f, -3);
                foreach (var item in catalogue.items.Where(item => item && item.shelfModel))
                {
                    var visual = catalogue.CreateVisual(item, parent.transform, true);
                    Assert.That(visual, Is.Not.Null, item.id);
                    try
                    {
                        var renderers = visual.GetComponentsInChildren<Renderer>()
                            .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
                        Assert.That(renderers, Is.Not.Empty, item.id);
                        float bottom = renderers.Min(renderer => renderer.bounds.min.y);
                        Assert.That(bottom, Is.EqualTo(parent.transform.position.y).Within(.001f),
                            $"{item.id}: model and sprite merchandise must share a surface-based placement origin.");
                    }
                    finally { Object.DestroyImmediate(visual); }
                }
            }
            finally { Object.DestroyImmediate(parent); }
        }

        // Read the source PNG into a temporary texture: production textures stay non-readable,
        // and compression or faint alpha noise cannot redefine the artwork's authored support line.
        static float VisibleBottom(Sprite sprite)
        {
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(ImageConversion.LoadImage(decoded, File.ReadAllBytes(path)), Is.True, path);
                var pixels = decoded.GetPixels32();
                float sourcePerPixelX = (float)decoded.width / sprite.texture.width;
                float sourcePerPixelY = (float)decoded.height / sprite.texture.height;
                Rect rect = sprite.rect;
                int xMin = Mathf.RoundToInt(rect.xMin * sourcePerPixelX);
                int xMax = Mathf.RoundToInt(rect.xMax * sourcePerPixelX);
                int yMin = Mathf.RoundToInt(rect.yMin * sourcePerPixelY);
                int yMax = Mathf.RoundToInt(rect.yMax * sourcePerPixelY);
                for (int y = yMin; y < yMax; y++)
                    for (int x = xMin; x < xMax; x++)
                        if (pixels[y * decoded.width + x].a >= VisibleAlpha)
                            return y / sourcePerPixelY - rect.yMin;
                Assert.Fail($"{path}: no artwork reaches the shader's visible alpha threshold.");
                return float.NaN;
            }
            finally { Object.DestroyImmediate(decoded); }
        }
    }
}
