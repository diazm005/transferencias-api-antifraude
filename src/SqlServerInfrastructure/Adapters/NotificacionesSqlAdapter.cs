using Domain.Entities.Internals;
using Domain.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SqlServerInfrastructure.Persistence;
using SqlServerInfrastructure.Persistence.Models;

namespace SqlServerInfrastructure.Adapters;

/// <summary>
/// Guarda avisos por canal vinculados al evento auditado. Preparar la entrega no equivale
/// a que SMS o correo hayan sido enviados; el proveedor y sus reintentos quedan pendientes.
/// </summary>
public sealed class NotificacionesSqlAdapter(
    IDbContextFactory<TransferenciasDbContext> contextFactory,
    TimeProvider timeProvider) : INotificadorCliente
{
    private static readonly string[] Canales = ["SMS", "Correo"];

    /// <summary>Encola los canales faltantes sin duplicar un evento y devuelve false: no hay entrega real.</summary>
    public async Task<bool> NotificarAsync(Transferencia transferencia, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transferencia);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var eventId = await db.AuditoriasTransferencia.AsNoTracking()
            .Where(x => x.TransferenciaId == transferencia.Id)
            .OrderByDescending(x => x.FechaEvento)
            .ThenByDescending(x => x.AuditoriaTransferenciaId)
            .Select(x => (long?)x.AuditoriaTransferenciaId)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Debe existir un evento de auditoría antes de preparar sus notificaciones.");
        var existing = await db.NotificacionesCliente.AsNoTracking()
            .Where(x => x.TransferenciaId == transferencia.Id && x.AuditoriaTransferenciaId == eventId)
            .Select(x => x.Canal).ToListAsync(ct);
        var now = timeProvider.GetUtcNow();
        foreach (var channel in Canales.Except(existing, StringComparer.Ordinal))
        {
            db.NotificacionesCliente.Add(new NotificacionClienteRow
            {
                TransferenciaId = transferencia.Id,
                AuditoriaTransferenciaId = eventId,
                Canal = channel,
                EstadoEnvio = "Pendiente",
                Intentos = 0,
                FechaCreacion = now,
                FechaEnvio = null,
                ProximoIntento = now,
                UltimoError = null
            });
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // La unicidad (evento, canal) protege frente a publicadores concurrentes. Solo
            // aceptamos el conflicto si ambos avisos efectivamente quedaron persistidos.
            await using var verification = await contextFactory.CreateDbContextAsync(ct);
            var persisted = await verification.NotificacionesCliente.AsNoTracking()
                .Where(x => x.TransferenciaId == transferencia.Id && x.AuditoriaTransferenciaId == eventId)
                .Select(x => x.Canal).ToListAsync(ct);
            if (Canales.Except(persisted, StringComparer.Ordinal).Any())
                throw;
        }
        // No se actualiza Transferencia.Notificada ni se marca ningún aviso como Enviada.
        return false;
    }
}
