using Verso.Core.Data.Entities;

namespace Verso.Core.Services;

public class SegmentEditingService
{
    public Segment? GetActiveSegment(IReadOnlyList<Segment> segments, TimeSpan currentPosition)
    {
        if (segments.Count == 0)
        {
            return null;
        }

        var positionSeconds = currentPosition.TotalSeconds;
        Segment? active = null;

        foreach (var segment in segments.OrderBy(s => s.SortOrder))
        {
            if (segment.StartSeconds <= positionSeconds)
            {
                active = segment;
            }
            else
            {
                break;
            }
        }

        return active;
    }

    public (Segment Before, Segment After)? SplitAtCaret(Segment segment, int caretIndex)
    {
        var text = segment.Text ?? "";
        caretIndex = Math.Clamp(caretIndex, 0, text.Length);

        var before = text[..caretIndex].Trim();
        var after = text[caretIndex..].Trim();

        if (before.Length == 0 || after.Length == 0)
        {
            return null;
        }

        // Divide o intervalo de tempo [start,end] proporcionalmente ao caret, para que as
        // duas metades fiquem sincronizadas com o áudio. Antes ambas herdavam o intervalo
        // completo do segmento original — sobrepostos, o destaque por playback (que pega o
        // último segmento com StartSeconds <= posição) sempre caía na segunda metade, e a
        // atribuição de locutor (que usa o segmento ativo de playback) bugava.
        var duration = segment.EndSeconds - segment.StartSeconds;
        var splitFraction = text.Length > 0 ? (double)caretIndex / text.Length : 0.5;
        var splitPoint = segment.StartSeconds + duration * splitFraction;

        var beforeSegment = CloneSegment(segment);
        beforeSegment.Text = before;
        beforeSegment.EndSeconds = splitPoint;

        var afterSegment = CloneSegment(segment);
        afterSegment.Id = Guid.NewGuid();
        afterSegment.Text = after;
        afterSegment.StartSeconds = splitPoint;

        return (beforeSegment, afterSegment);
    }
    public Segment? MergeWithPrevious(IReadOnlyList<Segment> segments, Segment active)
    {
        var ordered = segments.OrderBy(s => s.SortOrder).ToList();
        var index = ordered.FindIndex(s => s.Id == active.Id);

        if (index <= 0)
        {
            return null;
        }

        var previous = ordered[index - 1];
        previous.Text = $"{previous.Text} {active.Text}".Trim();
        // Estende o intervalo do segmento anterior até o fim do segmento mesclado, para
        // que o resultado cubra todo o áudio [previous.Start, active.End] — antes o fim
        // permanecia em previous.End, deixando a segunda metade do áudio sem sincronia.
        previous.EndSeconds = active.EndSeconds;
        return previous;
    }

    public void AssignSpeaker(Segment segment, Speaker speaker)
    {
        segment.SpeakerId = speaker.Id;
        segment.Speaker = speaker;
    }

    private static Segment CloneSegment(Segment segment) =>
        new()
        {
            Id = segment.Id,
            TranscriptionId = segment.TranscriptionId,
            StartSeconds = segment.StartSeconds,
            EndSeconds = segment.EndSeconds,
            Text = segment.Text,
            SortOrder = segment.SortOrder,
            SpeakerId = segment.SpeakerId,
            Speaker = segment.Speaker
        };
}
