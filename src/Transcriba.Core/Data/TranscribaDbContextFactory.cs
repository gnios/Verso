using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Transcriba.Core.Data;

/// <summary>
/// Usada apenas pelas ferramentas de design-time do EF Core (`dotnet ef migrations add`).
/// A configuração real de runtime (caminho do banco em %AppData%, migrations automáticas)
/// é feita por <c>DbBootstrapper</c>.
/// </summary>
public class TranscribaDbContextFactory : IDesignTimeDbContextFactory<TranscribaDbContext>
{
    public TranscribaDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TranscribaDbContext>();
        optionsBuilder.UseSqlite("Data Source=transcriba_design.db");
        return new TranscribaDbContext(optionsBuilder.Options);
    }
}
