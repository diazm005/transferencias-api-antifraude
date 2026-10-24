using Domain.Entities.Internals;
using Domain.Interfaces;
namespace AntifraudeInfrastructure;

public sealed class AntifraudeAdapter : IAntifraudeService
{
    /// <summary>
    /// Simula el nivel de riesgo según la cuenta de destino. El destino SIN_SERVICIO provoca un error de tiempo de espera para probar el manejo de fallos.
    /// </summary>
    public Task<NivelRiesgo> EvaluarAsync(Transferencia t, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Escenarios reproducibles, no son criterios de un proveedor real.
        if (t.CuentaDestino == "SIN_SERVICIO") throw new TimeoutException("Simulación de timeout.");
        return Task.FromResult(t.CuentaDestino switch
        {
            "RIESGO_ALTO" => NivelRiesgo.Alto,
            "RIESGO_MEDIO" => NivelRiesgo.Medio,
            _ => NivelRiesgo.Bajo
        });
    }
}

public sealed class OtpSimulado : IOtpService
{
    // Exclusivamente didáctico; un proveedor real verifica cliente, operación, expiración y uso único.
    /// <summary>
    /// Acepta únicamente el código ficticio 123456. Esta simulación no comprueba caducidad ni vincula el código al cliente o a la operación.
    /// </summary>
    public Task<bool> VerificarAsync(string dni, Guid id, string? codigo, CancellationToken ct = default) =>
        Task.FromResult(codigo == "123456");
}
