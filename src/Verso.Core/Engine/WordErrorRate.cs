namespace Verso.Core.Engine;

public static class WordErrorRate
{
    public static double Compute(string reference, string hypothesis)
    {
        var refWords = Tokenize(reference);
        var hypWords = Tokenize(hypothesis);
        if (refWords.Length == 0)
        {
            return hypWords.Length == 0 ? 0 : 1;
        }

        return Levenshtein(refWords, hypWords) / (double)refWords.Length;
    }

    private static string[] Tokenize(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '\'' or '-' ? c : ' ').ToArray();
        return new string(chars).Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static int Levenshtein(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var dp = new int[a.Count + 1, b.Count + 1];
        for (var i = 0; i <= a.Count; i++)
        {
            dp[i, 0] = i;
        }

        for (var j = 0; j <= b.Count; j++)
        {
            dp[0, j] = j;
        }

        for (var i = 1; i <= a.Count; i++)
        {
            for (var j = 1; j <= b.Count; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Count, b.Count];
    }
}
