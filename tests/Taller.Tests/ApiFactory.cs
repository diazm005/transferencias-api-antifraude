using CoreBancarioInfrastructure;
using AuditoriaInfrastructure;
using NotificacionInfrastructure;
using Domain.Entities.Internals;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlServerInfrastructure.Http;
using Transferencias.Api;

namespace Taller.Tests;

internal sealed class ApiFactory(string batchUrl, bool missingKey = false, string? sqlConnection = null)
    : WebApplicationFactory<ApiProgram>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // La conexión se valida durante la composición, antes de ConfigureServices del servidor de prueba.
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [HostDefaults.ContentRootKey] = ApiContentRoot(),
            ["ConnectionStrings:Transferencias"] = sqlConnection ?? "Server=servidor-ficticio;Database=BD_Equipo2;Integrated Security=True",
            ["Antifraude:BaseUrl"] = batchUrl
        }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(ApiContentRoot());
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Transferencias"] = sqlConnection ?? "Server=servidor-ficticio;Database=BD_Equipo2;Integrated Security=True",
            ["Antifraude:BaseUrl"] = batchUrl,
            ["Logging:LogLevel:Default"] = "Error",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Error"
        }));
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>("Bearer", options =>
            {
                options.TokenValidationParameters.IssuerSigningKeys = missingKey ? [] : [JwtTests.Key];
                options.TokenValidationParameters.IssuerSigningKey = null;
                options.TokenValidationParameters.ValidIssuer = "taller-transferencias-dev";
                options.TokenValidationParameters.ValidIssuers = null;
                options.TokenValidationParameters.ValidAudience = null;
                options.TokenValidationParameters.ValidAudiences = ["taller-transferencias-api"];
            });
            if (sqlConnection is not null) return; // Modo SQL: repositorios y adaptadores reales, sin sustituciones.
            // Solo las pruebas offline sustituyen almacenamiento. La API publicada siempre utiliza SQL + HTTP.
            services.AddSingleton<CoreBancarioAdapter>();
            services.AddSingleton<IConsultarClienteRepository>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
            services.AddSingleton<IConsultarCuentaRepository>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
            services.AddSingleton<IConsultarBancoRepository>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
            services.AddSingleton<ICoreBancarioGateway>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
            services.AddSingleton<IAuditoriaRepository, AuditoriaRepository>();
            services.AddSingleton<ISegmentoLimiteRepository, SegmentoLimiteRepository>();
            services.AddSingleton<IControlOperacion, ControlOperacionMemoria>();
            services.AddSingleton<INotificadorCliente, NotificacionesAdapter>();
            services.AddScoped<IAntifraudeService, AntifraudeRemotoPrueba>();
        });
    }

    // Resuelve la raíz de esta copia, también al clonar en otra máquina o ejecutarse desde CI.
    private static string ApiContentRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Transferencias.slnx")))
                return Path.Combine(directory.FullName, "src", "Transferencias.Api");
        throw new InvalidOperationException("No se encontró Transferencias.slnx en la copia de pruebas.");
    }
}

// Historial fijo únicamente para las pruebas sin SQL; el contrato HTTP y el servicio remoto son reales.
internal sealed class AntifraudeRemotoPrueba(ClienteBatchFraude client) : IAntifraudeService
{
    public async Task<NivelRiesgo> EvaluarAsync(Transferencia transfer, CancellationToken ct = default) =>
        (await client.EvaluarAsync(transfer.Id, new(transfer.Monto, 0, false), ct)).Nivel;
}
