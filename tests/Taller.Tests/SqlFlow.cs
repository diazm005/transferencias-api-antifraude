using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Adapters;
using Domain.Entities.Internals;
using Microsoft.EntityFrameworkCore;
using SqlServerInfrastructure.Persistence;
using SqlServerInfrastructure.Persistence.Models;

namespace Taller.Tests;

/// <summary>
/// Comprueba API → HTTP → batch → SQL con un cliente y una cuenta ficticios exclusivos de la prueba.
/// Solo modifica BD_Equipo2 y elimina en finally sus propias filas identificadas, sin tocar otras cuentas.
/// </summary>
internal static class SqlFlow
{
    public static async Task RunAsync(string batchUrl, Func<Task> stopBatch)
    {
        var connection = TransferenciasConnectionString.Validar(Environment.GetEnvironmentVariable("ConnectionStrings__Transferencias")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Transferencias", EnvironmentVariableTarget.User));
        var options = new DbContextOptionsBuilder<TransferenciasDbContext>().UseSqlServer(connection, sql => sql.CommandTimeout(15)).Options;
        var marker = "TST-" + Guid.NewGuid().ToString("N")[..16];
        var operationIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var segment = new SegmentoRow { Nombre = marker, LimiteDiario = 50000 };
        var customer = new ClienteRow { Nombres = "Cliente ficticio de integración " + marker };
        var account = new CuentaRow { NumeroCuenta = marker, SaldoDisponible = 100000 };
        var total = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FALLÓ SQL: " + name);
            Console.WriteLine($"OK SQL {++total}: {name}");
        }
        try
        {
            await using (var setup = new TransferenciasDbContext(options))
            {
                Check(await setup.Database.SqlQueryRaw<string>("SELECT CONVERT(nvarchar(128), DB_NAME()) AS [Value]").SingleAsync() == "BD_Equipo2",
                    "Conexión exclusiva a BD_Equipo2");
                var bank = await setup.Bancos.AsNoTracking().SingleAsync(b => b.EsBancoPropio && b.Activo);
                // El cliente de prueba tiene su propio segmento y saldo. No reutiliza saldos del equipo.
                do { customer.Dni = Random.Shared.Next(80000000, 99999999).ToString(); }
                while (await setup.Clientes.AnyAsync(c => c.Dni == customer.Dni));
                await using var transaction = await setup.Database.BeginTransactionAsync();
                setup.Segmentos.Add(segment);
                await setup.SaveChangesAsync();
                customer.SegmentoId = segment.SegmentoId;
                customer.FechaCreacion = DateTimeOffset.UtcNow;
                setup.Clientes.Add(customer);
                await setup.SaveChangesAsync();
                account.ClienteId = customer.ClienteId;
                account.BancoId = bank.BancoId;
                account.FechaCreacion = DateTimeOffset.UtcNow;
                setup.Cuentas.Add(account);
                await setup.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            await using var db = new TransferenciasDbContext(options);
            var destination = await db.Bancos.AsNoTracking().FirstAsync(b => !b.EsBancoPropio && b.Activo);
            await using var factory = new ApiFactory(batchUrl, sqlConnection: connection);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTests.Token(customer.Dni));
            var request = new InitiatePaymentExecutionRequest(operationIds[0], customer.Dni, account.NumeroCuenta, marker + "-D", destination.Codigo, 100);
            async Task Expect(InitiatePaymentExecutionRequest input, HttpStatusCode status, string name)
            {
                using var response = await client.PostAsJsonAsync("/api/transactions", input);
                Check(response.StatusCode == status, name + $" (HTTP {(int)response.StatusCode})");
            }
            await Expect(request, HttpStatusCode.OK, "Transferencia autorizada completa con SQL y batch real");
            await Expect(request, HttpStatusCode.OK, "Reintento no duplica el débito");
            Check(await db.MovimientosCuenta.CountAsync(m => m.TransferenciaId == request.TransactionReference) == 2,
                "Solo un débito de monto y uno de comisión");
            using (var query = await client.GetAsync("/api/transactions/" + request.TransactionReference))
                Check(query.StatusCode == HttpStatusCode.OK, "Consulta HTTP del seguimiento persistido");

            var high = request with { TransactionReference = operationIds[1], Amount = 40000 };
            await Expect(high, (HttpStatusCode)422, "Batch de riesgo alto bloquea la transferencia");
            var medium = request with { TransactionReference = operationIds[2], Amount = 10000 };
            await Expect(medium, HttpStatusCode.Conflict, "Riesgo medio exige confirmación");
            await Expect(medium with { OtpCode = "123456" }, HttpStatusCode.OK, "Confirmación conserva referencia y completa");
            Check(await db.EvaluacionesFraude.CountAsync(e => e.TransferenciaId == medium.TransactionReference) == 2,
                "Ambos intentos de evaluación quedan registrados");
            await stopBatch();
            var failure = request with { TransactionReference = operationIds[3] };
            await Expect(failure, HttpStatusCode.ServiceUnavailable, "Caída real del proceso batch produce 503");
            var evaluation = await db.EvaluacionesFraude.AsNoTracking().SingleAsync(e => e.TransferenciaId == failure.TransactionReference);
            Check(evaluation.EstadoEvaluacion == "Error" && evaluation.NivelRiesgoId is null && evaluation.FechaRespuesta is not null,
                "El error queda persistido sin asignar riesgo bajo");
            Check(await db.Transferencias.AnyAsync(t => t.TransferenciaId == failure.TransactionReference &&
                    t.EstadoTransferenciaId == (byte)EstadoTransferencia.NoDisponible), "La transferencia conserva el estado NoDisponible");
            Check(!await db.MovimientosCuenta.AnyAsync(m => m.TransferenciaId == high.TransactionReference || m.TransferenciaId == failure.TransactionReference),
                "Rechazo y caída no generan movimientos");
            Check(await db.Cuentas.Where(c => c.CuentaId == account.CuentaId).Select(c => c.SaldoDisponible).SingleAsync() == 89890m,
                "Saldo ficticio coincide con dos débitos y dos comisiones");
            Check(await db.AuditoriasTransferencia.Where(a => operationIds.Contains(a.TransferenciaId)).CountAsync() >= 8 &&
                await db.NotificacionesCliente.AnyAsync(n => operationIds.Contains(n.TransferenciaId) && n.EstadoEnvio == "Pendiente"),
                "Estados y notificaciones pendientes conservan seguimiento");
            Check(await db.EvaluacionesFraude.Where(e => operationIds.Contains(e.TransferenciaId) && e.EstadoEvaluacion == "Completada").AllAsync(e =>
                e.Proveedor == "FraudBatch.Http" && e.VersionRegla == "TALLER-1.0" && e.Motivo != null),
                "Las respuestas remotas conservan proveedor, versión y motivo");
        }
        finally
        {
            // Limpieza acotada a GUIDs y claves generadas por esta ejecución; no hay DELETE general ni DDL.
            await using var cleanup = new TransferenciasDbContext(options);
            await using (var transaction = await cleanup.Database.BeginTransactionAsync())
            {
                await cleanup.NotificacionesCliente.Where(x => operationIds.Contains(x.TransferenciaId)).ExecuteDeleteAsync();
                await cleanup.EvaluacionesFraude.Where(x => operationIds.Contains(x.TransferenciaId)).ExecuteDeleteAsync();
                await cleanup.MovimientosCuenta.Where(x => operationIds.Contains(x.TransferenciaId)).ExecuteDeleteAsync();
                await cleanup.AuditoriasTransferencia.Where(x => operationIds.Contains(x.TransferenciaId)).ExecuteDeleteAsync();
                await cleanup.Transferencias.Where(x => operationIds.Contains(x.TransferenciaId)).ExecuteDeleteAsync();
                await cleanup.Cuentas.Where(x => x.CuentaId == account.CuentaId && x.NumeroCuenta == marker).ExecuteDeleteAsync();
                await cleanup.Clientes.Where(x => x.ClienteId == customer.ClienteId && x.Nombres == customer.Nombres).ExecuteDeleteAsync();
                await cleanup.Segmentos.Where(x => x.SegmentoId == segment.SegmentoId && x.Nombre == marker).ExecuteDeleteAsync();
                await transaction.CommitAsync();
            }
            Check(!await cleanup.Transferencias.AnyAsync(x => operationIds.Contains(x.TransferenciaId)) &&
                !await cleanup.Cuentas.AnyAsync(x => x.NumeroCuenta == marker) && !await cleanup.Segmentos.AnyAsync(x => x.Nombre == marker),
                "Eliminadas y verificadas únicamente las filas ficticias de esta prueba");
        }
        Console.WriteLine($"Completadas: {total} comprobaciones de integración HTTP/SQL en BD_Equipo2.");
    }
}
