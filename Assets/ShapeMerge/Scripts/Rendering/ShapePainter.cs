using System.Collections.Generic;
using UnityEngine;

namespace ShapeMerge
{
    /// <summary>How a soft shape is painted. Colours are linear, not premultiplied.</summary>
    public struct ShapeLook
    {
        public Color Fill;
        public Color Outline;
        public float OutlineWidth;
        public float Size;      // characteristic radius; scales the shading features
        public float Shine;     // 0..1 gloss strength
        public float Flash;     // 0..1 white-out
        public float Glow;      // extra HDR brightness that feeds bloom
        public float Shadow;    // drop shadow opacity
        public float Alpha;

        public static ShapeLook Of(Color fill, Color outline, float size) => new ShapeLook
        {
            Fill = fill,
            Outline = outline,
            OutlineWidth = 0.055f,
            Size = size,
            Shine = 1f,
            Shadow = 0.3f,
            Alpha = 1f,
        };
    }

    /// <summary>
    /// Paints a closed CCW outline as a glossy gummy candy: drop shadow, thick outline,
    /// pillow shading, inner shade, gloss crescent, specular dot and an optional white flash.
    /// Every element follows the outline, so squash and stretch read perfectly.
    /// </summary>
    public static class ShapePainter
    {
        public static readonly Vector2 LightDir = new Vector2(-0.45f, 0.89f).normalized;
        public static readonly Vector2 ShadowOffset = new Vector2(0.07f, -0.11f);

        static Vector2[] normals = new Vector2[1024];
        static float[] light = new float[1024];

        public static void Paint(ShapeLayers layers, List<Vector2> outline, in ShapeLook look)
        {
            int n = outline.Count;
            if (n < 3 || look.Alpha <= 0.001f) return;
            if (normals.Length < n)
            {
                normals = new Vector2[n * 2];
                light = new float[n * 2];
            }

            Vector2 c = Vector2.zero;
            for (int i = 0; i < n; i++) c += outline[i];
            c /= n;

            float support = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 nrm = Draw.VertexNormal(outline, i);
                if (nrm.sqrMagnitude < 0.5f) nrm = (outline[i] - c).normalized;
                normals[i] = nrm;
                light[i] = Vector2.Dot(nrm, LightDir);
                support = Mathf.Max(support, Vector2.Dot(outline[i] - c, LightDir));
            }

            float a = look.Alpha;
            float px = Draw.Px;
            float ow = look.OutlineWidth;

            if (look.Shadow > 0f) PaintShadow(layers.Shadow, outline, c, n, look, ow);
            PaintOutline(layers.Outline, outline, n, Palette.Pm(look.Outline, a), ow, px);

            MeshLayer l = layers.Fill;
            Color baseColor = look.Fill;
            float glow = look.Glow;

            // Pillow fill: bright centre, lit rim on the top-left, deeper rim bottom-right.
            int center = l.Vertex(c, Palette.Pm(Palette.Mul(baseColor, 1.08f + glow), a));
            int mid = l.VertexCount;
            Color midColor = Palette.Pm(Palette.Mul(baseColor, 1.03f + glow), a);
            for (int i = 0; i < n; i++) l.Vertex(c + (outline[i] - c) * 0.6f, midColor);
            int rim = l.VertexCount;
            for (int i = 0; i < n; i++) l.Vertex(outline[i], Palette.Pm(Palette.Mul(baseColor, 0.84f + 0.16f * light[i] + glow), a));
            int fringe = l.VertexCount;
            for (int i = 0; i < n; i++) l.Vertex(outline[i] + normals[i] * px, Draw.Clear);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                l.Triangle(center, mid + i, mid + j);
                l.Quad(mid + i, rim + i, rim + j, mid + j);
                l.Quad(rim + i, fringe + i, fringe + j, rim + j);
            }

