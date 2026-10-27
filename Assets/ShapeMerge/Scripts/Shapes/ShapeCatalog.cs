using UnityEngine;

namespace ShapeMerge
{
    /// <summary>One rung of the merge ladder: circle, triangle, square, pentagon and up.</summary>
    public sealed class ShapeTier
    {
        public int Index;
        public string Name;
        public ShapeKind Kind;
        public int Sides;
        public float Radius;          // radius of a circle with the same area
        public float Area;
        public Color Color;           // sRGB
        public int Value;             // points scored for creating one
        public Vector2[] Outline;     // physics outline at scale 1, CCW, centred
        public float Mass;
        public SoftBodySettings Physics;

        public int PointCount => Outline.Length;
    }

    public static class ShapeCatalog
    {
        public const float BaseRadius = 0.43f;
        public const float Growth = 1.255f;    // each tier is ~1.6x the area of the last, so merging frees room
        const float PointSpacing = 0.12f;
        const int MinPoints = 20;
        const int MaxPoints = 64;
        const float StarInner = 0.56f;

        public static ShapeTier[] Build()
        {
            var defs = new (string name, ShapeKind kind, int sides, float roundness, string color)[]
            {
                ("Circle",   ShapeKind.Circle,  0, 0f,    "#FF5D73"),
                ("Triangle", ShapeKind.Polygon, 3, 0.22f, "#FF9F45"),
                ("Square",   ShapeKind.Polygon, 4, 0.19f, "#FFD23F"),
                ("Pentagon", ShapeKind.Polygon, 5, 0.17f, "#7AD95F"),
                ("Hexagon",  ShapeKind.Polygon, 6, 0.15f, "#2FD3C0"),
                ("Heptagon", ShapeKind.Polygon, 7, 0.14f, "#3DA5FF"),
                ("Octagon",  ShapeKind.Polygon, 8, 0.13f, "#8C6CFF"),
                ("Star",     ShapeKind.Star,    5, 0.17f, "#FF6FD8"),
            };

            var tiers = new ShapeTier[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                float radius = BaseRadius * Mathf.Pow(Growth, i);
                float area = Mathf.PI * radius * radius;

                float perimeter = ShapeGeometry.Perimeter(d.kind, d.sides, d.roundness, StarInner, area);
                int symmetry = d.kind == ShapeKind.Circle ? 4 : d.sides;
                int count = Mathf.Clamp(Mathf.RoundToInt(perimeter / PointSpacing), MinPoints, MaxPoints);
                count = Mathf.Max(symmetry * 3, Mathf.RoundToInt((float)count / symmetry) * symmetry);

                float size = i / (float)(defs.Length - 1);
                tiers[i] = new ShapeTier
                {
                    Index = i,
                    Name = d.name,
                    Kind = d.kind,
                    Sides = d.sides,
                    Radius = radius,
                    Area = area,
                    Color = Palette.Hex(d.color),
                    Value = 2 << i,
                    Outline = ShapeGeometry.Build(d.kind, d.sides, d.roundness, StarInner, area, count),
                    Mass = area,
                    Physics = new SoftBodySettings
                    {
                        // Small shapes jiggle fast, big ones slow and heavy.
                        ShapeFrequency = Mathf.Lerp(11f, 7f, size),
                        EdgeFrequency = 30f,
                        AreaFrequency = 45f,
                        JiggleDamping = 4f,
                        SurfaceViscosity = 50f,
                        LinearDrag = 0.05f,
                        AngularDrag = 0.35f,
                        Friction = 0.45f,
                    },
                };
            }
            return tiers;
        }
    }
}
