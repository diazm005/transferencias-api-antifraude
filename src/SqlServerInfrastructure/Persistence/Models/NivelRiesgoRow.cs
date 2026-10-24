namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Catálogo de resultados del evaluador, independiente del estado financiero.</summary>
public sealed class NivelRiesgoRow
{
    public byte NivelRiesgoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}
