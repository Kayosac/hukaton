using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Purely cosmetic card: discarded hands fluttering away, a card stuck in a target before it burns,
    /// the Return card flung skyward. No colliders, so it can never intercept a real throw.
    /// </summary>
    public sealed class CardDebris : MonoBehaviour
    {
        public CardView view;
        [Min(0)] public float gravity = 6;
        [Min(0)] public float drag = 1.5f;
        Vector3 velocity, angularVelocity;
        float age, lifetime, burnAt;
        bool stuck, burning;
        Transform anchor;
        Vector3 localPosition;
        Quaternion localRotation;
        SpringFloat wobble;
        Color burnColour;
        VfxBurst burnFx;
        ExpeditionWorld world;

        /// <summary>Free-flying cosmetic card.</summary>
        public void Fling(ExpeditionWorld owner, Vector3 initialVelocity, Vector3 spinDegrees, float seconds)
        {
            world = owner;
            velocity = initialVelocity;
            angularVelocity = spinDegrees;
            lifetime = seconds;
            stuck = false;
        }

        /// <summary>A card embedded in whatever it hit: quivers, then burns away in the card's colour.</summary>
        public void Stick(ExpeditionWorld owner, Transform target, float quiver, float burnDelay, Color colour, VfxBurst burn)
        {
            world = owner;
            stuck = true;
            anchor = target;
            if (anchor)
            {
                localPosition = anchor.InverseTransformPoint(transform.position);
                localRotation = Quaternion.Inverse(anchor.rotation) * transform.rotation;
            }
            wobble.Snap(0);
            wobble.Kick(quiver);
            burnAt = burnDelay;
            lifetime = burnDelay + .25f;
            burnColour = colour;
            burnFx = burn;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (stuck)
            {
                if (anchor && anchor.gameObject.activeInHierarchy)
                    transform.SetPositionAndRotation(anchor.TransformPoint(localPosition), anchor.rotation * localRotation);
                else if (anchor) { lifetime = Mathf.Min(lifetime, age + .05f); }
                wobble.Step(0, 9, .18f, dt);
                view.transform.localRotation = Quaternion.Euler(wobble.Value * 25, 0, 0);
                if (!burning && age >= burnAt) Burn();
                if (burning) view.SetFlash(Mathf.Clamp01((age - burnAt) / .2f), burnColour);
            }
            else
            {
                velocity += Vector3.down * gravity * dt;
                velocity *= 1f / (1f + drag * dt);
                transform.position += velocity * dt;
                transform.rotation = Quaternion.Euler(angularVelocity * dt) * transform.rotation;
                float fade = Mathf.Clamp01((lifetime - age) / .3f);
                view.SetAlpha(fade);
                transform.localScale = Vector3.one * Mathf.Lerp(.6f, 1, fade);
            }
            if (age >= lifetime) Destroy(gameObject);
        }

        void Burn()
        {
            burning = true;
            if (burnFx && world) world.Spawn(burnFx, transform.position, transform.rotation, burnColour, .7f);
        }
    }
}
