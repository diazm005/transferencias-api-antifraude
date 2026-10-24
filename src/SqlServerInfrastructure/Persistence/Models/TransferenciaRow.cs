namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Solicitud y último estado de la operación. El identificador recibido permite reconocer reintentos.</summary>
public sealed class TransferenciaRow
{
    public Guid TransferenciaId { get; set; }
    public long ClienteId { get; set; }
    public long CuentaOrigenId { get; set; }
    public int BancoDestinoId { get; set; }
    public string NumeroCuentaDestino { get; set; } = string.Empty;
    public long? CuentaDestinoInternaId { get; set; }
    public decimal Monto { get; set; }
    public decimal Comision { get; set; }
    /// <summary>Columna calculada por SQL Server a partir del monto y la comisión.</summary>
    public decimal TotalDebito { get; set; }
    public string Moneda { get; set; } = "PEN";
    public DateTimeOffset FechaSolicitud { get; set; }
    /// <summary>Día de Lima calculado en SQL; agrupa las operaciones para el límite diario.</summary>
    public DateOnly FechaNegocio { get; set; }
    public byte EstadoTransferenciaId { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public bool Notificada { get; set; }
    public byte[] VersionFila { get; set; } = [];
}
