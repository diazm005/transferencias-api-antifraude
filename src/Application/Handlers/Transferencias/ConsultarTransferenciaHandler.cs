using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
using Application.Ports;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
namespace Application.Handlers.Transferencias;

public sealed class ConsultarTransferenciaHandler(IAuditoriaRepository audit, ILogger<ConsultarTransferenciaHandler> logger) : IConsultarTransferencia
{
    /// <summary>
    /// Retrieves the last audited state of the payment execution and logs the outcome with the request trace.
    /// </summary>
    public async Task<EasyResult<RetrievePaymentExecutionAdapter>> RetrievePaymentExecutionAsync(TraceIdentifierAdapter trace, Guid transactionReference, CancellationToken ct = default)
    {
        using var _ = logger.BeginTraceScope(trace, transactionReference);
        var transfer = await audit.ObtenerAsync(transactionReference, ct);
        if (transfer is null)
        {
            logger.LogInformation("Operación no encontrada.");
            return EasyResult<RetrievePaymentExecutionAdapter>.Failure(404, "404", "Operación no encontrada.");
        }
        logger.LogInformation("Operación consultada con estado {Status}.", transfer.Estado);
        return EasyResult<RetrievePaymentExecutionAdapter>.Success(transfer.ToAdapter<RetrievePaymentExecutionAdapter>());
    }
}
