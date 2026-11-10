using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.EnemyDebug
{
    /// <summary>Presentation only. Never contains WorldCreature, Collider, Rigidbody or NavMeshAgent.</summary>
    [DefaultExecutionOrder(50)]
    public sealed class EnemyDebugPreview : MonoBehaviour
    {
        public WorldCreature source;
        public CreatureSpriteView view;
        public CreaturePose SelectedPose { get; private set; }
        float playedAt = float.NegativeInfinity;

        public bool HasAttack => source && source.temperament != Temperament.Timid && source.view.attack;

        public void Bind(WorldCreature prefab)
        {
            source = prefab;
            name = "Preview - " + source.name;
            var original = source.view;
            view.idle = original.idle;
            view.idleBack = original.idleBack;
            view.alarm = original.alarm;
            view.hurt = original.hurt;
            view.attack = original.attack;
            view.death = original.death;
            view.deathBack = original.deathBack;
            view.height = original.height;
            view.facesRight = original.facesRight;
            view.pixelSize = original.pixelSize;
            view.spriteRenderer.sharedMaterial = original.spriteRenderer.sharedMaterial;
            view.spriteRenderer.gameObject.layer = original.spriteRenderer.gameObject.layer;
            // Keep the real prefab scale, atlas PPU and shared foot pivot.
            transform.localScale = source.transform.localScale;
            view.spriteRenderer.sprite = view.idle;
            PlayPose(CreaturePose.Default);
            Present();
        }

        public bool PlayPose(CreaturePose pose)
        {
            if (pose == CreaturePose.Attack && !HasAttack) return false;
            SelectedPose = pose;
            playedAt = Time.time;
            return true;
        }

        public string PoseLabel => SelectedPose switch
        {
            CreaturePose.Alarm => source.temperament == Temperament.Timid ? "Испуг" : "Агрессия",
            CreaturePose.Hurt => "Получение урона",
            CreaturePose.Attack => "Атака",
            CreaturePose.Death => "Смерть",
            _ => "Исходное состояние"
        };

        void LateUpdate() => Present();

        public void Present()
        {
            if (!source || !view) return;
            // A short visual accent, then hold the selected drawing for inspection. No AI or locomotion.
            float t = Mathf.Clamp01((Time.time - playedAt) / .32f);
            float pulse = Mathf.Sin(t * Mathf.PI) * (1 - t);
            float amount = SelectedPose == CreaturePose.Hurt ? -.12f : SelectedPose == CreaturePose.Attack ? -.08f : .04f;
            float height = SelectedPose == CreaturePose.Default ? 1 : 1 + pulse * amount;
            view.Present(SelectedPose, transform.forward, Vector3.zero, new Vector2(1 / Mathf.Sqrt(height), height), 0);
            view.Tint(Color.white);
        }

        // Ray/quad selection uses the drawn plane. The previews need no selection colliders, even triggers.
        public bool Intersect(Ray ray, float reach, out float distance)
        {
            distance = 0;
            if (!isActiveAndEnabled || !view || !view.spriteRenderer || !view.spriteRenderer.sprite) return false;
            var sprite = view.spriteRenderer;
            var plane = new Plane(sprite.transform.forward, sprite.transform.position);
            if (!plane.Raycast(ray, out distance) || distance > reach) return false;
            Vector3 local = sprite.transform.InverseTransformPoint(ray.GetPoint(distance));
            Bounds bounds = sprite.sprite.bounds;
            return local.x >= bounds.min.x && local.x <= bounds.max.x
                && local.y >= bounds.min.y && local.y <= bounds.max.y;
        }
    }
}
