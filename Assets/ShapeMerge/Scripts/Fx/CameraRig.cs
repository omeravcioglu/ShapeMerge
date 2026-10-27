using UnityEngine;

namespace ShapeMerge
{
    /// <summary>Keeps the arena framed for any aspect ratio and adds trauma-based screen shake.</summary>
    public sealed class CameraRig
    {
        readonly Camera cam;
        float trauma;
        float noiseTime;
        SpringVector2 kick;
        SpringFloat zoom;

        public CameraRig(Camera cam)
        {
            this.cam = cam;
            zoom = new SpringFloat(0f);
        }

        public void AddTrauma(float amount) => trauma = Mathf.Min(1f, trauma + amount);

        /// <summary>A sharp directional nudge, e.g. cannon recoil.</summary>
        public void Kick(Vector2 impulse) => kick.Velocity += impulse;

        /// <summary>A quick zoom-in punch (positive) that springs back.</summary>
        public void ZoomPunch(float amount) => zoom.Velocity += amount;

        public void Tick(float dt, Arena arena)
        {
            float aspect = Mathf.Max(0.2f, cam.aspect);
            arena.Layout(aspect);
            Rect view = arena.View;
            float size = Mathf.Max(view.height * 0.5f, view.width * 0.5f / aspect);

            trauma = Mathf.Max(0f, trauma - dt * 1.5f);
            noiseTime += dt;
            float shake = trauma * trauma;
            float ox = (Mathf.PerlinNoise(noiseTime * 24f, 0.37f) - 0.5f) * 2f * shake * 0.32f;
            float oy = (Mathf.PerlinNoise(1.91f, noiseTime * 24f) - 0.5f) * 2f * shake * 0.32f;
            float roll = (Mathf.PerlinNoise(noiseTime * 17f, 4.2f) - 0.5f) * 2f * shake * 2.4f;

            kick.Step(Vector2.zero, 4.5f, 0.45f, dt);
            zoom.Step(0f, 3.5f, 0.5f, dt);

            cam.orthographicSize = size * (1f - Mathf.Clamp(zoom.Value, -0.1f, 0.1f));
            Vector2 c = view.center + new Vector2(ox, oy) + kick.Value;
            cam.transform.SetPositionAndRotation(new Vector3(c.x, c.y, -10f), Quaternion.Euler(0f, 0f, roll));
            Draw.Px = 2f * cam.orthographicSize / Mathf.Max(1, cam.pixelHeight);
        }
    }
}
