namespace Verso.Core.Data.Entities;

public class ResearchPage
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "📚";
    public string ColorName { get; set; } = "blue";   // chave em ColorCatalog
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Transcription> Transcriptions { get; set; } = [];
}
