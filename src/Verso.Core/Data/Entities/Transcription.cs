namespace Verso.Core.Data.Entities;

public class Transcription
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Icon { get; set; }
    public TranscriptionStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public double DurationSeconds { get; set; }
    /// <summary>Tempo de relógio que o motor demorou para transcrever (null até concluir). Em segundos.</summary>
    public double? ProcessingDurationSeconds { get; set; }
    public string? MediaFilePath { get; set; }       // caminho copiado em <appdir>/data/media
    public string Language { get; set; } = "pt";
    public ModelQuality Quality { get; set; }
    public SpeakerMode SpeakerMode { get; set; }
    public int? ResearchPageId { get; set; }
    public ResearchPage? ResearchPage { get; set; }
    public List<Segment> Segments { get; set; } = [];
    public List<Speaker> Speakers { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
}
