using Application.Internals.Adapters;
using Application.Internals.Executors;
using Microsoft.AspNetCore.Mvc;

namespace Transferencias.Api.Helpers;

/// <summary>Conserva el contrato HTTP de transferencias sin depender de la API anterior.</summary>
public static class EasyBianResponseHelper
{
    public static object Error(string code, string message) => WarningResponse([new() { Code = code, Message = message }]);

    public static object WarningResponse(IReadOnlyCollection<ValidationResultAdapter> errors) =>
        new { errors = errors.Select(e => new { status_code = e.Code, message = e.Message }).ToArray() };

    public static IActionResult ToActionResult<T>(this ControllerBase controller, EasyResult<T> result)
    {
        if (result.SuccessValue is null) return controller.StatusCode(result.Status, WarningResponse(result.ValidationValues));
        var name = result.SuccessValue.GetType().Name;
        var responseName = (name.EndsWith("Adapter", StringComparison.Ordinal) ? name[..^7] : name) + "Response";
        return controller.StatusCode(result.Status, new Dictionary<string, object?> { [responseName] = result.SuccessValue });
    }

    /// <summary>La traza vincula la solicitud HTTP con los eventos auditados por Aplicación.</summary>
    public static TraceIdentifierAdapter ToTrace(this HttpRequest request) => new()
    {
        MessageIdentifier = request.Headers["MessageIdentifier"].FirstOrDefault() ?? request.HttpContext.TraceIdentifier,
        DeviceIdentifier = request.Headers["DeviceIdentifier"].FirstOrDefault(),
        ChannelIdentifier = request.Headers["ChannelIdentifier"].FirstOrDefault()
    };
}
