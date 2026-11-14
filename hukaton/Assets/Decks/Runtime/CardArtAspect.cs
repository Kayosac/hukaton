using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Decks.Unity
{
    /// <summary>
    /// "Preserve Aspect" for card art by the card's own 2:3 shape instead of the sprite's pixels. Card art is stored
    /// stretched to a power-of-two texture (1024×2048) so it can be BC7-compressed with mipmaps; the stored aspect no
    /// longer says how the card looks. Same fit as <see cref="Image.preserveAspect"/>: the largest 2:3 rect inside,
    /// placed by the pivot. Use with the Image's own Preserve Aspect off.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Image))]
    public sealed class CardArtAspect : UnityEngine.UI.BaseMeshEffect
    {
        /// <summary>Width over height of every card face.</summary>
        public const float Aspect = 2f / 3f;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var rt = (RectTransform)transform;
            var r = rt.rect;
            if (r.width <= 0 || r.height <= 0) return;
            var fit = r;
            if (Aspect > r.width / r.height)
            {
                fit.height = r.width / Aspect;
                fit.y += (r.height - fit.height) * rt.pivot.y;
            }
            else
            {
                fit.width = r.height * Aspect;
                fit.x += (r.width - fit.width) * rt.pivot.x;
            }
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var p = v.position;
                p.x = fit.x + (p.x - r.x) / r.width * fit.width;
                p.y = fit.y + (p.y - r.y) / r.height * fit.height;
                v.position = p;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
