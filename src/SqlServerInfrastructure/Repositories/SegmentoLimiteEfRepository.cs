using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using SqlServerInfrastructure.Persistence;

namespace SqlServerInfrastructure.Repositories;

/// <summary>Consulta la configuración vigente de límites, manteniendo las decisiones en el dominio.</summary>
public sealed class SegmentoLimiteEfRepository(IDbContextFactory<TransferenciasDbContext> contextFactory)
    : ISegmentoLimiteRepository
{
    /// <summary>Devuelve el límite del segmento activo; un segmento inexistente o deshabilitado no permite transferir.</summary>
    public async Task<decimal> ObtenerLimiteAsync(string segmento, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Segmentos.AsNoTracking().Where(x => x.Nombre == segmento && x.Activo)
            .Select(x => (decimal?)x.LimiteDiario).SingleOrDefaultAsync(ct) ?? 0m;
    }
}