            // Inner shade hugging the bottom-right edge.
            float shadeDepth = look.Size * 0.3f;
            int shade = l.VertexCount;
            for (int i = 0; i < n; i++)
            {
                float k = 0.2f * Ease.Smooth(0.05f, 0.85f, -light[i]) * a;
                l.Vertex(outline[i], new Color(0f, 0f, 0f, k));
                l.Vertex(Inset(outline[i], c, shadeDepth), Draw.Clear);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                l.Quad(shade + i * 2, shade + i * 2 + 1, shade + j * 2 + 1, shade + j * 2);
            }

            if (look.Shine > 0f)
            {
                // Gloss crescent along the lit edge, soft on both sides.
                float d0 = look.Size * 0.07f + px, d1 = d0 + look.Size * 0.05f, d2 = d0 + look.Size * 0.24f;
                int gloss = l.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    float k = 0.5f * look.Shine * Ease.Smooth(0.3f, 0.95f, light[i]) * a;
                    l.Vertex(Inset(outline[i], c, d0), Draw.Clear);
                    l.Vertex(Inset(outline[i], c, d1), new Color(k, k, k, k));
                    l.Vertex(Inset(outline[i], c, d2), Draw.Clear);
                }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    int pi = gloss + i * 3, pj = gloss + j * 3;
                    l.Quad(pi, pi + 1, pj + 1, pj);
                    l.Quad(pi + 1, pi + 2, pj + 2, pj + 1);
                }

                // Specular dot.
                float s = 0.8f * look.Shine * a;
                Vector2 tangent = new Vector2(LightDir.y, -LightDir.x);
                Draw.Ellipse(l, c + LightDir * (support * 0.55f), tangent, look.Size * 0.14f, look.Size * 0.075f, new Color(s, s, s, s));
            }

            if (look.Flash > 0f)
            {
                // A touch brighter than white so the flash blooms.
                float f = look.Flash * a, hdr = f * 1.8f;
                Color fc = new Color(hdr, hdr, hdr, f);
                int fc0 = l.Vertex(c, fc);
                int ring = l.VertexCount;
                for (int i = 0; i < n; i++) l.Vertex(outline[i] + normals[i] * ow, fc);
                for (int i = 0; i < n; i++) l.Triangle(fc0, ring + i, ring + (i + 1) % n);
            }
        }

        static void PaintShadow(MeshLayer l, List<Vector2> outline, Vector2 c, int n, in ShapeLook look, float ow)
        {
            Color sc = new Color(0f, 0f, 0f, look.Shadow * look.Alpha);
            float soft = Mathf.Max(0.06f, look.Size * 0.14f);
            Vector2 o = ShadowOffset;
            int center = l.Vertex(c + o, sc);
            int first = l.VertexCount;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = outline[i] + o;
                l.Vertex(p + normals[i] * (ow - soft * 0.5f), sc);
                l.Vertex(p + normals[i] * (ow + soft * 0.5f), Draw.Clear);
            }
            for (int i = 0; i < n; i++)
            {
                int a0 = first + i * 2, b0 = first + (i + 1) % n * 2;
                l.Triangle(center, a0, b0);
                l.Quad(a0, a0 + 1, b0 + 1, b0);
            }
        }

        static void PaintOutline(MeshLayer l, List<Vector2> outline, int n, Color col, float ow, float px)
        {
            int first = l.VertexCount;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = outline[i], nrm = normals[i];
                l.Vertex(p - nrm * px, col);
                l.Vertex(p + nrm * ow, col);
                l.Vertex(p + nrm * (ow + px), Draw.Clear);
            }
            for (int i = 0; i < n; i++)
            {
                int a = first + i * 3, b = first + (i + 1) % n * 3;
                l.Quad(a, a + 1, b + 1, b);
                l.Quad(a + 1, a + 2, b + 2, b + 1);
            }
        }

        // Moves a point towards the centre by a fixed distance; radial insets never fold over.
        static Vector2 Inset(Vector2 p, Vector2 c, float d)
        {
            Vector2 v = p - c;
            float m = v.magnitude;
            if (m < 1e-6f) return p;
            return p - v * (Mathf.Min(d, m * 0.85f) / m);
        }
    }
}
