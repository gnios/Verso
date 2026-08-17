using System.Text;
using System.Text.RegularExpressions;

namespace Verso.Core.Engine;

/// <summary>
/// Vocabulário SentencePiece do Parakeet (vocab.txt no layout onnx-asr).
/// Tokens com U+2581 (▁) viram espaço, como no decoder do onnx-asr.
/// </summary>
public sealed class ParakeetVocab
{
    private static readonly Regex DecodeSpacePattern = new(@"\A\s|\s\B|(\s)\b", RegexOptions.Compiled);

    private readonly Dictionary<int, string> _tokens;

    public ParakeetVocab(IReadOnlyDictionary<int, string> tokens)
    {
        _tokens = new Dictionary<int, string>(tokens);
        Size = _tokens.Count == 0 ? 0 : _tokens.Keys.Max() + 1;
        BlankIndex = ResolveBlankIndex(_tokens, Size);
    }

    public int Size { get; }
    public int BlankIndex { get; }

    public string this[int id] => _tokens.TryGetValue(id, out var token) ? token : "";

    private static int ResolveBlankIndex(IReadOnlyDictionary<int, string> tokens, int size)
    {
        foreach (var name in new[] { "<blk>", "<blank>" })
        {
            foreach (var pair in tokens)
            {
                if (string.Equals(pair.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Key;
                }
            }
        }

        return Math.Max(0, size - 1);
    }

    public static ParakeetVocab Load(string path)
    {
        var tokens = new Dictionary<int, string>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var lastSpace = line.LastIndexOf(' ');
            if (lastSpace <= 0)
            {
                continue;
            }

            var token = line[..lastSpace].Replace('\u2581', ' ');
            if (!int.TryParse(line[(lastSpace + 1)..], out var id))
            {
                continue;
            }

            tokens[id] = token;
        }

        return new ParakeetVocab(tokens);
    }

    public string Join(IEnumerable<int> ids)
    {
        var raw = new StringBuilder();
        foreach (var id in ids)
        {
            raw.Append(this[id]);
        }

        var text = DecodeSpacePattern.Replace(raw.ToString(), m => m.Groups[1].Success ? " " : "");
        return text.Trim();
    }
}
