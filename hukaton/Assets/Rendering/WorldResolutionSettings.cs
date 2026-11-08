using UnityEngine;

namespace Hukaton.Rendering
{
    public enum WorldUpscaling { Integer, Fit, Adaptive }

    [CreateAssetMenu(menuName = "Hukaton/Rendering/Tuning/World Resolution", fileName = "WorldResolution")]
    public sealed class WorldResolutionSettings : ScriptableObject
    {
        public bool Enabled = true;
        [Tooltip("Ширина 3D-кадра для Integer и Fit. В Adaptive рассчитывается по высоте и пропорциям экрана.")]
        [Min(16)] public int Width = 640;
        [Tooltip("Высота 3D-кадра, включая небо, частицы и дизеринг. UI использует разрешение экрана. Диапазон: 16–4096. В Adaptive при достижении предела ширины 4096 высота также уменьшается.")]
        [Min(16)] public int Height = 360;
        [Tooltip("Adaptive: фиксированная высота, ширина под пропорции экрана, без полей. Integer: фиксированные Width/Height и целый множитель. Fit: фиксированные Width/Height с сохранением пропорций. Все режимы используют nearest-neighbor; Adaptive и Fit допускают дробный масштаб.")]
        public WorldUpscaling Upscaling = WorldUpscaling.Adaptive;
        public Color BorderColor = Color.black;

        public Vector2Int Resolution => new Vector2Int(Mathf.Clamp(Width, 16, 4096), Mathf.Clamp(Height, 16, 4096));

        public Vector2Int ResolutionForOutput(int width, int height)
        {
            if (Upscaling != WorldUpscaling.Adaptive) return Resolution;
            float aspect = Mathf.Max(1, width) / (float)Mathf.Max(1, height);
            // Retain pixel density vertically, and bound extreme aspect ratios without stretching the camera.
            float renderHeight = Mathf.Min(Resolution.y, 4096 / aspect);
            return new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(renderHeight * aspect), 1, 4096),
                Mathf.Clamp(Mathf.RoundToInt(renderHeight), 1, 4096));
        }

        // Pixel coordinates, origin at bottom left, matching Camera.WorldToViewportPoint.
        public Rect OutputRect(int width, int height)
        {
            width = Mathf.Max(1, width); height = Mathf.Max(1, height);
            if (Upscaling == WorldUpscaling.Adaptive) return new Rect(0, 0, width, height);
            Vector2Int size = Resolution;
            float scale = Mathf.Min(width / (float)size.x, height / (float)size.y);
            // A window smaller than the authored image must downsample to show the whole frame.
            if (Upscaling == WorldUpscaling.Integer && scale >= 1) scale = Mathf.Floor(scale);
            int w = Mathf.Min(width, Mathf.Max(1, Mathf.RoundToInt(size.x * scale)));
            int h = Mathf.Min(height, Mathf.Max(1, Mathf.RoundToInt(size.y * scale)));
            return new Rect((width - w) / 2, (height - h) / 2, w, h);
        }
    }
}
