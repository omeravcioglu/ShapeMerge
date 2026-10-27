using System.Collections.Generic;
using UnityEngine;

namespace ShapeMerge
{
    /// <summary>
    /// The launcher on the rail above the tub. It follows the pointer; hold to charge (the barrel
    /// squashes and the waiting shape squeezes), drag to aim, release to fire. A quick tap drops
    /// the shape gently; a full charge rams it into the pile.
    /// </summary>
    public sealed class Cannon
    {
        public const float BarrelLength = 0.95f;
        public const float BarrelWidth = 0.92f;
        public const float BaseRadius = 0.5f;
        public const float MaxAngle = 65f * Mathf.Deg2Rad;
        public const float MinSpeed = 1.5f;
        public const float MaxSpeed = 21f;
        public const float ChargeTime = 0.9f;

        public struct Shot
        {
            public Vector2 Dir;
            public float Speed;
            public float Power;
        }

        readonly Arena arena;
        readonly List<Vector2> path = new List<Vector2>(96);
        readonly List<Vector2> outline = new List<Vector2>(96);
        readonly Color bodyColor = Palette.LinearHex("#F1EFFF");
        readonly Color outlineColor = Palette.LinearHex("#26244F");
        readonly Color lipColor = Palette.LinearHex("#3B3A7A");
        readonly Color white = Palette.LinearHex("#FFFFFF");

        SpringFloat x, angle, recoil, squash, ring;
        float targetX, aimTarget, lockedX, time;
        bool armed;
        Vector2 jitter;

        public Shape Loaded;
        public bool Charging { get; private set; }
        public float Charge { get; private set; }

        public Cannon(Arena arena)
        {
            this.arena = arena;
            Reset();
        }

        public Vector2 Pivot => new Vector2(x.Value, arena.RailY) + jitter;
        public Vector2 Dir => new Vector2(Mathf.Sin(angle.Value), -Mathf.Cos(angle.Value));
        float LengthScale => 1f - 0.22f * squash.Value;
        float WidthScale => 1f + 0.14f * squash.Value;
        float RecoilOffset => recoil.Value * 0.2f;
        public Vector2 Muzzle => Pivot + Dir * (BarrelLength * LengthScale - RecoilOffset);
        public float SquashAmount => squash.Value;

        public static float SpeedFor(float power) => Mathf.Lerp(MinSpeed, MaxSpeed, Mathf.Pow(Mathf.Clamp01(power), 1.35f));

        public void Reset()
        {
            x = new SpringFloat(0f);
            angle = new SpringFloat(0f);
            recoil = squash = ring = new SpringFloat(0f);
            targetX = aimTarget = 0f;
            Charging = false;
            Charge = 0f;
            armed = false;
            Loaded = null;
        }

        /// <summary>Where the waiting shape hangs: off the mouth like a drop forming on a nozzle.</summary>
        public void HoldPose(Shape shape, out Vector2 position, out float rotation)
        {
            position = Muzzle + Dir * (shape.Body.RestRadius * shape.Body.Scale * 0.72f);
            rotation = angle.Value;
        }

        public Vector2 HoldStretch
        {
            get
            {
                float s = Mathf.Clamp(squash.Value, -0.6f, 1f);
                return new Vector2(1f + 0.16f * s, 1f - 0.2f * s);
            }
        }

        /// <summary>A squishy bump of the barrel and the held shape, e.g. when a shape lands in it.</summary>
        public void Bump(float amount) => squash.Velocity += amount;

