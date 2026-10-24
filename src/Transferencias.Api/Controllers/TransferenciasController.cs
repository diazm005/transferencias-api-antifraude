using Application.Adapters;
using Application.Ports;
using Microsoft.AspNetCore.Mvc;
using Transferencias.Api.Helpers;
using Microsoft.AspNetCore.Authorization;
using Transferencias.Api.Handlers;
namespace Transferencias.Api.Controllers;

[ApiController]
[Route("api/transactions")]
public sealed class TransferenciasController(IRealizarTransferencia payment, IConsultarTransferencia paymentQuery) : ControllerBase
{
    /// <summary>
    /// Inicia una transferencia propia: 200 completada, 202 pendiente, 409 OTP o referencia reutilizada,
    /// 422 rechazada, 503 no disponible, 400 entrada inválida y 401/403 autenticación o permiso insuficiente.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = JwtHandler.WritePolicy)]
    public async Task<IActionResult> InitiatePaymentExecution(InitiatePaymentExecutionRequest request, CancellationToken ct) =>
        this.ToActionResult(await payment.InitiatePaymentExecutionAsync(Request.ToTrace(), request, ct));

    /// <summary>
    /// Consulta el último estado auditado de una transferencia propia; devuelve 404 si no está disponible para el cliente.
    /// </summary>
    [HttpGet("{transactionReference:guid}")]
    [Authorize(Policy = JwtHandler.ReadPolicy)]
    public async Task<IActionResult> RetrievePaymentExecution(Guid transactionReference, CancellationToken ct) =>
        this.ToActionResult(await paymentQuery.RetrievePaymentExecutionAsync(Request.ToTrace(), transactionReference, ct));
}
