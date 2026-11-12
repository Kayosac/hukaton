using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Everything the ice shell of <see cref="IceEncasement"/> is made of: the shell variants (Blender meshes in
    /// Models/Ice), the shards it bursts into, its effects, sounds and fit. Referenced from
    /// <see cref="ExpeditionVfx.iceShell"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Ice Shell Kit")]
    public sealed class IceShellKit : ScriptableObject
    {
        [Tooltip("Shell prefabs: a crooked block of ice, base on y = 0, never smaller than the 1 × 1 × 1 m box (it hides a camera-facing drawing from every side). One is picked at random and fitted to the body.")]
        public GameObject[] shells = new GameObject[0];
        [Tooltip("Pieces the shell bursts into when a physical hit shatters it.")]
        public IceShard[] shards = new IceShard[0];

        [Header("Fit")]
        [Tooltip("Shell width over the reach of the body (the drawing turning to the camera about its feet).")]
        [Min(1)] public float widthPadding = 1.08f;
        [Tooltip("Shell height over the body's height.")]
        [Min(1)] public float heightPadding = 1.12f;
        [Tooltip("How far the shell sinks into the ground, as a share of its height.")]
        [Range(0, .3f)] public float sink = .04f;
        [Tooltip("Random tilt of the shell, degrees: nothing in this valley grows straight.")]
        [Range(0, 20)] public float tilt = 3;

        [Header("Timing")]
        [Min(.05f)] public float growSeconds = .32f;
        [Min(.1f)] public float meltSeconds = 1.4f;

        [Header("Shatter")]
        public Vector2Int shardCount = new Vector2Int(8, 13);
        [Tooltip("Outward speed of the shards, m/s.")]
        public Vector2 shardSpeed = new Vector2(2.5f, 5.5f);
        [Tooltip("How long a shard lies on the ground before it shrinks away, s.")]
        public Vector2 shardLifetime = new Vector2(2.5f, 4f);
        [Min(0)] public float shatterHitStop = .07f;
        [Min(0)] public float shatterTrauma = .28f;

        [Header("Feedback")]
        public Color tint = new Color(.55f, .9f, 1f);
        public VfxBurst encaseVfx;
        public VfxBurst shatterVfx;
        [Tooltip("Steam while the shell melts; held for the melt.")]
        public VfxBurst meltVfx;
        public SfxCue encaseCue, shatterCue, meltCue;
    }
}
