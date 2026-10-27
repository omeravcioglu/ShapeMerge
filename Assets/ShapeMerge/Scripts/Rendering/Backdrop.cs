using System.Collections.Generic;
using UnityEngine;

namespace ShapeMerge
{
    /// <summary>Background gradient with drifting bokeh, the glass tub, its rim and the danger line.</summary>
    public sealed class Backdrop
    {
        struct Mote
        {
            public Vector2 Pos;
            public float Size, Speed, Phase, Alpha;
            public Color Color;
        }

        readonly MeshLayer background, glass, front;
        readonly Mote[] motes = new Mote[26];
        readonly List<Vector2> path = new List<Vector2>(128);

        readonly Color top = Palette.LinearHex("#2B2D66");
        readonly Color bottom = Palette.LinearHex("#121331");
        readonly Color haze = Palette.LinearHex("#5B5FD6");
        readonly Color wall = Palette.LinearHex("#E4E3FF");
        readonly Color wallShade = Palette.LinearHex("#8F8DD6");
        readonly Color rail = Palette.LinearHex("#3E3F86");
        readonly Color danger = Palette.LinearHex("#FF3B5C");

        public Backdrop(Material material, Transform parent, ShapeTier[] tiers)
        {
            background = new MeshLayer("Background", material, -100, parent);
            glass = new MeshLayer("Tub Glass", material, -60, parent);
            front = new MeshLayer("Tub Walls", material, 10, parent);

            var rng = new System.Random(99);
            for (int i = 0; i < motes.Length; i++)
            {
                motes[i] = new Mote
                {
                    Pos = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()),
                    Size = 0.25f + (float)rng.NextDouble() * 1.1f,
                    Speed = 0.012f + (float)rng.NextDouble() * 0.025f,
                    Phase = (float)rng.NextDouble() * 6.28f,
                    Alpha = 0.05f + (float)rng.NextDouble() * 0.1f,
                    Color = Palette.Linear(tiers[rng.Next(tiers.Length)].Color),
                };
            }
        }

        public void Render(Camera cam, Arena arena, float time, float dangerLevel)
        {
            RenderBackground(cam, arena, time);
            RenderTub(arena, time, dangerLevel);
        }

        void RenderBackground(Camera cam, Arena arena, float time)
        {
            MeshLayer l = background;
            l.Clear();

            // Oversized so camera shake and roll never reveal an edge.
            float h = cam.orthographicSize * 1.4f, w = h * cam.aspect;
            Vector2 c = cam.transform.position;
            int a = l.Vertex(c + new Vector2(-w, -h), Palette.Pm(bottom, 1f));
            int b = l.Vertex(c + new Vector2(w, -h), Palette.Pm(bottom, 1f));
            int d = l.Vertex(c + new Vector2(w, h), Palette.Pm(top, 1f));
            int e = l.Vertex(c + new Vector2(-w, h), Palette.Pm(top, 1f));
            l.Quad(a, b, d, e);

            // Soft haze behind the tub.
            Draw.Soft(l, new Vector2(0f, arena.Top * 0.5f), arena.Width * 1.25f, Palette.Glow(haze, 0.22f));

            // Bokeh drifting slowly upward, wrapping around the view.
            for (int i = 0; i < motes.Length; i++)
            {
                Mote m = motes[i];
                float y = Mathf.Repeat(m.Pos.y + time * m.Speed, 1f);
                float x = m.Pos.x + Mathf.Sin(time * 0.3f + m.Phase) * 0.02f;
                var p = new Vector2(c.x + (x * 2f - 1f) * w, c.y + (y * 2f - 1f) * h);
                float breathe = 0.75f + 0.25f * Mathf.Sin(time * 0.8f + m.Phase);
                Draw.Soft(l, p, m.Size, Palette.Glow(m.Color, m.Alpha * breathe));
            }
            l.Apply();
        }

