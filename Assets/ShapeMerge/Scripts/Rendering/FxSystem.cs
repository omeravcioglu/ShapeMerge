using System.Collections.Generic;
using UnityEngine;

namespace ShapeMerge
{
    /// <summary>
    /// Pooled particles and shockwave rings drawn into one mesh: confetti in the shapes' own
    /// forms, twinkling sparkles, glows, dust and smoke.
    /// </summary>
    public sealed class FxSystem
    {
        enum Kind : byte { Confetti, Sparkle, Puff, Glow }

        struct Particle
        {
            public Kind Kind;
            public int Shape;           // index into the unit outline table
            public Vector2 Pos, Vel;
            public float Rot, Spin;
            public float Size, Grow;    // size multiplier reached at end of life
            public float Life, MaxLife;
            public float Drag, Gravity;
            public Color Color;         // linear; glows are additive
        }

        struct Shockwave
        {
            public Vector2 Pos;
            public float From, To, Width;
            public float Life, MaxLife;
            public Color Color;         // premultiplied or additive
        }

        const int MaxParticles = 2000;

        readonly Particle[] particles = new Particle[MaxParticles];
        readonly List<Shockwave> rings = new List<Shockwave>(32);
        readonly MeshLayer layer;
        readonly ShapeTier[] tiers;
        readonly Vector2[][] unitShapes;
        readonly System.Random rng = new System.Random(1234);
        readonly Color white = Palette.LinearHex("#FFFFFF");
        readonly Color sparkleTint = Palette.LinearHex("#FFF4C9");
        int count;

        public FxSystem(Material material, int sortingOrder, Transform parent, ShapeTier[] tiers)
        {
            this.tiers = tiers;
            layer = new MeshLayer("Fx", material, sortingOrder, parent);

            // Unit outlines per tier (simplified), plus a 4-point sparkle at the end.
            unitShapes = new Vector2[tiers.Length + 1][];
            for (int t = 0; t < tiers.Length; t++)
            {
                ShapeTier tier = tiers[t];
                int sides = tier.Kind == ShapeKind.Circle ? 12 : tier.Kind == ShapeKind.Star ? tier.Sides * 2 : tier.Sides;
                var pts = new Vector2[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = Mathf.PI * 0.5f + 2f * Mathf.PI * i / sides;
                    float r = tier.Kind == ShapeKind.Star && (i & 1) == 1 ? 0.5f : 1f;
                    pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }
                unitShapes[t] = pts;
            }
            var sparkle = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.PI * 0.5f + Mathf.PI * i / 4f;
                sparkle[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ((i & 1) == 0 ? 1f : 0.22f);
            }
            unitShapes[tiers.Length] = sparkle;
        }

        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        Vector2 RandDir() => Angle(Rand(0f, 2f * Mathf.PI));

        static Vector2 Angle(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));

        void Emit(in Particle p)
        {
            if (count >= MaxParticles) return;
            particles[count++] = p;
        }

        void AddRing(Vector2 pos, float from, float to, float width, float life, Color color)
        {
            rings.Add(new Shockwave { Pos = pos, From = from, To = to, Width = width, MaxLife = life, Color = color });
        }

        public void Clear()
        {
            count = 0;
            rings.Clear();
        }

        // ---------------------------------------------------------------- emitters

        public void MergeBurst(Vector2 pos, ShapeTier from, ShapeTier to, int combo)
        {
            float big = to.Index / (float)(tiers.Length - 1);
            Color toColor = Palette.Linear(to.Color), fromColor = Palette.Linear(from.Color);

            Emit(new Particle { Kind = Kind.Glow, Pos = pos, Size = to.Radius * 2.6f, Grow = 1.5f, MaxLife = 0.32f, Color = Palette.Glow(toColor, 1.4f) });
            AddRing(pos, to.Radius * 0.5f, to.Radius * (2.4f + big), 0.17f + 0.08f * big, 0.5f, Palette.Glow(toColor, 2.2f));
            AddRing(pos, to.Radius * 0.3f, to.Radius * 1.7f, 0.07f, 0.3f, Palette.Glow(white, 1.6f));

            int confetti = 10 + to.Index * 3 + combo * 3;
            for (int i = 0; i < confetti; i++)
            {
                bool useNew = i % 3 == 0;
                Vector2 dir = RandDir();
                float speed = Rand(3f, 8.5f) * (0.85f + 0.12f * to.Index);
                Emit(new Particle
                {
                    Kind = Kind.Confetti,
                    Shape = useNew ? to.Index : from.Index,
                    Pos = pos + dir * to.Radius * Rand(0.2f, 0.7f),
                    Vel = dir * speed + new Vector2(0f, 2.5f),
                    Rot = Rand(0f, 6.28f),
                    Spin = Rand(-12f, 12f),
                    Size = Rand(0.07f, 0.14f) * (1f + 0.08f * to.Index),
                    Grow = 0.2f,
                    MaxLife = Rand(0.55f, 1.05f),
                    Drag = 1.6f,
                    Gravity = 13f,
                    Color = Palette.Mul(useNew ? toColor : fromColor, Rand(0.95f, 1.25f)),
                });
            }

            Sparkles(pos, to.Radius * 1.2f, 5 + to.Index + combo, 0.12f + 0.02f * to.Index);
        }

