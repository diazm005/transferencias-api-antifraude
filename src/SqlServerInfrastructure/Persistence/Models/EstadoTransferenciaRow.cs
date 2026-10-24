namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Catálogo cuyos identificadores conservan la numeración del estado del dominio.</summary>
public sealed class EstadoTransferenciaRow
{
    public byte EstadoTransferenciaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}
