using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The branch made by «Ветвь-мост»: grows horizontally out of the surface it was thrown at, a walkway for the player
    /// and for creatures until the end of the outing. The root transform stays unscaled: its +Z is the growth direction
    /// and its origin is the top-centre of the root end; the box collider (the walking surface, top at local y = 0) and
    /// the crooked visual (authored for <see cref="authoredLength"/>) follow the current length while it grows.
    /// Creatures: once grown, a small navigation mesh is built on the branch's top and joined to the valley's mesh with
    /// off-mesh links at the ends that come close to walkable ground. Fire and Lightning (<see cref="IIgnitable"/>)
    /// burn it: it chars, collapses and the path is gone.
    /// </summary>
    public sealed class BranchBridge : MonoBehaviour, IOutingSpawned, ICardZone, IIgnitable
    {
        [Header("Authored parts")]
        [Tooltip("The walking surface on this (unscaled) root.")]
        public BoxCollider walkway;
        [Tooltip("Crooked branch mesh along +Z, its top at y = 0, authored for authoredLength × authoredWidth × authoredThickness.")]
        public Transform visual;
        public Renderer[] barkRenderers;
        [Min(.1f)] public float authoredLength = 5, authoredWidth = 1.2f, authoredThickness = .42f;

        [Header("Growth")]
        [Min(.05f)] public float growSeconds = .9f;
        public VfxBurst rootVfx, tipVfx;
        public Color leafTint = new Color(.55f, .7f, .35f);
        public SfxCue growSound;

        [Header("Burning")]
        public Material charredMaterial;
        [Tooltip("Looping fire spread along the branch while it burns (Retro Arsenal).")]
        public VfxBurst fireVfx;
        public Color fireTint = new Color(1f, .55f, .2f);
        [Min(.3f)] public float fireSpacing = 1.4f;
        [Tooltip("From catching fire to collapsing, seconds.")]
        [Min(.1f)] public float burnSeconds = 2.4f;
        [Tooltip("Fire or lightning this close to the branch sets it alight (direct hits always do).")]
        [Min(0)] public float igniteReach = .7f;
        public VfxBurst ashVfx;
        public Color ashTint = new Color(.35f, .33f, .3f);
        public SfxCue igniteSound, crackleLoop, collapseSound;
        [Min(.05f)] public float collapseSeconds = .5f;

        [Header("Navigation for creatures")]
        public bool buildNavigation = true;
        [Tooltip("Agent type of the valley's creatures (Humanoid = 0).")]
        public int agentTypeId;
        [Min(.01f)] public float navVoxelSize = .05f;
        [Tooltip("An end of the branch joins the valley's navigation mesh if walkable ground is this close.")]
        [Min(.1f)] public float linkSearch = 1.6f;
        [Min(0)] public float linkMaxRise = 1.1f;

        public ExpeditionWorld World { get; private set; }
        public float Length { get; private set; }
        public float CurrentLength { get; private set; }
        public float Width { get; private set; } = 1.2f;
        public float Thickness { get; private set; } = .42f;
        public bool Grown { get; private set; }
        public bool Burning { get; private set; }
        /// <summary>Links from the branch to the valley's navigation mesh (0–2), for tests and debugging.</summary>
        public int NavigationLinks => links.Count;
        public bool HasNavigation => navigationAdded;

        public CardZoneKind Kind => CardZoneKind.BranchBridge;
        public Bounds Bounds => walkway ? walkway.bounds : new Bounds(transform.position, Vector3.one);
        public bool Contains(Vector3 point) => walkway && walkway.enabled && (walkway.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;

        float age, burnAge, collapseAge;
        bool collapsing, navigationAdded;
        NavMeshDataInstance navigation;
        NavMeshData navigationData;
        readonly List<NavMeshLinkInstance> links = new List<NavMeshLinkInstance>();
        readonly List<VfxBurst> fires = new List<VfxBurst>();

        public void OnSpawned(ExpeditionWorld world)
        {
            World = world;
            if (world) world.Zones.Add(this);
        }

        /// <summary>Starts growing to <paramref name="length"/> metres (from the root, embedded part included).</summary>
        public void Grow(float length, float width, float thickness)
        {
            Length = Mathf.Max(.1f, length);
            Width = Mathf.Max(.1f, width);
            Thickness = Mathf.Max(.05f, thickness);
            age = 0;
            Grown = false;
            ApplyLength(Length * BranchBridgeMath.Growth(0) + .05f);
            if (World)
            {
                World.Spawn(rootVfx, transform.position + transform.forward * .2f, Quaternion.LookRotation(transform.forward), leafTint, .8f);
                World.Play(growSound, transform.position);
            }
        }

        void ApplyLength(float length)
        {
            CurrentLength = length;
            if (walkway)
            {
                walkway.size = new Vector3(Width, Thickness, length);
                walkway.center = new Vector3(0, -Thickness * .5f, length * .5f);
            }
            if (visual)
                visual.localScale = new Vector3(Width / authoredWidth, Thickness / authoredThickness, length / authoredLength);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!Grown && !collapsing)
            {
                age += dt;
                float t = age / growSeconds;
                ApplyLength(Mathf.Max(.05f, Length * BranchBridgeMath.Growth(t)));
                if (t >= 1)
                {
                    Grown = true;
                    ApplyLength(Length);
                    if (World) World.Spawn(tipVfx, transform.TransformPoint(0, .1f, Length - .3f), Quaternion.identity, leafTint, .9f);
                    if (!Burning) BuildNavigation();
                }
            }
            if (Burning && !collapsing)
            {
                burnAge += dt;
                if (World && World.sfx) World.sfx.Loop(this, crackleLoop, transform.TransformPoint(0, 0, CurrentLength * .5f), .8f);
                // Smoulders and sags a little before it gives way.
                if (visual) visual.localRotation = Quaternion.Euler(Mathf.Sin(burnAge * 23) * .6f + burnAge * 1.2f, 0, Mathf.Sin(burnAge * 17) * .5f);
                if (burnAge >= burnSeconds) Collapse();
            }
            if (collapsing)
            {
                collapseAge += dt;
                float t = Mathf.Clamp01(collapseAge / collapseSeconds);
                if (visual)
                {
                    visual.localPosition = Vector3.down * (t * t * 2.5f);
                    visual.localRotation = Quaternion.Euler(t * 25, 0, t * 8);
                    var scale = visual.localScale;
                    float shrink = 1 - t * .7f;
                    visual.localScale = new Vector3(Width / authoredWidth * shrink, Thickness / authoredThickness * shrink, scale.z);
                }
                if (t >= 1) Destroy(gameObject);
            }
        }

        // ---------- fire ----------

        public bool Ignite(IgnitionSource source, Vector3 point)
        {
            if (Burning || collapsing || !walkway || !walkway.enabled) return false;
            if ((walkway.ClosestPoint(point) - point).sqrMagnitude > igniteReach * igniteReach) return false;
            Burning = true;
            burnAge = 0;
            RemoveNavigation();
            if (charredMaterial && barkRenderers != null)
                foreach (var renderer in barkRenderers)
                    if (renderer)
                    {
                        var materials = renderer.sharedMaterials;
                        // The bark chars; leaves (the last slot) burn too.
                        for (int i = 0; i < materials.Length; i++) materials[i] = charredMaterial;
                        renderer.sharedMaterials = materials;
                    }
            if (World)
            {
                World.Play(igniteSound, point);
                float length = Mathf.Max(.5f, CurrentLength);
                for (float s = Mathf.Min(.6f, length * .5f); s < length; s += fireSpacing)
                {
                    var fire = World.Spawn(fireVfx, transform.TransformPoint(0, .05f, s), Quaternion.identity, fireTint, .8f);
                    if (fire) { fire.HoldFor(burnSeconds + collapseSeconds); fire.Follow(transform, fire.transform.position - transform.position); fires.Add(fire); }
                }
            }
            return true;
        }

        void Collapse()
        {
            collapsing = true;
            collapseAge = 0;
            if (walkway) walkway.enabled = false;
            if (World) World.Zones.Remove(this);
            RemoveNavigation();
            foreach (var fire in fires) if (fire) fire.Release(.5f);
            fires.Clear();
            if (World)
            {
                if (World.sfx) World.sfx.StopLoop(this);
                World.Play(collapseSound, transform.TransformPoint(0, 0, CurrentLength * .5f));
                for (float s = .5f; s < CurrentLength; s += fireSpacing * 1.3f)
                    World.Spawn(ashVfx, transform.TransformPoint(0, -.1f, s), Quaternion.identity, ashTint, .8f);
            }
        }

        // ---------- navigation ----------

        void BuildNavigation()
        {
            if (!buildNavigation || navigationAdded) return;
            var settings = NavMesh.GetSettingsByID(agentTypeId);
            if (settings.agentTypeID == -1) return;
            settings.overrideVoxelSize = true;
            settings.voxelSize = navVoxelSize;
            Vector3 forward = transform.forward;
            // Links first, against the valley's mesh as it is (before the branch's own mesh exists).
            Vector3 tip = transform.TransformPoint(0, 0, Length - .3f);
            Vector3 root = transform.TransformPoint(0, 0, Mathf.Min(Length * .5f, .5f));
            TryLink(tip, tip + forward * .8f);
            TryLink(root, root - forward * .9f);
            var size = new Vector3(Width, Thickness, Length);
            var matrix = Matrix4x4.TRS(transform.TransformPoint(0, -Thickness * .5f, Length * .5f), transform.rotation, Vector3.one);
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = size, transform = matrix, area = 0 }
            };
            var bounds = new Bounds(transform.TransformPoint(0, 0, Length * .5f), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int z = 0; z <= 1; z++)
                bounds.Encapsulate(transform.TransformPoint(x * Width * .5f, 0, z * Length));
            bounds.Expand(new Vector3(1, 4, 1));
            navigationData = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (navigationData)
            {
                navigation = NavMesh.AddNavMeshData(navigationData);
                navigationAdded = true;
            }
        }

        void TryLink(Vector3 onBranch, Vector3 probe)
        {
            if (!NavMesh.SamplePosition(probe, out var hit, linkSearch, NavMesh.AllAreas)) return;
            if (Mathf.Abs(hit.position.y - onBranch.y) > linkMaxRise) return;
            var link = new NavMeshLinkData
            {
                startPosition = onBranch,
                endPosition = hit.position,
                width = Mathf.Max(.1f, Width * .5f),
                costModifier = -1,
                bidirectional = true,
                area = 0,
                agentTypeID = agentTypeId
            };
            links.Add(NavMesh.AddLink(link));
        }

        void RemoveNavigation()
        {
            foreach (var link in links) NavMesh.RemoveLink(link);
            links.Clear();
            if (navigationAdded) { NavMesh.RemoveNavMeshData(navigation); navigationAdded = false; }
            if (navigationData) { Destroy(navigationData); navigationData = null; }
        }

        void OnDisable()
        {
            if (World) World.Zones.Remove(this);
            if (World && World.sfx) World.sfx.StopLoop(this);
        }

        void OnDestroy()
        {
            RemoveNavigation();
            foreach (var fire in fires) if (fire) fire.Release(.3f);
        }
    }
}
