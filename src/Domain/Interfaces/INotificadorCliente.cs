using Domain.Entities.Internals;
namespace Domain.Interfaces;
public interface INotificadorCliente
{
    /// <summary>
    /// Solicita la notificación. Devuelve true solo si el adaptador confirma el envío; encolar no equivale a entregar.
    /// </summary>
    Task<bool> NotificarAsync(Transferencia transferencia, CancellationToken ct = default);
}
