namespace Verso.Core.Data.Entities;

public class Speaker
{
    public Guid Id { get; set; }
    public Guid TranscriptionId { get; set; }        // escopo por transcrição (ver Tech Decisions)
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#2eaadc";
}
