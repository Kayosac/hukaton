using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// One crooked electric arc between two chain targets of «Разряд молнии»: the Retro Arsenal lightning beam line,
    /// re-jagged every few hundredths of a second, thinning out and gone after <see cref="lifetime"/>.
    /// Prefab: <c>Assets/Game/Prefabs/Cards/Lightning/Lightning arc.prefab</c>.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class LightningArc : MonoBehaviour
    {
        public LineRenderer line;
        [Min(2)] public int points = 11;
        [Tooltip("Sideways kinks, m (largest in the middle of the arc).")]
        [Min(0)] public float jitter = .35f;
        [Min(.02f)] public float lifetime = .35f;
        [Min(.01f)] public float flickerSeconds = .035f;
        [Min(0)] public float width = .45f;

        Vector3 from, to;
        float age, nextFlicker;

        public static LightningArc Spawn(LightningArc prefab, Vector3 from, Vector3 to, Transform parent)
        {
            if (!prefab) return null;
            var arc = Instantiate(prefab, from, Quaternion.identity, parent);
            arc.Strike(from, to);
            return arc;
        }

        public void Strike(Vector3 start, Vector3 end)
        {
            from = start;
            to = end;
            age = 0;
            nextFlicker = 0;
            if (!line) line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points;
            Shape();
        }

        void Shape()
        {
            Vector3 axis = to - from;
            Vector3 side = Vector3.Cross(axis, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(axis, Vector3.right);
            side.Normalize();
            Vector3 lift = Vector3.Cross(side, axis).normalized;
            for (int i = 0; i < points; i++)
            {
                float t = i / (float)(points - 1);
                float bend = Mathf.Sin(t * Mathf.PI) * jitter;
                Vector3 kink = i == 0 || i == points - 1 ? Vector3.zero : (side * Random.Range(-1f, 1f) + lift * Random.Range(-1f, 1f)) * bend;
                line.SetPosition(i, from + axis * t + kink);
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= nextFlicker)
            {
                nextFlicker = age + flickerSeconds;
                Shape();
            }
            line.widthMultiplier = width * Mathf.Clamp01(1 - age / lifetime) * Random.Range(.7f, 1.15f);
            if (age >= lifetime) Destroy(gameObject);
        }
    }
}
