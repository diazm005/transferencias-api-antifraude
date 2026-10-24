using Domain.Entities.Internals;
namespace Domain.Interfaces;
public interface IAntifraudeService
{
    /// <summary>
    /// Consulta el nivel de riesgo de la transferencia antes de autorizar cualquier débito.
    /// </summary>
    Task<NivelRiesgo> EvaluarAsync(Transferencia transferencia, CancellationToken ct = default);
}
