namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Cuenta administrada por el core local del taller, con saldo y control de concurrencia.</summary>
public sealed class CuentaRow
{
    public long CuentaId { get; set; }
    public long ClienteId { get; set; }
    public int BancoId { get; set; }
    public string NumeroCuenta { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;
    public string Moneda { get; set; } = "PEN";
    public decimal SaldoDisponible { get; set; }
    /// <summary>Token generado por SQL Server; no es una fecha ni se calcula en C#.</summary>
    public byte[] VersionFila { get; set; } = [];
    public DateTimeOffset FechaCreacion { get; set; }
}
