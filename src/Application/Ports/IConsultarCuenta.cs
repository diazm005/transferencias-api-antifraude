using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
namespace Application.Ports;
public interface IConsultarCuenta
{
    /// <summary>
    /// Retrieves an account and its available balance. Returns 200 with the account or 404 when it does not exist.
    /// </summary>
    Task<EasyResult<RetrieveAccountAdapter>> RetrieveAccountAsync(TraceIdentifierAdapter trace, string accountNumber, CancellationToken ct = default);
}
