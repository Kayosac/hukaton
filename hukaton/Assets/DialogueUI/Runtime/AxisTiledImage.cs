using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Hakaton.DialogueUI
{
    /// <summary>
    /// Repeats a frame edge along its length and stretches its full thickness once.
    /// Image's ordinary Tiled mode repeats both axes: pixel rounding can make a
    /// 15-pixel edge 16 pixels tall and expose a second row of its dark outline.
    /// </summary>
    [AddComponentMenu("UI/Axis Tiled Image")]
    public sealed class AxisTiledImage : Image
    {
        public enum TilingAxis { Horizontal, Vertical }

        [SerializeField] TilingAxis axis;

        public TilingAxis Axis
        {
            get => axis;
            set
            {
                if (axis == value) return;
                axis = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            // overrideSprite returns the active sprite, including the normal sprite.
            var source = overrideSprite;
            if (type != Type.Tiled || !source || source.border != Vector4.zero)
            {
                base.OnPopulateMesh(vertices);
                return;
            }

            vertices.Clear();
            var rect = GetPixelAdjustedRect();
            if (!fillCenter || rect.width <= 0 || rect.height <= 0) return;

            bool horizontal = axis == TilingAxis.Horizontal;
            float length = horizontal ? rect.width : rect.height;
            float tileLength = (horizontal ? source.rect.width : source.rect.height) / multipliedPixelsPerUnit;
            if (!(tileLength > 0) || float.IsInfinity(tileLength) || float.IsNaN(tileLength)) tileLength = length;

            // Respect uGUI's 16-bit mesh budget, as the stock tiled Image does.
            const int maxTiles = 16383;
            if (length / tileLength > maxTiles) tileLength = length / maxTiles;
            int count = Mathf.Min(maxTiles, Mathf.CeilToInt(length / tileLength));
            var uv = DataUtility.GetOuterUV(source);

            for (int i = 0; i < count; i++)
            {
                float start = i * tileLength;
                float end = Mathf.Min(length, (i + 1) * tileLength);
                if (end <= start) break;
                float fraction = Mathf.Clamp01((end - start) / tileLength);
                var lower = horizontal ? new Vector2(rect.xMin + start, rect.yMin) : new Vector2(rect.xMin, rect.yMin + start);
                var upper = horizontal ? new Vector2(rect.xMin + end, rect.yMax) : new Vector2(rect.xMax, rect.yMin + end);
                var uvUpper = horizontal ? new Vector2(Mathf.Lerp(uv.x, uv.z, fraction), uv.w) : new Vector2(uv.z, Mathf.Lerp(uv.y, uv.w, fraction));

                int first = vertices.currentVertCount;
                vertices.AddVert(new Vector3(lower.x, lower.y), color, new Vector2(uv.x, uv.y));
                vertices.AddVert(new Vector3(lower.x, upper.y), color, new Vector2(uv.x, uvUpper.y));
                vertices.AddVert(new Vector3(upper.x, upper.y), color, uvUpper);
                vertices.AddVert(new Vector3(upper.x, lower.y), color, new Vector2(uvUpper.x, uv.y));
                vertices.AddTriangle(first, first + 1, first + 2);
                vertices.AddTriangle(first + 2, first + 3, first);
            }
        }
    }
}
