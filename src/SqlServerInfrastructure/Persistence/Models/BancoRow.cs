namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Fila del catálogo bancario. Evita repetir nombres y distingue el banco propio.</summary>
public sealed class BancoRow
{
    public int BancoId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool EsBancoPropio { get; set; }
    public bool Activo { get; set; } = true;
}
