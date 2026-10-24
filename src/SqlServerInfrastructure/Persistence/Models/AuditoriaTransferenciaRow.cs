namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Evento de estado que permite explicar el recorrido de una transferencia.</summary>
public sealed class AuditoriaTransferenciaRow
{
    public long AuditoriaTransferenciaId { get; set; }
    public Guid TransferenciaId { get; set; }
    public byte EstadoTransferenciaId { get; set; }
    public DateTimeOffset FechaEvento { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public string? TraceIdentifier { get; set; }
    public string Origen { get; set; } = "Application";
}
