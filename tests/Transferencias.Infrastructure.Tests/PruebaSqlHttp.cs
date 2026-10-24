using System.Data.Common;
using System.Net;
using Domain.Entities.Internals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using SqlServerInfrastructure.Adapters;
using SqlServerInfrastructure.Http;
using SqlServerInfrastructure.Persistence;
using SqlServerInfrastructure.Persistence.Models;

/// <summary>Prueba HTTP y seguimiento SQL con filas propias que se revierten siempre; no ejecuta débitos.</summary>
internal static class PruebaSqlHttp
{
    public static async Task<int> EjecutarAsync()
    {
        try
        {
            var connection = TransferenciasConnectionString.Validar(Environment.GetEnvironmentVariable("ConnectionStrings__Transferencias"));
            var options = new DbContextOptionsBuilder<TransferenciasDbContext>().UseSqlServer(connection).Options;
            await using var db = new TransferenciasDbContext(options);
            await db.Database.OpenConnectionAsync();
            var name = await db.Database.SqlQueryRaw<string>("SELECT CONVERT(nvarchar(128), DB_NAME()) AS [Value]").SingleAsync();
            if (name != "BD_Equipo2") throw new InvalidOperationException("Base no autorizada.");
            var operationId = Guid.NewGuid();
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                try
                {
                    var source = await (from a in db.Cuentas.AsNoTracking()
                                        join c in db.Clientes.AsNoTracking() on a.ClienteId equals c.ClienteId
                                        join b in db.Bancos.AsNoTracking() on a.BancoId equals b.BancoId
                                        where a.Activa && c.Activo && b.EsBancoPropio && b.Activo
                                        select new { a.CuentaId, a.ClienteId, a.NumeroCuenta, c.Dni }).FirstAsync();
                    var destination = await db.Bancos.AsNoTracking().FirstAsync(x => !x.EsBancoPropio && x.Activo);
                    var now = DateTimeOffset.UtcNow;
                    db.Transferencias.Add(new TransferenciaRow
                    {
                        TransferenciaId = operationId, ClienteId = source.ClienteId, CuentaOrigenId = source.CuentaId,
                        BancoDestinoId = destination.BancoId, NumeroCuentaDestino = "PRUEBA-HTTP-ROLLBACK", Monto = 40_000m,
                        Comision = 5, Moneda = "PEN", FechaSolicitud = now, EstadoTransferenciaId = (byte)EstadoTransferencia.NoDisponible,
                        Mensaje = "Prueba temporal de seguimiento HTTP; se revierte."
                    });
                    db.AuditoriasTransferencia.Add(new AuditoriaTransferenciaRow
                    {
                        TransferenciaId = operationId, EstadoTransferenciaId = (byte)EstadoTransferencia.NoDisponible,
                        FechaEvento = now, Mensaje = "Prueba de integración sin débito.", Origen = "Tests.Infrastructure"
                    });
                    await db.SaveChangesAsync();
                    var sharedOptions = new DbContextOptionsBuilder<TransferenciasDbContext>()
                        .UseSqlServer(db.Database.GetDbConnection()).Options;
                    var factory = new FabricaTransaccional(sharedOptions, transaction.GetDbTransaction());
                    var transfer = new Transferencia(operationId, source.Dni, source.NumeroCuenta, "PRUEBA-HTTP-ROLLBACK",
                        destination.Codigo, 40_000m, 5m, now, EstadoTransferencia.NoDisponible, "Prueba temporal");
                    using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("Antifraude__BaseUrl") ?? "http://127.0.0.1:5310/"), Timeout = TimeSpan.FromSeconds(2) };
                    var adapter = new AntifraudeHttpAdapter(factory, new ClienteBatchFraude(http), TimeProvider.System,
                        NullLogger<AntifraudeHttpAdapter>.Instance);
                    var result = await adapter.EvaluarAsync(transfer);
                    var saved = await db.EvaluacionesFraude.AsNoTracking().SingleAsync(x => x.TransferenciaId == operationId);
                    Check(result == NivelRiesgo.Alto && saved.EstadoEvaluacion == "Completada" &&
                        saved.NivelRiesgoId == (byte)NivelRiesgo.Alto && saved.FechaRespuesta is not null &&
                        saved.Proveedor == "FraudBatch.Http" && !string.IsNullOrWhiteSpace(saved.Motivo),
                        "Batchero HTTP real y registro SQL de la respuesta correctos");

                    using var failed = PruebasHttp.Crear((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
                    var unavailable = new AntifraudeHttpAdapter(factory, new ClienteBatchFraude(failed), TimeProvider.System,
                        NullLogger<AntifraudeHttpAdapter>.Instance);
                    try { await unavailable.EvaluarAsync(transfer); throw new InvalidOperationException("Se aceptó una caída del batchero."); }
                    catch (HttpRequestException) { }
                    var error = await db.EvaluacionesFraude.AsNoTracking().Where(x => x.TransferenciaId == operationId)
                        .OrderByDescending(x => x.NumeroIntento).FirstAsync();
                    Check(error.NumeroIntento == 2 && error.EstadoEvaluacion == "Error" && error.NivelRiesgoId is null &&
                        error.FechaRespuesta is not null && error.Motivo!.Contains("503"), "HTTP 503 simulado deja un intento Error en SQL");
                    Check(!await db.MovimientosCuenta.AnyAsync(x => x.TransferenciaId == operationId), "La evaluación no generó movimientos financieros");
                }
                finally { await transaction.RollbackAsync(CancellationToken.None); }
            }
            await using var verification = new TransferenciasDbContext(options);
            Check(!await verification.Transferencias.AnyAsync(x => x.TransferenciaId == operationId) &&
                !await verification.EvaluacionesFraude.AnyAsync(x => x.TransferenciaId == operationId) &&
                !await verification.AuditoriasTransferencia.AnyAsync(x => x.TransferenciaId == operationId),
                "Rollback confirmado: no permanecen las filas de prueba en BD_Equipo2");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Prueba HTTP/SQL interrumpida: {ex.GetType().Name}. Las filas de prueba no se confirman.");
            if (ex is InvalidOperationException) Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("OK: " + message);
    }
    private sealed class FabricaTransaccional(DbContextOptions<TransferenciasDbContext> options, DbTransaction transaction)
        : IDbContextFactory<TransferenciasDbContext>
    {
        public TransferenciasDbContext CreateDbContext()
        {
            var db = new TransferenciasDbContext(options);
            db.Database.UseTransaction(transaction);
            return db;
        }
    }
}
