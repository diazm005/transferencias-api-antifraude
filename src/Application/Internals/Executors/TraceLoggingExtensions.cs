using Application.Internals.Adapters;
using Microsoft.Extensions.Logging;
namespace Application.Internals.Executors;

internal static class TraceLoggingExtensions
{
    /// <summary>
    /// Abre un alcance de log con los identificadores de la petición para que cada línea del handler y de la infraestructura quede correlacionada.
    /// </summary>
    public static IDisposable? BeginTraceScope(this ILogger logger, TraceIdentifierAdapter trace, Guid? operacionId = null)
    {
        var datos = new Dictionary<string, object?>
        {
            ["MessageIdentifier"] = trace.MessageIdentifier,
            ["DeviceIdentifier"] = trace.DeviceIdentifier,
            ["ChannelIdentifier"] = trace.ChannelIdentifier
        };
        if (operacionId is not null) datos["OperacionId"] = operacionId;
        return logger.BeginScope(datos);
    }
}
