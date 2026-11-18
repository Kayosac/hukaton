using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The silhouette «Эхолот» (<see cref="SonarEffect"/>) puts on one enemy: a flat turquoise copy of its drawing (a
    /// creature) or of its meshes (a chest mimic), drawn with the sonar material — bright where walls and rock stand in
    /// front, dim where it is in the open. The copies are children of the enemy's own renderers, so they move, flip and
    /// open with it. It lights up when the wave gets there, breathes for its time and fades; it goes out early when the
    /// enemy dies, is switched off or is gone. A second wave on a lit enemy only makes it last longer.
    /// </summary>
    public sealed class SonarMark : MonoBehaviour
    {
        static readonly List<SonarMark> Live = new List<SonarMark>();
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int VertexColourId = Shader.PropertyToID("_VertexColour");

        Component target;
        WorldCreature creature;
        ChestMimic mimic;
        ExpeditionWorld world;
        Color colour;
        float litAt, until;
        SfxCue foundSound;
        float foundVolume;
        bool announced, ending;
        float endAt;
        readonly List<(SpriteRenderer source, SpriteRenderer copy)> sprites = new List<(SpriteRenderer, SpriteRenderer)>();
        readonly List<(Renderer source, Renderer copy)> meshes = new List<(Renderer, Renderer)>();
        MaterialPropertyBlock block;

        /// <summary>The enemy this mark is on.</summary>
        public Component Target => target;
        /// <summary>The silhouette shows now (the wave got there, not faded out yet).</summary>
        public bool Shown => !ending && Time.time >= litAt && Time.time < until && target;
        /// <summary>Every silhouette copy this mark drew.</summary>
        public int Copies => sprites.Count + meshes.Count;

        /// <summary>The mark on <paramref name="target"/> now, or null.</summary>
        public static SonarMark On(Component target)
        {
            foreach (var mark in Live) if (mark && mark.target == target && !mark.ending) return mark;
            return null;
        }

        /// <summary>
        /// Lights <paramref name="target"/> (a creature or a mimic) after <paramref name="delay"/> s for
        /// <paramref name="seconds"/>. One lit already lasts longer instead.
        /// </summary>
        public static SonarMark Light(ExpeditionWorld world, Component target, Material material, Color colour, float delay, float seconds,
            SfxCue foundSound = null, float foundVolume = .5f)
        {
            if (!world || !target || !material) return null;
            var mark = On(target);
            if (mark)
            {
                mark.until = Mathf.Max(mark.until, Time.time + delay + seconds);
                return mark;
            }
            var go = new GameObject("Sonar mark");
            go.transform.SetParent(world.spawnedObjects, false);
            mark = go.AddComponent<SonarMark>();
            mark.Begin(world, target, material, colour, delay, seconds, foundSound, foundVolume);
            return mark;
        }

        void OnEnable() => Live.Add(this);
        void OnDisable() => Live.Remove(this);

        void Begin(ExpeditionWorld outing, Component lit, Material material, Color tint, float delay, float seconds, SfxCue sound, float volume)
        {
            world = outing;
            target = lit;
            creature = lit as WorldCreature;
            mimic = lit as ChestMimic;
            colour = tint;
            litAt = Time.time + Mathf.Max(0, delay);
            until = litAt + seconds;
            foundSound = sound;
            foundVolume = volume;
            block = new MaterialPropertyBlock();
            if (creature)
            {
                var source = creature.view ? creature.view.spriteRenderer : null;
                if (source) sprites.Add((source, SpriteCopy(source, material)));
            }
            else
            {
                foreach (var renderer in lit.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(renderer is MeshRenderer) || renderer.GetComponent<ParticleSystem>()) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (!filter || !filter.sharedMesh) continue;
                    meshes.Add((renderer, MeshCopy(renderer, filter.sharedMesh, material)));
                }
            }
            Apply(0);
        }

        static SpriteRenderer SpriteCopy(SpriteRenderer source, Material material)
        {
            var go = new GameObject("Sonar silhouette");
            go.layer = source.gameObject.layer;
            go.transform.SetParent(source.transform, false);
            var copy = go.AddComponent<SpriteRenderer>();
            copy.sharedMaterial = material;
            copy.sortingLayerID = source.sortingLayerID;
            copy.sortingOrder = source.sortingOrder + 1;
            copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.sprite = source.sprite;
            return copy;
        }

        static Renderer MeshCopy(Renderer source, Mesh mesh, Material material)
        {
            var go = new GameObject("Sonar silhouette");
            go.layer = source.gameObject.layer;
            go.transform.SetParent(source.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var copy = go.AddComponent<MeshRenderer>();
            var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            copy.sharedMaterials = materials;
            copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            copy.receiveShadows = false;
            return copy;
        }

        bool TargetGone => !target || !target.gameObject.activeInHierarchy
            || creature && !creature.Alive || mimic && !mimic.Alive;

        void LateUpdate()
        {
            float now = Time.time;
            if (!ending && (TargetGone || now >= until))
            {
                ending = true;
                // Already fading on its own at the end of its time; cut short, it fades quickly.
                endAt = now >= until ? now : now + .4f;
            }
            if (ending && (now >= endAt || !target))
            {
                Destroy(gameObject);
                return;
            }
            if (!announced && now >= litAt)
            {
                announced = true;
                if (world && foundSound) world.Play(foundSound, target.transform.position, foundVolume, Random.Range(.96f, 1.06f));
            }
            float strength = ending ? (now < litAt ? 0 : Mathf.Clamp01((endAt - now) / .4f) * .85f) : SonarRules.Strength(now - litAt, until - litAt);
            Apply(strength);
        }

        void Apply(float strength)
        {
            var tint = new Color(colour.r, colour.g, colour.b, colour.a * strength);
            foreach (var (source, copy) in sprites)
            {
                if (!copy) continue;
                bool on = strength > .001f && source && source.enabled && source.gameObject.activeInHierarchy && source.sprite;
                copy.enabled = on;
                if (!on) continue;
                copy.sprite = source.sprite;
                copy.flipX = source.flipX;
                copy.flipY = source.flipY;
                copy.color = tint;
                copy.GetPropertyBlock(block);
                block.SetColor(TintId, Color.white);
                block.SetFloat(VertexColourId, 1);
                copy.SetPropertyBlock(block);
            }
            foreach (var (source, copy) in meshes)
            {
                if (!copy) continue;
                bool on = strength > .001f && source && source.enabled && source.gameObject.activeInHierarchy;
                copy.enabled = on;
                if (!on) continue;
                copy.GetPropertyBlock(block);
                block.SetColor(TintId, tint);
                block.SetFloat(VertexColourId, 0);
                copy.SetPropertyBlock(block);
            }
        }

        void OnDestroy()
        {
            foreach (var (_, copy) in sprites) if (copy) Destroy(copy.gameObject);
            foreach (var (_, copy) in meshes) if (copy) Destroy(copy.gameObject);
            sprites.Clear();
            meshes.Clear();
        }
    }
}