        void RenderTub(Arena arena, float time, float dangerLevel)
        {
            float left = arena.Left, right = arena.Right, floor = arena.Floor, topY = arena.Top, rc = arena.CornerRadius;

            // Glass interior with a faint vertical gradient.
            glass.Clear();
            BuildTubPath(arena, 0f, path); // the fan closes the open top
            Vector2 center = new Vector2(0f, topY * 0.5f);
            int ci = glass.Vertex(center, new Color(0.05f, 0.05f, 0.09f, 0.05f));
            int first = glass.VertexCount;
            for (int i = 0; i < path.Count; i++)
            {
                float k = Mathf.InverseLerp(topY, floor, path[i].y);
                float alpha = Mathf.Lerp(0.035f, 0.1f, k);
                glass.Vertex(path[i], new Color(alpha * 0.85f, alpha * 0.85f, alpha, alpha));
            }
            for (int i = 0; i < path.Count; i++) glass.Triangle(ci, first + i, first + (i + 1) % path.Count);

            // Light streak on the glass.
            Draw.Line(glass, new Vector2(left + 0.35f, topY - 0.6f), new Vector2(left + 0.35f, floor + rc + 0.5f), 0.12f, new Color(0.05f, 0.05f, 0.06f, 0.05f));

            // Danger zone tint above the line.
            if (dangerLevel > 0f)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 10f);
                float k = dangerLevel * (0.08f + 0.1f * pulse);
                int q0 = glass.Vertex(new Vector2(left, arena.DangerY), Draw.Clear);
                int q1 = glass.Vertex(new Vector2(right, arena.DangerY), Draw.Clear);
                int q2 = glass.Vertex(new Vector2(right, topY + 0.6f), Palette.Pm(danger, k));
                int q3 = glass.Vertex(new Vector2(left, topY + 0.6f), Palette.Pm(danger, k));
                glass.Quad(q0, q1, q2, q3);
            }
            glass.Apply();

            front.Clear();

            // Rail the cannon slides on.
            Draw.Line(front, new Vector2(left - 0.45f, arena.RailY), new Vector2(right + 0.45f, arena.RailY), 0.14f, Palette.Pm(rail, 0.9f));

            // Thick rounded U for the tub walls, with a darker under-stroke for depth.
            const float wallWidth = 0.2f;
            BuildTubPath(arena, wallWidth * 0.5f, path);
            Draw.Stroke(front, path, wallWidth + 0.06f, Palette.Pm(wallShade, 1f), false);
            Draw.Stroke(front, path, wallWidth - 0.02f, Palette.Pm(wall, 1f), false);
            BuildTubPath(arena, wallWidth * 0.25f, path);
            Draw.Stroke(front, path, 0.035f, Palette.Pm(Palette.Mul(wall, 1.15f), 0.9f), false);

            // Dashed danger line; heats up red as danger builds.
            float dangerPulse = dangerLevel > 0f ? 0.5f + 0.5f * Mathf.Sin(time * 12f) : 0f;
            Color lineColor = Palette.Lerp(new Color(0.22f, 0.22f, 0.25f, 0.22f), Palette.Glow(danger, 1.4f + dangerPulse), dangerLevel);
            if (dangerLevel > 0f) lineColor.a = Mathf.Lerp(0.22f, 0.6f, dangerLevel);
            float dash = 0.26f, gap = 0.16f, scroll = Mathf.Repeat(time * 0.4f, dash + gap);
            for (float x = left + 0.18f - scroll; x < right - 0.18f; x += dash + gap)
            {
                float x0 = Mathf.Max(x, left + 0.18f), x1 = Mathf.Min(x + dash, right - 0.18f);
                if (x1 - x0 > 0.02f) Draw.Line(front, new Vector2(x0, arena.DangerY), new Vector2(x1, arena.DangerY), 0.055f, lineColor);
            }
            front.Apply();
        }

        // Inner tub outline offset outward by `offset`: up the left wall, round the floor, up the right.
        static void BuildTubPath(Arena arena, float offset, List<Vector2> output)
        {
            output.Clear();
            float left = arena.Left - offset, right = arena.Right + offset, floor = arena.Floor - offset;
            float rc = arena.CornerRadius + offset, topY = arena.Top;
            const int segs = 10;
            output.Add(new Vector2(left, topY));
            for (int s = 0; s <= segs; s++)
            {
                float a = Mathf.PI + 0.5f * Mathf.PI * s / segs;
                output.Add(new Vector2(left + rc + Mathf.Cos(a) * rc, floor + rc + Mathf.Sin(a) * rc));
            }
            for (int s = 0; s <= segs; s++)
            {
                float a = 1.5f * Mathf.PI + 0.5f * Mathf.PI * s / segs;
                output.Add(new Vector2(right - rc + Mathf.Cos(a) * rc, floor + rc + Mathf.Sin(a) * rc));
            }
            output.Add(new Vector2(right, topY));
        }
    }
}
