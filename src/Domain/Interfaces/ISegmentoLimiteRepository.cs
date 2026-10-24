namespace Domain.Interfaces;
public interface ISegmentoLimiteRepository
{
    /// <summary>
    /// Consulta el límite diario aplicable al segmento del cliente.
    /// </summary>
    Task<decimal> ObtenerLimiteAsync(string segmento, CancellationToken ct = default);
}
