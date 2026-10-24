using Application;
using Application.Ports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using System.Text.Json.Serialization;
using Transferencias.Api.Handlers;
using Transferencias.Api.Helpers;
using Transferencias.Api.Security;
using Transferencias.Infrastructure;

namespace Transferencias.Api;

/// <summary>Punto de composición: conecta los contratos con adaptadores; no decide reglas bancarias.</summary>
public partial class ApiProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("El taller requiere Development: OTP y cámara todavía son simulados.");

        builder.Services.AddApplicationServices();
        builder.Services.AddTallerInfrastructure(builder.Configuration);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IClienteActual, ClienteActualHttp>();
        builder.Services.AddTransferenciasJwt();
        builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = _ =>
                new BadRequestObjectResult(EasyBianResponseHelper.Error("400", "Solicitud inválida. Revisa los campos del contrato.")));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo { Title = "Transferencias — Taller Equipo 2", Version = "v1" });
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                Description = "Token local del taller. Pega únicamente el token."
            });
            o.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        });
        builder.Services.AddExceptionHandler<InfrastructureExceptionHandler>();
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { service = "Transferencias.Api", status = "Alive" })).AllowAnonymous();
        app.MapControllers();
        app.Run();
    }
}
