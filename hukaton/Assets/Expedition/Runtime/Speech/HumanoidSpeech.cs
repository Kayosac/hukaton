using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Opt-in humanoid speech. Only reads its assigned creature/world and presents the authored bubble.
    /// The presence of this component makes an enemy conversational without changing its combat temperament.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class HumanoidSpeech : MonoBehaviour
    {
        [Header("Authored references")]
        public WorldCreature creature;
        public CreatureSpeechProfile profile;
        public CreatureSpeechBubble bubble;

        readonly RaycastHit[] sightHits = new RaycastHit[32];
        CreatureSpeechController controller;
        WorldCreature subscribedCreature;
        CreatureSpeechMood? previousMood;
        float nextSightCheck;
        bool visibleToPlayer;

        void OnEnable()
        {
            controller = controller ?? new CreatureSpeechController(
                new System.Random(Random.Range(int.MinValue, int.MaxValue)));
            subscribedCreature = creature;
            if (subscribedCreature) subscribedCreature.OutingReset += ResetSpeech;
            ResetSpeech();
        }

        void OnDisable()
        {
            if (subscribedCreature) subscribedCreature.OutingReset -= ResetSpeech;
            subscribedCreature = null;
            ResetSpeech();
        }

        public void ResetSpeech()
        {
            controller?.Reset();
            previousMood = null;
            nextSightCheck = 0;
            visibleToPlayer = false;
            if (bubble) bubble.Hide();
        }

        void LateUpdate()
        {
            var world = creature ? creature.world : null;
            if (!creature || !creature.isActiveAndEnabled || !creature.Alive || !profile || !bubble ||
                !world || !world.workshop || !world.motor || !world.Running)
            {
                controller?.Step(Time.time, profile, null, false);
                if (bubble) bubble.Hide();
                return;
            }
            // Game-time timers stay frozen with the game, and pausing never starts a fresh line.
            if (Time.timeScale <= 0) return;

            Vector3 toPlayer = world.motor.transform.position - creature.transform.position;
            float radius = Mathf.Max(.1f, profile.reactionDistance);
            bool inRange = toPlayer.sqrMagnitude <= radius * radius;
            var mood = CreatureSpeechController.MoodFor(creature.State, previousMood);
            if (mood.HasValue) previousMood = mood;

            if (inRange && Time.time >= nextSightCheck)
            {
                nextSightCheck = Time.time + .12f;
                visibleToPlayer = !profile.requireLineOfSight || HasLineOfSight(world);
            }
            if (!inRange)
            {
                // Re-check as soon as the player returns, not against a stale pre-wall sight sample.
                visibleToPlayer = false;
                nextSightCheck = 0;
            }

            controller.Step(Time.time, profile, mood, inRange && visibleToPlayer, inRange);
            if (controller.CurrentLine == null) bubble.Hide();
            else if (!bubble.IsVisible || bubble.Text != controller.CurrentLine) bubble.Show(controller.CurrentLine);
        }

        bool HasLineOfSight(ExpeditionWorld world)
        {
            Vector3 eye = creature.view ? creature.view.Top : creature.transform.position + Vector3.up;
            Vector3 playerEye = world.viewCamera ? world.viewCamera.transform.position : world.motor.transform.position + Vector3.up * 1.2f;
            if (world.IsVisionBlocked(eye, playerEye)) return false;
            Vector3 ray = playerEye - eye;
            float distance = ray.magnitude;
            if (distance < .01f) return true;
            int count = Physics.RaycastNonAlloc(eye, ray / distance, sightHits, distance, profile.occluderMask, QueryTriggerInteraction.Ignore);
            // A saturated result may have omitted a wall. Failing closed prevents speech through it.
            if (count == sightHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = sightHits[i].transform;
                if (hit == null || hit.IsChildOf(creature.transform) || hit.IsChildOf(world.motor.transform)) continue;
                if (sightHits[i].rigidbody == creature.body) continue;
                return false;
            }
            return true;
        }
    }
}
