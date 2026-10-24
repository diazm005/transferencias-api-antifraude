namespace Domain.Entities.Internals;

public enum NivelRiesgo { Bajo, Medio, Alto }
public enum EstadoTransferencia { Rechazada, RequiereConfirmacion, NoDisponible, Pendiente, Completada }

// Nunca se almacena el OTP en el registro de auditoría.
public sealed record Transferencia(Guid Id, string ClienteDni, string CuentaOrigen,
    string CuentaDestino, string BancoDestino, decimal Monto, decimal Comision,
    DateTimeOffset Fecha, EstadoTransferencia Estado, string Mensaje, bool Notificada = false);
