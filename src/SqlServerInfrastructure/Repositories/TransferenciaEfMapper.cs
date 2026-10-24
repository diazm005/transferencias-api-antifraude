using Domain.Entities.Internals;
using Microsoft.EntityFrameworkCore;
using SqlServerInfrastructure.Persistence;
using SqlServerInfrastructure.Persistence.Models;

namespace SqlServerInfrastructure.Repositories;

/// <summary>Traduce filas relacionales al contrato del dominio sin exponer EF a las capas interiores.</summary>
internal static class TransferenciaEfMapper
{
    /// <summary>Reconstruye la operación usando sus claves reales; también permite consultar entidades desactivadas históricas.</summary>
    internal static Task<Transferencia?> ObtenerAsync(TransferenciasDbContext db, Guid id, CancellationToken ct) =>
        (from transferencia in db.Transferencias.AsNoTracking()
         join cliente in db.Clientes.AsNoTracking() on transferencia.ClienteId equals cliente.ClienteId
         join origen in db.Cuentas.AsNoTracking() on transferencia.CuentaOrigenId equals origen.CuentaId
         join banco in db.Bancos.AsNoTracking() on transferencia.BancoDestinoId equals banco.BancoId
         where transferencia.TransferenciaId == id
         select new Transferencia(transferencia.TransferenciaId, cliente.Dni, origen.NumeroCuenta,
             transferencia.NumeroCuentaDestino, banco.Codigo, transferencia.Monto, transferencia.Comision,
             transferencia.FechaSolicitud, (EstadoTransferencia)transferencia.EstadoTransferenciaId,
             transferencia.Mensaje, transferencia.Notificada)).SingleOrDefaultAsync(ct);

    /// <summary>Impide reutilizar una referencia con otros datos financieros o cambiar la fecha original.</summary>
    internal static void ExigirMismaSolicitud(Transferencia original, Transferencia actual)
    {
        if (original.Id != actual.Id || original.ClienteDni != actual.ClienteDni ||
            original.CuentaOrigen != actual.CuentaOrigen || original.CuentaDestino != actual.CuentaDestino ||
            !string.Equals(original.BancoDestino, actual.BancoDestino, StringComparison.OrdinalIgnoreCase) ||
            original.Monto != actual.Monto || original.Comision != actual.Comision ||
            FechaSql(original.Fecha) != FechaSql(actual.Fecha))
            throw new InvalidOperationException("La referencia de transferencia ya tiene datos inmutables diferentes.");
    }

    /// <summary>Reproduce la precisión datetimeoffset(3) para comparar sin falsos conflictos por fracciones de milisegundo.</summary>
    internal static DateTimeOffset FechaSql(DateTimeOffset fecha)
    {
        const long precision = TimeSpan.TicksPerMillisecond;
        var ticks = checked(((fecha.Ticks + precision / 2) / precision) * precision);
        return new DateTimeOffset(ticks, fecha.Offset);
    }
}
