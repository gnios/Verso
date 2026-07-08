namespace Verso.Core.Data.Entities;

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ColorName { get; set; } = "blue";  // sempre "blue" para tags novas (fidelidade ao protótipo)
}
