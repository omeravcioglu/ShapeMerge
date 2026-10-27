using System.Collections.Generic;
using UnityEngine;

namespace ShapeMerge
{
    public enum ShapeKind { Circle, Polygon, Star }

    /// <summary>Builds rounded, evenly sampled outlines for every shape kind.</summary>
    public static class ShapeGeometry
    {
        const int CornerSegments = 16;

        /// <summary>
        /// Returns a counter-clockwise outline of <paramref name="count"/> evenly spaced points,
        /// centred on its point average and scaled to enclose exactly <paramref name="area"/>.
        /// Sampling starts on a corner tip so symmetric counts land a point on every tip.
        /// </summary>
        public static Vector2[] Build(ShapeKind kind, int sides, float roundness, float starInner, float area, int count)
        {
            var dense = new List<Vector2>(512);
            int start = BuildDense(kind, sides, roundness, starInner, dense);
            Vector2[] pts = Resample(dense, start, count);

            Vector2 mean = Vector2.zero;
            for (int i = 0; i < pts.Length; i++) mean += pts[i];
            mean /= pts.Length;
            for (int i = 0; i < pts.Length; i++) pts[i] -= mean;

            float scale = Mathf.Sqrt(area / Mathf.Max(SoftBody.PolygonArea(pts, pts.Length), 1e-6f));
            for (int i = 0; i < pts.Length; i++) pts[i] *= scale;
            return pts;
        }

        /// <summary>Perimeter of the shape when scaled to the given area.</summary>
        public static float Perimeter(ShapeKind kind, int sides, float roundness, float starInner, float area)
        {
            var dense = new List<Vector2>(512);
            BuildDense(kind, sides, roundness, starInner, dense);
            Vector2[] arr = dense.ToArray();
            float scale = Mathf.Sqrt(area / Mathf.Max(SoftBody.PolygonArea(arr, arr.Length), 1e-6f));
            float len = 0f;
            for (int i = 0, j = arr.Length - 1; i < arr.Length; j = i++) len += (arr[i] - arr[j]).magnitude;
            return len * scale;
        }

        // Unit-circumradius outline; returns the index of the first corner tip.
        static int BuildDense(ShapeKind kind, int sides, float roundness, float starInner, List<Vector2> output)
        {
            if (kind == ShapeKind.Circle)
            {
                const int n = 256;
                for (int i = 0; i < n; i++)
                {
                    float a = Mathf.PI * 0.5f + 2f * Mathf.PI * i / n;
                    output.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
                }
                return 0;
            }

            Vector2[] corners;
            float[] radii;
            if (kind == ShapeKind.Star)
            {
                corners = new Vector2[sides * 2];
                radii = new float[sides * 2];
                for (int k = 0; k < sides * 2; k++)
                {
                    bool outer = (k & 1) == 0;
                    float a = Mathf.PI * 0.5f + Mathf.PI * k / sides;
                    float r = outer ? 1f : starInner;
                    corners[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    radii[k] = outer ? roundness : roundness * 0.7f;
                }
            }
            else
            {
                // Odd shapes point up, even shapes sit on a flat side.
                float start = Mathf.PI * 0.5f + ((sides & 1) == 0 ? Mathf.PI / sides : 0f);
                corners = new Vector2[sides];
                radii = new float[sides];
                for (int k = 0; k < sides; k++)
                {
                    float a = start + 2f * Mathf.PI * k / sides;
                    corners[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    radii[k] = roundness;
                }
            }

            var tips = new List<int>(corners.Length);
            RoundCorners(corners, radii, output, tips);
            return tips.Count > 0 ? tips[0] : 0;
        }

        // Replaces each corner with a tangent arc; works for convex and concave corners alike.
        static void RoundCorners(Vector2[] c, float[] radius, List<Vector2> output, List<int> tips)
        {
            int n = c.Length;
            for (int k = 0; k < n; k++)
            {
                Vector2 v = c[k], p = c[(k + n - 1) % n], q = c[(k + 1) % n];
                Vector2 d1 = (p - v).normalized, d2 = (q - v).normalized;
                float half = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d1, d2), -1f, 1f)) * 0.5f;
                if (radius[k] <= 0f || half < 1e-3f || half > Mathf.PI * 0.5f - 1e-3f)
                {
                    tips.Add(output.Count);
                    output.Add(v);
                    continue;
                }

                float r = radius[k];
                float tangent = r / Mathf.Tan(half);
                float maxTangent = 0.45f * Mathf.Min((p - v).magnitude, (q - v).magnitude);
                if (tangent > maxTangent)
                {
                    tangent = maxTangent;
                    r = tangent * Mathf.Tan(half);
                }

                Vector2 t1 = v + d1 * tangent, t2 = v + d2 * tangent;
                Vector2 center = v + (d1 + d2).normalized * (r / Mathf.Sin(half));
                float a1 = Mathf.Atan2(t1.y - center.y, t1.x - center.x);
                float a2 = Mathf.Atan2(t2.y - center.y, t2.x - center.x);
                float da = a2 - a1;
                while (da > Mathf.PI) da -= 2f * Mathf.PI;
                while (da < -Mathf.PI) da += 2f * Mathf.PI;

                for (int s = 0; s <= CornerSegments; s++)
                {
                    if (s == CornerSegments / 2) tips.Add(output.Count);
                    float a = a1 + da * s / CornerSegments;
                    output.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }
        }

        static Vector2[] Resample(List<Vector2> pts, int start, int count)
        {
            int n = pts.Count;
            var cum = new float[n + 1];
            for (int i = 0; i < n; i++)
                cum[i + 1] = cum[i] + (pts[(start + i + 1) % n] - pts[(start + i) % n]).magnitude;

            float total = cum[n];
            var result = new Vector2[count];
            int seg = 0;
            for (int k = 0; k < count; k++)
            {
                float target = total * k / count;
                while (seg < n - 1 && cum[seg + 1] < target) seg++;
                float segLen = cum[seg + 1] - cum[seg];
                float t = segLen > 1e-9f ? (target - cum[seg]) / segLen : 0f;
                result[k] = Vector2.Lerp(pts[(start + seg) % n], pts[(start + seg + 1) % n], t);
            }
            return result;
        }
    }
}
