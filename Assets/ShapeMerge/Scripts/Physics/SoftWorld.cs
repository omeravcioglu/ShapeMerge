using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShapeMerge
{
    public struct SoftContact
    {
        public SoftBody A;
        public SoftBody B;          // null when A touched the container
        public Vector2 Point;
        public Vector2 Normal;      // pushes A away from B (or the wall)
        public float ImpactSpeed;   // largest closing speed seen during the step
    }

    /// <summary>
    /// Small-step XPBD world for soft bodies inside an open-topped tub. Each frame is split
    /// into many tiny substeps with a single constraint pass each, which keeps stacks stable
    /// and lets fast shots squeeze into gaps without tunnelling.
    /// </summary>
    public sealed class SoftWorld
    {
        public Vector2 Gravity = new Vector2(0f, -16f);
        public float SubstepRate = 480f;
        public int MaxSubsteps = 12;
        public float ContactMargin = 0.045f;
        public float MaxSpeed = 45f;

        public bool HasContainer;
        public float Left = -3.6f, Right = 3.6f, Floor = 0f, CornerRadius = 0.6f;
        public float WallFriction = 0.3f;

        /// <summary>Return false to let two bodies pass through each other.</summary>
        public Func<SoftBody, SoftBody, bool> CollisionFilter;

        public IReadOnlyList<SoftBody> Bodies => bodies;
        public readonly List<SoftContact> Contacts = new List<SoftContact>();

        readonly List<SoftBody> bodies = new List<SoftBody>();
        readonly Dictionary<long, int> contactIndex = new Dictionary<long, int>();
        float[] gradX = new float[128], gradY = new float[128];
        int nextId;
        bool flipOrder;

        public void Add(SoftBody body)
        {
            body.Id = nextId++;
            body.RefreshBounds();
            bodies.Add(body);
            if (body.Count > gradX.Length)
            {
                gradX = new float[body.Count];
                gradY = new float[body.Count];
            }
        }

        public void Remove(SoftBody body) => bodies.Remove(body);

        public void Clear()
        {
            bodies.Clear();
            Contacts.Clear();
            contactIndex.Clear();
        }

        public void Step(float dt)
        {
            Contacts.Clear();
            contactIndex.Clear();
            if (dt <= 0f || bodies.Count == 0) return;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * SubstepRate - 0.01f), 1, MaxSubsteps);
            float h = dt / steps;
            for (int s = 0; s < steps; s++) Substep(h);
            for (int i = 0; i < bodies.Count; i++) bodies[i].FinishStep();
        }

        void Substep(float h)
        {
            int count = bodies.Count;
            for (int i = 0; i < count; i++) Integrate(bodies[i], h);

            for (int i = 0; i < count; i++)
            {
                SoftBody b = bodies[i];
                SolveEdges(b, h);
                SolveArea(b, h);
                SolveShape(b, h);
                b.RefreshBounds();
            }

            // Alternate the pair order so resolution bias doesn't make stacks drift sideways.
            flipOrder = !flipOrder;
            float m = ContactMargin;
            for (int ii = 0; ii < count; ii++)
            {
                SoftBody a = bodies[flipOrder ? count - 1 - ii : ii];
                if (!a.Collidable || a.Held) continue;
                for (int jj = ii + 1; jj < count; jj++)
                {
                    SoftBody b = bodies[flipOrder ? count - 1 - jj : jj];
                    if (!b.Collidable || b.Held) continue;
                    if (a.Max.x + m < b.Min.x || b.Max.x + m < a.Min.x ||
                        a.Max.y + m < b.Min.y || b.Max.y + m < a.Min.y) continue;
                    if (CollisionFilter != null && !CollisionFilter(a, b)) continue;
                    CollidePoints(a, b);
                    CollidePoints(b, a);
                }
            }

            if (HasContainer)
                for (int i = 0; i < count; i++) CollideContainer(bodies[i]);

            for (int i = 0; i < count; i++) UpdateVelocities(bodies[i], h);
        }

        // Fraction of a full position correction that a spring of this frequency applies per substep.
        static float SpringBlend(float frequency, float h)
        {
            if (frequency <= 0f) return 1f;
            float x = 2f * Mathf.PI * frequency * h;
            x *= x;
            return x / (1f + x);
        }

        void Integrate(SoftBody b, float h)
        {
            Vector2[] pos = b.Pos, prev = b.Prev, vel = b.Vel;
            int n = b.Count;
            float gx = b.Held ? 0f : Gravity.x * b.GravityScale;
            float gy = b.Held ? 0f : Gravity.y * b.GravityScale;
            float maxSpeed2 = MaxSpeed * MaxSpeed;

            float k = 0f, c = 0f;
            if (b.Attracted)
            {
                float w = 2f * Mathf.PI * b.AttractFrequency;
                k = w * w;
                c = 2f * w; // critically damped
            }

            for (int i = 0; i < n; i++)
            {
                prev[i] = pos[i];
                float vx = vel[i].x + gx * h, vy = vel[i].y + gy * h;
                if (b.Attracted)
                {
                    vx += ((b.AttractPoint.x - pos[i].x) * k - vx * c) * h;
                    vy += ((b.AttractPoint.y - pos[i].y) * k - vy * c) * h;
                }
                float s2 = vx * vx + vy * vy;
                if (s2 > maxSpeed2)
                {
                    float f = MaxSpeed / Mathf.Sqrt(s2);
                    vx *= f; vy *= f;
                }
                vel[i] = new Vector2(vx, vy);
                pos[i] = new Vector2(pos[i].x + vx * h, pos[i].y + vy * h);
            }
        }

        void SolveEdges(SoftBody b, float h)
        {
            float blend = 0.5f * SpringBlend(b.Settings.EdgeFrequency, h);
            Vector2[] pos = b.Pos, rest = b.Rest;
            float sx = b.Scale * b.Stretch.x, sy = b.Scale * b.Stretch.y;
            int n = b.Count;
            for (int i = n - 1, j = 0; j < n; i = j++)
            {
                float dx = pos[j].x - pos[i].x, dy = pos[j].y - pos[i].y;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-7f) continue;
                float rx = (rest[j].x - rest[i].x) * sx, ry = (rest[j].y - rest[i].y) * sy;
                float restLen = Mathf.Sqrt(rx * rx + ry * ry);
                float corr = (len - restLen) / len * blend;
                float cx = dx * corr, cy = dy * corr;
                pos[i].x += cx; pos[i].y += cy;
                pos[j].x -= cx; pos[j].y -= cy;
            }
        }

        void SolveArea(SoftBody b, float h)
        {
            Vector2[] pos = b.Pos;
            int n = b.Count;
            float area = SoftBody.PolygonArea(pos, n);
            float target = b.RestArea * b.Scale * b.Scale * b.Stretch.x * b.Stretch.y;
            float c = area - target;

            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 pn = pos[i == n - 1 ? 0 : i + 1];
                Vector2 pp = pos[i == 0 ? n - 1 : i - 1];
                float gx = 0.5f * (pn.y - pp.y), gy = 0.5f * (pp.x - pn.x);
                gradX[i] = gx; gradY[i] = gy;
                sum += gx * gx + gy * gy;
            }
            if (sum < 1e-12f) return;

            float s = -c / sum * SpringBlend(b.Settings.AreaFrequency, h);
            for (int i = 0; i < n; i++)
            {
                pos[i].x += gradX[i] * s;
                pos[i].y += gradY[i] * s;
            }
        }

        void SolveShape(SoftBody b, float h)
        {
            Vector2[] pos = b.Pos, rest = b.Rest;
            int n = b.Count;
            float sx = b.Scale * b.Stretch.x, sy = b.Scale * b.Stretch.y;

            float cx = 0f, cy = 0f;
            for (int i = 0; i < n; i++) { cx += pos[i].x; cy += pos[i].y; }
            cx /= n; cy /= n;

            // Best-fit rotation of the rest shape onto the current points (2D Kabsch).
            float dotSum = 0f, crossSum = 0f;
            for (int i = 0; i < n; i++)
            {
                float rx = rest[i].x * sx, ry = rest[i].y * sy;
                float qx = pos[i].x - cx, qy = pos[i].y - cy;
                dotSum += rx * qx + ry * qy;
                crossSum += rx * qy - ry * qx;
            }
            float angle = Mathf.Atan2(crossSum, dotSum);
            b.Angle = angle;

            float blend = SpringBlend(b.Settings.ShapeFrequency, h);
            float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
            for (int i = 0; i < n; i++)
            {
                float rx = rest[i].x * sx, ry = rest[i].y * sy;
                float gx = cx + cs * rx - sn * ry, gy = cy + sn * rx + cs * ry;
                pos[i].x += (gx - pos[i].x) * blend;
                pos[i].y += (gy - pos[i].y) * blend;
            }

            if (!b.Held) return;

            // Held: carry the body rigidly onto the hold position, so fast moves never smear it...
            float mx = 0f, my = 0f;
            for (int i = 0; i < n; i++) { mx += pos[i].x; my += pos[i].y; }
            mx = b.HoldPosition.x - mx / n;
            my = b.HoldPosition.y - my / n;
            for (int i = 0; i < n; i++) { pos[i].x += mx; pos[i].y += my; }

            // ...then pull each point onto its spot in the pose, tighter near the anchor so the far side wobbles.
            float hc = Mathf.Cos(b.HoldAngle), hs = Mathf.Sin(b.HoldAngle);
            float near = SpringBlend(b.HoldNearFrequency, h), far = SpringBlend(b.HoldFarFrequency, h);
            float invR = 1f / Mathf.Max(b.RestRadius, 1e-4f);
            for (int i = 0; i < n; i++)
            {
                float side = (rest[i].x * b.HoldAnchor.x + rest[i].y * b.HoldAnchor.y) * invR; // -1..1
                float t = Mathf.Clamp01(side * 0.5f + 0.5f);
                float k = far + (near - far) * t * t;
                float rx = rest[i].x * sx, ry = rest[i].y * sy;
                float gx = b.HoldPosition.x + hc * rx - hs * ry;
                float gy = b.HoldPosition.y + hs * rx + hc * ry;
                pos[i].x += (gx - pos[i].x) * k;
                pos[i].y += (gy - pos[i].y) * k;
            }
        }

        // Pushes A's points out of B's polygon (keeping a small visual gap) and drags B's edges back.
        void CollidePoints(SoftBody a, SoftBody b)
        {
            float m = ContactMargin, m2 = m * m;
            float minX = b.Min.x - m, minY = b.Min.y - m, maxX = b.Max.x + m, maxY = b.Max.y + m;
            float bcx = b.Center.x, bcy = b.Center.y;
            float br = b.BoundRadius + m, br2 = br * br;
            Vector2[] ap = a.Pos, aq = a.Prev, av = a.Vel;
            Vector2[] bp = b.Pos, bq = b.Prev, bv = b.Vel;
            int an = a.Count, bn = b.Count;
            float wa = a.InvMass, wb = b.InvMass;
            float mu = 0.5f * (a.Settings.Friction + b.Settings.Friction);

            for (int k = 0; k < an; k++)
            {
                float px = ap[k].x, py = ap[k].y;
                if (px < minX || px > maxX || py < minY || py > maxY) continue;
                float ox = px - bcx, oy = py - bcy;
                if (ox * ox + oy * oy > br2) continue;

                bool inside = false;
                float best = float.MaxValue, bestT = 0f;
                int bestEdge = 0;
                float x0 = bp[bn - 1].x, y0 = bp[bn - 1].y;
                for (int e = 0; e < bn; e++)
                {
                    float x1 = bp[e].x, y1 = bp[e].y;
                    if ((y0 > py) != (y1 > py) && px < x0 + (py - y0) * (x1 - x0) / (y1 - y0)) inside = !inside;
                    float ex = x1 - x0, ey = y1 - y0;
                    float len2 = ex * ex + ey * ey;
                    float t = len2 > 1e-12f ? ((px - x0) * ex + (py - y0) * ey) / len2 : 0f;
                    if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                    float dx = x0 + ex * t - px, dy = y0 + ey * t - py;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < best) { best = d2; bestT = t; bestEdge = e; }
                    x0 = x1; y0 = y1;
                }
                if (!inside && best >= m2) continue;

                int i1 = bestEdge, i0 = bestEdge == 0 ? bn - 1 : bestEdge - 1;
                float t1 = bestT, t0 = 1f - bestT;
                float qx = bp[i0].x * t0 + bp[i1].x * t1, qy = bp[i0].y * t0 + bp[i1].y * t1;
                float dist = Mathf.Sqrt(best);
                float nx, ny;
                if (dist > 1e-6f)
                {
                    nx = (px - qx) / dist; ny = (py - qy) / dist;
                    if (inside) { nx = -nx; ny = -ny; }
                }
                else
                {
                    float ex = bp[i1].x - bp[i0].x, ey = bp[i1].y - bp[i0].y;
                    float el = Mathf.Sqrt(ex * ex + ey * ey);
                    if (el < 1e-9f) continue;
                    nx = ey / el; ny = -ex / el; // outward normal of a CCW edge
                }

                float pen = inside ? dist + m : m - dist;
                float wsum = wa + wb * (t0 * t0 + t1 * t1);
                if (wsum <= 0f) continue;

                float rvx = av[k].x - (bv[i0].x * t0 + bv[i1].x * t1);
                float rvy = av[k].y - (bv[i0].y * t0 + bv[i1].y * t1);
                float closing = -(rvx * nx + rvy * ny);

                float la = wa * pen / wsum, lb = wb * pen / wsum;
                ap[k].x += nx * la; ap[k].y += ny * la;
                bp[i0].x -= nx * lb * t0; bp[i0].y -= ny * lb * t0;
                bp[i1].x -= nx * lb * t1; bp[i1].y -= ny * lb * t1;

                if (mu > 0f)
                {
                    float dpx = (ap[k].x - aq[k].x) - ((bp[i0].x - bq[i0].x) * t0 + (bp[i1].x - bq[i1].x) * t1);
                    float dpy = (ap[k].y - aq[k].y) - ((bp[i0].y - bq[i0].y) * t0 + (bp[i1].y - bq[i1].y) * t1);
                    float dn = dpx * nx + dpy * ny;
                    float tx = dpx - dn * nx, ty = dpy - dn * ny;
                    float tl = Mathf.Sqrt(tx * tx + ty * ty);
                    if (tl > 1e-9f)
                    {
                        float limit = mu * pen;
                        float remove = tl <= limit ? 1f : limit / tl;
                        float fa = wa / wsum * remove, fb = wb / wsum * remove;
                        ap[k].x -= tx * fa; ap[k].y -= ty * fa;
                        bp[i0].x += tx * fb * t0; bp[i0].y += ty * fb * t0;
                        bp[i1].x += tx * fb * t1; bp[i1].y += ty * fb * t1;
                    }
                }

                RecordContact(a, b, new Vector2(qx, qy), new Vector2(nx, ny), closing);
            }
        }

        void CollideContainer(SoftBody b)
        {
            if (!b.Collidable || b.Held) return;
            float m = ContactMargin;
            float left = Left + m, right = Right - m, floor = Floor + m;
            float rc = Mathf.Max(CornerRadius, m);
            float ccxL = Left + rc, ccxR = Right - rc, ccy = Floor + rc, cr = rc - m;
            float mu = WallFriction;
            Vector2[] pos = b.Pos, prev = b.Prev, vel = b.Vel;

            bool anyTouch = false;
            float bestImpact = float.MinValue;
            Vector2 bestPoint = Vector2.zero, bestNormal = Vector2.up;

            for (int i = 0; i < b.Count; i++)
            {
                float px = pos[i].x, py = pos[i].y;
                bool touched = false;
                float impact = 0f;
                Vector2 normal = Vector2.zero;

                if (px < left)
                {
                    float pen = left - px;
                    px = left;
                    py = prev[i].y + Slide(py - prev[i].y, mu * pen);
                    touched = true; impact = -vel[i].x; normal = new Vector2(1f, 0f);
                }
                else if (px > right)
                {
                    float pen = px - right;
                    px = right;
                    py = prev[i].y + Slide(py - prev[i].y, mu * pen);
                    touched = true; impact = vel[i].x; normal = new Vector2(-1f, 0f);
                }

                if (py < floor)
                {
                    float pen = floor - py;
                    py = floor;
                    px = prev[i].x + Slide(px - prev[i].x, mu * pen);
                    touched = true; impact = Mathf.Max(impact, -vel[i].y); normal = new Vector2(0f, 1f);
                }

                // Rounded inner corners of the tub.
                if (py < ccy && (px < ccxL || px > ccxR))
                {
                    float cx = px < ccxL ? ccxL : ccxR;
                    float dx = px - cx, dy = py - ccy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > cr && d > 1e-6f)
                    {
                        float nx = -dx / d, ny = -dy / d; // points back inside
                        float pen = d - cr;
                        px += nx * pen; py += ny * pen;
                        float tx = -ny, ty = nx;
                        float slide = (px - prev[i].x) * tx + (py - prev[i].y) * ty;
                        float keep = Slide(slide, mu * pen);
                        px -= tx * (slide - keep); py -= ty * (slide - keep);
                        touched = true;
                        impact = Mathf.Max(impact, -(vel[i].x * nx + vel[i].y * ny));
                        normal = new Vector2(nx, ny);
                    }
                }

                pos[i] = new Vector2(px, py);
                if (touched && impact > bestImpact)
                {
                    anyTouch = true;
                    bestImpact = impact;
                    bestPoint = pos[i];
                    bestNormal = normal;
                }
            }

            if (anyTouch) RecordContact(b, null, bestPoint, bestNormal, Mathf.Max(0f, bestImpact));
        }

        // Coulomb friction on a tangential displacement: sticks below the limit, slides past it.
        static float Slide(float d, float limit)
        {
            if (d > limit) return d - limit;
            if (d < -limit) return d + limit;
            return 0f;
        }

        void UpdateVelocities(SoftBody b, float h)
        {
            Vector2[] pos = b.Pos, prev = b.Prev, vel = b.Vel;
            int n = b.Count;
            float inv = 1f / h;

            float cx = 0f, cy = 0f, vx = 0f, vy = 0f;
            for (int i = 0; i < n; i++)
            {
                float ux = (pos[i].x - prev[i].x) * inv, uy = (pos[i].y - prev[i].y) * inv;
                vel[i] = new Vector2(ux, uy);
                cx += pos[i].x; cy += pos[i].y;
                vx += ux; vy += uy;
            }
            cx /= n; cy /= n; vx /= n; vy /= n;

            // Split into rigid motion + wobble, damp the wobble hard and the rigid motion gently.
            float l = 0f, inertia = 0f;
            for (int i = 0; i < n; i++)
            {
                float rx = pos[i].x - cx, ry = pos[i].y - cy;
                l += rx * (vel[i].y - vy) - ry * (vel[i].x - vx);
                inertia += rx * rx + ry * ry;
            }
            float w = inertia > 1e-8f ? l / inertia : 0f;

            SoftBodySettings s = b.Settings;
            float keepWobble = Mathf.Exp(-s.JiggleDamping * h);
            float keepLinear = Mathf.Exp(-s.LinearDrag * h);
            float keepAngular = Mathf.Exp(-s.AngularDrag * h);
            float nvx = vx * keepLinear, nvy = vy * keepLinear, nw = w * keepAngular;

            for (int i = 0; i < n; i++)
            {
                float rx = pos[i].x - cx, ry = pos[i].y - cy;
                float ux = vel[i].x - (vx - w * ry), uy = vel[i].y - (vy + w * rx);
                vel[i] = new Vector2(nvx - nw * ry + ux * keepWobble, nvy + nw * rx + uy * keepWobble);
            }

            // Surface viscosity: pull each velocity towards its neighbours' average. Point-to-point
            // ripples die almost instantly while the big squash-and-stretch modes survive.
            float smooth = 1f - Mathf.Exp(-s.SurfaceViscosity * h);
            if (smooth <= 0f) return;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = vel[i == 0 ? n - 1 : i - 1], c = vel[i == n - 1 ? 0 : i + 1];
                gradX[i] = vel[i].x + ((a.x + c.x) * 0.5f - vel[i].x) * smooth;
                gradY[i] = vel[i].y + ((a.y + c.y) * 0.5f - vel[i].y) * smooth;
            }
            for (int i = 0; i < n; i++) vel[i] = new Vector2(gradX[i], gradY[i]);
        }

        void RecordContact(SoftBody a, SoftBody b, Vector2 point, Vector2 normal, float impact)
        {
            long key;
            if (b == null) key = ((long)a.Id << 32) | 0xFFFFFFFFL;
            else if (a.Id < b.Id) key = ((long)a.Id << 32) | (uint)b.Id;
            else key = ((long)b.Id << 32) | (uint)a.Id;

            if (contactIndex.TryGetValue(key, out int index))
            {
                SoftContact c = Contacts[index];
                if (impact > c.ImpactSpeed)
                {
                    c.ImpactSpeed = impact;
                    c.Point = point;
                    c.Normal = c.A == a ? normal : -normal;
                    Contacts[index] = c;
                }
                return;
            }

            contactIndex[key] = Contacts.Count;
            Contacts.Add(new SoftContact { A = a, B = b, Point = point, Normal = normal, ImpactSpeed = Mathf.Max(0f, impact) });
        }
    }
}