        public void Sparkles(Vector2 pos, float radius, int amount, float size)
        {
            for (int i = 0; i < amount; i++)
            {
                Vector2 dir = RandDir();
                Emit(new Particle
                {
                    Kind = Kind.Sparkle,
                    Shape = tiers.Length,
                    Pos = pos + dir * radius * Rand(0.3f, 1f),
                    Vel = dir * Rand(0.5f, 2.5f) + new Vector2(0f, 1f),
                    Rot = Rand(-0.3f, 0.3f),
                    Spin = Rand(-2f, 2f),
                    Size = size * Rand(0.6f, 1.3f),
                    Grow = 0f,
                    MaxLife = Rand(0.35f, 0.7f),
                    Drag = 2f,
                    Gravity = 0f,
                    Color = Palette.Glow(sparkleTint, Rand(1.6f, 2.8f)),
                });
            }
        }

        public void Impact(Vector2 pos, Vector2 normal, float strength)
        {
            int puffs = 2 + Mathf.RoundToInt(4f * strength);
            var tangent = new Vector2(-normal.y, normal.x);
            for (int i = 0; i < puffs; i++)
            {
                float side = (i & 1) == 0 ? 1f : -1f;
                Emit(new Particle
                {
                    Kind = Kind.Puff,
                    Pos = pos + tangent * side * Rand(0f, 0.15f),
                    Vel = tangent * side * Rand(1f, 3f) * (0.5f + strength) + normal * Rand(0.2f, 1f),
                    Size = Rand(0.08f, 0.16f) * (0.7f + strength),
                    Grow = 2.4f,
                    MaxLife = Rand(0.3f, 0.5f),
                    Drag = 5f,
                    Gravity = -1.2f,
                    Color = new Color(1f, 1f, 1f, 0.22f + 0.1f * strength),
                });
            }
        }

