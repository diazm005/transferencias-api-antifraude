namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Efecto contable sobre una cuenta. Se guarda junto al cambio de saldo en la misma transacción.</summary>
public sealed class MovimientoCuentaRow
{
    public long MovimientoCuentaId { get; set; }
    public Guid TransferenciaId { get; set; }
    public long CuentaId { get; set; }
    public string TipoMovimiento { get; set; } = string.Empty;
    public string Naturaleza { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public decimal SaldoAnterior { get; set; }
    public decimal SaldoPosterior { get; set; }
    public DateTimeOffset FechaMovimiento { get; set; }
}
