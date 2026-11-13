using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// The root of the god's mountains scene: what the game needs from the place when it loads it next to its own scene
    /// (<see cref="GodMountainsVisit"/>). Opened alone in the editor, the scene switches on its own test player and speech
    /// plate (<see cref="standalone"/>), so Play walks the whole level as before. In the game they stay off and the
    /// game's player and plate are handed to the encounter instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodMountainsLevel : MonoBehaviour
    {
        /// <summary>Set by the game while it loads and runs the level: the scene's own test rig stays off.</summary>
        public static bool Hosted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Hosted = false;

        public GodEncounter encounter;
        public GodMountainsAtmosphere atmosphere;
        [Tooltip("Where the player wakes up (Z0, facing the wall).")]
        public Transform spawn;
        [Tooltip("The camera's far plane while the player is here, m: the god and the far ranges must be seen.")]
        [Min(100)] public float farClip = 12000;
        [Tooltip("Played alone (the scene opened by itself) these are switched on: the test player and the god's own speech " +
                 "plate. Saved switched off; in the game they stay off.")]
        public GameObject[] standalone = new GameObject[0];

        void Awake()
        {
            if (Hosted) return;
            foreach (var o in standalone) if (o) o.SetActive(true);
        }
    }
}
