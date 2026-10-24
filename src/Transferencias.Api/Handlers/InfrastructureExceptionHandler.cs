using Application.Internals.Adapters;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Transferencias.Api.Helpers;

namespace Transferencias.Api.Handlers;

/// <summary>Traduce indisponibilidad de SQL o contención de una operación a una respuesta recuperable y sin datos de conexión.</summary>
public sealed class InfrastructureExceptionHandler(ILogger<InfrastructureExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var sql = exception as SqlException ?? exception.InnerException as SqlException;
        if (exception is not TimeoutException && sql is null) return false;
        // Una violación de integridad revela un fallo del servicio y conserva el manejo general 500.
        if (sql is { Number: 2601 or 2627 or 547 or 515 or 8115 }) return false;
        logger.LogWarning(exception, "No se pudo completar la solicitud por indisponibilidad de infraestructura.");
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(EasyBianResponseHelper.WarningResponse(
            new List<ValidationResultAdapter>
            {
                new() { Code = "503", Message = "Servicio temporalmente no disponible. Consulta o reintenta con la misma referencia de operación." }
            }), cancellationToken: ct);
        return true;
    }
}
