using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShapeMerge
{
    /// <summary>
    /// Runtime URP volume: bloom for the HDR flashes, a vignette that turns red in danger and
    /// chromatic aberration kicks on big merges.
    /// </summary>
    public sealed class PostFx
    {
        const float BaseBloom = 0.85f;
        const float BaseVignette = 0.3f;

        readonly Bloom bloom;
        readonly Vignette vignette;
        readonly ChromaticAberration chroma;
        float chromaKick, bloomKick;

        public PostFx(Transform parent)
        {
            var go = new GameObject("Post FX");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Shape Merge Post FX";

            bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 0.95f;
            bloom.intensity.value = BaseBloom;
            bloom.scatter.value = 0.72f;
            bloom.tint.value = Color.white;
            bloom.highQualityFiltering.value = true;

            // Keep the authored palette exact; nothing in the scene relies on tonemapping.
            profile.Add<Tonemapping>(true).mode.value = TonemappingMode.None;

            vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = BaseVignette;
            vignette.smoothness.value = 0.5f;
            vignette.color.value = Color.black;
            vignette.rounded.value = false;

            chroma = profile.Add<ChromaticAberration>(true);
            chroma.intensity.value = 0f;

            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.value = 6f;
            color.contrast.value = 5f;
            color.postExposure.value = 0f;

            volume.sharedProfile = profile;
        }

        public void Kick(float chromaAmount, float bloomAmount)
        {
            chromaKick = Mathf.Max(chromaKick, chromaAmount);
            bloomKick = Mathf.Max(bloomKick, bloomAmount);
        }

        public void Tick(float dt, float danger, float time)
        {
            chromaKick = Mathf.MoveTowards(chromaKick, 0f, dt * 2.2f);
            bloomKick = Mathf.MoveTowards(bloomKick, 0f, dt * 2.5f);
            chroma.intensity.value = Mathf.Clamp01(chromaKick);
            bloom.intensity.value = BaseBloom + bloomKick;

            float pulse = danger > 0f ? (0.5f + 0.5f * Mathf.Sin(time * 9f)) * danger : 0f;
            vignette.color.value = Color.Lerp(Color.black, new Color(0.7f, 0.04f, 0.12f), danger * 0.9f);
            vignette.intensity.value = BaseVignette + 0.15f * pulse + 0.1f * danger;
        }
    }
}
