using Hakaton.Feel;
using Hakaton.ShopBlockout;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// The plain, cheeky goblin standing on the counter during the tutorial: a hand-drawn sprite turned to the
    /// player, a little bounce on every line, and a poof when he leaves for «дела поважнее».
    /// </summary>
    public sealed class CounterGoblin : MonoBehaviour
    {
        [Tooltip("The hand-drawn goblin (the rig's sprite component: idle, and the startled drawing as «pleased» for loud lines).")]
        public SuspendedGoblinSprite face;
        [Tooltip("Player camera, assigned by the scene.")]
        public Transform viewer;
        public VfxBurst poofVfx;
        public AudioSource audioSource;
        public AudioClip[] chatter;
        public AudioClip poofSound;

        SpringFloat bounce;
        Vector3 rest;

        void Awake() => rest = face.transform.localScale;

        public void Show(bool visible) => gameObject.SetActive(visible);

        /// <summary>A line: a hop, a mutter, a short startled face on the loud ones.</summary>
        public void Speak(bool excited)
        {
            bounce.Kick(excited ? 9 : 5);
            if (excited) face.Please();
            if (audioSource && chatter != null && chatter.Length > 0)
                audioSource.PlayOneShot(chatter[Random.Range(0, chatter.Length)], .55f);
        }

        /// <summary>Gone in a puff of smoke.</summary>
        public void Poof()
        {
            VfxBurst.Spawn(poofVfx, face.transform.position, Quaternion.identity, null, new Color(.7f, .9f, .5f));
            if (audioSource && poofSound) AudioSource.PlayClipAtPoint(poofSound, face.transform.position, .8f);
            gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (viewer)
            {
                Vector3 away = transform.position - viewer.position;
                away.y = 0;
                if (away.sqrMagnitude > .0001f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
            bounce.Step(0, 12, .35f, Time.unscaledDeltaTime);
            float squash = bounce.Value * .02f;
            face.transform.localScale = new Vector3(rest.x * (1 - squash), rest.y * (1 + squash), rest.z);
        }
    }
}
