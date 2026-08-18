namespace Verso.Core.Engine.Diarization;

public static class WordBoundaryTextSplitter
{
    public const double PunctuationWindowSeconds = 0.4;

    public static (string Left, string Right) Split(
        string text,
        double startSeconds,
        double endSeconds,
        double splitSeconds)
    {
        var trimmed = text ?? "";
        if (trimmed.Length == 0)
        {
            return ("", "");
        }

        var tokens = SplitTokens(trimmed);
        if (tokens.Count == 0)
        {
            return ("", "");
        }

        var duration = Math.Max(endSeconds - startSeconds, 1e-6);
        var clamped = Math.Clamp(splitSeconds, startSeconds, endSeconds);

        var punctBest = -1;
        var punctDist = double.MaxValue;
        var wordBest = 0;
        var wordDist = double.MaxValue;

        for (var i = 0; i <= tokens.Count; i++)
        {
            var t = startSeconds + duration * i / tokens.Count;
            var dist = Math.Abs(t - clamped);
            if (dist < wordDist)
            {
                wordDist = dist;
                wordBest = i;
            }

            if (i > 0 && EndsWithSentencePunctuation(tokens[i - 1]) && dist <= PunctuationWindowSeconds && dist < punctDist)
            {
                punctDist = dist;
                punctBest = i;
            }
        }

        var cut = punctBest >= 0 ? punctBest : wordBest;
        return (Join(tokens, 0, cut), Join(tokens, cut, tokens.Count));
    }

    private static List<string> SplitTokens(string text)
    {
        var tokens = new List<string>();
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                if (start >= 0)
                {
                    tokens.Add(text[start..i]);
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
        {
            tokens.Add(text[start..]);
        }

        return tokens;
    }

    private static bool EndsWithSentencePunctuation(string token)
    {
        for (var i = token.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(token[i]))
            {
                continue;
            }

            return token[i] is '.' or '?' or '!';
        }

        return false;
    }

    private static string Join(List<string> tokens, int from, int to)
    {
        if (to <= from)
        {
            return "";
        }

        return string.Join(' ', tokens.GetRange(from, to - from));
    }
}
