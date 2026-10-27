using UnityEngine;

namespace ShapeMerge
{
    /// <summary>Damped spring for juicy one-dimensional motion: scale pops, recoil, aim.</summary>
    public struct SpringFloat
    {
        public float Value;
        public float Velocity;

        public SpringFloat(float value)
        {
            Value = value;
            Velocity = 0f;
        }

        /// <param name="frequency">Oscillation frequency in Hz.</param>
        /// <param name="damping">Damping ratio: 1 settles without overshoot, lower is bouncier.</param>
        public float Step(float target, float frequency, float damping, float dt)
        {
            float w = 2f * Mathf.PI * frequency;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * w / 0.2f), 1, 32); // stays stable on frame hitches
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Velocity += ((target - Value) * w * w - Velocity * 2f * damping * w) * h;
                Value += Velocity * h;
            }
            return Value;
        }
    }

    public struct SpringVector2
    {
        public Vector2 Value;
        public Vector2 Velocity;

        public SpringVector2(Vector2 value)
        {
            Value = value;
            Velocity = Vector2.zero;
        }

        public Vector2 Step(Vector2 target, float frequency, float damping, float dt)
        {
            float w = 2f * Mathf.PI * frequency;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * w / 0.2f), 1, 32);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Velocity += ((target - Value) * (w * w) - Velocity * (2f * damping * w)) * h;
                Value += Velocity * h;
            }
            return Value;
        }
    }

    public static class Ease
    {
        public static float OutCubic(float t)
        {
            t = 1f - Mathf.Clamp01(t);
            return 1f - t * t * t;
        }

        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + t * t * ((overshoot + 1f) * t + overshoot);
        }

        public static float InQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t;
        }

        public static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
