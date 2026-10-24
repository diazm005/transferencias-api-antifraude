using Domain.Entities.Internals;
namespace Domain.Interfaces;
public interface ICamaraCompensacionGateway
{
    // Implementaciones reales deben enviar Id como clave de idempotencia.
    /// <summary>
    /// Solicita el envío de la transferencia al banco de destino. El ID debe evitar envíos duplicados ante reintentos.
    /// </summary>
    Task EnviarAsync(Transferencia transferencia, CancellationToken ct = default);
}
