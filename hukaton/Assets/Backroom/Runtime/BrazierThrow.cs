using System;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Backroom
{
    /// <summary>
    /// A card thrown from the brazier's list into its fire, in the world. It takes over from the list's card
    /// seamlessly: same place on screen, same size, same shade, still for the moment the list fades over it; then it
    /// flies a real ballistic arc (gravity only) into the bowl, the end-over-end tumble easing in, warming to full
    /// colour as it goes, lands with a little settle, catches — edges glow, the paper chars black and curls — and is gone.
    /// </summary>
    public sealed class BrazierThrow : MonoBehaviour
    {
        const float Gravity = 9.81f;

        CardView view;
        Vector3 start, velocity, target, spinAxis, cardSize;
        Quaternion startRotation, restRotation;
        float age, flightTime, spinSpeed, burnAge = -1;
        Action landed;
        float hold;
        Color startTint = Color.white;
        const float SpinEaseIn = .3f;
        VfxBurst burnFx;
        Color burnColour;

        public static readonly Color Ember = new Color(1, .45f, .12f);

        /// <summary>
        /// Throws a world card from a point of the screen (pixels) into <paramref name="bowl"/>. <paramref name="cardPrefab"/>
        /// is the expedition's card debris (a two-sided card quad); only its <see cref="CardView"/> is used.
        /// </summary>
        public static BrazierThrow Launch(GameObject cardPrefab, Texture front, Texture back, int level, Camera camera,
            Vector2 screen, float screenWidth, Vector3 bowl, VfxBurst burnFx, Action landed, float hold = 0, Color? startTint = null)
        {
            if (!cardPrefab || !camera)
            {
                Debug.LogWarning($"[Brazier] cannot throw: card prefab {(cardPrefab ? "ok" : "missing")}, camera {(camera ? "ok" : "missing")}");
                return null;
            }
            var go = Instantiate(cardPrefab);
            go.name = "Card into the brazier";
            // The debris prefab's own flight must not run: this throw drives it.
            foreach (var behaviour in go.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
            var view = go.GetComponentInChildren<CardView>(true);
            if (!view) { Debug.LogWarning("[Brazier] cannot throw: no CardView in the card prefab"); Destroy(go); return null; }
            view.Show(front, back, level);
            var tint = startTint ?? Color.white;
            view.SetTint(tint);

            // Start where the card is on screen, as big as it looks there.
            const float distance = .62f;
            var ray = camera.ScreenPointToRay(screen);
            float worldPerPixel = 2 * distance * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) / camera.pixelHeight;
            float width = Mathf.Max(.02f, screenWidth * worldPerPixel);

            var t = go.AddComponent<BrazierThrow>();
            t.view = view;
            t.cardSize = new Vector3(width, width * 1.5f, 1);
            view.transform.localScale = t.cardSize;
            t.start = ray.GetPoint(distance);
            t.startRotation = camera.transform.rotation;
            go.transform.SetPositionAndRotation(t.start, t.startRotation);
            t.target = bowl;
            t.burnFx = burnFx;
            t.burnColour = Ember;
            t.landed = landed;
            t.hold = Mathf.Max(0, hold);
            t.startTint = tint;

            // A real throw: the time comes from the distance, the launch velocity from the arc that lands in the bowl.
            float span = Vector3.Distance(t.start, bowl);
            t.flightTime = Mathf.Clamp(.36f + span * .22f, .45f, .75f);
            t.velocity = (bowl - t.start) / t.flightTime + .5f * Gravity * t.flightTime * Vector3.up;
            // End over end around the card's width, with a little wobble off the axis.
            t.spinAxis = (camera.transform.right + UnityEngine.Random.insideUnitSphere * .25f).normalized;
            t.spinSpeed = UnityEngine.Random.Range(560f, 760f) * (UnityEngine.Random.value < .5f ? -1 : 1);
            // Where it comes to rest: lying back in the bowl, at an odd angle.
            t.restRotation = Quaternion.Euler(-70 + UnityEngine.Random.Range(-12f, 12f), camera.transform.eulerAngles.y + UnityEngine.Random.Range(-35f, 35f), UnityEngine.Random.Range(-15f, 15f));
            return t;
        }

        /// <summary>When the card reaches the bowl after launch, seconds (the hold included).</summary>
        public float FlightTime => hold + flightTime;

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (burnAge < 0)
            {
                // Still in the list's place while the list fades over it.
                if (age < hold) return;
                float t = Mathf.Min(age - hold, flightTime);
                transform.position = start + velocity * t + .5f * Gravity * t * t * Vector3.down;
                // The tumble eases in, so the card leaves its place cleanly instead of snapping round.
                float turned = t < SpinEaseIn ? .5f * t * t / SpinEaseIn : t - .5f * SpinEaseIn;
                transform.rotation = Quaternion.AngleAxis(spinSpeed * turned, spinAxis) * startRotation;
                view.SetTint(Color.Lerp(startTint, Color.white, Mathf.Clamp01(t / .25f)));
                // Heat on the way down: the face warms as it nears the fire.
                float near = Mathf.InverseLerp(flightTime * .6f, flightTime, t);
                view.SetGlow(near * .5f, Ember);
                if (t >= flightTime) Land();
                return;
            }

            burnAge += dt;
            // Settles into the bowl, then burns: glowing edges, charring, curling, gone.
            transform.rotation = Quaternion.Slerp(transform.rotation, restRotation, 1 - Mathf.Exp(-dt * 14));
            transform.position = target + Vector3.up * Mathf.Lerp(.02f, .06f, Mathf.Clamp01(burnAge / .9f));
            float catchUp = Mathf.Clamp01(burnAge / .18f);
            float char_ = Mathf.Clamp01((burnAge - .1f) / .55f);
            view.SetFlash(catchUp * (1 - char_ * .6f) * .85f, burnColour);
            view.SetGlow(1 - char_, Ember);
            view.SetTint(Color.Lerp(Color.white, new Color(.14f, .08f, .06f), char_));
            view.SetAlpha(1 - Mathf.Clamp01((burnAge - .45f) / .45f));
            float curl = Mathf.Clamp01((burnAge - .15f) / .7f);
            view.transform.localScale = new Vector3(cardSize.x * (1 - curl * .35f), cardSize.y * (1 - curl * .6f), 1);
            if (burnAge >= .95f) Destroy(gameObject);
        }

        void Land()
        {
            burnAge = 0;
            transform.position = target;
            if (burnFx) VfxBurst.Spawn(burnFx, target, Quaternion.identity, null, burnColour, .8f);
            landed?.Invoke();
        }
    }
}
