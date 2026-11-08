using UnityEngine;

namespace Hukaton.Rendering
{
    public enum DitheringPattern { Bayer2x2, Bayer4x4, Bayer8x8, Noise }

    [CreateAssetMenu(menuName = "Hukaton/Rendering/Tuning/Dithering", fileName = "Dithering")]
    public sealed class DitheringSettings : ScriptableObject
    {
        [Tooltip("Включить полноэкранный дизеринг.")]
        public bool Enabled = true;
        [Tooltip("Смешивание с исходной картинкой. Ноль полностью отключает проход.")]
        [Range(0, 1)] public float Intensity = .65f;
        [Tooltip("Рисунок упорядоченного дизеринга или неподвижный шум.")]
        public DitheringPattern Pattern = DitheringPattern.Bayer4x4;
        [Tooltip("Размер точки в пикселях 3D-изображения при включённом World Resolution; иначе — в экранных пикселях. Дизеринг применяется до увеличения картинки.")]
        [Range(1, 8)] public int PixelSize = 2;
        [Tooltip("Количество градаций на цветовой канал. Меньше — заметнее дизеринг.")]
        [Range(2, 64)] public int ColorLevels = 16;
        [Tooltip("Сила порогового узора. Ноль оставляет только постеризацию цвета.")]
        [Range(0, 1)] public float DitherStrength = 1;
        [Tooltip("Чёрно-белая палитра вместо цветной.")]
        public bool Monochrome;
        [Tooltip("Также показывать эффект в Scene View. Game View обрабатывается всегда.")]
        public bool ShowInSceneView;

        public bool IsActive => Enabled && Intensity > 0;

        static readonly int ParametersId = Shader.PropertyToID("_DitherParameters");
        static readonly int PatternId = Shader.PropertyToID("_DitherPattern");
        static readonly int MonochromeId = Shader.PropertyToID("_DitherMonochrome");
        static readonly int PixelScaleId = Shader.PropertyToID("_DitherPixelScale");

        public void ApplyTo(Material material, Vector2 renderScale)
        {
            material.SetVector(ParametersId, new Vector4(Enabled ? Mathf.Clamp01(Intensity) : 0,
                Mathf.Clamp01(DitherStrength), Mathf.Clamp(ColorLevels, 2, 64) - 1, Mathf.Clamp(PixelSize, 1, 8)));
            material.SetInt(PatternId, Mathf.Clamp((int)Pattern, 0, 3));
            material.SetFloat(MonochromeId, Monochrome ? 1 : 0);
            material.SetVector(PixelScaleId, new Vector4(Mathf.Max(.01f, renderScale.x), Mathf.Max(.01f, renderScale.y), 0, 0));
        }
    }
}
