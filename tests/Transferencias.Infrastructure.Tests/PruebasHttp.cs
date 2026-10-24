using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Domain.Entities.Internals;
using Domain.Services;
using SqlServerInfrastructure.Http;

internal static class PruebasHttp
{
    public static async Task<int> EjecutarAsync()
    {
        var count = 0;
        var reference = Guid.NewGuid();
        var context = new ContextoFraude(100, 0, false);
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FALLÓ HTTP: " + name);
            Console.WriteLine("OK HTTP: " + name);
            count++;
        }
        foreach (var risk in new[] { NivelRiesgo.Bajo, NivelRiesgo.Medio, NivelRiesgo.Alto })
        {
            using var http = Crear(async (request, _) =>
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/fraud/evaluations" &&
                    payload.RootElement.GetProperty("transactionReference").GetGuid() == reference &&
                    payload.RootElement.GetProperty("amount").GetDecimal() == 100 &&
                    payload.RootElement.GetProperty("previousDebitsIn15Minutes").GetInt32() == 0 &&
                    !payload.RootElement.GetProperty("knownDestination").GetBoolean(), "Se envía el contrato de evaluación al batchero");
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new
                    { transactionReference = reference, riskLevel = risk.ToString(), reason = "Prueba", ruleVersion = PoliticaFraude.Version }) };
            });
            var result = await new ClienteBatchFraude(http).EvaluarAsync(reference, context);
            Check(result.Nivel == risk, "Se respeta el riesgo " + risk);
        }
        var invalidBodies = new[]
        {
            "no es JSON", "null", "{}",
            JsonSerializer.Serialize(new { transactionReference = Guid.NewGuid(), riskLevel = "Bajo", reason = "Prueba", ruleVersion = PoliticaFraude.Version }),
            JsonSerializer.Serialize(new { transactionReference = reference, riskLevel = "Desconocido", reason = "Prueba", ruleVersion = PoliticaFraude.Version }),
            JsonSerializer.Serialize(new { transactionReference = reference, riskLevel = "0", reason = "Prueba", ruleVersion = PoliticaFraude.Version }),
            JsonSerializer.Serialize(new { transactionReference = reference, riskLevel = "Bajo", reason = "", ruleVersion = PoliticaFraude.Version }),
            JsonSerializer.Serialize(new { transactionReference = reference, riskLevel = "Bajo", reason = "Prueba", ruleVersion = "OTRA" })
        };
        foreach (var body in invalidBodies)
        {
            using var http = Crear((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
            try { await new ClienteBatchFraude(http).EvaluarAsync(reference, context); Check(false, "Respuesta inválida"); }
            catch (HttpRequestException) { Check(true, "Una respuesta inválida no se convierte en riesgo bajo"); }
        }
        using (var http = Crear((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))
        {
            try { await new ClienteBatchFraude(http).EvaluarAsync(reference, context); Check(false, "HTTP 503"); }
            catch (HttpRequestException ex) { Check(ex.StatusCode == HttpStatusCode.ServiceUnavailable, "Se propaga la caída del batchero"); }
        }
        using (var http = Crear(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); }))
        using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(30)))
        {
            try { await new ClienteBatchFraude(http).EvaluarAsync(reference, context, timeout.Token); Check(false, "Cancelación"); }
            catch (OperationCanceledException) { Check(true, "La consulta HTTP respeta el tiempo máximo y la cancelación"); }
        }
        return count;
    }

    internal static HttpClient Crear(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new Simulador(send)) { BaseAddress = new Uri("http://batch-ficticio/") };
    private sealed class Simulador(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
