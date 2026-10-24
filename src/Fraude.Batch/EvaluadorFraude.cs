namespace Fraude.Batch;

// Contrato propio del evaluador. No comparte DLL, entidades ni conexión SQL con transferencias.
public sealed record EntradaFraude(Guid? TransactionReference, decimal? Amount,
    int? PreviousDebitsIn15Minutes, bool? KnownDestination);
public sealed record SalidaFraude(Guid TransactionReference, string RiskLevel, string Reason, string RuleVersion);

/// <summary>Reglas ficticias del taller. Riesgo alto bloquea la operación; no acredita por sí solo un fraude.</summary>
public static class EvaluadorFraude
{
    public const string Version = "TALLER-1.0";

    public static Dictionary<string, string[]> Validar(EntradaFraude input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.TransactionReference is null || input.TransactionReference == Guid.Empty)
            errors["transactionReference"] = ["Indica una referencia válida."];
        if (input.Amount is not { } amount || amount <= 0 || amount > 9_999_999_999_999_999.99m || decimal.Round(amount, 2) != amount)
            errors["amount"] = ["Indica un monto positivo compatible con decimal(18,2)."];
        if (input.PreviousDebitsIn15Minutes is null or < 0)
            errors["previousDebitsIn15Minutes"] = ["Indica la cantidad de débitos previos, mayor o igual que cero."];
        if (input.KnownDestination is null)
            errors["knownDestination"] = ["Indica si el destino es conocido."];
        return errors;
    }

    /// <summary>Evalúa una entrada completa y produce una decisión explicable y versionada.</summary>
    public static SalidaFraude Evaluar(EntradaFraude input)
    {
        if (Validar(input).Count != 0) throw new ArgumentException("Entrada de evaluación inválida.");
        var (level, reason) = input switch
        {
            { Amount: >= 40_000m } => ("Alto", "El monto es igual o mayor que S/ 40 000."),
            { PreviousDebitsIn15Minutes: >= 5 } => ("Alto", "La cuenta registra al menos cinco débitos previos en quince minutos."),
            { Amount: >= 10_000m } => ("Medio", "El monto es igual o mayor que S/ 10 000."),
            { KnownDestination: false, Amount: >= 5_000m } => ("Medio", "Destino nuevo con monto desde S/ 5 000."),
            _ => ("Bajo", "No se cumplen las condiciones de riesgo medio o alto del taller.")
        };
        return new(input.TransactionReference!.Value, level, reason, Version);
    }
}
