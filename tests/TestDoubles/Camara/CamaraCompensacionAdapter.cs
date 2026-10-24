using System.Collections.Concurrent;
using Domain.Entities.Internals;
using Domain.Interfaces;
namespace CamaraCompensatorioInfrastructure;

public sealed class CamaraCompensacionAdapter : ICamaraCompensacionGateway
{
    private readonly ConcurrentDictionary<Guid, Transferencia> _enviadas = new();
    /// <summary>
    /// Registra un envío simulado una sola vez por ID. El destino CAMARA_TIMEOUT simula una respuesta incierta; no se conecta con otro banco.
    /// </summary>
    public Task EnviarAsync(Transferencia t, CancellationToken ct = default)
    {
        if (t.CuentaDestino == "CAMARA_TIMEOUT") throw new TimeoutException("Resultado externo desconocido.");
        _enviadas.TryAdd(t.Id, t);
        return Task.CompletedTask;
    }
}
