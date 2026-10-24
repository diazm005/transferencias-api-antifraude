using Domain.Interfaces;

namespace CoreBancarioInfrastructure;

/// <summary>Serializa solicitudes de la demo. Se registra una sola instancia para todos los handlers.</summary>
public sealed class ControlOperacionMemoria : IControlOperacion
{
    private readonly SemaphoreSlim _turno = new(1, 1);

    public async Task<IAsyncDisposable> AdquirirAsync(Guid operacionId, CancellationToken ct = default)
    {
        await _turno.WaitAsync(ct);
        return new Turno(_turno);
    }

    private sealed class Turno(SemaphoreSlim semaforo) : IAsyncDisposable
    {
        private int _liberado;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _liberado, 1) == 0) semaforo.Release();
            return ValueTask.CompletedTask;
        }
    }
}
