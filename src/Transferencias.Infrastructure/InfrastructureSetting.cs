using Domain.Entities.Internals;
using Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlServerInfrastructure;

namespace Transferencias.Infrastructure;

/// <summary>Selecciona adaptadores sin modificar los casos de uso ni las entidades compartidas.</summary>
public static class InfrastructureSetting
{
    public static IServiceCollection AddTallerInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Reutiliza el mapeo ER y los repositorios existentes. El guardián exige BD_Equipo2.
        // La URL HTTP es el único vínculo con Fraude.Batch; no hay referencia a su ensamblado.
        services.AddSqlServerInfrastructure(config.GetConnectionString("Transferencias"), config["Antifraude:BaseUrl"]);
        services.AddSingleton<IOtpService, OtpTaller>();
        services.AddSingleton<ICamaraCompensacionGateway, CamaraTaller>();
        return services;
    }
}

/// <summary>Confirmación ficticia para capacitación. Un proveedor real reemplazará esta implementación.</summary>
internal sealed class OtpTaller : IOtpService
{
    public Task<bool> VerificarAsync(string clienteDni, Guid operacionId, string? codigo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(codigo == "123456");
    }
}

/// <summary>Simula aceptación externa; no envía dinero a otro banco. El core SQL realiza la contabilidad local.</summary>
internal sealed class CamaraTaller : ICamaraCompensacionGateway
{
    public Task EnviarAsync(Transferencia transferencia, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (transferencia.CuentaDestino == "CAMARA_TIMEOUT") throw new TimeoutException("Resultado externo desconocido.");
        return Task.CompletedTask;
    }
}
