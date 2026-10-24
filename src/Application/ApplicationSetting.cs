using Application.Handlers.Clientes;
using Application.Handlers.Cuentas;
using Application.Handlers.Transferencias;
using Application.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class ApplicationSetting
{
    /// <summary>
    /// Registra los handlers y el reloj en el contenedor de dependencias. Las implementaciones de infraestructura se seleccionan en la API.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ConsultarClienteHandler>();
        services.AddScoped<ConsultarCuentaHandler>();
        services.AddScoped<ConsultarTransferenciaHandler>();
        // El control de concurrencia pertenece al adaptador; cada solicitud obtiene su propio handler.
        services.AddScoped<RealizarTransferenciaHandler>();
        services.AddScoped<Handlers.AccesoCliente>();
        services.AddScoped<IConsultarCliente>(sp => sp.GetRequiredService<Handlers.AccesoCliente>());
        services.AddScoped<IConsultarCuenta>(sp => sp.GetRequiredService<Handlers.AccesoCliente>());
        services.AddScoped<IConsultarTransferencia>(sp => sp.GetRequiredService<Handlers.AccesoCliente>());
        services.AddScoped<IRealizarTransferencia>(sp => sp.GetRequiredService<Handlers.AccesoCliente>());
        return services;
    }
}
