namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Relaciona el documento del cliente con el segmento y las cuentas que posee.</summary>
public sealed class ClienteRow
{
    public long ClienteId { get; set; }
    public string Dni { get; set; } = string.Empty;
    public string Nombres { get; set; } = string.Empty;
    public int SegmentoId { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset FechaCreacion { get; set; }
}
