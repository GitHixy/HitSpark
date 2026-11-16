using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HitSpark.Models;

namespace HitSpark.Rendering;

/// <summary>
/// Colour correction for colour vision deficiencies, applied to every preset at compile time.
/// Works in Oklab: for protan and deutan the red-green axis (a), which those viewers lose, is
/// folded into the blue-yellow axis (b) they still see, so reds and greens part into warm and
/// cool. Tritan viewers lose blue-yellow instead, so yellows are darkened and blues lightened.
/// Classic RGB daltonisation was tried first and pushed pastel pairs (buffs and debuffs) together.
/// Simulation for previews uses Machado, Oliveira and Fernandes (2009) at full severity.
/// </summary>
public static class ColorAssist
{
    public static ColorVision Mode { get; private set; } = ColorVision.Normal;
    public static float Strength { get; private set; } = 1f;

    public static bool Active => Mode != ColorVision.Normal && Strength > 0f;

    /// <summary>Sets the filter. Callers recompile presets afterwards.</summary>
    public static void Configure(ColorVision mode, float strength)
    {
        Mode = Enum.IsDefined(mode) ? mode : ColorVision.Normal;
        Strength = Math.Clamp(strength, 0f, 1f);
    }

    private static readonly Matrix4x4 Protan = Rows(
        0.152286f, 1.052583f, -0.204868f,
        0.114503f, 0.786281f, 0.099216f,
        -0.003882f, -0.048116f, 1.051998f);

    private static readonly Matrix4x4 Deutan = Rows(
        0.367322f, 0.860646f, -0.227968f,
        0.280085f, 0.672501f, 0.047413f,
        -0.011820f, 0.042940f, 0.968881f);

    private static readonly Matrix4x4 Tritan = Rows(
        1.255528f, -0.076749f, -0.178779f,
        -0.078411f, 0.930809f, 0.147602f,
        0.004733f, 0.691367f, 0.303900f);

    /// <summary>The colour corrected for the current mode; alpha is untouched.</summary>
    public static Vector4 Apply(Vector4 color) => Active ? Apply(color, Mode, Strength) : color;

    public static uint Apply(uint packed)
        => Active ? ImGui.ColorConvertFloat4ToU32(Apply(ImGui.ColorConvertU32ToFloat4(packed))) : packed;

    public static Vector4 Apply(Vector4 color, ColorVision mode, float strength)
    {
        if (mode == ColorVision.Normal || strength <= 0f) return color;

        var lab = ToOklab(new Vector3(ToLinear(color.X), ToLinear(color.Y), ToLinear(color.Z)));
        lab = mode == ColorVision.Tritanopia
            ? lab with { X = lab.X - 0.5f * lab.Z * strength }
            : lab with { Z = lab.Z + lab.Y * strength };
        var rgb = FromOklab(lab);

        return new Vector4(
            ToSrgb(Math.Clamp(rgb.X, 0f, 1f)),
            ToSrgb(Math.Clamp(rgb.Y, 0f, 1f)),
            ToSrgb(Math.Clamp(rgb.Z, 0f, 1f)),
            color.W);
    }

    // Oklab (Björn Ottosson, 2020): X = L, Y = a, Z = b.
    private static Vector3 ToOklab(Vector3 c)
    {
        var l = MathF.Cbrt(0.4122214708f * c.X + 0.5363325363f * c.Y + 0.0514459929f * c.Z);
        var m = MathF.Cbrt(0.2119034982f * c.X + 0.6806995451f * c.Y + 0.1073969566f * c.Z);
        var s = MathF.Cbrt(0.0883024619f * c.X + 0.2817188376f * c.Y + 0.6299787005f * c.Z);
        return new Vector3(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    private static Vector3 FromOklab(Vector3 lab)
    {
        var l = lab.X + 0.3963377774f * lab.Y + 0.2158037573f * lab.Z;
        var m = lab.X - 0.1055613458f * lab.Y - 0.0638541728f * lab.Z;
        var s = lab.X - 0.0894841775f * lab.Y - 1.2914855480f * lab.Z;
        l *= l * l;
        m *= m * m;
        s *= s * s;
        return new Vector3(
            4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
            -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
            -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
    }

    /// <summary>How the colour looks to someone with the deficiency, for previews.</summary>
    public static Vector4 Simulate(Vector4 color, ColorVision mode)
    {
        if (mode == ColorVision.Normal) return color;
        var sim = mode switch
        {
            ColorVision.Protanopia => Protan,
            ColorVision.Deuteranopia => Deutan,
            _ => Tritan,
        };

        var seen = Mul(sim, new Vector3(ToLinear(color.X), ToLinear(color.Y), ToLinear(color.Z)));
        return new Vector4(
            ToSrgb(Math.Clamp(seen.X, 0f, 1f)),
            ToSrgb(Math.Clamp(seen.Y, 0f, 1f)),
            ToSrgb(Math.Clamp(seen.Z, 0f, 1f)),
            color.W);
    }

    private static Matrix4x4 Rows(float a, float b, float c, float d, float e, float f, float g, float h, float i)
        => new(a, b, c, 0f, d, e, f, 0f, g, h, i, 0f, 0f, 0f, 0f, 1f);

    private static Vector3 Mul(Matrix4x4 m, Vector3 v) => new(
        m.M11 * v.X + m.M12 * v.Y + m.M13 * v.Z,
        m.M21 * v.X + m.M22 * v.Y + m.M23 * v.Z,
        m.M31 * v.X + m.M32 * v.Y + m.M33 * v.Z);

    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float ToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
}
