using System;
using System.Collections.Generic;
using System.Text;

using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SqlServerInfrastructure.Adapters;
using SqlServerInfrastructure.Coordination;
using SqlServerInfrastructure.Persistence;
using SqlServerInfrastructure.Repositories;
using SqlServerInfrastructure.Http;

namespace SqlServerInfrastructure;

public static class SqlServerSetting
{
    /// <summary>Conecta los repositorios SQL y el cliente HTTP del batchero, conservando el seguimiento en SQL.</summary>
    public static IServiceCollection AddSqlServerInfrastructure(this IServiceCollection services, string? connectionString,
        string? antifraudeBaseUrl = "http://127.0.0.1:5310/")
    {
        var validated = TransferenciasConnectionString.Validar(connectionString);
        services.AddDbContextFactory<TransferenciasDbContext>(options => options.UseSqlServer(validated,
            sql => sql.CommandTimeout(15)));
        // Cada repositorio crea contextos cortos; no se comparte un contexto entre solicitudes.
        // Sin reintentos SQL automáticos: un resultado financiero incierto exige conciliación.
        services.AddScoped<CoreBancarioEfRepository>();
        services.AddScoped<IConsultarClienteRepository>(sp => sp.GetRequiredService<CoreBancarioEfRepository>());
        services.AddScoped<IConsultarCuentaRepository>(sp => sp.GetRequiredService<CoreBancarioEfRepository>());
        services.AddScoped<IConsultarBancoRepository>(sp => sp.GetRequiredService<CoreBancarioEfRepository>());
        services.AddScoped<ICoreBancarioGateway>(sp => sp.GetRequiredService<CoreBancarioEfRepository>());
        services.AddScoped<IAuditoriaRepository, AuditoriaEfRepository>();
        services.AddScoped<ISegmentoLimiteRepository, SegmentoLimiteEfRepository>();
        services.AddScoped<IControlOperacion, ControlOperacionSql>();
        if (!Uri.TryCreate(antifraudeBaseUrl, UriKind.Absolute, out var batchUri) ||
            batchUri.Scheme is not ("http" or "https") || batchUri.Query.Length != 0 || batchUri.Fragment.Length != 0 ||
            batchUri.UserInfo.Length != 0)
            throw new InvalidOperationException("Antifraude:BaseUrl debe ser una dirección HTTP/HTTPS absoluta del batchero.");
        services.AddHttpClient<ClienteBatchFraude>(client =>
        {
            client.BaseAddress = new Uri(batchUri.AbsoluteUri.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(2);
            client.MaxResponseContentBufferSize = 16_384;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddScoped<IAntifraudeService, AntifraudeHttpAdapter>();
        services.AddScoped<INotificadorCliente, NotificacionesSqlAdapter>();
        return services;
    }
}
