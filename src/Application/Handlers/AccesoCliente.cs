using Application.Adapters;
using Application.Handlers.Clientes;
using Application.Handlers.Cuentas;
using Application.Handlers.Transferencias;
using Application.Internals.Adapters;
using Application.Internals.Executors;
using Application.Ports;
using Domain.Interfaces;

namespace Application.Handlers;

/// <summary>
/// Decora los casos de uso con autorización por titular. Se ejecuta antes del flujo financiero;
/// los handlers internos conservan las validaciones de negocio y de idempotencia.
/// </summary>
public sealed class AccesoCliente(IClienteActual identity, IConsultarCuentaRepository accounts,
    IAuditoriaRepository audit, ConsultarClienteHandler customers, ConsultarCuentaHandler accountQuery,
    ConsultarTransferenciaHandler transfers, RealizarTransferenciaHandler payment)
    : IConsultarCliente, IConsultarCuenta, IConsultarTransferencia, IRealizarTransferencia
{
    private bool EsTitular(string dni) => !string.IsNullOrEmpty(identity.Dni) && identity.Dni == dni;

    // Un recurso ajeno y uno inexistente tienen la misma respuesta para no revelar su existencia.
    public Task<EasyResult<RetrieveCustomerAdapter>> RetrieveCustomerAsync(TraceIdentifierAdapter trace,
        string nationalId, CancellationToken ct = default) => EsTitular(nationalId)
        ? customers.RetrieveCustomerAsync(trace, nationalId, ct)
        : Task.FromResult(EasyResult<RetrieveCustomerAdapter>.Failure(404, "404", "Cliente no encontrado."));

    public async Task<EasyResult<RetrieveAccountAdapter>> RetrieveAccountAsync(TraceIdentifierAdapter trace,
        string accountNumber, CancellationToken ct = default)
    {
        var account = await accounts.ObtenerCuentaAsync(accountNumber, ct);
        if (account is null || !EsTitular(account.ClienteDni))
            return EasyResult<RetrieveAccountAdapter>.Failure(404, "404", "Cuenta no encontrada.");
        return await accountQuery.RetrieveAccountAsync(trace, accountNumber, ct);
    }

    public async Task<EasyResult<RetrievePaymentExecutionAdapter>> RetrievePaymentExecutionAsync(
        TraceIdentifierAdapter trace, Guid transactionReference, CancellationToken ct = default)
    {
        var transfer = await audit.ObtenerAsync(transactionReference, ct);
        if (transfer is null || !EsTitular(transfer.ClienteDni))
            return EasyResult<RetrievePaymentExecutionAdapter>.Failure(404, "404", "Operación no encontrada.");
        return await transfers.RetrievePaymentExecutionAsync(trace, transactionReference, ct);
    }

    public async Task<EasyResult<InitiatePaymentExecutionAdapter>> InitiatePaymentExecutionAsync(
        TraceIdentifierAdapter trace, InitiatePaymentExecutionRequest request, CancellationToken ct = default)
    {
        if (!EsTitular(request.CustomerNationalId))
            return EasyResult<InitiatePaymentExecutionAdapter>.Failure(403, "403", "No tienes permiso para realizar esta operación.");

        // Primero se valida el contrato para no enviar números de cuenta nulos al repositorio.
        var validation = new InitiatePaymentExecutionRequestValidator().Validate(request);
        if (!validation.IsValid)
            return EasyResult<InitiatePaymentExecutionAdapter>.Failure(400, validation.Errors.Select(e =>
                new ValidationResultAdapter { Code = "400", Message = e.ErrorMessage, Field = e.PropertyName }).ToList());

        var account = await accounts.ObtenerCuentaAsync(request.SourceAccountNumber, ct);
        if (account is null || !EsTitular(account.ClienteDni))
            return EasyResult<InitiatePaymentExecutionAdapter>.Failure(403, "403", "No tienes permiso para realizar esta operación.");
        var stored = await audit.ObtenerAsync(request.TransactionReference, ct);
        if (stored is not null && !EsTitular(stored.ClienteDni))
            return EasyResult<InitiatePaymentExecutionAdapter>.Failure(404, "404", "Operación no encontrada.");
        return await payment.InitiatePaymentExecutionAsync(trace, request, ct);
    }
}
