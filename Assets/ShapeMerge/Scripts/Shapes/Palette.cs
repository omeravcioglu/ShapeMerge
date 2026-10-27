using UnityEngine;

namespace ShapeMerge
{
    /// <summary>Colour helpers. Palette colours are authored in sRGB; meshes take linear colours.</summary>
    public static class Palette
    {
        public static Color Hex(string hex)
        {
            int start = hex.Length > 0 && hex[0] == '#' ? 1 : 0;
            uint v = System.Convert.ToUInt32(hex.Substring(start, 6), 16);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }

        public static Color Linear(Color srgb) => new Color(ToLinear(srgb.r), ToLinear(srgb.g), ToLinear(srgb.b), srgb.a);

        public static Color LinearHex(string hex) => Linear(Hex(hex));

        static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        /// <summary>Premultiplied colour for the shared blend mode (One, OneMinusSrcAlpha).</summary>
        public static Color Pm(Color linear, float alpha) =>
            new Color(linear.r * alpha, linear.g * alpha, linear.b * alpha, alpha);

        /// <summary>Additive glow under premultiplied blending: zero alpha means "add".</summary>
        public static Color Glow(Color linear, float intensity) =>
            new Color(linear.r * intensity, linear.g * intensity, linear.b * intensity, 0f);

        /// <summary>Scales the colour channels, keeping alpha.</summary>
        public static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        /// <summary>Scales every channel: fades a premultiplied colour.</summary>
        public static Color Scale(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a * k);

        public static Color Lerp(Color a, Color b, float t) =>
            new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);

        /// <summary>Shifts an sRGB colour in HSV space and returns it in linear space.</summary>
        public static Color ShadeLinear(Color srgb, float valueMul, float satMul, float hueShift = 0f)
        {
            Color.RGBToHSV(srgb, out float h, out float s, out float v);
            h = Mathf.Repeat(h + hueShift, 1f);
            Color c = Color.HSVToRGB(h, Mathf.Clamp01(s * satMul), Mathf.Clamp01(v * valueMul));
            c.a = srgb.a;
            return Linear(c);
        }
    }
}
