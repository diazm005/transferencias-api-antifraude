namespace Domain.Entities.Internals;

public static class CalculadoraDeComision
{
    /// <summary>
    /// Calcula la comisión: S/ 5 entre bancos distintos y S/ 0 dentro del mismo banco.
    /// </summary>
    public static decimal Calcular(string bancoOrigen, string bancoDestino) =>
        string.Equals(bancoOrigen, bancoDestino, StringComparison.OrdinalIgnoreCase) ? 0m : 5m;
}
