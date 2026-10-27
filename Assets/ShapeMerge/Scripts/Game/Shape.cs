namespace ShapeMerge
{
    public enum ShapeState { Loaded, Live, Merging }

    /// <summary>A shape in play: its tier, its soft body and its animation state.</summary>
    public sealed class Shape
    {
        public readonly ShapeTier Tier;
        public readonly SoftBody Body;

        public ShapeState State = ShapeState.Live;
        public MergeJob Merge;

        public float Age;
        public float SinceLaunch;   // grace period before the danger line counts it
        public float Flash;         // white-out after being created by a merge
        public float Glow;          // HDR shine that feeds bloom
        public float Danger;        // 0..1 tint while resting above the danger line
        public float LastImpact = -10f;
        public bool Seated;         // finished hopping into the cannon

        public SpringFloat ScaleSpring;
        public float ScaleTarget = 1f;

        public Shape(ShapeTier tier, SoftBody body, float scale)
        {
            Tier = tier;
            Body = body;
            ScaleSpring = new SpringFloat(scale);
            body.Owner = this;
        }

        public bool CanMerge => State == ShapeState.Live && Merge == null && Age > 0.05f;
    }

    /// <summary>Two shapes being slurped together before they become one bigger shape.</summary>
    public sealed class MergeJob
    {
        public Shape A, B;
        public float Time;
    }
}
