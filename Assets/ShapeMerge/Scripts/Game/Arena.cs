using UnityEngine;

namespace ShapeMerge
{
    /// <summary>World layout: the tub, the danger line, the cannon rail and where the HUD sits.</summary>
    public sealed class Arena
    {
        public readonly float Left = -3.6f;
        public readonly float Right = 3.6f;
        public readonly float Floor = 0f;
        public readonly float Top = 9.2f;
        public readonly float CornerRadius = 0.6f;

        public float Width => Right - Left;
        public float DangerY => Top - 0.7f;
        public float RailY => Top + 1.85f;

        public bool Portrait { get; private set; }
        public Rect View { get; private set; }          // world rect the camera must show
        public Vector2 NextBubble { get; private set; }
        public Vector2 ScoreAnchor { get; private set; }
        public Vector2 BestAnchor { get; private set; }
        public Vector2 HintAnchor { get; private set; }
        public Vector2 BannerAnchor { get; private set; }
        public Vector2 ComboAnchor { get; private set; }

        public Arena() => Layout(16f / 9f);

        public void Layout(float aspect)
        {
            Portrait = aspect < 1.05f;
            if (Portrait)
            {
                // Top bar: SCORE on the left, BEST in the middle, NEXT on the right.
                View = Rect.MinMaxRect(Left - 0.55f, Floor - 1.1f, Right + 0.55f, RailY + 3.4f);
                ScoreAnchor = new Vector2(Left + 1.2f, RailY + 1.85f);
                BestAnchor = new Vector2(0f, RailY + 1.75f);
                NextBubble = new Vector2(Right - 1.1f, RailY + 1.85f);
            }
            else
            {
                View = Rect.MinMaxRect(Left - 4.9f, Floor - 1.0f, Right + 4.9f, RailY + 1.35f);
                NextBubble = new Vector2(Right + 2.3f, RailY - 1.0f);
                ScoreAnchor = new Vector2(Left - 2.5f, Top - 0.3f);
                BestAnchor = new Vector2(Left - 2.5f, Top - 2.2f);
            }
            HintAnchor = new Vector2(0f, Floor - 0.6f);
            BannerAnchor = new Vector2(0f, Top * 0.62f);
            ComboAnchor = new Vector2(0f, DangerY - 0.9f);
        }
    }
}
