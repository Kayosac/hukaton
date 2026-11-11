using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>One animated element of the teaser. The timeline calls it every frame with the teaser time in seconds
    /// (negative during the pre-roll), so the result depends only on the frame number.</summary>
    public abstract class TeaserTrack : MonoBehaviour
    {
        public abstract void Evaluate(float seconds);
    }
}
