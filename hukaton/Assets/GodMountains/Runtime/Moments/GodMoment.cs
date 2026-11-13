using System.Collections;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// A one-off event on the walk to the god: fires once, when the player has first walked <see cref="atMetres"/> along
    /// the route (and its own condition holds). The level is loaded fresh for every visit, nothing needs resetting.
    /// </summary>
    [RequireComponent(typeof(GodMoments))]
    public abstract class GodMoment : MonoBehaviour
    {
        [Tooltip("Fires when the player has walked this far along the route, m.")]
        public float atMetres;

        public bool Fired { get; private set; }
        GodMoments hub;
        protected GodMoments Hub => hub ? hub : hub = GetComponent<GodMoments>();

        protected virtual void Awake() { }

        /// <summary>Where this moment happens, for the editor's shots: the view it is meant for and its key points.</summary>
        public virtual (Vector3 eye, Vector3 forward) PreviewView() => Hub.ViewAt(atMetres);

        /// <summary>The key points of the moment (where things fall, burst, drift), worked out the same way as in play.</summary>
        public virtual void PreviewPoints(System.Collections.Generic.List<Vector3> points) { }

        protected virtual bool Ready => true;

        void Update()
        {
            if (Fired || !Hub || !Hub.Running || Hub.encounter.Stage != GodStage.Path) return;
            if (Hub.Progress >= atMetres && Ready) Fire();
        }

        /// <summary>Plays the moment now (the route calls it; tests may call it directly).</summary>
        public void Fire()
        {
            if (Fired) return;
            Fired = true;
            StartCoroutine(Play());
        }

        protected abstract IEnumerator Play();
    }
}
