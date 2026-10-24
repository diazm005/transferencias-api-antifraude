using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities.Internals;
using Domain.Services;

namespace SqlServerInfrastructure.Http;

/// <summary>Consume el contrato HTTP del batchero y valida su respuesta antes de permitir usar el riesgo.</summary>
public sealed class ClienteBatchFraude(HttpClient http)
{
    public async Task<DecisionFraude> EvaluarAsync(Guid reference, ContextoFraude context, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/fraud/evaluations", new
        {
            transactionReference = reference,
            amount = context.Monto,
            previousDebitsIn15Minutes = context.DebitosPreviosEnQuinceMinutos,
            knownDestination = context.DestinoConocido
        }, ct);
        response.EnsureSuccessStatusCode();
        Respuesta? result;
        try { result = await response.Content.ReadFromJsonAsync<Respuesta>(ct); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        { throw new HttpRequestException("El batchero devolvió una respuesta ilegible.", ex); }
        if (result is null || result.TransactionReference != reference ||
            result.RuleVersion != PoliticaFraude.Version || string.IsNullOrWhiteSpace(result.Reason) || result.Reason.Length > 500)
            throw new HttpRequestException("La respuesta del batchero no corresponde a la solicitud o a la versión esperada.");
        var risk = result.RiskLevel?.ToUpperInvariant() switch
        {
            "BAJO" => NivelRiesgo.Bajo,
            "MEDIO" => NivelRiesgo.Medio,
            "ALTO" => NivelRiesgo.Alto,
            _ => throw new HttpRequestException("El batchero devolvió un nivel de riesgo desconocido.")
        };
        return new(risk, result.Reason, result.RuleVersion);
    }

    private sealed record Respuesta(Guid TransactionReference, string? RiskLevel, string? Reason, string? RuleVersion);
}
