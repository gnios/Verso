using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Verso.Core.Data;

/// <summary>
/// Usada apenas pelas ferramentas de design-time do EF Core (`dotnet ef migrations add`).
/// A configuração real de runtime (caminho do banco em %AppData%, migrations automáticas)
/// é feita por <c>DbBootstrapper</c>.
/// </summary>
public class VersoDbContextFactory : IDesignTimeDbContextFactory<VersoDbContext>
{
    public VersoDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VersoDbContext>();
        optionsBuilder.UseSqlite("Data Source=verso_design.db");
        return new VersoDbContext(optionsBuilder.Options);
    }
}
