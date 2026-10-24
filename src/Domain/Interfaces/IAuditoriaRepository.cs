using Domain.Entities.Internals;
namespace Domain.Interfaces;
public interface IAuditoriaRepository
{
    /// <summary>
    /// Recupera el último estado registrado de la operación o null si no existe.
    /// </summary>
    Task<Transferencia?> ObtenerAsync(Guid id, CancellationToken ct = default);
    /// <summary>
    /// Registra el estado de la transferencia para auditoría y futuras consultas.
    /// </summary>
    Task GuardarAsync(Transferencia transferencia, CancellationToken ct = default);
}
