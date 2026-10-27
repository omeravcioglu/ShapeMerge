using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ShapeMerge
{
    /// <summary>
    /// Screen overlay: rolling score, best, the NEXT label, floating score pops, combo and
    /// new-shape banners and the game-over card. Labels are pinned to world anchors so the
    /// layout follows the arena (and its screen shake) on any aspect ratio.
    /// </summary>
    public sealed class Hud
    {
        sealed class Floater
        {
            public Text Text;
            public Vector2 World;
            public float Age, Life, Rise, Scale;
            public Color Color;
            public bool Active;
        }

        static readonly Color Ink = new Color(0.97f, 0.96f, 1f, 1f);
        static readonly Color Muted = new Color(0.68f, 0.68f, 0.92f, 1f);

        readonly RectTransform root;
        readonly CanvasScaler scaler;
        readonly Font font;
        bool? portrait;
        readonly Text scoreLabel, scoreValue, bestLabel, bestValue, nextLabel, hint, banner, combo;
        readonly Image overDim;
        readonly Text overTitle, overScore, overBest, overPrompt;
        readonly List<Floater> floaters = new List<Floater>();

        float shownScore;
        int targetScore;
        SpringFloat scorePunch, bestPunch, bannerPop, overPop, comboPop;
        float bannerAge = 99f, comboAge = 99f, comboSize = 1f, chrome = 1f;
        Color bannerColor = Ink, comboColor = Ink;
        bool overVisible;
        float overAge;
        float hintAlpha = 1f;
        bool hintWanted = true;
        float time;

        public Hud(Transform parent)
        {
            var go = new GameObject("HUD", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            root = (RectTransform)go.transform;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            scoreLabel = Label("Score Label", 34, Muted, "SCORE");
            scoreValue = Label("Score", 104, Ink, "0");
            bestLabel = Label("Best Label", 30, Muted, "BEST");
            bestValue = Label("Best", 54, Ink, "0");
            nextLabel = Label("Next Label", 30, Muted, "NEXT");
            hint = Label("Hint", 30, Muted, "Hold to charge  •  drag to aim  •  release to shoot");
            banner = Label("Banner", 92, Ink, "");
            combo = Label("Combo", 64, Ink, "");
            combo.gameObject.SetActive(false);

            var dim = new GameObject("Game Over Dim", typeof(RectTransform));
            dim.transform.SetParent(root, false);
            overDim = dim.AddComponent<Image>();
            overDim.color = new Color(0.03f, 0.02f, 0.09f, 0.72f);
            overDim.raycastTarget = false;
            var dimRect = overDim.rectTransform;
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;

            overTitle = Label("Game Over Title", 132, Ink, "GAME OVER");
            overScore = Label("Game Over Score", 64, Ink, "");
            overBest = Label("Game Over Best", 40, Muted, "");
            overPrompt = Label("Game Over Prompt", 36, Muted, "Click or tap to play again");
            SetOverVisible(false);

            for (int i = 0; i < 20; i++)
            {
                Text t = Label("Floater", 56, Ink, "");
                t.gameObject.SetActive(false);
                floaters.Add(new Floater { Text = t });
            }
        }

        Text Label(string name, int size, Color color, string text)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.color = color;
            t.text = text;
            t.rectTransform.sizeDelta = new Vector2(800f, size * 1.4f);
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.02f, 0.01f, 0.08f, 0.6f);
            shadow.effectDistance = new Vector2(0f, -Mathf.Max(2f, size * 0.06f));
            return t;
        }

        // ---------------------------------------------------------------- API

        public void SetScore(int score, bool instant = false)
        {
            if (score > targetScore && !instant) scorePunch.Velocity += 5f;
            targetScore = score;
            if (instant) shownScore = score;
        }

        public void SetBest(int best, bool punch = false)
        {
            bestValue.text = best.ToString();
            if (punch) bestPunch.Velocity += 6f;
        }

        public void HideHint() => hintWanted = false;

        public void Popup(string text, Vector2 world, Color srgb, float scale, float life = 1f)
        {
            // Nudge upward past any fresh pop-up nearby so numbers never pile on top of each other.
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int i = 0; i < floaters.Count; i++)
                {
                    Floater o = floaters[i];
                    if (!o.Active || o.Age > 0.6f) continue;
                    // Text height in world units is roughly 0.7 per unit of pop-up scale.
                    float gap = 0.36f * (o.Scale + scale) + 0.08f;
                    float oy = o.World.y + o.Rise * Ease.OutCubic(o.Age / o.Life);
                    if (Mathf.Abs(o.World.x - world.x) < 1.6f * Mathf.Max(o.Scale, scale) && Mathf.Abs(oy - world.y) < gap)
                    {
                        world.y = oy + gap;
                        moved = true;
                    }
                }
                if (!moved) break;
            }

            Floater f = null;
            for (int i = 0; i < floaters.Count; i++)
                if (!floaters[i].Active) { f = floaters[i]; break; }
            if (f == null)
            {
                f = floaters[0];
                for (int i = 1; i < floaters.Count; i++)
                    if (floaters[i].Age > f.Age) f = floaters[i];
            }
            f.Text.text = text;
            f.World = world;
            f.Age = 0f;
            f.Life = life;
            f.Rise = 1.1f;
            f.Scale = scale;
            f.Color = Color.Lerp(srgb, Color.white, 0.35f);
            f.Active = true;
            f.Text.gameObject.SetActive(true);
        }

        /// <summary>Pops the combo counter at the top of the tub; it grows with the chain.</summary>
        public void Combo(int count, Color srgb)
        {
            combo.text = "COMBO ×" + count;
            comboColor = Color.Lerp(srgb, Color.white, 0.3f);
            comboSize = 0.85f + 0.07f * Mathf.Min(count, 10);
            comboAge = 0f;
            comboPop = new SpringFloat(0.3f);
        }

        public void Banner(string text, Color srgb)
        {
            banner.text = text;
            bannerColor = Color.Lerp(srgb, Color.white, 0.2f);
            bannerAge = 0f;
            bannerPop = new SpringFloat(0f);
        }

        public void ShowGameOver(int score, int best, bool newBest)
        {
            overScore.text = "Score  " + score;
            overBest.text = newBest ? "NEW BEST!" : "Best  " + best;
            overBest.color = newBest ? new Color(1f, 0.85f, 0.35f) : Muted;
            overAge = 0f;
            overPop = new SpringFloat(0f);
            SetOverVisible(true);
        }

        public void HideGameOver() => SetOverVisible(false);

        void SetOverVisible(bool visible)
        {
            overVisible = visible;
            overDim.gameObject.SetActive(visible);
            overTitle.gameObject.SetActive(visible);
            overScore.gameObject.SetActive(visible);
            overBest.gameObject.SetActive(visible);
            overPrompt.gameObject.SetActive(visible);
        }

        // ---------------------------------------------------------------- per frame

        public void Tick(float dt, Camera cam, Arena arena)
        {
            time += dt;

            // Landscape scales the UI with screen height, portrait with screen width.
            if (portrait != arena.Portrait)
            {
                portrait = arena.Portrait;
                scaler.referenceResolution = arena.Portrait ? new Vector2(1080f, 1920f) : new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = arena.Portrait ? 0f : 1f;
            }

            float gap = targetScore - shownScore;
            shownScore = Mathf.MoveTowards(shownScore, targetScore, Mathf.Max(10f, Mathf.Abs(gap) * 9f) * dt);
            scoreValue.text = Mathf.RoundToInt(shownScore).ToString();
            float punch = scorePunch.Step(0f, 4.5f, 0.3f, dt);
            float bestBump = bestPunch.Step(0f, 4.5f, 0.3f, dt);

            Place(scoreLabel, cam, arena.ScoreAnchor, new Vector2(0f, 78f));
            Place(scoreValue, cam, arena.ScoreAnchor, Vector2.zero);
            scoreValue.rectTransform.localScale = Vector3.one * (1f + 0.25f * punch);
            Place(bestLabel, cam, arena.BestAnchor, new Vector2(0f, 46f));
            Place(bestValue, cam, arena.BestAnchor, Vector2.zero);
            bestValue.rectTransform.localScale = Vector3.one * (1f + 0.25f * bestBump);
            Place(nextLabel, cam, arena.NextBubble + Vector2.up * 1.3f, Vector2.zero);

            // The regular labels step aside while the game-over card is up.
            chrome = Mathf.MoveTowards(chrome, overVisible ? 0f : 1f, dt * 4f);
            SetAlpha(scoreLabel, Muted.a * chrome);
            SetAlpha(scoreValue, chrome);
            SetAlpha(bestLabel, Muted.a * chrome);
            SetAlpha(bestValue, chrome);
            SetAlpha(nextLabel, Muted.a * chrome);

            hintAlpha = Mathf.MoveTowards(hintAlpha, hintWanted && !overVisible ? 1f : 0f, dt * 1.5f);
            hint.gameObject.SetActive(hintAlpha > 0.01f);
            if (hintAlpha > 0.01f)
            {
                Place(hint, cam, arena.HintAnchor, Vector2.zero);
                Color c = Muted;
                c.a = hintAlpha * (0.75f + 0.25f * Mathf.Sin(time * 3f));
                hint.color = c;
            }

            UpdateFloaters(dt, cam);
            UpdateCombo(dt, cam, arena);
            UpdateBanner(dt, cam, arena);
            UpdateGameOver(dt);
        }

        void UpdateCombo(float dt, Camera cam, Arena arena)
        {
            const float life = 1.4f;
            comboAge += dt;
            bool show = comboAge < life;
            combo.gameObject.SetActive(show);
            if (!show) return;
            float s = comboPop.Step(1f, 4f, 0.32f, dt);
            float fade = comboAge < life - 0.3f ? 1f : (life - comboAge) / 0.3f;
            Place(combo, cam, arena.ComboAnchor, Vector2.zero);
            combo.rectTransform.localScale = Vector3.one * (Mathf.Max(0f, s) * comboSize);
            Color c = comboColor;
            c.a = fade;
            combo.color = c;
        }

        void UpdateFloaters(float dt, Camera cam)
        {
            for (int i = 0; i < floaters.Count; i++)
            {
                Floater f = floaters[i];
                if (!f.Active) continue;
                f.Age += dt;
                float t = f.Age / f.Life;
                if (t >= 1f)
                {
                    f.Active = false;
                    f.Text.gameObject.SetActive(false);
                    continue;
                }
                Place(f.Text, cam, f.World + Vector2.up * (f.Rise * Ease.OutCubic(t)), Vector2.zero);
                float scale = t < 0.15f ? Ease.OutBack(t / 0.15f, 2.6f) : 1f - 0.2f * (t - 0.15f);
                f.Text.rectTransform.localScale = Vector3.one * (f.Scale * scale);
                Color c = f.Color;
                c.a = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                f.Text.color = c;
            }
        }

        void UpdateBanner(float dt, Camera cam, Arena arena)
        {
            const float life = 1.7f;
            bannerAge += dt;
            bool show = bannerAge < life;
            banner.gameObject.SetActive(show);
            if (!show) return;
            float s = bannerPop.Step(1f, 3.2f, 0.4f, dt);
            float fade = bannerAge < life - 0.35f ? 1f : (life - bannerAge) / 0.35f;
            Place(banner, cam, arena.BannerAnchor, Vector2.zero);
            banner.rectTransform.localScale = Vector3.one * Mathf.Max(0f, s);
            Color c = bannerColor;
            c.a = fade;
            banner.color = c;
        }

        void UpdateGameOver(float dt)
        {
            if (!overVisible) return;
            overAge += dt;
            float s = overPop.Step(1f, 2.6f, 0.45f, dt);
            float fade = Mathf.Clamp01(overAge / 0.35f);
            overDim.color = new Color(0.03f, 0.02f, 0.09f, 0.72f * fade);

            overTitle.rectTransform.anchoredPosition = new Vector2(0f, 150f);
            overTitle.rectTransform.localScale = Vector3.one * Mathf.Max(0f, s);
            overScore.rectTransform.anchoredPosition = new Vector2(0f, 10f);
            overBest.rectTransform.anchoredPosition = new Vector2(0f, -60f);
            overPrompt.rectTransform.anchoredPosition = new Vector2(0f, -170f);

            SetAlpha(overTitle, fade);
            SetAlpha(overScore, Mathf.Clamp01((overAge - 0.2f) / 0.3f));
            SetAlpha(overBest, Mathf.Clamp01((overAge - 0.35f) / 0.3f));
            SetAlpha(overPrompt, Mathf.Clamp01((overAge - 0.6f) / 0.3f) * (0.6f + 0.4f * Mathf.Sin(time * 4f)));
        }

        static void SetAlpha(Text t, float a)
        {
            Color c = t.color;
            c.a = a;
            t.color = c;
        }

        void Place(Text t, Camera cam, Vector2 world, Vector2 offset)
        {
            Vector3 screen = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local);
            t.rectTransform.anchoredPosition = local + offset;
        }
    }
}
