namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Aviso pendiente o enviado por canal, asociado al evento preciso de auditoría.</summary>
public sealed class NotificacionClienteRow
{
    public long NotificacionClienteId { get; set; }
    public Guid TransferenciaId { get; set; }
    public long AuditoriaTransferenciaId { get; set; }
    public string Canal { get; set; } = string.Empty;
    public string EstadoEnvio { get; set; } = "Pendiente";
    public int Intentos { get; set; }
    public DateTimeOffset FechaCreacion { get; set; }
    public DateTimeOffset? FechaEnvio { get; set; }
    public DateTimeOffset? ProximoIntento { get; set; }
    public string? UltimoError { get; set; }
    public byte[] VersionFila { get; set; } = [];
}
