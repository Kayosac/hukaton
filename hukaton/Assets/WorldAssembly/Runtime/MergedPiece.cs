using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>A piece of a chunk's <see cref="MergedLook"/> that can change (it burns, it wakes): its sources there.</summary>
    public sealed class MergedPiece : MonoBehaviour, IMergedPart
    {
        public MergedLook look;
        public int[] sources = new int[0];

        public void Unmerge()
        {
            if (!look) return;
            foreach (int i in sources) look.Unmerge(i);
        }
    }
}
