namespace Domain.Interfaces;
public interface IOtpService
{
    /// <summary>
    /// Verifica el código de confirmación para el cliente y la operación. Devuelve true si se acepta.
    /// </summary>
    Task<bool> VerificarAsync(string clienteDni, Guid operacionId, string? codigo, CancellationToken ct = default);
}
