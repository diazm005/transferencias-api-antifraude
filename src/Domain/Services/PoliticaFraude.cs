using Domain.Entities.Internals;

namespace Domain.Services;

/// <summary>Datos de negocio necesarios para evaluar el riesgo, sin depender de EF ni de SQL.</summary>
public sealed record ContextoFraude(decimal Monto, int DebitosPreviosEnQuinceMinutos, bool DestinoConocido);

/// <summary>Decisión explicable y versionada; riesgo alto no acredita por sí solo un fraude.</summary>
public sealed record DecisionFraude(NivelRiesgo Nivel, string Motivo, string VersionRegla);

/// <summary>
/// Reglas ficticias y deterministas del taller. La infraestructura obtiene el historial;
/// el dominio decide el riesgo sin consultar servicios ni modificar saldos.
/// </summary>
public static class PoliticaFraude
{
    public const string Version = "TALLER-1.0";

    /// <summary>Evalúa primero las condiciones de riesgo alto, luego medio y finalmente bajo.</summary>
    public static DecisionFraude Evaluar(ContextoFraude contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (contexto.Monto <= 0)
            throw new ArgumentOutOfRangeException(nameof(contexto), "El monto debe ser mayor que cero.");
        if (contexto.DebitosPreviosEnQuinceMinutos < 0)
            throw new ArgumentOutOfRangeException(nameof(contexto), "El número de débitos no puede ser negativo.");

        if (contexto.Monto >= 40_000m)
            return new(NivelRiesgo.Alto, "El monto es igual o mayor que S/ 40 000.", Version);
        if (contexto.DebitosPreviosEnQuinceMinutos >= 5)
            return new(NivelRiesgo.Alto, "La cuenta origen registra al menos cinco débitos previos en quince minutos.", Version);
        if (contexto.Monto >= 10_000m)
            return new(NivelRiesgo.Medio, "El monto es igual o mayor que S/ 10 000.", Version);
        if (!contexto.DestinoConocido && contexto.Monto >= 5_000m)
            return new(NivelRiesgo.Medio, "El destino no tiene débitos previos desde esta cuenta y el monto alcanza S/ 5 000.", Version);

        return new(NivelRiesgo.Bajo, "No se cumplen las condiciones de riesgo medio o alto de la regla del taller.", Version);
    }
}
