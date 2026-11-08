using UnityEngine;

namespace Hakaton.Character
{
    public enum NpcActorState { Idle, Arriving, AtCounter, Reacting, Turning, Leaving }

    /// <summary>A reusable visitor on an authored route. The actor root and step pivot sit at its feet.</summary>
    [ExecuteAlways]
    public sealed class SpriteNpcActor : MonoBehaviour
    {
        [Header("Authored rig and route")]
        public SpriteRenderer spriteRenderer;
        public Transform facingRoot;
        public Transform stepRoot;
        public Transform viewer;
        [Tooltip("World-space waypoints from entrance to counter, outside this actor's hierarchy.")]
        public Transform[] route;
        [Tooltip("The authored stepRoot scale represents this appearance; other appearances scale relative to it.")]
        public NpcAppearance defaultAppearance;

        [Header("Movement")]
        [Min(.01f)] public float walkSpeed = 2.2f;
        [Min(.01f)] public float strideLength = 1.1f;
        [Range(0, .15f)] public float stepAmplitude = .04f;
        [Min(.01f)] public float stepResponse = 12;

        [Header("Departure")]
        [Min(0)] public float reactionDuration = 1.15f;
        [Min(.01f)] public float turnDuration = .42f;
        [Range(0, 85)] public float turnAngle = 75;
        [Range(.01f, 1)] public float narrowestTurnScale = .12f;

        public NpcActorState State { get; private set; }
        public bool IsBusy => State != NpcActorState.Idle;
        public bool ReadyForTrade => State == NpcActorState.AtCounter;
        public bool IsDeparting => State == NpcActorState.Reacting
            || State == NpcActorState.Turning || State == NpcActorState.Leaving;
        public NpcAppearance CurrentAppearance { get; private set; }
        public NpcExpression CurrentExpression { get; private set; }

        static readonly int SpriteUvRect = Shader.PropertyToID("_SpriteUVRect");
        MaterialPropertyBlock properties;
        Sprite uvSprite;
        SpriteRenderer uvRenderer;
        Vector3 authoredStepScale;
        Quaternion authoredFacingRotation;
        Quaternion billboardRotation;
        bool initialized, showingBack;
        int targetWaypoint, lastReachedWaypoint;
        float authoredBodyScale, phaseTime, turnProgress, stepPhase, stepWeight;

        void Awake()
        {
            if (!Application.IsPlaying(gameObject))
            {
                RefreshSpriteUv();
                return;
            }
            if (!Initialize()) enabled = false;
        }

        bool Initialize()
        {
            if (initialized) return true;
            if (!spriteRenderer || !facingRoot || !stepRoot || !viewer
                || facingRoot == transform || !facingRoot.IsChildOf(transform)
                || stepRoot == facingRoot || !stepRoot.IsChildOf(facingRoot)
                || !spriteRenderer.transform.IsChildOf(stepRoot) || !RouteIsValid())
            {
                Debug.LogError("SpriteNpcActor needs an authored sprite, facing/step children, viewer and an external entrance-to-counter route.", this);
                return false;
            }
            authoredStepScale = stepRoot.localScale;
            authoredBodyScale = defaultAppearance && defaultAppearance.bodyScale > 0
                && !float.IsInfinity(defaultAppearance.bodyScale) ? defaultAppearance.bodyScale : 1;
            authoredFacingRotation = facingRoot.localRotation;
            billboardRotation = transform.rotation;
            if (properties == null) properties = new MaterialPropertyBlock();
            State = NpcActorState.Idle;
            spriteRenderer.enabled = false;
            initialized = true;
            return true;
        }

        bool RouteIsValid()
        {
            if (route == null || route.Length < 2) return false;
            for (int i = 0; i < route.Length; i++)
                if (!route[i] || route[i].IsChildOf(transform)) return false;
            return true;
        }

        /// <summary>Only an idle actor can start a visit; a busy actor is never reset or teleported.</summary>
        public bool BeginVisit(NpcAppearance appearance = null)
        {
            if (IsBusy || !Initialize()) return false;
            var selected = appearance ? appearance : defaultAppearance;
            if (!selected || !selected.IsComplete || !RouteIsValid())
            {
                Debug.LogError("A visit needs all five appearance sprites, a positive body scale and a valid authored route.", this);
                return false;
            }
            CurrentAppearance = selected;
            CurrentExpression = NpcExpression.Neutral;
            showingBack = false;
            phaseTime = turnProgress = stepPhase = stepWeight = 0;
            lastReachedWaypoint = 0;
            targetWaypoint = 1;
            transform.position = route[0].position;
            State = NpcActorState.Arriving;
            SetSprite(selected.neutral);
            spriteRenderer.enabled = true;
            Present(0, 0);
            return true;
        }

        public void SetExpression(NpcExpression expression)
        {
            CurrentExpression = expression;
            if (CurrentAppearance && !showingBack) SetSprite(CurrentAppearance.ForExpression(expression));
        }

        /// <summary>Show the result, turn, then return along the portion of the route already reached.</summary>
        public bool Leave(NpcExpression outcome)
        {
            if (State != NpcActorState.Arriving && State != NpcActorState.AtCounter) return false;
            SetExpression(outcome);
            PrepareReturnRoute();
            phaseTime = 0;
            State = NpcActorState.Reacting;
            return true;
        }

        /// <summary>Cancellation still turns and walks out; it never hides a visitor halfway along a route.</summary>
        public void Cancel()
        {
            if (State == NpcActorState.Idle || State == NpcActorState.Turning || State == NpcActorState.Leaving) return;
            if (State != NpcActorState.Reacting) PrepareReturnRoute();
            BeginTurning();
        }

