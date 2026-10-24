namespace Domain.Entities.Internals;

public static class Reglas
{
    /// <summary>
    /// Indica si el monto alcanza S/ 10 000 o el riesgo es medio y se necesita una confirmación adicional mediante un código OTP.
    /// </summary>
    public static bool RequiereOtp(decimal monto, NivelRiesgo riesgo) =>
        monto >= 10_000m || riesgo == NivelRiesgo.Medio;

    /// <summary>
    /// Comprueba monto, estado de cuenta, saldo con comisión y límite acumulado diario. Devuelve el motivo de rechazo o null cuando las reglas se cumplen.
    /// </summary>
    public static string? Validar(Cuenta cuenta, decimal monto, decimal comision,
        decimal acumuladoHoy, decimal limiteDiario)
    {
        if (monto <= 0 || decimal.Round(monto, 2) != monto)
            return "El monto debe ser positivo y tener como máximo dos decimales.";
        if (!cuenta.Activa) return "La cuenta no está activa.";
        if (monto > cuenta.Saldo - comision) return "Saldo insuficiente para monto y comisión.";
        if (monto > limiteDiario - acumuladoHoy) return "Se supera el límite diario del cliente.";
        return null;
    }
}
