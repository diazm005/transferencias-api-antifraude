using Domain.Entities.Internals;
using Domain.Interfaces;

namespace CoreBancarioInfrastructure;

// Datos ficticios del taller. El bloqueo agrupa las actualizaciones en memoria; no sustituye una transacción persistente.
public sealed class CoreBancarioAdapter : IConsultarClienteRepository, IConsultarCuentaRepository, ICoreBancarioGateway, IConsultarBancoRepository
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Cliente> _clientes = new()
    {
        ["12345678"] = new("12345678", "Ana Demo", "Preferente"),
        ["87654321"] = new("87654321", "Luis Demo", "Regular")
    };
    private readonly Dictionary<string, Cuenta> _cuentas = new()
    {
        ["001"] = new("001", "12345678", "BANCO_A", true, 100_000m),
        ["002"] = new("002", "87654321", "BANCO_A", true, 2_000m),
        ["003"] = new("003", "12345678", "BANCO_A", false, 20_000m)
    };
    private readonly Dictionary<(string, DateOnly), decimal> _acumulados = new();
    private readonly HashSet<Guid> _debitos = new();

    /// <summary>El catálogo de demostración contiene únicamente los dos bancos del taller.</summary>
    public Task<bool> ExisteAsync(string codigo, CancellationToken ct = default) => Task.FromResult(
        string.Equals(codigo, "BANCO_A", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(codigo, "BANCO_B", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Busca un cliente en los datos ficticios del taller. Devuelve null si el DNI no está registrado.
    /// </summary>
    public Task<Cliente?> ObtenerClienteAsync(string dni, CancellationToken ct = default) =>
        Task.FromResult(_clientes.GetValueOrDefault(dni));

    /// <summary>
    /// Obtiene los datos actuales de una cuenta protegiendo la lectura frente a modificaciones simultáneas.
    /// </summary>
    public Task<Cuenta?> ObtenerCuentaAsync(string numero, CancellationToken ct = default)
    {
        lock (_sync) return Task.FromResult(_cuentas.GetValueOrDefault(numero));
    }

    /// <summary>
    /// Devuelve la suma de montos debitados al cliente en la fecha indicada, sin incluir comisiones.
    /// </summary>
    public Task<decimal> ObtenerAcumuladoAsync(string dni, DateOnly fecha, CancellationToken ct = default)
    {
        lock (_sync) return Task.FromResult(_acumulados.GetValueOrDefault((dni, fecha)));
    }

    /// <summary>
    /// Revalida saldo y límite y actualiza los saldos y el acumulado bajo un mismo bloqueo. Si el destino es del mismo banco, también lo acredita. Devuelve null al completar o si el ID ya fue debitado; en caso de rechazo, devuelve el motivo.
    /// </summary>
    public Task<string?> DebitarAsync(Transferencia t, DateOnly fecha, decimal limite, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_debitos.Contains(t.Id)) return Task.FromResult<string?>(null);
            if (!_cuentas.TryGetValue(t.CuentaOrigen, out var origen) || origen.ClienteDni != t.ClienteDni)
                return Task.FromResult<string?>("Cuenta de origen inválida.");
            var key = (t.ClienteDni, fecha);
            var acumulado = _acumulados.GetValueOrDefault(key);
            var error = Reglas.Validar(origen, t.Monto, t.Comision, acumulado, limite);
            if (error is not null) return Task.FromResult<string?>(error);
            Cuenta? destino = null;
            if (string.Equals(origen.Banco, t.BancoDestino, StringComparison.OrdinalIgnoreCase))
            {
                destino = _cuentas.GetValueOrDefault(t.CuentaDestino);
                if (destino is null || !destino.Activa || destino.Numero == origen.Numero)
                    return Task.FromResult<string?>("Cuenta de destino inválida.");
            }
            _cuentas[origen.Numero] = origen with { Saldo = origen.Saldo - t.Monto - t.Comision };
            if (destino is not null)
                _cuentas[destino.Numero] = destino with { Saldo = destino.Saldo + t.Monto };
            _acumulados[key] = acumulado + t.Monto;
            _debitos.Add(t.Id);
            return Task.FromResult<string?>(null);
        }
    }
}
