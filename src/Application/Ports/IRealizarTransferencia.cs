using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
namespace Application.Ports;
public interface IRealizarTransferencia
{
    /// <summary>
    /// Initiates a payment execution. The HTTP status of the result reflects the operation state; 400 and 409 indicate an invalid request or a reused reference.
    /// </summary>
    Task<EasyResult<InitiatePaymentExecutionAdapter>> InitiatePaymentExecutionAsync(TraceIdentifierAdapter trace, InitiatePaymentExecutionRequest request, CancellationToken ct = default);
}
