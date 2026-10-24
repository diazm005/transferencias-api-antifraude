using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
namespace Application.Ports;
public interface IConsultarCliente
{
    /// <summary>
    /// Retrieves a customer by national ID. Returns 200 with the customer or 404 when it does not exist.
    /// </summary>
    Task<EasyResult<RetrieveCustomerAdapter>> RetrieveCustomerAsync(TraceIdentifierAdapter trace, string nationalId, CancellationToken ct = default);
}
