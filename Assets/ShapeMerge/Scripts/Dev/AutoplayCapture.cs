using System.IO;
using System.Text;
using UnityEngine;

namespace ShapeMerge
{
    /// <summary>
    /// Development helper. Launch a player build with <c>-shapemerge-autoplay &lt;folder&gt;</c> and a
    /// bot plays for a while, saving screenshots and frame timings into the folder, then quits.
    /// Does nothing in normal runs.
    /// </summary>
    public sealed class AutoplayCapture : MonoBehaviour
    {
        static readonly float[] Shots = { 1.5f, 8f, 20f, 35f, 50f, 60f, 70f, 80f, 90f, 100f, 110f, 120f, 130f, 140f, 150f, 160f };
        static readonly (float start, int frames, float step)[] Bursts = { (5.2f, 10, 0.035f), (30f, 12, 0.05f) };
        float Duration = 165f;     // override with -shapemerge-autoplay-seconds
        const float StackFrom = 55f; // after this the bot drops shapes on the sides to reach game over

        string folder;
        float time, phaseTimer, holdTime;
        int phase, shotIndex;
        readonly int[] burstFrame = new int[Bursts.Length];
        float aimX, aimOffset;
        readonly System.Random rng = new System.Random(5);
        readonly StringBuilder log = new StringBuilder();
        float frameAccum, frameMax, overTimer;
        int frameCount, overShots;
        ShapeMergeGame game;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            AutoplayCapture capture = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-shapemerge-autoplay" && capture == null)
                {
                    var go = new GameObject("Autoplay Capture");
                    capture = go.AddComponent<AutoplayCapture>();
                    capture.folder = args[i + 1];
                    DontDestroyOnLoad(go);
                    GameInput.AutopilotActive = true;
                }
            }
            if (capture == null) return;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-shapemerge-autoplay-seconds" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                    capture.Duration = seconds;
        }

        void Start()
        {
            Directory.CreateDirectory(folder);
            GameInput.AutopilotPointer = new Vector2(0f, 6f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            time += dt;
            frameAccum += dt;
            frameMax = Mathf.Max(frameMax, dt);
            frameCount++;
            if (frameAccum >= 1f)
            {
                log.AppendLine($"t={time:F0}s fps={frameCount / frameAccum:F0} worst={frameMax * 1000f:F1}ms");
                frameAccum = frameMax = 0f;
                frameCount = 0;
            }

            Drive(dt);

            if (shotIndex < Shots.Length && time >= Shots[shotIndex])
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"shot_{shotIndex:D2}_{Shots[shotIndex]:F0}s.png"));
                shotIndex++;
            }

            for (int b = 0; b < Bursts.Length; b++)
            {
                var burst = Bursts[b];
                if (burstFrame[b] < burst.frames && time >= burst.start + burstFrame[b] * burst.step)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"burst{b}_{burstFrame[b]:D2}.png"));
                    burstFrame[b]++;
                }
            }

            // While the game-over card is up, grab a few frames of it.
            if (game == null) game = FindAnyObjectByType<ShapeMergeGame>();
            if (overShots < 3 && game != null && game.IsGameOver)
            {
                overTimer += dt;
                if (overTimer >= 0.5f + overShots * 1.2f)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"over_{overShots}_{time:F0}s.png"));
                    overShots++;
                }
            }

            if (time >= Duration)
            {
                File.WriteAllText(Path.Combine(folder, "timings.txt"), log.ToString());
                Application.Quit();
            }
        }

        // Move, then hold to charge while dragging the aim, then release; repeat.
        void Drive(float dt)
        {
            // Let the game-over card sit for a while before clicking to play again.
            if (game != null && game.IsGameOver && overTimer < 4.5f)
            {
                GameInput.AutopilotDown = false;
                phase = 0;
                return;
            }

            phaseTimer -= dt;
            switch (phase)
            {
                case 0:
                    if (time > StackFrom)
                    {
                        aimX = rng.NextDouble() < 0.5 ? -2.7f : 2.7f;
                        aimOffset = 0f;
                        holdTime = 0.02f;
                    }
                    else
                    {
                        aimX = (float)(rng.NextDouble() * 5.6 - 2.8);
                        aimOffset = (float)(rng.NextDouble() * 4.0 - 2.0);
                        holdTime = rng.NextDouble() < 0.5 ? 0.05f : (float)(0.1 + rng.NextDouble() * 0.8);
                    }
                    GameInput.AutopilotPointer = new Vector2(aimX, 6f);
                    GameInput.AutopilotDown = false;
                    phaseTimer = 0.3f;
                    phase = 1;
                    break;
                case 1:
                    if (phaseTimer > 0f) break;
                    GameInput.AutopilotDown = true;
                    phaseTimer = holdTime;
                    phase = 2;
                    break;
                case 2:
                    GameInput.AutopilotPointer = Vector2.Lerp(GameInput.AutopilotPointer, new Vector2(aimX + aimOffset, 3f), dt * 6f);
                    if (phaseTimer > 0f) break;
                    GameInput.AutopilotDown = false;
                    phaseTimer = 0.15f;
                    phase = 3;
                    break;
                default:
                    if (phaseTimer <= 0f) phase = 0;
                    break;
            }
        }
    }
}
