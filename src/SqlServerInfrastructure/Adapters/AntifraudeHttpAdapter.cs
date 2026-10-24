using Domain.Entities.Internals;
using Domain.Interfaces;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SqlServerInfrastructure.Http;
using SqlServerInfrastructure.Persistence;
using SqlServerInfrastructure.Persistence.Models;

namespace SqlServerInfrastructure.Adapters;

/// <summary>Consulta al batchero por HTTP y conserva cada intento, resultado o error en la base del servicio.</summary>
public sealed class AntifraudeHttpAdapter(IDbContextFactory<TransferenciasDbContext> factory,
    ClienteBatchFraude batch, TimeProvider clock, ILogger<AntifraudeHttpAdapter> logger) : IAntifraudeService
{
    public async Task<NivelRiesgo> EvaluarAsync(Transferencia transferencia, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var transfer = await db.Transferencias.AsNoTracking().SingleOrDefaultAsync(x => x.TransferenciaId == transferencia.Id, ct)
            ?? throw new InvalidOperationException("Debe registrarse la transferencia antes de solicitar su evaluación.");
        var previous = await db.EvaluacionesFraude.Where(x => x.TransferenciaId == transferencia.Id)
            .OrderByDescending(x => x.NumeroIntento).FirstOrDefaultAsync(ct);
        var now = FechaSql(clock.GetUtcNow());
        // El handler mantiene el bloqueo por operación. Un intento HTTP todavía abierto aquí
        // pertenece a una ejecución interrumpida y se cierra antes de iniciar el siguiente.
        if (previous is { Proveedor: "FraudBatch.Http", EstadoEvaluacion: "Procesando" })
        {
            previous.EstadoEvaluacion = "Error";
            previous.Motivo = "La ejecución anterior se interrumpió sin registrar respuesta; se inicia otro intento.";
            previous.FechaRespuesta = now < previous.FechaSolicitud ? previous.FechaSolicitud : now;
        }
        var evaluation = new EvaluacionFraudeRow
        {
            TransferenciaId = transferencia.Id,
            NumeroIntento = checked((previous?.NumeroIntento ?? 0) + 1),
            EstadoEvaluacion = "Procesando",
            VersionRegla = PoliticaFraude.Version,
            Proveedor = "FraudBatch.Http",
            FechaSolicitud = now
        };
        db.EvaluacionesFraude.Add(evaluation);
        // Procesando tiene nivel y fecha de respuesta nulos; no es una aprobación provisional.
        await db.SaveChangesAsync(ct);
        try
        {
            var windowStart = now.AddMinutes(-15);
            var history = from movement in db.MovimientosCuenta.AsNoTracking()
                          join operation in db.Transferencias.AsNoTracking()
                              on movement.TransferenciaId equals operation.TransferenciaId
                          where movement.CuentaId == transfer.CuentaOrigenId && operation.CuentaOrigenId == transfer.CuentaOrigenId
                              && movement.TipoMovimiento == "DEBITO_MONTO" && movement.Naturaleza == "D"
                              && movement.TransferenciaId != transfer.TransferenciaId && movement.FechaMovimiento <= now
                              && operation.FechaSolicitud <= now
                          select new { movement.TransferenciaId, movement.FechaMovimiento, operation.BancoDestinoId, operation.NumeroCuentaDestino };
            var count = await history.Where(x => x.FechaMovimiento >= windowStart)
                .Select(x => x.TransferenciaId).Distinct().CountAsync(ct);
            var known = await history.AnyAsync(x => x.BancoDestinoId == transfer.BancoDestinoId &&
                x.NumeroCuentaDestino == transfer.NumeroCuentaDestino, ct);

            var decision = await batch.EvaluarAsync(transfer.TransferenciaId, new ContextoFraude(transfer.Monto, count, known), ct);
            evaluation.EstadoEvaluacion = "Completada";
            evaluation.NivelRiesgoId = (byte)decision.Nivel;
            evaluation.Motivo = decision.Motivo;
            evaluation.VersionRegla = decision.VersionRegla;
            evaluation.FechaRespuesta = RespuestaDesde(evaluation.FechaSolicitud);
            await db.SaveChangesAsync(ct);
            return decision.Nivel;
        }
        catch (Exception ex)
        {
            // El token HTTP puede estar cancelado. Un plazo independiente permite registrar el fallo
            // antes de devolver la indisponibilidad; jamás se convierte un error en riesgo bajo.
            await RegistrarErrorAsync(evaluation.EvaluacionFraudeId, ex);
            throw;
        }
    }

    private async Task RegistrarErrorAsync(long evaluationId, Exception error)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var db = await factory.CreateDbContextAsync(cleanup.Token);
            var row = await db.EvaluacionesFraude.SingleOrDefaultAsync(x => x.EvaluacionFraudeId == evaluationId, cleanup.Token);
            if (row is null || row.EstadoEvaluacion == "Completada") return;
            row.EstadoEvaluacion = "Error";
            row.NivelRiesgoId = null;
            row.Motivo = error switch
            {
                OperationCanceledException => "La consulta al batchero fue cancelada o excedió el tiempo de espera.",
                HttpRequestException { StatusCode: { } code } => $"El batchero respondió HTTP {(int)code}; no se obtuvo una evaluación válida.",
                HttpRequestException => "No se pudo contactar al batchero o su respuesta no es válida.",
                _ => "No se pudo completar la evaluación por un fallo de infraestructura."
            };
            row.FechaRespuesta = RespuestaDesde(row.FechaSolicitud);
            await db.SaveChangesAsync(cleanup.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo cerrar el intento antifraude {EvaluationId}; requiere revisión.", evaluationId);
        }
    }

    private DateTimeOffset RespuestaDesde(DateTimeOffset requested)
    {
        var now = FechaSql(clock.GetUtcNow());
        return now < requested ? requested : now;
    }
    private static DateTimeOffset FechaSql(DateTimeOffset value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMillisecond));
}
