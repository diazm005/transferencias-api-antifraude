using Domain.Entities.Internals;
namespace Domain.Interfaces;
public interface ICoreBancarioGateway
{
    /// <summary>
    /// Consulta el monto acumulado del cliente en el día indicado, sin comisiones.
    /// </summary>
    Task<decimal> ObtenerAcumuladoAsync(string clienteDni, DateOnly fecha, CancellationToken ct = default);
    // Debe revalidar saldo y límite y registrar el débito de forma atómica.
    // Para mismo banco también acredita al destino en la misma operación.
    /// <summary>
    /// Revalida y registra el débito de forma atómica, acreditando también un destino interno. Devuelve el motivo del rechazo o null si la operación se acepta.
    /// </summary>
    Task<string?> DebitarAsync(Transferencia transferencia, DateOnly fecha, decimal limite, CancellationToken ct = default);
}
