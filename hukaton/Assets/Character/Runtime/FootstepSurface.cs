using UnityEngine;

namespace Hakaton.Character
{
    /// <summary>
    /// Gives a walkable collider its own footsteps (the creaky boards of the shop). FirstPersonAudio uses it for the
    /// ground the player stands on, ahead of the material-name wood check.
    /// </summary>
    public sealed class FootstepSurface : MonoBehaviour
    {
        [Tooltip("Picked at random, never the same clip twice in a row, so the creaks do not form a pattern.")]
        public AudioClip[] clips;
        [Range(0, 2)] public float volume = 1;
        int last = -1;

        public AudioClip NextClip()
        {
            if (clips == null || clips.Length == 0) return null;
            int i = 0;
            if (clips.Length > 1)
            {
                i = Random.Range(0, last < 0 ? clips.Length : clips.Length - 1);
                if (last >= 0 && i >= last) i++;
            }
            last = i;
            return clips[i];
        }
    }
}
