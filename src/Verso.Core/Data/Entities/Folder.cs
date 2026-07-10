using System.ComponentModel.DataAnnotations.Schema;

namespace Verso.Core.Data.Entities;

// Keep old table name to avoid migration — concept rename only.
[Table("ResearchPages")]
public class Folder
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "📚";
    public string ColorName { get; set; } = "blue";   // chave em ColorCatalog
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Transcription> Transcriptions { get; set; } = [];
}
