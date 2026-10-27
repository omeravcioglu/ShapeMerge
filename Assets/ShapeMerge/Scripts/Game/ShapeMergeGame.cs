using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ShapeMerge
{
    /// <summary>
    /// Shape Merge, a soft-body merge game: shoot squishy shapes into the tub; two equal shapes
    /// that touch fuse into the next one (circle, triangle, square, pentagon, hexagon, heptagon,
    /// octagon, star). Keep the pile under the dashed line.
    /// Add this component to any GameObject and press Play; it builds everything at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShapeMergeGame : MonoBehaviour
    {
        enum Phase { Playing, GameOver }

        const string BestKey = "ShapeMerge.Best";
        const float LoadTravelTime = 0.32f;

        [Header("Physics")]
        [SerializeField] float gravity = 16f;
        [SerializeField] float substepRate = 480f;

        [Header("Rules")]
        [Tooltip("Seconds a resting shape may poke above the dashed line before the game ends.")]
        [SerializeField] float dangerSeconds = 3f;
        [Tooltip("Merges this close together chain into a combo.")]
        [SerializeField] float comboWindow = 1.4f;
        [Tooltip("How long two shapes squish into each other before they fuse.")]
        [SerializeField] float slurpTime = 0.085f;
        [SerializeField] float reloadTime = 0.3f;

        ShapeTier[] tiers;
        ShapeLook[] looks;
        float[] previewRadius;
        readonly Arena arena = new Arena();
        readonly List<Shape> shapes = new List<Shape>(64);
        readonly List<MergeJob> merges = new List<MergeJob>();
        readonly List<Shape> popQueue = new List<Shape>();
        readonly List<Vector2> outline = new List<Vector2>(512);
        readonly System.Random rng = new System.Random();
        Vector2[] previewPoints = new Vector2[128];

        SoftWorld world;
        Camera cam;
        CameraRig rig;
        PostFx post;
        Sfx sfx;
        Hud hud;
        FxSystem fx;
        Backdrop backdrop;
        Cannon cannon;
        GameInput input;
        ShapeLayers shapeLayers, cannonLayers;
        MeshLayer guideLayer, glowLayer, bubbleLayer;
        Material material;
        Color dangerRed;

        Phase phase;
        int score, best, highestTier, combo, shots, nextTier;
        float time, comboTimer, dangerTimer, dangerLevel, dangerTickTimer, reloadTimer;
        float hitstop, slowmo = 1f, overTimer, overShownAt, popTimer, nextDelay;
        bool overShown, newBest;
        SpringFloat nextPop;

        public bool IsGameOver => phase == Phase.GameOver;

        void Awake()
        {
            // Smooth frame pacing in builds; in the editor the Game view's own VSync toggle applies
            // (setting it here would also rewrite the project's quality settings).
            if (!Application.isEditor) QualitySettings.vSyncCount = 1;

            Shader shader = Resources.Load<Shader>("ShapeMerge/ShapeMergeUnlit");
            if (shader == null)
            {
                Debug.LogError("Shape Merge: Resources/ShapeMerge/ShapeMergeUnlit.shader is missing.");
                enabled = false;
                return;
            }
            material = new Material(shader) { name = "Shape Merge Unlit" };

            tiers = ShapeCatalog.Build();
            looks = new ShapeLook[tiers.Length];
            previewRadius = new float[tiers.Length];
            for (int i = 0; i < tiers.Length; i++)
            {
                looks[i] = ShapeLook.Of(Palette.Linear(tiers[i].Color), Palette.ShadeLinear(tiers[i].Color, 0.42f, 1.1f, -0.012f), tiers[i].Radius);
                foreach (Vector2 p in tiers[i].Outline) previewRadius[i] = Mathf.Max(previewRadius[i], p.magnitude);
            }
            dangerRed = Palette.LinearHex("#FF2E4D");

            SetupCamera();

            world = new SoftWorld
            {
                Gravity = new Vector2(0f, -gravity),
                SubstepRate = substepRate,
                HasContainer = true,
                Left = arena.Left,
                Right = arena.Right,
                Floor = arena.Floor,
                CornerRadius = arena.CornerRadius,
                CollisionFilter = ShouldCollide,
            };

            Transform root = transform;
            backdrop = new Backdrop(material, root, tiers);
            shapeLayers = new ShapeLayers("Shapes", material, 0, root);
            guideLayer = new MeshLayer("Aim Guide", material, 12, root);
            bubbleLayer = new MeshLayer("Next Bubble", material, 14, root);
            cannonLayers = new ShapeLayers("Cannon", material, 20, root);
            glowLayer = new MeshLayer("Cannon Glow", material, 24, root);
            fx = new FxSystem(material, 30, root, tiers);
            sfx = new Sfx(root);
            hud = new Hud(root);
            post = new PostFx(root);
            input = new GameInput();
            cannon = new Cannon(arena);

            best = PlayerPrefs.GetInt(BestKey, 0);
            StartGame();
        }

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Hex("#141533");
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.allowHDR = true;
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            rig = new CameraRig(cam);
        }

        void StartGame()
        {
            foreach (Shape s in shapes) world.Remove(s.Body);
            shapes.Clear();
            merges.Clear();
            popQueue.Clear();
            world.Clear();
            fx.Clear();
            cannon.Reset();

            phase = Phase.Playing;
            score = combo = highestTier = shots = 0;
            comboTimer = dangerTimer = dangerLevel = 0f;
            hitstop = 0f;
            slowmo = 1f;
            reloadTimer = 0.35f;
            overShown = newBest = false;
            nextTier = RollTier();
            nextPop = new SpringFloat(0f);

            hud.SetScore(0, true);
            hud.SetBest(best);
            hud.HideGameOver();
        }

        // ---------------------------------------------------------------- frame

        void Update()
        {
            float raw = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            time += raw;
            input.Poll(cam);

            // Hitstop freezes the world for a beat on big merges; slow motion eases back to normal.
            float dt = raw;
            if (hitstop > 0f)
            {
                hitstop -= raw;
                dt = 0f;
            }
            slowmo = Mathf.MoveTowards(slowmo, 1f, raw * 0.9f);
            dt *= slowmo;

            rig.Tick(raw, arena);
            if (input.Restart)
            {
                StartGame();
                return;
            }

            if (phase == Phase.Playing) UpdatePlaying(dt);
            else UpdateGameOver(raw);

            // The new preview waits until the outgoing shape has hopped clear of the bubble.
            nextDelay -= raw;
            if (nextDelay <= 0f) nextPop.Step(1f, 3.2f, 0.42f, raw);
            UpdateShapes(dt);
            UpdateMerges(dt);
            world.Step(dt);
            HandleContacts();
            fx.Update(dt);
            post.Tick(raw, dangerLevel, time);
            hud.Tick(raw, cam, arena);
            sfx.Tick(raw, cannon.Charging ? cannon.Charge : -1f);
        }

        void UpdatePlaying(float dt)
        {
            if (cannon.Loaded == null)
            {
                reloadTimer -= dt;
                if (reloadTimer <= 0f) LoadNext();
            }

            Shape loaded = cannon.Loaded;
            bool ready = loaded != null && loaded.Age > LoadTravelTime * 0.8f;
            if (cannon.Tick(dt, input, ready, out Cannon.Shot shot) && loaded != null) Fire(loaded, shot);

            if (cannon.Loaded != null)
            {
                // Hop the next shape from its bubble into the cannon along an arc, with a little spin.
                Shape s = cannon.Loaded;
                SoftBody b = s.Body;
                cannon.HoldPose(s, out Vector2 pose, out float angle);
                float t = Mathf.Clamp01(s.Age / LoadTravelTime);
                float e = Ease.OutCubic(t);
                b.HoldPosition = Vector2.LerpUnclamped(arena.NextBubble, pose, e) + Vector2.up * (Mathf.Sin(t * Mathf.PI) * 0.9f);
                b.HoldAngle = angle + (1f - e) * Mathf.PI * 0.5f;
                b.Stretch = cannon.HoldStretch;
                if (!s.Seated && s.Age >= LoadTravelTime)
                {
                    s.Seated = true;
                    cannon.Bump(5f); // plop into the nozzle
                }
            }

            comboTimer -= dt;
            UpdateDanger(dt);
        }

        void LoadNext()
        {
            int tier = nextTier;
            nextTier = RollTier();
            nextPop = new SpringFloat(0f);
            nextDelay = 0.22f;

            Shape s = CreateShape(tier, arena.NextBubble, 0f, 0.55f);
            s.State = ShapeState.Loaded;
            s.Body.Held = true;
            s.Body.Collidable = false;
            s.Body.HoldPosition = arena.NextBubble;
            cannon.Loaded = s;
            sfx.Load();
        }

        int RollTier()
        {
            // Start with circles and triangles; bigger shots unlock as the player climbs.
            int max = highestTier >= 4 ? 3 : highestTier >= 2 ? 2 : 1;
            float[] weights = { 10f, 7f, 4f, 2.2f };
            float total = 0f;
            for (int i = 0; i <= max; i++) total += weights[i];
            float r = (float)rng.NextDouble() * total;
            for (int i = 0; i <= max; i++)
            {
                r -= weights[i];
                if (r <= 0f) return i;
            }
            return 0;
        }

        void Fire(Shape s, Cannon.Shot shot)
        {
            cannon.Loaded = null;
            SoftBody b = s.Body;
            b.Held = false;
            b.Collidable = true;
            b.Stretch = Vector2.one;
            s.State = ShapeState.Live;
            s.SinceLaunch = 0f;

            // The front leaves a little faster than the back: a juicy stretch on launch.
            Vector2 c = b.Center;
            float r = Mathf.Max(0.1f, b.RestRadius * b.Scale);
            for (int i = 0; i < b.Count; i++)
            {
                float along = Vector2.Dot(b.Pos[i] - c, shot.Dir) / r;
                b.Vel[i] = shot.Dir * (shot.Speed * (1f + 0.16f * along));
            }

            reloadTimer = reloadTime;
            shots++;
            if (shots >= 3) hud.HideHint();
            sfx.Shoot(shot.Power);
            fx.Muzzle(cannon.Muzzle, shot.Dir, s.Tier, shot.Power);
            rig.Kick(-shot.Dir * (0.6f + 1.6f * shot.Power));
            rig.AddTrauma(0.04f + 0.14f * shot.Power);
        }

        Shape CreateShape(int tierIndex, Vector2 position, float angle, float scale)
        {
            ShapeTier tier = tiers[tierIndex];
            var body = new SoftBody(tier.Outline, tier.Mass, tier.Physics);
            body.Place(position, angle, scale);
            world.Add(body);
            var s = new Shape(tier, body, scale);
            shapes.Add(s);
            return s;
        }

        void RemoveShape(Shape s)
        {
            world.Remove(s.Body);
            shapes.Remove(s);
            if (cannon.Loaded == s) cannon.Loaded = null;
        }

        void UpdateShapes(float dt)
        {
            float last = tiers.Length - 1;
            for (int i = 0; i < shapes.Count; i++)
            {
                Shape s = shapes[i];
                s.Age += dt;
                s.SinceLaunch += dt;
                s.Flash = Mathf.Max(0f, s.Flash - dt * 3.2f);
                s.Glow = Mathf.Max(0f, s.Glow - dt * 2f);
                // Bouncy scale spring: merged shapes inflate from a tiny seed and overshoot.
                float scale = s.ScaleSpring.Step(s.ScaleTarget, Mathf.Lerp(4.2f, 2.8f, s.Tier.Index / last), 0.42f, dt);
                s.Body.Scale = Mathf.Max(0.05f, scale);
            }
        }

        // ---------------------------------------------------------------- merging

        static bool ShouldCollide(SoftBody a, SoftBody b)
        {
            MergeJob m = ((Shape)a.Owner).Merge;
            return m == null || m != ((Shape)b.Owner).Merge;
        }

        static Vector2 MergePoint(MergeJob m)
        {
            float ma = m.A.Body.Mass, mb = m.B.Body.Mass;
            return (m.A.Body.Center * ma + m.B.Body.Center * mb) / (ma + mb);
        }

        void HandleContacts()
        {
            List<SoftContact> contacts = world.Contacts;
            for (int i = 0; i < contacts.Count; i++)
            {
                SoftContact c = contacts[i];
                var a = (Shape)c.A.Owner;
                Shape b = c.B != null ? (Shape)c.B.Owner : null;
                if (b != null && phase == Phase.Playing && a.Tier == b.Tier && a.CanMerge && b.CanMerge)
                {
                    StartMerge(a, b);
                    continue;
                }

                if (c.ImpactSpeed < 3.2f) continue;
                if (time - a.LastImpact < 0.12f || (b != null && time - b.LastImpact < 0.12f)) continue;
                a.LastImpact = time;
                if (b != null) b.LastImpact = time;
                float strength = Mathf.InverseLerp(3.2f, 16f, c.ImpactSpeed);
                sfx.Impact(strength, b != null ? Mathf.Max(a.Tier.Index, b.Tier.Index) : a.Tier.Index);
                fx.Impact(c.Point, c.Normal, strength);
                if (strength > 0.4f) rig.AddTrauma(0.06f * strength);
            }
        }

        void StartMerge(Shape a, Shape b)
        {
            var job = new MergeJob { A = a, B = b };
            a.Merge = b.Merge = job;
            a.State = b.State = ShapeState.Merging;
            a.Body.Attracted = b.Body.Attracted = true;
            a.Body.AttractFrequency = b.Body.AttractFrequency = 6.5f;
            a.Body.AttractPoint = b.Body.AttractPoint = MergePoint(job);
            a.ScaleTarget = b.ScaleTarget = 0.62f;
            a.Glow = b.Glow = 0.7f;
            merges.Add(job);
            sfx.MergeStart(a.Tier.Index);
        }

        void UpdateMerges(float dt)
        {
            for (int i = merges.Count - 1; i >= 0; i--)
            {
                MergeJob m = merges[i];
                m.Time += dt;
                Vector2 point = MergePoint(m);
                m.A.Body.AttractPoint = point;
                m.B.Body.AttractPoint = point;
                if (m.Time < slurpTime) continue;
                merges.RemoveAt(i);
                CompleteMerge(m, point);
            }
        }

        void CompleteMerge(MergeJob m, Vector2 point)
        {
            Shape a = m.A, b = m.B;
            ShapeTier from = a.Tier;
            float ma = a.Body.Mass, mb = b.Body.Mass;
            Vector2 velocity = (a.Body.Velocity * ma + b.Body.Velocity * mb) / (ma + mb);
            RemoveShape(a);
            RemoveShape(b);

            combo = comboTimer > 0f ? combo + 1 : 1;
            comboTimer = comboWindow;

            if (from.Index == tiers.Length - 1)
            {
                Supernova(point, from);
                return;
            }

            // The new shape starts as a tiny seed and inflates, shoving its neighbours aside.
            ShapeTier to = tiers[from.Index + 1];
            const float seed = 0.3f;
            float r = to.Radius * seed;
            point.x = Mathf.Clamp(point.x, arena.Left + r, arena.Right - r);
            point.y = Mathf.Max(point.y, arena.Floor + r);
            Shape s = CreateShape(to.Index, point, ((float)rng.NextDouble() - 0.5f) * 0.5f, seed);
            s.Body.SetVelocity(velocity * 0.5f + new Vector2(0f, 1.5f));
            s.Flash = 1f;
            s.Glow = 1.1f;

            int gained = to.Value * combo;
            score += gained;
            hud.SetScore(score);
            bool unlocked = to.Index > highestTier;
            highestTier = Mathf.Max(highestTier, to.Index);

            float big = to.Index / (float)(tiers.Length - 1);
            fx.MergeBurst(point, from, to, combo);
            sfx.Merge(to.Index, combo);
            rig.AddTrauma(0.1f + 0.3f * big + 0.03f * combo);
            rig.ZoomPunch(0.3f + 0.9f * big);
            post.Kick(0.12f + 0.45f * big, 0.25f + 0.8f * big);
            if (to.Index >= 4) hitstop = Mathf.Max(hitstop, 0.035f + 0.015f * (to.Index - 4));

            hud.Popup("+" + gained, point + Vector2.up * (to.Radius * 0.6f), to.Color, 0.8f + 0.1f * to.Index);
            if (combo >= 2) hud.Combo(combo, to.Color);
            if (unlocked && to.Index >= 3) hud.Banner(to.Name.ToUpperInvariant() + "!", to.Color);
        }

        // Two stars collide: both vanish in a blast that shoves the whole pile.
        void Supernova(Vector2 point, ShapeTier star)
        {
            int gained = star.Value * 8 * combo;
            score += gained;
            hud.SetScore(score);
            for (int i = 0; i < shapes.Count; i++)
            {
                Shape s = shapes[i];
                if (s.State != ShapeState.Live) continue;
                Vector2 d = s.Body.Center - point;
                float dist = Mathf.Max(0.6f, d.magnitude);
                s.Body.AddVelocity(d / dist * Mathf.Min(16f, 22f / dist));
                s.Flash = 0.7f;
            }
            fx.Supernova(point);
            sfx.Supernova();
            rig.AddTrauma(1f);
            rig.ZoomPunch(2.5f);
            post.Kick(1f, 2.5f);
            hitstop = 0.14f;
            hud.Popup("+" + gained, point + Vector2.up, star.Color, 1.6f, 1.6f);
            hud.Banner("SUPERNOVA!", star.Color);
        }

        // ---------------------------------------------------------------- danger and game over

        void UpdateDanger(float dt)
        {
            bool over = false;
            float line = arena.DangerY;
            for (int i = 0; i < shapes.Count; i++)
            {
                Shape s = shapes[i];
                bool above = s.State == ShapeState.Live && s.SinceLaunch > 1.2f &&
                             s.Body.Max.y > line && s.Body.Velocity.sqrMagnitude < 16f;
                s.Danger = Mathf.MoveTowards(s.Danger, above ? 1f : 0f, dt * 3f);
                over |= above;
            }

            dangerTimer = over ? dangerTimer + dt : Mathf.Max(0f, dangerTimer - dt * 2f);
            dangerLevel = Mathf.Clamp01(dangerTimer / dangerSeconds);
            if (over)
            {
                dangerTickTimer -= dt;
                if (dangerTickTimer <= 0f)
                {
                    sfx.DangerTick(dangerLevel);
                    dangerTickTimer = Mathf.Lerp(0.5f, 0.14f, dangerLevel);
                }
            }
            else
            {
                dangerTickTimer = 0f;
            }

            if (dangerTimer >= dangerSeconds) GameOver();
        }

        void GameOver()
        {
            phase = Phase.GameOver;
            overTimer = popTimer = 0f;
            overShown = false;
            slowmo = 0.25f;
            hitstop = 0.1f;
            rig.AddTrauma(0.6f);
            post.Kick(0.7f, 0.6f);
            sfx.GameOver();

            foreach (MergeJob m in merges)
            {
                Release(m.A);
                Release(m.B);
            }
            merges.Clear();

            popQueue.Clear();
            popQueue.AddRange(shapes);
            popQueue.Sort((p, q) => q.Body.Max.y.CompareTo(p.Body.Max.y)); // top of the pile first

            newBest = score > best;
            if (newBest)
            {
                best = score;
                PlayerPrefs.SetInt(BestKey, best);
                PlayerPrefs.Save();
            }
        }

        static void Release(Shape s)
        {
            s.Merge = null;
            s.State = ShapeState.Live;
            s.Body.Attracted = false;
            s.ScaleTarget = 1f;
        }

        void UpdateGameOver(float raw)
        {
            overTimer += raw;
            dangerLevel = Mathf.MoveTowards(dangerLevel, 0f, raw);
            cannon.Tick(raw, input, false, out _);

            // Pop the pile one shape at a time, top to bottom.
            if (overTimer > 0.6f && popQueue.Count > 0)
            {
                popTimer -= raw;
                if (popTimer <= 0f)
                {
                    Shape s = popQueue[0];
                    popQueue.RemoveAt(0);
                    if (shapes.Contains(s)) PopShape(s);
                    popTimer = Mathf.Lerp(0.09f, 0.04f, Mathf.Clamp01(popQueue.Count / 30f));
                }
            }

            if (!overShown && popQueue.Count == 0 && overTimer > 0.8f)
            {
                overShown = true;
                overShownAt = overTimer;
                hud.ShowGameOver(score, best, newBest);
                if (newBest) hud.SetBest(best, true);
            }

            if (overShown && overTimer - overShownAt > 0.7f && input.Pressed) StartGame();
        }

        void PopShape(Shape s)
        {
            fx.Pop(s.Body.Center, s.Tier);
            sfx.PopShape(s.Tier.Index);
            rig.AddTrauma(0.05f);
            RemoveShape(s);
        }

        // ---------------------------------------------------------------- rendering

        void LateUpdate()
        {
            if (material == null) return;
            backdrop.Render(cam, arena, time, dangerLevel);

            shapeLayers.Clear();
            for (int i = 0; i < shapes.Count; i++) PaintShape(shapes[i]);
            shapeLayers.Apply();

            guideLayer.Clear();
            if (phase == Phase.Playing) cannon.RenderGuide(guideLayer, world, time);
            guideLayer.Apply();

            cannonLayers.Clear();
            glowLayer.Clear();
            bubbleLayer.Clear();
            int tintTier = cannon.Loaded != null ? cannon.Loaded.Tier.Index : nextTier;
            cannon.Render(cannonLayers, glowLayer, looks[tintTier].Fill);
            RenderNext();
            cannonLayers.Apply();
            glowLayer.Apply();
            bubbleLayer.Apply();

            fx.Render(time);
        }

        void PaintShape(Shape s)
        {
            SoftBody b = s.Body;
            int sub = b.Count <= 24 ? 4 : b.Count <= 40 ? 3 : 2;
            Draw.SmoothClosed(b.Pos, b.Count, sub, outline);
            ShapeLook look = looks[s.Tier.Index];
            look.Size = s.Tier.Radius * b.Scale;
            look.Flash = s.Flash;
            look.Glow = s.Glow * 0.7f;
            if (s.Danger > 0f)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 14f);
                look.Fill = Palette.Lerp(look.Fill, dangerRed, s.Danger * (0.3f + 0.35f * pulse));
            }
            ShapePainter.Paint(shapeLayers, outline, look);
        }

        void RenderNext()
        {
            Vector2 p = arena.NextBubble + new Vector2(0f, Mathf.Sin(time * 2.1f) * 0.06f);
            const float bubble = 0.92f;
            Draw.Disc(bubbleLayer, p, bubble, new Color(0.05f, 0.05f, 0.08f, 0.08f));
            Draw.Ring(bubbleLayer, p, bubble, 0.05f, new Color(0.3f, 0.3f, 0.42f, 0.45f));
            if (phase != Phase.Playing) return;

            ShapeTier tier = tiers[nextTier];
            float scale = Mathf.Min(1f, 0.6f / previewRadius[nextTier]) * Mathf.Max(0f, nextPop.Value);
            if (scale <= 0.01f) return;

            float rot = Mathf.Sin(time * 1.4f) * 0.12f;
            float cs = Mathf.Cos(rot) * scale, sn = Mathf.Sin(rot) * scale;
            Vector2[] src = tier.Outline;
            if (previewPoints.Length < src.Length) previewPoints = new Vector2[src.Length];
            for (int i = 0; i < src.Length; i++)
                previewPoints[i] = p + new Vector2(cs * src[i].x - sn * src[i].y, sn * src[i].x + cs * src[i].y);
            Draw.SmoothClosed(previewPoints, src.Length, src.Length <= 24 ? 4 : 3, outline);

            ShapeLook look = looks[nextTier];
            look.Size = tier.Radius * scale;
            look.Shadow = 0.2f;
            ShapePainter.Paint(cannonLayers, outline, look);
        }
    }
}
