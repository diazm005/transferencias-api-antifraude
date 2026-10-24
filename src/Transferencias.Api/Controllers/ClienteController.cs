using Application.Ports;
using Microsoft.AspNetCore.Mvc;
using Transferencias.Api.Helpers;
using Microsoft.AspNetCore.Authorization;
using Transferencias.Api.Handlers;
namespace Transferencias.Api.Controllers;

[ApiController]
[Authorize(Policy = JwtHandler.ReadPolicy)]
[Route("api/client")]
public sealed class ClienteController(IConsultarCliente customer) : ControllerBase
{
    /// <summary>
    /// Consulta los datos del cliente autenticado; devuelve 404 si no existe o pertenece a otra identidad.
    /// </summary>
    [HttpGet("{nationalId}")]
    public async Task<IActionResult> RetrieveCustomer(string nationalId, CancellationToken ct) =>
        this.ToActionResult(await customer.RetrieveCustomerAsync(Request.ToTrace(), nationalId, ct));
}
