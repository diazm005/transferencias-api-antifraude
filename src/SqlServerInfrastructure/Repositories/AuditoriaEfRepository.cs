using System.Diagnostics;
using Domain.Entities.Internals;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using SqlServerInfrastructure.Persistence;
using SqlServerInfrastructure.Persistence.Models;

namespace SqlServerInfrastructure.Repositories;

/// <summary>Conserva el estado actual y su historial en una misma transacción SQL.</summary>
public sealed class AuditoriaEfRepository(
    IDbContextFactory<TransferenciasDbContext> contextFactory, TimeProvider clock) : IAuditoriaRepository
{
    /// <summary>Recupera el último resultado registrado, aunque el cliente o el banco se hayan desactivado después.</summary>
    public async Task<Transferencia?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await TransferenciaEfMapper.ObtenerAsync(db, id, ct);
    }

    /// <summary>
    /// Guarda la operación y crea un evento cuando cambia su estado o motivo.
    /// Actualizar únicamente la confirmación de notificación no duplica el evento financiero.
    /// Las solicitudes sin entidades resolubles se rechazan antes de llamar a este contrato.
    /// </summary>
    public async Task GuardarAsync(Transferencia transferencia, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // HOLDLOCK protege también la ausencia de una fila durante el alta de la referencia.
        var row = await db.Transferencias.FromSqlInterpolated($"""
            SELECT * FROM [transferencias].[Transferencia] WITH (UPDLOCK, HOLDLOCK)
            WHERE [TransferenciaId] = {transferencia.Id}
            """).SingleOrDefaultAsync(ct);
        var nueva = row is null;
        var cambiaEvento = nueva || row!.EstadoTransferenciaId != (byte)transferencia.Estado ||
            row.Mensaje != transferencia.Mensaje;

        if (row is null)
        {
            row = await CrearSolicitudAsync(db, transferencia, ct);
            db.Transferencias.Add(row);
        }
        else
        {
            var original = await TransferenciaEfMapper.ObtenerAsync(db, transferencia.Id, ct)
                ?? throw new InvalidOperationException("La transferencia registrada tiene referencias inconsistentes.");
            TransferenciaEfMapper.ExigirMismaSolicitud(original, transferencia);
        }

        row.EstadoTransferenciaId = (byte)transferencia.Estado;
        row.Mensaje = transferencia.Mensaje;
        // No se borra una confirmación de entrega al repetir exactamente el mismo evento.
        row.Notificada = cambiaEvento ? transferencia.Notificada : row.Notificada || transferencia.Notificada;
        if (cambiaEvento)
        {
            db.AuditoriasTransferencia.Add(new AuditoriaTransferenciaRow
            {
                TransferenciaId = transferencia.Id,
                EstadoTransferenciaId = (byte)transferencia.Estado,
                Mensaje = transferencia.Mensaje,
                FechaEvento = TransferenciaEfMapper.FechaSql(clock.GetUtcNow()),
                TraceIdentifier = Activity.Current?.TraceId.ToString(),
                Origen = "Application"
            });
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>Resuelve las claves existentes; nunca inventa clientes, bancos o cuentas para satisfacer una relación.</summary>
    private static async Task<TransferenciaRow> CrearSolicitudAsync(
        TransferenciasDbContext db, Transferencia transferencia, CancellationToken ct)
    {
        if (transferencia.Id == Guid.Empty || transferencia.Monto <= 0 || transferencia.Comision < 0 ||
            decimal.Round(transferencia.Monto, 2) != transferencia.Monto ||
            decimal.Round(transferencia.Comision, 2) != transferencia.Comision)
            throw new InvalidOperationException("Los datos financieros no pueden almacenarse en el modelo de transferencias.");

        var clienteId = await db.Clientes.Where(x => x.Dni == transferencia.ClienteDni)
            .Select(x => (long?)x.ClienteId).SingleOrDefaultAsync(ct);
        var origen = await (from cuenta in db.Cuentas.AsNoTracking()
                            join banco in db.Bancos.AsNoTracking() on cuenta.BancoId equals banco.BancoId
                            where cuenta.NumeroCuenta == transferencia.CuentaOrigen && banco.EsBancoPropio
                            select cuenta).SingleOrDefaultAsync(ct);
        var bancoDestino = await db.Bancos.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Codigo == transferencia.BancoDestino, ct);
        if (clienteId is null || origen is null || origen.ClienteId != clienteId || bancoDestino is null)
            throw new InvalidOperationException("La solicitud no tiene cliente, origen y banco resolubles en el modelo SQL.");

        var destinoId = bancoDestino.EsBancoPropio
            ? await db.Cuentas.Where(x => x.BancoId == bancoDestino.BancoId &&
                x.NumeroCuenta == transferencia.CuentaDestino).Select(x => (long?)x.CuentaId).SingleOrDefaultAsync(ct)
            : null;
        if (destinoId == origen.CuentaId)
            throw new InvalidOperationException("La cuenta de origen y destino no pueden ser la misma.");

        return new TransferenciaRow
        {
            TransferenciaId = transferencia.Id,
            ClienteId = clienteId.Value,
            CuentaOrigenId = origen.CuentaId,
            BancoDestinoId = bancoDestino.BancoId,
            NumeroCuentaDestino = transferencia.CuentaDestino,
            CuentaDestinoInternaId = destinoId,
            Monto = transferencia.Monto,
            Comision = transferencia.Comision,
            Moneda = "PEN",
            FechaSolicitud = TransferenciaEfMapper.FechaSql(transferencia.Fecha),
            EstadoTransferenciaId = (byte)transferencia.Estado,
            Mensaje = transferencia.Mensaje,
            Notificada = transferencia.Notificada
        };
    }
}
