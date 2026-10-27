using UnityEngine;

namespace ShapeMerge
{
    /// <summary>
    /// Spring tuning for a soft body. Stiffnesses are spring frequencies in Hz, so the feel
    /// stays the same no matter how many substeps the world runs per frame.
    /// </summary>
    [System.Serializable]
    public struct SoftBodySettings
    {
        public float ShapeFrequency;   // how hard it springs back to its rest shape (the jiggle)
        public float EdgeFrequency;    // perimeter stretch resistance
        public float AreaFrequency;    // volume / pressure resistance, 0 = incompressible
        public float JiggleDamping;    // 1/s, damps wobble without slowing the body down
        public float SurfaceViscosity; // 1/s, smooths neighbouring point velocities to kill crinkles
        public float LinearDrag;       // 1/s
        public float AngularDrag;      // 1/s
        public float Friction;
    }

    /// <summary>
    /// A closed, counter-clockwise ring of equal point masses. Perimeter edges, an area
    /// (pressure) constraint and shape matching keep it springy; SoftWorld steps it and
    /// resolves collisions.
    /// </summary>
    public sealed class SoftBody
    {
        public readonly int Count;
        public readonly Vector2[] Pos;
        public readonly Vector2[] Prev;
        public readonly Vector2[] Vel;
        public readonly Vector2[] Rest;     // rest outline centred on its point average
        public readonly float RestArea;
        public readonly float RestRadius;
        public readonly float InvMass;      // per point

        public SoftBodySettings Settings;
        public float Scale = 1f;            // uniform rest-shape scale, animated for pops and growth
        public Vector2 Stretch = Vector2.one; // extra rest-shape scale along the body's local axes
        public float GravityScale = 1f;
        public bool Collidable = true;

        // A held body (the shot sitting in the cannon) chases a pose instead of falling.
        // Points on the HoldAnchor side follow tightly, the far side lags and wobbles.
        public bool Held;
        public Vector2 HoldPosition;
        public float HoldAngle;
        public Vector2 HoldAnchor = new Vector2(0f, 1f);
        public float HoldNearFrequency = 16f;
        public float HoldFarFrequency = 6f;

        // Pulls every point towards a spot; used to slurp a merging pair together.
        public bool Attracted;
        public Vector2 AttractPoint;
        public float AttractFrequency = 7f;

        // Refreshed by the world.
        public Vector2 Center;
        public float Angle;
        public Vector2 Min, Max;
        public float BoundRadius;
        public Vector2 Velocity;
        public float AngularVelocity;

        public object Owner;
        public int Id = -1;

        Vector2 lastValidCenter;

        public SoftBody(Vector2[] outline, float mass, SoftBodySettings settings)
        {
            Count = outline.Length;
            Pos = new Vector2[Count];
            Prev = new Vector2[Count];
            Vel = new Vector2[Count];
            Rest = new Vector2[Count];

            Vector2 mean = Vector2.zero;
            for (int i = 0; i < Count; i++) mean += outline[i];
            mean /= Count;

            float r2 = 0f;
            for (int i = 0; i < Count; i++)
            {
                Rest[i] = outline[i] - mean;
                r2 = Mathf.Max(r2, Rest[i].sqrMagnitude);
            }
            RestRadius = Mathf.Sqrt(r2);
            RestArea = PolygonArea(Rest, Count);
            InvMass = Count / Mathf.Max(mass, 1e-4f);
            Settings = settings;
        }

        public float Mass => Count / InvMass;

        public float CurrentArea => PolygonArea(Pos, Count);

        /// <summary>Snaps the body to its rest shape at a pose, at rest.</summary>
        public void Place(Vector2 center, float angle, float scale)
        {
            Scale = scale;
            float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
            float sx = scale * Stretch.x, sy = scale * Stretch.y;
            for (int i = 0; i < Count; i++)
            {
                float rx = Rest[i].x * sx, ry = Rest[i].y * sy;
                Pos[i] = new Vector2(center.x + cs * rx - sn * ry, center.y + sn * rx + cs * ry);
                Prev[i] = Pos[i];
                Vel[i] = Vector2.zero;
            }
            Angle = angle;
            RefreshBounds();
            Velocity = Vector2.zero;
            AngularVelocity = 0f;
            lastValidCenter = Center;
        }

        public void SetVelocity(Vector2 v)
        {
            for (int i = 0; i < Count; i++) Vel[i] = v;
            Velocity = v;
        }

        public void AddVelocity(Vector2 v)
        {
            for (int i = 0; i < Count; i++) Vel[i] += v;
            Velocity += v;
        }

        /// <summary>Recomputes the centre, bounds and bounding radius from the current points.</summary>
        public void RefreshBounds()
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            float cx = 0f, cy = 0f;
            for (int i = 0; i < Count; i++)
            {
                float x = Pos[i].x, y = Pos[i].y;
                cx += x; cy += y;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            cx /= Count; cy /= Count;
            float r2 = 0f;
            for (int i = 0; i < Count; i++)
            {
                float dx = Pos[i].x - cx, dy = Pos[i].y - cy;
                float d2 = dx * dx + dy * dy;
                if (d2 > r2) r2 = d2;
            }
            Center = new Vector2(cx, cy);
            Min = new Vector2(minX, minY);
            Max = new Vector2(maxX, maxY);
            BoundRadius = Mathf.Sqrt(r2);
        }

        /// <summary>Called by the world after a full step: velocity stats plus a NaN safety net.</summary>
        internal void FinishStep()
        {
            float sx = 0f, sy = 0f;
            for (int i = 0; i < Count; i++) { sx += Pos[i].x; sy += Pos[i].y; }
            if (float.IsNaN(sx) || float.IsNaN(sy) || float.IsInfinity(sx) || float.IsInfinity(sy))
            {
                Place(lastValidCenter, float.IsNaN(Angle) ? 0f : Angle, Scale);
                return;
            }

            RefreshBounds();
            float vx = 0f, vy = 0f;
            for (int i = 0; i < Count; i++) { vx += Vel[i].x; vy += Vel[i].y; }
            vx /= Count; vy /= Count;

            float l = 0f, inertia = 0f;
            for (int i = 0; i < Count; i++)
            {
                float rx = Pos[i].x - Center.x, ry = Pos[i].y - Center.y;
                l += rx * (Vel[i].y - vy) - ry * (Vel[i].x - vx);
                inertia += rx * rx + ry * ry;
            }
            Velocity = new Vector2(vx, vy);
            AngularVelocity = inertia > 1e-8f ? l / inertia : 0f;
            lastValidCenter = Center;
        }

        public static float PolygonArea(Vector2[] p, int n)
        {
            float a = 0f;
            for (int i = 0, j = n - 1; i < n; j = i++) a += p[j].x * p[i].y - p[i].x * p[j].y;
            return 0.5f * a;
        }
    }
}
