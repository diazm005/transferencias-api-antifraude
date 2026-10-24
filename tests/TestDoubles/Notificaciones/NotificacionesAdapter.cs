using System.Collections.Concurrent;
using Domain.Entities.Internals;
using Domain.Interfaces;
namespace NotificacionInfrastructure;

public sealed class NotificacionesAdapter : INotificadorCliente
{
    private readonly ConcurrentQueue<Transferencia> _notificaciones = new();
    public IReadOnlyCollection<Transferencia> Notificaciones => _notificaciones.ToArray();
    /// <summary>
    /// Registra un envío simulado en memoria; el taller no envía SMS ni correos reales.
    /// </summary>
    public Task<bool> NotificarAsync(Transferencia t, CancellationToken ct = default)
    {
        _notificaciones.Enqueue(t);
        return Task.FromResult(true);
    }
}
