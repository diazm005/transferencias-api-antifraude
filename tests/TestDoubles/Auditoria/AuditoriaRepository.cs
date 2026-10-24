using System.Collections.Concurrent;
using Domain.Entities.Internals;
using Domain.Interfaces;
namespace AuditoriaInfrastructure;

public sealed class AuditoriaRepository : IAuditoriaRepository
{
    private readonly ConcurrentDictionary<Guid, Transferencia> _operaciones = new();
    private readonly ConcurrentQueue<Transferencia> _historial = new();
    public IReadOnlyCollection<Transferencia> Historial => _historial.ToArray();

    /// <summary>
    /// Consulta el último estado registrado de una operación; devuelve null si aún no existe.
    /// </summary>
    public Task<Transferencia?> ObtenerAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_operaciones.GetValueOrDefault(id));

    /// <summary>
    /// Agrega una entrada al historial y actualiza el último estado de la operación en memoria.
    /// </summary>
    public Task GuardarAsync(Transferencia t, CancellationToken ct = default)
    {
        _historial.Enqueue(t);
        _operaciones[t.Id] = t;
        return Task.CompletedTask;
    }
}

public sealed class SegmentoLimiteRepository : ISegmentoLimiteRepository
{
    /// <summary>
    /// Obtiene el límite diario ficticio del segmento. Devuelve cero para segmentos desconocidos.
    /// </summary>
    public Task<decimal> ObtenerLimiteAsync(string segmento, CancellationToken ct = default) =>
        Task.FromResult(segmento switch { "Preferente" => 50_000m, "Regular" => 15_000m, _ => 0m });
}
