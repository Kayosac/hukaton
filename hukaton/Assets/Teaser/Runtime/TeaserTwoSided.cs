using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>
    /// Keeps a flat, double-sided drawing (a world-space logo) readable from behind: when its back faces the camera
    /// the drawing is mirrored, so a full turn shows the text the right way round on both sides. Put it after the
    /// tracks that rotate the drawing.
    /// </summary>
    public sealed class TeaserTwoSided : TeaserTrack
    {
        public Transform drawing;
        public Camera viewCamera;

        public override void Evaluate(float seconds)
        {
            if (!drawing || !viewCamera) return;
            Vector3 toDrawing = drawing.position - viewCamera.transform.position;
            float side = Vector3.Dot(drawing.forward, toDrawing) >= 0 ? 1 : -1;
            var scale = drawing.localScale;
            if (Mathf.Sign(scale.x) != side)
            {
                scale.x = Mathf.Abs(scale.x) * side;
                drawing.localScale = scale;
            }
        }
    }
}
