using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
using Application.Ports;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
namespace Application.Handlers.Clientes;

public sealed class ConsultarClienteHandler(IConsultarClienteRepository customers, ILogger<ConsultarClienteHandler> logger) : IConsultarCliente
{
    /// <summary>
    /// Retrieves the customer through the repository and logs the outcome with the request trace.
    /// </summary>
    public async Task<EasyResult<RetrieveCustomerAdapter>> RetrieveCustomerAsync(TraceIdentifierAdapter trace, string nationalId, CancellationToken ct = default)
    {
        using var _ = logger.BeginTraceScope(trace);
        var customer = await customers.ObtenerClienteAsync(nationalId, ct);
        if (customer is null)
        {
            logger.LogInformation("Cliente no encontrado.");
            return EasyResult<RetrieveCustomerAdapter>.Failure(404, "404", "Cliente no encontrado.");
        }
        logger.LogInformation("Cliente consultado.");
        return EasyResult<RetrieveCustomerAdapter>.Success(customer.ToAdapter());
    }
}
