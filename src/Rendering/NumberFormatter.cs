using System;

namespace HitSpark.Rendering;

/// <summary>
/// Allocation-free number formatting into a caller-provided buffer: digit grouping and
/// K/M/B abbreviation with one decimal while it still says something ("12.3K", "1.2M").
/// </summary>
public static class NumberFormatter
{
    public static int Write(Span<char> dst, long value, bool separators, bool abbreviate, long abbreviateFrom)
    {
        var written = 0;
        if (value < 0)
        {
            if (dst.Length == 0) return 0;
            dst[written++] = '-';
            value = -value;
        }

        if (abbreviate && value >= abbreviateFrom)
        {
            var (divisor, suffix) = value switch
            {
                >= 1_000_000_000 => (1_000_000_000L, 'B'),
                >= 1_000_000 => (1_000_000L, 'M'),
                _ => (1_000L, 'K'),
            };

            // One decimal below 100 of the unit, whole units above: 12.3K, 123K.
            var scaled = (double)value / divisor;
            var format = scaled < 100 ? "0.#" : "0";
            if (!scaled.TryFormat(dst[written..], out var n, format, System.Globalization.CultureInfo.InvariantCulture))
                return written;
            written += n;
            if (written < dst.Length) dst[written++] = suffix;
            return written;
        }

        if (!separators)
        {
            value.TryFormat(dst[written..], out var n, default, System.Globalization.CultureInfo.InvariantCulture);
            return written + n;
        }

        // Grouped: format plainly into scratch, then copy with commas every three digits.
        Span<char> digits = stackalloc char[20];
        value.TryFormat(digits, out var count, default, System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 0; i < count; i++)
        {
            if (i > 0 && (count - i) % 3 == 0)
            {
                if (written >= dst.Length) return written;
                dst[written++] = ',';
            }

            if (written >= dst.Length) return written;
            dst[written++] = digits[i];
        }

        return written;
    }
}
