namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Solicitud y respuesta durable de antifraude. La ausencia de respuesta no equivale a riesgo bajo.</summary>
public sealed class EvaluacionFraudeRow
{
    public long EvaluacionFraudeId { get; set; }
    public Guid TransferenciaId { get; set; }
    public int NumeroIntento { get; set; } = 1;
    public string EstadoEvaluacion { get; set; } = "Pendiente";
    public byte? NivelRiesgoId { get; set; }
    public string? Motivo { get; set; }
    public string VersionRegla { get; set; } = string.Empty;
    public string Proveedor { get; set; } = string.Empty;
    public DateTimeOffset FechaSolicitud { get; set; }
    public DateTimeOffset? FechaRespuesta { get; set; }
    public byte[] VersionFila { get; set; } = [];
}
