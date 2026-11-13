using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A scene object drawn together with its neighbours as one merged mesh (the world's chunks merge their still
    /// dressing by material, so a chunk is a dozen draws, not hundreds). Before anything changes how it looks — fire
    /// chars it, a card hides it — it is <see cref="Unmerge"/>d: cut out of the merged mesh and drawn by its own
    /// renderers from then on.
    /// </summary>
    public interface IMergedPart
    {
        void Unmerge();
    }

    public static class MergedLooks
    {
        /// <summary>Call before changing a scene object's renderers, scale or visibility: it is drawn by its own renderers from now on.</summary>
        public static void Unmerge(GameObject sceneObject)
        {
            if (!sceneObject) return;
            foreach (var part in sceneObject.GetComponentsInParent<IMergedPart>(true)) part.Unmerge();
            foreach (var part in sceneObject.GetComponentsInChildren<IMergedPart>(true)) part.Unmerge();
        }
    }
}
