using Domain.Entities.Internals;
namespace Domain.Interfaces;
public interface IConsultarClienteRepository
{
    /// <summary>
    /// Obtiene un cliente por DNI o devuelve null si no existe.
    /// </summary>
    Task<Cliente?> ObtenerClienteAsync(string dni, CancellationToken ct = default);
}
