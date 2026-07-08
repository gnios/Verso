namespace Verso.Core.Data.Entities;

public class Segment
{
    public Guid Id { get; set; }
    public Guid TranscriptionId { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string Text { get; set; } = "";
    public int SortOrder { get; set; }               // ordenação estável após split/merge
    public Guid? SpeakerId { get; set; }
    public Speaker? Speaker { get; set; }
}
