using System;
using HitSpark.Models;

namespace HitSpark.Rendering;

/// <summary>
/// Easing curves over normalised time. All inputs are clamped to [0,1]; back, elastic and
/// bounce deliberately leave that range on the output side - that overshoot is the point.
/// </summary>
public static class Ease
{
    private const float BackOvershoot = 1.70158f;

    public static float Apply(Easing easing, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        switch (easing)
        {
            case Easing.QuadIn: return t * t;
            case Easing.QuadOut: return 1f - (1f - t) * (1f - t);
            case Easing.QuadInOut: return t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) * 0.5f;
            case Easing.CubicIn: return t * t * t;
            case Easing.CubicOut: return 1f - MathF.Pow(1f - t, 3f);
            case Easing.CubicInOut: return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) * 0.5f;
            case Easing.QuartOut: return 1f - MathF.Pow(1f - t, 4f);
            case Easing.ExpoOut: return t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);
            case Easing.BackOut:
            {
                const float c3 = BackOvershoot + 1f;
                var u = t - 1f;
                return 1f + c3 * u * u * u + BackOvershoot * u * u;
            }
            case Easing.ElasticOut:
            {
                if (t <= 0f) return 0f;
                if (t >= 1f) return 1f;
                const float c4 = 2f * MathF.PI / 3f;
                return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * c4) + 1f;
            }
            case Easing.BounceOut: return Bounce(t);
            default: return t;
        }
    }

    private static float Bounce(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;

        if (t < 1f / d1) return n1 * t * t;
        if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
        if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
        t -= 2.625f / d1;
        return n1 * t * t + 0.984375f;
    }

    /// <summary>
    /// The "impact" curve used by punch: full size on the first frame, a quick squash below
    /// rest, then a small rebound. Starts at 1, ends at 0.
    /// </summary>
    public static float Punch(float u)
    {
        if (u >= 1f) return 0f;
        if (u <= 0f) return 1f;
        var decay = (1f - u) * (1f - u);
        return decay * MathF.Cos(u * MathF.PI * 2.2f);
    }

    public static string Name(Easing easing) => easing switch
    {
        Easing.Linear => "Linear",
        Easing.QuadIn => "Quad in",
        Easing.QuadOut => "Quad out",
        Easing.QuadInOut => "Quad in-out",
        Easing.CubicIn => "Cubic in",
        Easing.CubicOut => "Cubic out",
        Easing.CubicInOut => "Cubic in-out",
        Easing.QuartOut => "Quart out",
        Easing.ExpoOut => "Expo out",
        Easing.BackOut => "Back (overshoot)",
        Easing.ElasticOut => "Elastic",
        Easing.BounceOut => "Bounce",
        _ => easing.ToString(),
    };
}
