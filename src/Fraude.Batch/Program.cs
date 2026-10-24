using System.Text.Json;

namespace Fraude.Batch;

public static class BatchProgram
{
    /// <summary>Sin opciones ofrece HTTP; --once lee una entrada JSON por stdin y escribe una salida JSON.</summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(["--once"]))
        {
            try
            {
                var input = await JsonSerializer.DeserializeAsync<EntradaFraude>(Console.OpenStandardInput(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var errors = input is null ? new Dictionary<string, string[]> { ["body"] = ["Falta la entrada JSON."] } : EvaluadorFraude.Validar(input);
                if (errors.Count != 0) { Console.WriteLine(JsonSerializer.Serialize(new { errors })); return 2; }
                Console.WriteLine(JsonSerializer.Serialize(EvaluadorFraude.Evaluar(input!), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return 0;
            }
            catch (JsonException)
            {
                Console.WriteLine("{\"errors\":{\"body\":[\"JSON inválido.\"]}}");
                return 2;
            }
        }
        await using var app = Build(args);
        await app.RunAsync();
        return 0;
    }

    /// <summary>Servicio independiente: no recibe DNI, saldo, claves de SQL ni JWT del cliente.</summary>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(BatchProgram).Assembly.FullName });
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("El evaluador requiere Development hasta configurar identidad entre servicios.");
        if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5310");
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16_384);
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapGet("/health", () => Results.Ok(new { service = "Fraude.Batch", status = "Healthy", ruleVersion = EvaluadorFraude.Version }));
        app.MapPost("/api/fraud/evaluations", (EntradaFraude input) =>
        {
            var errors = EvaluadorFraude.Validar(input);
            return errors.Count != 0 ? Results.ValidationProblem(errors) : Results.Ok(EvaluadorFraude.Evaluar(input));
        });
        return app;
    }
}
