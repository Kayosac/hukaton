using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// The cave prologue's level: everything that is the place itself — rock, dressing, light, the roots, the water,
    /// the bush with the goblin, the start and hand-over markers, the shores. It lives in its own scene
    /// (TutorialCave.unity) with its own lighting settings; <see cref="CaveTutorial"/> loads it next to the game scene
    /// for the prologue and unloads it on the way to the shop. Open that scene alone to work on the cave's look.
    /// </summary>
    public sealed class CaveLevel : MonoBehaviour
    {
        [Header("Markers")]
        public Transform start;
        public Transform rootsZone, waterZone, bushZone;
        [Tooltip("Safe spots on both banks: a fall into the water returns the player to the nearest one.")]
        public Transform[] shores = new Transform[0];

        [Header("Lessons")]
        public WorldObstacle rootsObstacle;
        public WorldObstacle waterObstacle;
        [Tooltip("The bush: any card landing on it shakes it; only Strike brings the goblin out.")]
        public Transform bush;
        [Tooltip("The plain goblin hidden in the bush.")]
        public CounterGoblin goblin;
    }
}