        void PrepareReturnRoute()
        {
            targetWaypoint = lastReachedWaypoint;
        }

        void Update()
        {
            // Prefab previews (including a prefab stage opened during Play) only need atlas UVs.
            // Explicit BeginVisit/Advance calls remain available to deterministic EditMode tests.
            if (!Application.IsPlaying(gameObject)) RefreshSpriteUv();
            else Advance(Time.deltaTime);
        }

        /// <summary>Advance the real actor, also useful for deterministic route and presentation tests.</summary>
        public void Advance(float deltaTime)
        {
            if (!initialized || !IsBusy || !(deltaTime > 0) || float.IsInfinity(deltaTime)) return;
            float remaining = deltaTime;
            float travelled = 0;
            while (remaining > 0 && IsBusy)
            {
                switch (State)
                {
                    case NpcActorState.Arriving:
                    case NpcActorState.Leaving:
                        travelled += MoveToWaypoint(ref remaining);
                        break;
                    case NpcActorState.Reacting:
                        SpendPhaseTime(ref remaining, Mathf.Max(0, reactionDuration));
                        if (phaseTime >= Mathf.Max(0, reactionDuration)) BeginTurning();
                        break;
                    case NpcActorState.Turning:
                        float duration = Mathf.Max(.001f, turnDuration);
                        SpendPhaseTime(ref remaining, duration);
                        turnProgress = Mathf.Clamp01(phaseTime / duration);
                        if (!showingBack && turnProgress >= .5f)
                        {
                            showingBack = true;
                            SetSprite(CurrentAppearance.back);
                        }
                        if (turnProgress >= 1)
                        {
                            State = NpcActorState.Leaving;
                            turnProgress = 0;
                        }
                        break;
                    default:
                        remaining = 0;
                        break;
                }
            }
            if (IsBusy) Present(deltaTime, travelled);
        }

        void SpendPhaseTime(ref float remaining, float duration)
        {
            float spent = Mathf.Min(remaining, Mathf.Max(0, duration - phaseTime));
            phaseTime += spent;
            remaining -= spent;
        }

        float MoveToWaypoint(ref float remaining)
        {
            if (targetWaypoint < 0)
            {
                CompleteVisit();
                return 0;
            }
            Vector3 destination = route[targetWaypoint].position;
            float distance = Vector3.Distance(transform.position, destination);
            float speed = Mathf.Max(.01f, walkSpeed);
            float movement = Mathf.Min(distance, speed * remaining);
            transform.position = Vector3.MoveTowards(transform.position, destination, movement);
            if (movement >= distance)
            {
                remaining = Mathf.Max(0, remaining - distance / speed);
                if (State == NpcActorState.Arriving)
                {
                    lastReachedWaypoint = targetWaypoint++;
                    if (targetWaypoint == route.Length) State = NpcActorState.AtCounter;
                }
                else if (--targetWaypoint < 0) CompleteVisit();
            }
            else remaining = 0;
            return movement;
        }

        void BeginTurning()
        {
            State = NpcActorState.Turning;
            phaseTime = turnProgress = 0;
        }

        void Present(float deltaTime, float travelled)
        {
            stepPhase = Mathf.Repeat(stepPhase + travelled * Mathf.PI * 2 / Mathf.Max(.01f, strideLength), Mathf.PI * 2);
            bool walking = travelled > .00001f && (State == NpcActorState.Arriving || State == NpcActorState.Leaving);
            stepWeight = Mathf.Lerp(stepWeight, walking ? 1 : 0, 1 - Mathf.Exp(-stepResponse * deltaTime));
            float turn = State == NpcActorState.Turning ? Mathf.Sin(turnProgress * Mathf.PI) : 0;
            float width = Mathf.Lerp(1, narrowestTurnScale, turn);
            Vector3 scale = authoredStepScale * (CurrentAppearance.bodyScale / authoredBodyScale);
            scale.x *= width;
            scale.y *= 1 + Mathf.Sin(stepPhase) * stepAmplitude * stepWeight;
            stepRoot.localScale = scale;

            Vector3 awayFromViewer = transform.position - viewer.position;
            awayFromViewer.y = 0;
            if (awayFromViewer.sqrMagnitude > .000001f)
                billboardRotation = Quaternion.LookRotation(awayFromViewer, Vector3.up);
            facingRoot.rotation = billboardRotation * authoredFacingRotation * Quaternion.Euler(0, turn * turnAngle, 0);
        }

        void SetSprite(Sprite sprite)
        {
            if (!sprite) return;
            spriteRenderer.sprite = sprite;
            RefreshSpriteUv();
        }

        void RefreshSpriteUv()
        {
            if (!spriteRenderer || !spriteRenderer.sprite) return;
            Sprite sprite = spriteRenderer.sprite;
            if (uvRenderer == spriteRenderer && uvSprite == sprite && properties != null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            // FullRect atlas cells are authored; preserve the renderer's other property overrides.
            Rect rect = sprite.textureRect;
            Texture2D texture = sprite.texture;
            spriteRenderer.GetPropertyBlock(properties);
            properties.SetVector(SpriteUvRect, new Vector4(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height));
            spriteRenderer.SetPropertyBlock(properties);
            uvRenderer = spriteRenderer;
            uvSprite = sprite;
        }

        void CompleteVisit()
        {
            State = NpcActorState.Idle;
            spriteRenderer.enabled = false;
            stepPhase = stepWeight = turnProgress = 0;
            stepRoot.localScale = authoredStepScale * (CurrentAppearance.bodyScale / authoredBodyScale);
            facingRoot.localRotation = authoredFacingRotation;
        }
    }
}
