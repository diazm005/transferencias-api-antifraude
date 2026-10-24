using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Application.Internals.Adapters;
using Transferencias.Api.Helpers;

namespace Transferencias.Api.Handlers;

/// <summary>Configura el validador JWT del taller. Las claves se cargan desde User Secrets, nunca desde código.</summary>
public static class JwtHandler
{
    public const string ReadPolicy = "TransferenciasRead";
    public const string WritePolicy = "TransferenciasWrite";

    public static void AddTransferenciasJwt(this IServiceCollection services)
    {
        // AddJwtBearer lee Authentication:Schemes:Bearer, incluida la clave creada por dotnet user-jwts.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.MapInboundClaims = false;
            options.SaveToken = false;
            options.IncludeErrorDetails = false;
            var validation = options.TokenValidationParameters;
            validation.ValidateIssuer = true;
            validation.ValidateAudience = true;
            validation.ValidateIssuerSigningKey = true;
            validation.ValidateLifetime = true;
            validation.RequireExpirationTime = true;
            validation.RequireSignedTokens = true;
            validation.ClockSkew = TimeSpan.FromSeconds(30);
            validation.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    // Una identidad ambigua o sin DNI no puede acceder a los casos de uso de clientes.
                    var ids = context.Principal!.FindAll("national_id").Select(c => c.Value).ToArray();
                    if (ids.Length != 1 || ids[0].Length != 8 || ids[0].Any(c => c < '0' || c > '9'))
                        context.Fail("Identidad de cliente inválida.");
                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = 401;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await context.Response.WriteAsJsonAsync(EasyBianResponseHelper.WarningResponse(
                        [new ValidationResultAdapter { Code = "401", Message = "Se requiere un token válido y vigente." }]));
                },
                OnForbidden = context =>
                {
                    context.Response.StatusCode = 403;
                    return context.Response.WriteAsJsonAsync(EasyBianResponseHelper.WarningResponse(
                        [new ValidationResultAdapter { Code = "403", Message = "El token no tiene el permiso requerido." }]));
                }
            };
        });
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Validate(options => !string.IsNullOrWhiteSpace(options.TokenValidationParameters.ValidIssuer) &&
                options.TokenValidationParameters.ValidAudiences?.Any() == true &&
                options.TokenValidationParameters.IssuerSigningKeys?.Any(k => k.KeySize >= 256) == true,
                "JWT no está configurado. Genera un token local con scripts/nuevo-token.ps1; la clave se guarda en User Secrets.")
            .ValidateOnStart();
        services.AddAuthorization(options =>
        {
            // Nuevos endpoints quedan protegidos por defecto; solo la disponibilidad mínima es pública.
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy(ReadPolicy, p => p.RequireAuthenticatedUser().RequireAssertion(context =>
                HasScope(context.User, "transferencias.read")));
            options.AddPolicy(WritePolicy, p => p.RequireAuthenticatedUser().RequireAssertion(context =>
                HasScope(context.User, "transferencias.write")));
        });
    }

    private static bool HasScope(System.Security.Claims.ClaimsPrincipal user, string required) =>
        user.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(required, StringComparer.Ordinal);
}
