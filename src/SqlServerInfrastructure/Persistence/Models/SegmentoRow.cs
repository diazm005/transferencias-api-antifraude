namespace SqlServerInfrastructure.Persistence.Models;

/// <summary>Guarda el límite diario por segmento; el cálculo y la validación pertenecen al dominio.</summary>
public sealed class SegmentoRow
{
    public int SegmentoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal LimiteDiario { get; set; }
    public bool Activo { get; set; } = true;
}
