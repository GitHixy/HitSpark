using System;
using HitSpark.Models;

namespace HitSpark.Rendering;

/// <summary>Applies a style's <see cref="TextCase"/> to fly text, in place where it can.</summary>
public static class TextCasing
{
    public static void Apply(Span<char> text, TextCase mode)
    {
        switch (mode)
        {
            case TextCase.Upper:
                for (var i = 0; i < text.Length; i++) text[i] = char.ToUpperInvariant(text[i]);
                break;
            case TextCase.Lower:
                for (var i = 0; i < text.Length; i++) text[i] = char.ToLowerInvariant(text[i]);
                break;
            case TextCase.Capitalized:
                var wordStart = true;
                for (var i = 0; i < text.Length; i++)
                {
                    var c = text[i];
                    if (char.IsLetter(c))
                    {
                        text[i] = wordStart ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
                        wordStart = false;
                    }
                    else
                    {
                        // Apostrophes stay inside a word ("Bahamut's"), digits too.
                        wordStart = !char.IsDigit(c) && c is not '\'' and not '’';
                    }
                }

                break;
        }
    }

    public static string? Apply(string? text, TextCase mode)
    {
        if (mode == TextCase.AsIs || string.IsNullOrEmpty(text)) return text;
        return string.Create(text.Length, (text, mode), static (span, state) =>
        {
            state.text.AsSpan().CopyTo(span);
            Apply(span, state.mode);
        });
    }
}
