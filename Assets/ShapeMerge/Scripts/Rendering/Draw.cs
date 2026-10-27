using System.Collections.Generic;
using UnityEngine;

namespace ShapeMerge
{
    /// <summary>
    /// Anti-aliased 2D primitives written straight into mesh layers. Edges get a one-pixel
    /// alpha fringe, so everything stays smooth without MSAA. Colours are linear and
    /// premultiplied (see <see cref="Palette.Pm"/>).
    /// </summary>
    public static class Draw
    {
        /// <summary>World size of one screen pixel; the camera rig refreshes it every frame.</summary>
        public static float Px = 0.01f;

        public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        static readonly List<Vector2> scratch = new List<Vector2>(256);

        public static int Segments(float radius) => Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / (Px * 5f)), 12, 96);

        public static void Disc(MeshLayer l, Vector2 c, float r, Color col)
        {
            if (r <= 0f) return;
            int segs = Segments(r);
            float half = Px * 0.5f;
            float inner = Mathf.Max(0f, r - half), outer = r + half;
            int center = l.Vertex(c, col);
            int first = l.VertexCount;
            for (int i = 0; i < segs; i++)
            {
                float a = i * (2f * Mathf.PI / segs);
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                l.Vertex(c + d * inner, col);
                l.Vertex(c + d * outer, Clear);
            }
            for (int i = 0; i < segs; i++)
            {
                int a0 = first + i * 2, b0 = first + (i + 1) % segs * 2;
                l.Triangle(center, a0, b0);
                l.Quad(a0, a0 + 1, b0 + 1, b0);
            }
        }

        /// <summary>A band of the given width centred on a circle of <paramref name="radius"/>.</summary>
        public static void Arc(MeshLayer l, Vector2 c, float radius, float width, Color col, float startAngle, float sweep)
        {
            if (radius <= 0f || Mathf.Abs(sweep) < 1e-4f) return;
            if (width < Px)
            {
                // Thinner than a pixel: draw one pixel wide but fainter.
                col = Palette.Scale(col, Mathf.Max(0f, width) / Px);
                width = Px;
            }
            int segs = Mathf.Max(3, Mathf.CeilToInt(Segments(radius + width) * Mathf.Abs(sweep) / (2f * Mathf.PI)));
            float half = Px * 0.5f;
            float r0 = Mathf.Max(0f, radius - width * 0.5f - half), r1 = Mathf.Max(0f, radius - width * 0.5f + half);
            float r2 = radius + width * 0.5f - half, r3 = radius + width * 0.5f + half;
            if (r2 < r1) r1 = r2 = radius;

            int first = l.VertexCount;
            for (int i = 0; i <= segs; i++)
            {
                float a = startAngle + sweep * i / segs;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                l.Vertex(c + d * r0, Clear);
                l.Vertex(c + d * r1, col);
                l.Vertex(c + d * r2, col);
                l.Vertex(c + d * r3, Clear);
            }
            for (int i = 0; i < segs; i++)
            {
                int a = first + i * 4, b = a + 4;
                l.Quad(a, a + 1, b + 1, b);
                l.Quad(a + 1, a + 2, b + 2, b + 1);
                l.Quad(a + 2, a + 3, b + 3, b + 2);
            }
        }

        public static void Ring(MeshLayer l, Vector2 c, float radius, float width, Color col) =>
            Arc(l, c, radius, width, col, 0f, 2f * Mathf.PI);

        /// <summary>A capsule (line with round caps).</summary>
        public static void Line(MeshLayer l, Vector2 a, Vector2 b, float width, Color col)
        {
            float r = width * 0.5f;
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f)
            {
                Disc(l, a, r, col);
                return;
            }
            d /= len;
            var n = new Vector2(-d.y, d.x);
            int segs = Mathf.Max(3, Segments(r) / 2);
            scratch.Clear();
            for (int i = 0; i <= segs; i++)
            {
                float t = Mathf.PI * i / segs;
                scratch.Add(b + (-n * Mathf.Cos(t) + d * Mathf.Sin(t)) * r);
            }
            for (int i = 0; i <= segs; i++)
            {
                float t = Mathf.PI * i / segs;
                scratch.Add(a + (n * Mathf.Cos(t) - d * Mathf.Sin(t)) * r);
            }
            Polygon(l, scratch, col);
        }

        /// <summary>Fills a CCW star-shaped polygon (fan from its centroid) with an AA edge.</summary>
        public static void Polygon(MeshLayer l, List<Vector2> pts, Color col)
        {
            int n = pts.Count;
            if (n < 3) return;
            Vector2 c = Vector2.zero;
            for (int i = 0; i < n; i++) c += pts[i];
            c /= n;
            float half = Px * 0.5f;
            int center = l.Vertex(c, col);
            int first = l.VertexCount;
            for (int i = 0; i < n; i++)
            {
                Vector2 nrm = VertexNormal(pts, i);
                l.Vertex(pts[i] - nrm * half, col);
                l.Vertex(pts[i] + nrm * half, Clear);
            }
            for (int i = 0; i < n; i++)
            {
                int a0 = first + i * 2, b0 = first + (i + 1) % n * 2;
                l.Triangle(center, a0, b0);
                l.Quad(a0, a0 + 1, b0 + 1, b0);
            }
        }

        /// <summary>A stroked polyline with mitred joins and AA on both sides.</summary>
        public static void Stroke(MeshLayer l, List<Vector2> path, float width, Color col, bool closed)
        {
            int n = path.Count;
            if (n < 2) return;
            float half = width * 0.5f, aa = Px * 0.5f;
            int first = l.VertexCount;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = closed ? path[(i + n - 1) % n] : path[Mathf.Max(0, i - 1)];
                Vector2 next = closed ? path[(i + 1) % n] : path[Mathf.Min(n - 1, i + 1)];
                Vector2 t1 = (path[i] - prev).normalized, t2 = (next - path[i]).normalized;
                if (t1.sqrMagnitude < 1e-8f) t1 = t2;
                if (t2.sqrMagnitude < 1e-8f) t2 = t1;
                Vector2 t = (t1 + t2).normalized;
                if (t.sqrMagnitude < 1e-8f) t = t2;
                var nrm = new Vector2(-t.y, t.x);
                float miter = 1f / Mathf.Max(0.35f, Vector2.Dot(nrm, new Vector2(-t2.y, t2.x)));
                Vector2 p = path[i];
                l.Vertex(p + nrm * ((half + aa) * miter), Clear);
                l.Vertex(p + nrm * ((half - aa) * miter), col);
                l.Vertex(p - nrm * ((half - aa) * miter), col);
                l.Vertex(p - nrm * ((half + aa) * miter), Clear);
            }
            int segments = closed ? n : n - 1;
            for (int i = 0; i < segments; i++)
            {
                int a = first + i * 4, b = first + (i + 1) % n * 4;
                l.Quad(a, a + 1, b + 1, b);
                l.Quad(a + 1, a + 2, b + 2, b + 1);
                l.Quad(a + 2, a + 3, b + 3, b + 2);
            }
            if (!closed)
            {
                Disc(l, path[0], half, col);
                Disc(l, path[n - 1], half, col);
            }
        }

        /// <summary>A soft round sprite: the shader turns the [-1, 1] UVs into a smooth falloff.</summary>
        public static void Soft(MeshLayer l, Vector2 c, float r, Color col)
        {
            int a = l.Vertex(c + new Vector2(-r, -r), col, new Vector2(-1f, -1f));
            int b = l.Vertex(c + new Vector2(r, -r), col, new Vector2(1f, -1f));
            int d = l.Vertex(c + new Vector2(r, r), col, new Vector2(1f, 1f));
            int e = l.Vertex(c + new Vector2(-r, r), col, new Vector2(-1f, 1f));
            l.Quad(a, b, d, e);
        }

        public static void Ellipse(MeshLayer l, Vector2 c, Vector2 axis, float ru, float rv, Color col)
        {
            var v = new Vector2(-axis.y, axis.x);
            int segs = Mathf.Clamp(Segments(Mathf.Max(ru, rv)), 12, 40);
            scratch.Clear();
            for (int i = 0; i < segs; i++)
            {
                float a = i * (2f * Mathf.PI / segs);
                scratch.Add(c + axis * (Mathf.Cos(a) * ru) + v * (Mathf.Sin(a) * rv));
            }
            Polygon(l, scratch, col);
        }

        /// <summary>Writes a CCW rounded rectangle outline into <paramref name="output"/>.</summary>
        public static void RoundedRect(List<Vector2> output, Vector2 center, Vector2 half, float radius, float angle)
        {
            output.Clear();
            radius = Mathf.Clamp(radius, 0.001f, Mathf.Min(half.x, half.y));
            int segs = Mathf.Max(3, Segments(radius) / 4);
            float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
            float ix = half.x - radius, iy = half.y - radius;
            for (int k = 0; k < 4; k++)
            {
                float cx = k == 0 || k == 1 ? ix : -ix;
                float cy = k == 1 || k == 2 ? iy : -iy;
                float start = -0.5f * Mathf.PI + k * 0.5f * Mathf.PI;
                for (int s = 0; s <= segs; s++)
                {
                    float a = start + 0.5f * Mathf.PI * s / segs;
                    float x = cx + Mathf.Cos(a) * radius, y = cy + Mathf.Sin(a) * radius;
                    output.Add(center + new Vector2(cs * x - sn * y, sn * x + cs * y));
                }
            }
        }

        public static void Circle(List<Vector2> output, Vector2 center, float radius)
        {
            output.Clear();
            int segs = Segments(radius);
            for (int i = 0; i < segs; i++)
            {
                float a = i * (2f * Mathf.PI / segs);
                output.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }

        /// <summary>Smooth closed Catmull-Rom curve through the points.</summary>
        public static void SmoothClosed(Vector2[] pts, int n, int subdivisions, List<Vector2> output)
        {
            output.Clear();
            float inv = 1f / subdivisions;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = pts[i == 0 ? n - 1 : i - 1], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
                for (int s = 0; s < subdivisions; s++)
                {
                    float t = s * inv, t2 = t * t, t3 = t2 * t;
                    float x = 0.5f * (2f * p1.x + (p2.x - p0.x) * t + (2f * p0.x - 5f * p1.x + 4f * p2.x - p3.x) * t2 + (3f * p1.x - p0.x - 3f * p2.x + p3.x) * t3);
                    float y = 0.5f * (2f * p1.y + (p2.y - p0.y) * t + (2f * p0.y - 5f * p1.y + 4f * p2.y - p3.y) * t2 + (3f * p1.y - p0.y - 3f * p2.y + p3.y) * t3);
                    output.Add(new Vector2(x, y));
                }
            }
        }

        public static Vector2 VertexNormal(List<Vector2> p, int i)
        {
            int n = p.Count;
            Vector2 t = p[(i + 1) % n] - p[(i + n - 1) % n];
            float m = t.magnitude;
            return m > 1e-7f ? new Vector2(t.y / m, -t.x / m) : Vector2.zero;
        }
    }
}
