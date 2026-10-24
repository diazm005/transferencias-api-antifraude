using Domain.Entities.Internals;
namespace Domain.Interfaces;
public interface IConsultarCuentaRepository
{
    /// <summary>
    /// Obtiene una cuenta por número o devuelve null si no existe.
    /// </summary>
    Task<Cuenta?> ObtenerCuentaAsync(string numero, CancellationToken ct = default);
}
