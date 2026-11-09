using UnityEngine;

namespace Hukaton.Rendering
{
    /// <summary>Identifies the presentation camera for the upscaled world, native artwork and UI.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldResolutionOutput : MonoBehaviour
    {
        [System.NonSerialized] public WorldResolution Owner;
    }
}
