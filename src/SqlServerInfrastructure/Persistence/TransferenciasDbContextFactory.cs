using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SqlServerInfrastructure.Persistence;

/// <summary>Permite inspeccionar el contexto con herramientas EF sin iniciar la API ni guardar secretos.</summary>
public sealed class TransferenciasDbContextFactory : IDesignTimeDbContextFactory<TransferenciasDbContext>
{
    /// <summary>Construye el contexto para BD_Equipo2; no abre la conexión ni modifica el esquema.</summary>
    public TransferenciasDbContext CreateDbContext(string[] args)
    {
        var connectionString = TransferenciasConnectionString.Validar(
            Environment.GetEnvironmentVariable("ConnectionStrings__Transferencias"));
        var options = new DbContextOptionsBuilder<TransferenciasDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(30))
            .Options;
        return new TransferenciasDbContext(options);
    }
}
