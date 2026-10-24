namespace Domain.Entities.Internals;

// Copia inmutable de la cuenta: el saldo solo lo modifica el sistema bancario central.
public sealed record Cuenta(string Numero, string ClienteDni, string Banco, bool Activa, decimal Saldo);
