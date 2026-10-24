namespace Domain.Interfaces;

/// <summary>Coordina una operación completa para impedir que dos solicitudes ejecuten el mismo débito y envío.</summary>
public interface IControlOperacion
{
    /// <summary>Espera el turno de la referencia y devuelve un recurso que libera el bloqueo al terminar.</summary>
    Task<IAsyncDisposable> AdquirirAsync(Guid operacionId, CancellationToken ct = default);
}