        public bool Tick(float dt, GameInput input, bool ready, out Shot shot)
        {
            shot = default;
            time += dt;
            bool fired = false;
            float minX = arena.Left + 0.8f, maxX = arena.Right - 0.8f;

            if (!Charging)
            {
                if (input.UsingPointer) targetX = input.PointerWorld.x;
                else targetX += input.Move * 7.5f * dt;
                targetX = Mathf.Clamp(targetX, minX, maxX);
                x.Step(targetX, 4.5f, 0.9f, dt);

                // Swing like a pendulum against the motion; the hanging shape sways with it.
                aimTarget = Mathf.Clamp(-x.Velocity * 0.045f, -0.4f, 0.4f);

                if (input.Pressed) armed = true;
                if (!input.Down) armed = false;
                if (armed && ready)
                {
                    Charging = true;
                    Charge = 0f;
                    lockedX = x.Value;
                    armed = false;
                }
            }
            else
            {
                x.Step(lockedX, 6f, 0.9f, dt);
                if (input.UsingPointer)
                {
                    Vector2 d = input.PointerWorld - new Vector2(lockedX, arena.RailY);
                    aimTarget = Mathf.Clamp(Mathf.Atan2(d.x, Mathf.Max(0.35f, -d.y)), -MaxAngle, MaxAngle);
                }
                else
                {
                    aimTarget = Mathf.Clamp(aimTarget + input.Move * 1.5f * dt, -MaxAngle, MaxAngle);
                }

                Charge = Mathf.Min(1f, Charge + dt / ChargeTime);

                if (input.Cancel || !ready)
                {
                    Charging = false;
                    Charge = 0f;
                }
                else if (!input.Down)
                {
                    Charging = false;
                    shot = new Shot { Dir = Dir, Speed = SpeedFor(Charge), Power = Charge };
                    recoil.Velocity += 7f + 12f * Charge;
                    squash.Velocity -= 7f + 9f * Charge;
                    Charge = 0f;
                    fired = true;
                }
            }

            angle.Step(aimTarget, Charging ? 6f : 2.4f, Charging ? 0.85f : 0.3f, dt);
            recoil.Step(0f, 5f, 0.35f, dt);
            squash.Step(Charging ? Charge : 0f, Charging ? 3f : 5f, Charging ? 0.9f : 0.32f, dt);
            ring.Step(Charging ? 1f : 0f, 5f, 0.75f, dt);
            jitter = Charging && Charge >= 1f
                ? new Vector2(Mathf.Sin(time * 91f), Mathf.Cos(time * 73f)) * 0.02f
                : Vector2.zero;
            return fired;
        }

        public void Render(ShapeLayers layers, MeshLayer glow, Color tint)
        {
            Vector2 p = Pivot, d = Dir;
            float rot = Mathf.Atan2(d.y, d.x);
            float len = BarrelLength * LengthScale, wid = BarrelWidth * WidthScale;
            float back = RecoilOffset;
            float heat = Mathf.Clamp01(squash.Value);

            var barrel = ShapeLook.Of(Palette.Lerp(bodyColor, tint, 0.15f + 0.3f * heat), outlineColor, 0.55f);
            barrel.OutlineWidth = 0.065f;
            barrel.Shadow = 0.25f;
            barrel.Glow = 0.25f * heat;
            Draw.RoundedRect(outline, p + d * (len * 0.5f - back), new Vector2(len * 0.5f + 0.05f, wid * 0.5f), 0.24f, rot);
            ShapePainter.Paint(layers, outline, barrel);

            var lip = ShapeLook.Of(lipColor, outlineColor, 0.35f);
            lip.OutlineWidth = 0.065f;
            lip.Shadow = 0.2f;
            lip.Shine = 0.6f;
            Draw.RoundedRect(outline, p + d * (len - back - 0.05f), new Vector2(0.13f, wid * 0.5f + 0.07f), 0.1f, rot);
            ShapePainter.Paint(layers, outline, lip);

            // Squishy bulb in the colour of the waiting shape: squeeze it to shoot.
            float full = Charging && Charge >= 1f ? 0.5f + 0.5f * Mathf.Sin(time * 24f) : 0f;
            var hub = ShapeLook.Of(Palette.Lerp(bodyColor, tint, 0.7f), outlineColor, BaseRadius);
            hub.OutlineWidth = 0.065f;
            hub.Shadow = 0.25f;
            hub.Glow = 0.12f * heat + 0.25f * full;
            float along = BaseRadius * (1f - 0.18f * squash.Value), across = BaseRadius * (1f + 0.2f * squash.Value);
            var side = new Vector2(-d.y, d.x);
            outline.Clear();
            int segs = Draw.Segments(BaseRadius);
            for (int i = 0; i < segs; i++)
            {
                float a = i * (2f * Mathf.PI / segs);
                outline.Add(p + d * (Mathf.Cos(a) * along) + side * (Mathf.Sin(a) * across));
            }
            ShapePainter.Paint(layers, outline, hub);

            // Power ring around the hub.
            float show = ring.Value;
            if (show > 0.01f)
            {
                float radius = BaseRadius + 0.22f;
                Draw.Ring(glow, p, radius, 0.1f, new Color(0f, 0f, 0f, 0.35f * show));
                Draw.Arc(glow, p, radius, 0.1f, Palette.Glow(tint, (1.5f + 1.5f * full) * show), Mathf.PI * 0.5f, -2f * Mathf.PI * Charge);
                if (full > 0f) Draw.Soft(glow, p, radius * 1.8f, Palette.Glow(tint, 0.25f * full * show));
            }
        }

