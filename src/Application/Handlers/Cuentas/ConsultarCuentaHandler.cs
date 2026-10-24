using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
using Application.Ports;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
namespace Application.Handlers.Cuentas;

public sealed class ConsultarCuentaHandler(IConsultarCuentaRepository accounts, ILogger<ConsultarCuentaHandler> logger) : IConsultarCuenta
{
    /// <summary>
    /// Retrieves the account through the repository and logs the outcome with the request trace.
    /// </summary>
    public async Task<EasyResult<RetrieveAccountAdapter>> RetrieveAccountAsync(TraceIdentifierAdapter trace, string accountNumber, CancellationToken ct = default)
    {
        using var _ = logger.BeginTraceScope(trace);
        var account = await accounts.ObtenerCuentaAsync(accountNumber, ct);
        if (account is null)
        {
            logger.LogInformation("Cuenta no encontrada.");
            return EasyResult<RetrieveAccountAdapter>.Failure(404, "404", "Cuenta no encontrada.");
        }
        logger.LogInformation("Cuenta consultada.");
        return EasyResult<RetrieveAccountAdapter>.Success(account.ToAdapter());
    }
}
