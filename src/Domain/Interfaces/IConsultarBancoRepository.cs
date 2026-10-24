namespace Domain.Interfaces;

/// <summary>Consulta el catálogo de bancos habilitados para recibir transferencias.</summary>
public interface IConsultarBancoRepository
{
    Task<bool> ExisteAsync(string codigo, CancellationToken ct = default);
}