        /// <summary>Dotted ballistic preview that flows towards where the shot will land.</summary>
        public void RenderGuide(MeshLayer l, SoftWorld world, float time)
        {
            if (Loaded == null) return;
            HoldPose(Loaded, out Vector2 start, out _);
            float radius = Loaded.Tier.Radius * 0.9f;
            float power = Charging ? Charge : 0f;
            Vector2 v = Dir * SpeedFor(power);
            Vector2 g = world.Gravity;

            path.Clear();
            path.Add(start);
            Vector2 end = start;
            bool hit = false;
            const float step = 0.025f;
            for (int i = 1; i <= 90; i++)
            {
                float t = i * step;
                Vector2 p = start + v * t + g * (0.5f * t * t);
                if (Blocked(world, p, radius))
                {
                    // Bisect for a tidy landing point.
                    float lo = (i - 1) * step, hi = t;
                    for (int k = 0; k < 6; k++)
                    {
                        float mid = 0.5f * (lo + hi);
                        Vector2 m = start + v * mid + g * (0.5f * mid * mid);
                        if (Blocked(world, m, radius)) hi = mid; else lo = mid;
                    }
                    end = start + v * lo + g * (0.5f * lo * lo);
                    path.Add(end);
                    hit = true;
                    break;
                }
                path.Add(p);
                end = p;
            }

            Color tint = Palette.Lerp(white, Palette.Linear(Loaded.Tier.Color), 0.45f);
            float strength = Charging ? 1f : 0.35f;
            const float spacing = 0.32f;
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += (path[i] - path[i - 1]).magnitude;
            float along = Mathf.Repeat(time * 1.8f, 1f) * spacing;
            int seg = 1;
            float segStart = 0f;
            while (along < total && seg < path.Count)
            {
                float segLen = (path[seg] - path[seg - 1]).magnitude;
                if (along > segStart + segLen)
                {
                    segStart += segLen;
                    seg++;
                    continue;
                }
                float k = segLen > 1e-5f ? (along - segStart) / segLen : 0f;
                Vector2 dot = Vector2.Lerp(path[seg - 1], path[seg], k);
                float f = along / Mathf.Max(total, 0.01f);
                float fadeIn = Mathf.Clamp01(along / 0.4f);
                Draw.Disc(l, dot, Mathf.Lerp(0.075f, 0.04f, f), Palette.Pm(tint, Mathf.Lerp(0.9f, 0.3f, f) * strength * fadeIn));
                along += spacing;
            }

            if (hit)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 8f);
                Draw.Ring(l, end, radius * (0.95f + 0.08f * pulse), 0.05f, Palette.Pm(tint, (0.35f + 0.25f * pulse) * strength));
            }
        }

        bool Blocked(SoftWorld world, Vector2 p, float r)
        {
            if (p.x - r < arena.Left || p.x + r > arena.Right || p.y - r < arena.Floor) return true;
            var bodies = world.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                SoftBody b = bodies[i];
                if (!b.Collidable || b.Held) continue;
                float reach = b.BoundRadius + r;
                if ((p - b.Center).sqrMagnitude > reach * reach) continue;
                if (DistanceToBody(b, p, out bool inside) < r * 0.85f || inside) return true;
            }
            return false;
        }

        static float DistanceToBody(SoftBody b, Vector2 p, out bool inside)
        {
            inside = false;
            float best = float.MaxValue;
            Vector2[] pts = b.Pos;
            int n = b.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = pts[j], c = pts[i];
                if ((a.y > p.y) != (c.y > p.y) && p.x < a.x + (p.y - a.y) * (c.x - a.x) / (c.y - a.y)) inside = !inside;
                Vector2 e = c - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, e) / Mathf.Max(e.sqrMagnitude, 1e-9f));
                best = Mathf.Min(best, (p - (a + e * t)).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }
    }
}
