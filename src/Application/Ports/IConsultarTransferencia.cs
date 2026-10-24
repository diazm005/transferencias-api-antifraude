using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
namespace Application.Ports;
public interface IConsultarTransferencia
{
    /// <summary>
    /// Retrieves the last audited state of a payment execution. Returns 200 with the payment execution or 404 when it does not exist.
    /// </summary>
    Task<EasyResult<RetrievePaymentExecutionAdapter>> RetrievePaymentExecutionAsync(TraceIdentifierAdapter trace, Guid transactionReference, CancellationToken ct = default);
}