        public void Muzzle(Vector2 pos, Vector2 dir, ShapeTier tier, float power)
        {
            Color tint = Palette.Linear(tier.Color);
            Emit(new Particle { Kind = Kind.Glow, Pos = pos, Size = 0.55f + 0.4f * power, Grow = 1.4f, MaxLife = 0.14f, Color = Palette.Glow(white, 0.8f + power) });
            AddRing(pos, 0.25f, 0.8f + 0.6f * power, 0.07f, 0.24f, Palette.Glow(tint, 1.8f));
            int puffs = 5 + Mathf.RoundToInt(7f * power);
            var side = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < puffs; i++)
            {
                Emit(new Particle
                {
                    Kind = Kind.Puff,
                    Pos = pos + side * Rand(-0.25f, 0.25f),
                    Vel = dir * Rand(1f, 3f + 5f * power) + side * Rand(-2.2f, 2.2f),
                    Size = Rand(0.1f, 0.2f),
                    Grow = 2.6f,
                    MaxLife = Rand(0.3f, 0.55f),
                    Drag = 5.5f,
                    Gravity = -1.5f,
                    Color = new Color(1f, 1f, 1f, 0.3f),
                });
            }
        }

        public void Pop(Vector2 pos, ShapeTier tier)
        {
            Color c = Palette.Linear(tier.Color);
            AddRing(pos, tier.Radius * 0.4f, tier.Radius * 1.8f, 0.12f, 0.35f, Palette.Glow(c, 2f));
            Emit(new Particle { Kind = Kind.Glow, Pos = pos, Size = tier.Radius * 2f, Grow = 1.3f, MaxLife = 0.25f, Color = Palette.Glow(c, 1.2f) });
            for (int i = 0; i < 9 + tier.Index * 2; i++)
            {
                Vector2 dir = RandDir();
                Emit(new Particle
                {
                    Kind = Kind.Confetti,
                    Shape = tier.Index,
                    Pos = pos + dir * tier.Radius * 0.5f,
                    Vel = dir * Rand(2.5f, 7f) + new Vector2(0f, 2f),
                    Rot = Rand(0f, 6.28f),
                    Spin = Rand(-10f, 10f),
                    Size = Rand(0.07f, 0.13f),
                    Grow = 0.2f,
                    MaxLife = Rand(0.5f, 0.9f),
                    Drag = 1.4f,
                    Gravity = 12f,
                    Color = c,
                });
            }
        }

        public void Supernova(Vector2 pos)
        {
            AddRing(pos, 0.5f, 9f, 0.45f, 0.9f, Palette.Glow(white, 2.5f));
            AddRing(pos, 0.3f, 6.5f, 0.25f, 0.7f, Palette.Glow(Palette.Linear(tiers[tiers.Length - 1].Color), 3f));
            AddRing(pos, 0.2f, 4f, 0.12f, 0.5f, Palette.Glow(sparkleTint, 3f));
            Emit(new Particle { Kind = Kind.Glow, Pos = pos, Size = 6f, Grow = 1.6f, MaxLife = 0.6f, Color = Palette.Glow(white, 2f) });
            for (int i = 0; i < 140; i++)
            {
                int t = rng.Next(tiers.Length);
                Vector2 dir = RandDir();
                Emit(new Particle
                {
                    Kind = Kind.Confetti,
                    Shape = t,
                    Pos = pos + dir * Rand(0f, 1.5f),
                    Vel = dir * Rand(4f, 15f) + new Vector2(0f, 3f),
                    Rot = Rand(0f, 6.28f),
                    Spin = Rand(-14f, 14f),
                    Size = Rand(0.09f, 0.2f),
                    Grow = 0.2f,
                    MaxLife = Rand(0.9f, 1.8f),
                    Drag = 1.2f,
                    Gravity = 10f,
                    Color = Palette.Linear(tiers[t].Color),
                });
            }
            Sparkles(pos, 3f, 40, 0.2f);
        }

        // ---------------------------------------------------------------- simulation

        public void Update(float dt)
        {
            if (dt <= 0f) return;
            for (int i = count - 1; i >= 0; i--)
            {
                ref Particle p = ref particles[i];
                p.Life += dt;
                if (p.Life >= p.MaxLife)
                {
                    particles[i] = particles[--count];
                    continue;
                }
                p.Vel *= Mathf.Exp(-p.Drag * dt);
                p.Vel.y -= p.Gravity * dt;
                p.Pos += p.Vel * dt;
                p.Rot += p.Spin * dt;
            }

            for (int i = rings.Count - 1; i >= 0; i--)
            {
                Shockwave r = rings[i];
                r.Life += dt;
                if (r.Life >= r.MaxLife) rings.RemoveAt(i);
                else rings[i] = r;
            }
        }

        public void Render(float time)
        {
            layer.Clear();

            for (int i = 0; i < rings.Count; i++)
            {
                Shockwave r = rings[i];
                float t = r.Life / r.MaxLife;
                float radius = Mathf.Lerp(r.From, r.To, Ease.OutCubic(t));
                float fade = (1f - t) * (1f - t);
                Draw.Ring(layer, r.Pos, radius, r.Width * (1f - 0.7f * t), Palette.Scale(r.Color, fade));
            }

            for (int i = 0; i < count; i++)
            {
                ref Particle p = ref particles[i];
                float t = p.Life / p.MaxLife;
                float size = p.Size * Mathf.Lerp(1f, p.Grow, t);
                switch (p.Kind)
                {
                    case Kind.Glow:
                        Draw.Soft(layer, p.Pos, size, Palette.Scale(p.Color, (1f - t) * (1f - t)));
                        break;
                    case Kind.Puff:
                        Draw.Soft(layer, p.Pos, size, Palette.Scale(Palette.Pm(p.Color, p.Color.a), 1f - t));
                        break;
                    case Kind.Sparkle:
                    {
                        float twinkle = 0.6f + 0.4f * Mathf.Sin(time * 40f + i * 1.7f);
                        float pop = t < 0.2f ? Ease.OutBack(t / 0.2f) : 1f - (t - 0.2f) / 0.8f;
                        DrawShape(p.Shape, p.Pos, size * pop * twinkle, p.Rot, p.Color);
                        break;
                    }
                    default:
                    {
                        float fade = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                        float pop = t < 0.12f ? Ease.OutBack(t / 0.12f) : 1f;
                        DrawShape(p.Shape, p.Pos, size * pop, p.Rot, Palette.Pm(p.Color, fade));
                        break;
                    }
                }
            }

            layer.Apply();
        }

        void DrawShape(int shape, Vector2 pos, float size, float rot, Color col)
        {
            if (size <= 0.002f) return;
            Vector2[] unit = unitShapes[shape];
            float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
            float half = Draw.Px * 0.5f;
            int center = layer.Vertex(pos, col);
            int first = layer.VertexCount;
            int n = unit.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 u = unit[i];
                var d = new Vector2(cs * u.x - sn * u.y, sn * u.x + cs * u.y);
                float len = d.magnitude;
                Vector2 dn = len > 1e-6f ? d / len : Vector2.zero;
                layer.Vertex(pos + d * size - dn * half, col);
                layer.Vertex(pos + d * size + dn * half, Draw.Clear);
            }
            for (int i = 0; i < n; i++)
            {
                int a0 = first + i * 2, b0 = first + (i + 1) % n * 2;
                layer.Triangle(center, a0, b0);
                layer.Quad(a0, a0 + 1, b0 + 1, b0);
            }
        }
    }
}
